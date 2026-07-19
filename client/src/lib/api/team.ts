/**
 * Team, invitations, API keys and integrations.
 *
 * These are the workspace-administration surfaces. Grouped together because
 * they share one audience and one permission model.
 */

import { DEFAULT_WORKSPACE_SETTINGS } from "@/lib/db/seed";
import { collection, read, write } from "@/lib/db/storage";
import type {
  ApiKey,
  Integration,
  IntegrationStatus,
  Invitation,
  Organization,
  User,
  UserRole,
  WorkspaceSettings,
} from "@/types/domain";
import { ApiError, generateId, now, request } from "./client";

const users = collection<User>("users");
const invitations = collection<Invitation>("invitations");
const apiKeys = collection<ApiKey>("api_keys");
const integrations = collection<Integration>("integrations");
const organizations = collection<Organization>("organizations");

/* -------------------------------------------------------------------------- */
/* Members                                                                    */
/* -------------------------------------------------------------------------- */

export interface MemberQuery {
  search?: string;
  role?: UserRole[];
  status?: User["status"][];
}

export function listMembers(query: MemberQuery = {}): Promise<User[]> {
  return request(() =>
    users
      .all()
      .filter((user) => {
        if (query.role?.length && !query.role.includes(user.role)) return false;
        if (query.status?.length && !query.status.includes(user.status)) {
          return false;
        }
        const needle = query.search?.trim().toLowerCase();
        if (!needle) return true;
        return [user.name, user.email, user.jobTitle, user.department].some(
          (field) => field.toLowerCase().includes(needle),
        );
      })
      // Owner first, then admins, then everyone else — the order people expect
      // when scanning a member list for who can change things.
      .sort((a, b) => {
        const rank: Record<UserRole, number> = {
          owner: 0,
          admin: 1,
          member: 2,
          guest: 3,
        };
        return rank[a.role] - rank[b.role] || a.name.localeCompare(b.name);
      }),
  );
}

export function updateMemberRole(id: string, role: UserRole): Promise<User> {
  return request(() => {
    const user = users.find(id);
    if (!user) throw new ApiError("Member not found", 404);

    // A workspace with no owner cannot be administered, so the last one is
    // protected rather than silently demoted.
    if (user.role === "owner" && role !== "owner") {
      const owners = users.all().filter((u) => u.role === "owner");
      if (owners.length === 1) {
        throw new ApiError(
          "This is the only owner. Promote someone else first.",
          409,
        );
      }
    }

    return users.update(id, { role, updatedAt: now() }) as User;
  });
}

export function setMemberStatus(
  id: string,
  status: User["status"],
): Promise<User> {
  return request(() => {
    const updated = users.update(id, { status, updatedAt: now() });
    if (!updated) throw new ApiError("Member not found", 404);
    return updated;
  });
}

export function removeMember(id: string): Promise<void> {
  return request(() => {
    const user = users.find(id);
    if (!user) throw new ApiError("Member not found", 404);
    if (user.role === "owner") {
      throw new ApiError("An owner cannot be removed", 409);
    }
    users.remove(id);
  });
}

/** Capability matrix backing the permissions table. */
export const ROLE_PERMISSIONS: {
  capability: string;
  description: string;
  roles: Record<UserRole, boolean>;
}[] = [
  {
    capability: "View meetings & summaries",
    description: "Read recorded meetings, transcripts and AI output",
    roles: { owner: true, admin: true, member: true, guest: true },
  },
  {
    capability: "Record & upload",
    description: "Start recordings and add documents",
    roles: { owner: true, admin: true, member: true, guest: false },
  },
  {
    capability: "Manage action items",
    description: "Create, assign and close action items",
    roles: { owner: true, admin: true, member: true, guest: false },
  },
  {
    capability: "Invite members",
    description: "Send and revoke workspace invitations",
    roles: { owner: true, admin: true, member: false, guest: false },
  },
  {
    capability: "Manage integrations",
    description: "Connect and disconnect third-party tools",
    roles: { owner: true, admin: true, member: false, guest: false },
  },
  {
    capability: "Manage billing & ownership",
    description: "Change the plan and transfer ownership",
    roles: { owner: true, admin: false, member: false, guest: false },
  },
];

/* -------------------------------------------------------------------------- */
/* Invitations                                                                */
/* -------------------------------------------------------------------------- */

const INVITE_TTL_DAYS = 14;

export function listInvitations(): Promise<Invitation[]> {
  return request(() =>
    invitations
      .all()
      .sort(
        (a, b) =>
          new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime(),
      ),
  );
}

export function inviteMember(input: {
  email: string;
  role: UserRole;
  invitedById: string;
}): Promise<Invitation> {
  return request(() => {
    const email = input.email.trim().toLowerCase();

    if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) {
      throw new ApiError("Enter a valid email address", 422);
    }
    if (users.all().some((user) => user.email.toLowerCase() === email)) {
      throw new ApiError("That person is already a member", 409);
    }
    if (
      invitations
        .all()
        .some((inv) => inv.email === email && inv.status === "pending")
    ) {
      throw new ApiError(
        "An invitation is already pending for that address",
        409,
      );
    }

    const timestamp = now();
    return invitations.insert({
      id: generateId("inv"),
      email,
      role: input.role,
      status: "pending",
      invitedById: input.invitedById,
      expiresAt: new Date(
        Date.now() + INVITE_TTL_DAYS * 24 * 60 * 60 * 1000,
      ).toISOString(),
      createdAt: timestamp,
      updatedAt: timestamp,
    });
  });
}

export function revokeInvitation(id: string): Promise<Invitation> {
  return request(() => {
    const updated = invitations.update(id, {
      status: "revoked",
      updatedAt: now(),
    });
    if (!updated) throw new ApiError("Invitation not found", 404);
    return updated;
  });
}

export function resendInvitation(id: string): Promise<Invitation> {
  return request(() => {
    const updated = invitations.update(id, {
      status: "pending",
      expiresAt: new Date(
        Date.now() + INVITE_TTL_DAYS * 24 * 60 * 60 * 1000,
      ).toISOString(),
      updatedAt: now(),
    });
    if (!updated) throw new ApiError("Invitation not found", 404);
    return updated;
  });
}

/* -------------------------------------------------------------------------- */
/* API keys                                                                   */
/* -------------------------------------------------------------------------- */

export function listApiKeys(): Promise<ApiKey[]> {
  return request(() =>
    apiKeys
      .all()
      .sort(
        (a, b) =>
          new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime(),
      ),
  );
}

/**
 * Creates a key. The full secret is returned once here and never surfaced
 * again by the UI — the list only ever shows the prefix.
 */
export function createApiKey(input: {
  name: string;
  scopes: ("read" | "write")[];
}): Promise<ApiKey> {
  return request(() => {
    if (!input.name.trim()) throw new ApiError("Give the key a name", 422);
    if (input.scopes.length === 0) {
      throw new ApiError("Select at least one scope", 422);
    }

    const secret = `cdn_sk_${Math.random().toString(36).slice(2)}${Math.random()
      .toString(36)
      .slice(2)}`;
    const timestamp = now();

    return apiKeys.insert({
      id: generateId("key"),
      name: input.name.trim(),
      prefix: secret.slice(0, 12),
      secret,
      lastUsedAt: null,
      revokedAt: null,
      scopes: input.scopes,
      createdAt: timestamp,
      updatedAt: timestamp,
    });
  });
}

export function revokeApiKey(id: string): Promise<ApiKey> {
  return request(() => {
    const updated = apiKeys.update(id, {
      revokedAt: now(),
      updatedAt: now(),
    });
    if (!updated) throw new ApiError("Key not found", 404);
    return updated;
  });
}

export function deleteApiKey(id: string): Promise<void> {
  return request(() => {
    apiKeys.remove(id);
  });
}

/* -------------------------------------------------------------------------- */
/* Integrations                                                               */
/* -------------------------------------------------------------------------- */

export function listIntegrations(): Promise<Integration[]> {
  return request(() => integrations.all());
}

/**
 * Simulates an OAuth connect. Slower than a normal call because it stands in
 * for a redirect out to the provider and back.
 */
export async function connectIntegration(id: string): Promise<Integration> {
  await new Promise((resolve) => setTimeout(resolve, 900));

  return request(() => {
    const integration = integrations.find(id);
    if (!integration) throw new ApiError("Integration not found", 404);

    return integrations.update(id, {
      status: "connected",
      connectedAt: now(),
      accountLabel: integration.accountLabel ?? "amara.osei@northwind.io",
      updatedAt: now(),
    }) as Integration;
  });
}

export function disconnectIntegration(id: string): Promise<Integration> {
  return request(() => {
    const updated = integrations.update(id, {
      status: "disconnected" as IntegrationStatus,
      connectedAt: null,
      accountLabel: null,
      updatedAt: now(),
    });
    if (!updated) throw new ApiError("Integration not found", 404);
    return updated;
  });
}

/* -------------------------------------------------------------------------- */
/* Organizations                                                              */
/* -------------------------------------------------------------------------- */

export function listOrganizations(): Promise<Organization[]> {
  return request(() =>
    organizations
      .all()
      // Current first, then alphabetical — the switcher reads better that way.
      .sort(
        (a, b) =>
          Number(b.isCurrent) - Number(a.isCurrent) ||
          a.name.localeCompare(b.name),
      ),
  );
}

/**
 * Switches the active organization.
 *
 * Exactly one is current at a time, so this clears the flag everywhere before
 * setting it — a second "current" org would make the switcher ambiguous.
 */
export function switchOrganization(id: string): Promise<Organization> {
  return request(() => {
    const all = organizations.all();
    if (!all.some((org) => org.id === id)) {
      throw new ApiError("Organization not found", 404);
    }

    organizations.replaceAll(
      all.map((org) => ({
        ...org,
        isCurrent: org.id === id,
        updatedAt: now(),
      })),
    );

    return organizations.find(id) as Organization;
  });
}

export function createOrganization(input: {
  name: string;
  ownerId: string;
}): Promise<Organization> {
  return request(() => {
    const name = input.name.trim();
    if (!name) throw new ApiError("Give the organization a name", 422);

    const slug = name
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, "-")
      .replace(/^-|-$/g, "");

    if (organizations.all().some((org) => org.slug === slug)) {
      throw new ApiError("An organization with that name already exists", 409);
    }

    const timestamp = now();
    return organizations.insert({
      id: generateId("org"),
      name,
      slug,
      plan: "team",
      memberIds: [input.ownerId],
      // A newly created org does not steal focus from the current one.
      isCurrent: false,
      ownerId: input.ownerId,
      createdAt: timestamp,
      updatedAt: timestamp,
    });
  });
}

export function renameOrganization(
  id: string,
  name: string,
): Promise<Organization> {
  return request(() => {
    const trimmed = name.trim();
    if (!trimmed) throw new ApiError("Name cannot be empty", 422);

    const updated = organizations.update(id, {
      name: trimmed,
      updatedAt: now(),
    });
    if (!updated) throw new ApiError("Organization not found", 404);
    return updated;
  });
}

export function deleteOrganization(id: string): Promise<void> {
  return request(() => {
    const org = organizations.find(id);
    if (!org) throw new ApiError("Organization not found", 404);
    if (org.isCurrent) {
      throw new ApiError(
        "Switch to another organization before deleting this one",
        409,
      );
    }
    organizations.remove(id);
  });
}

/* -------------------------------------------------------------------------- */
/* Workspace settings                                                         */
/* -------------------------------------------------------------------------- */

/** Synchronous read; the settings form needs a value on first render. */
export function getWorkspaceSettings(): WorkspaceSettings {
  return {
    ...DEFAULT_WORKSPACE_SETTINGS,
    ...read<Partial<WorkspaceSettings>>("workspace", {}),
  };
}

export function saveWorkspaceSettings(
  patch: Partial<WorkspaceSettings>,
): WorkspaceSettings {
  const next = { ...getWorkspaceSettings(), ...patch };
  write("workspace", next);
  return next;
}
