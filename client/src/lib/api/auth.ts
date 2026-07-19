/**
 * Simulated Google OAuth.
 *
 * The flow mirrors a real OAuth round trip closely enough that swapping in
 * NextAuth or a real provider would only change the body of `signInWithGoogle`:
 * a redirect out, a token back, a session persisted, a profile created on first
 * sign-in.
 */

import { seedIfEmpty } from "@/lib/db/seed";
import { clearAll, collection, read, write } from "@/lib/db/storage";
import type { AuthSession, User } from "@/types/domain";
import { ApiError, generateId, now, request } from "./client";

const SESSION_TTL_DAYS = 30;

/**
 * The demo identity. On first sign-in this becomes the signed-in user; the
 * seeded workspace is already built around them so the dashboard is populated.
 */
const DEMO_IDENTITY = {
  name: "Amara Osei",
  email: "amara.osei@northwind.io",
  jobTitle: "VP Product",
  department: "Product",
};

/** Reads the persisted session without a simulated round trip. */
export function getStoredSession(): AuthSession | null {
  const session = read<AuthSession | null>("session", null);
  if (!session) return null;

  // An expired token is treated as signed out rather than silently accepted.
  if (new Date(session.expiresAt).getTime() < Date.now()) return null;

  return session;
}

/**
 * Simulates the Google consent round trip.
 *
 * On first sign-in the seeded owner account is claimed as the user's profile;
 * on later sign-ins the existing profile is reused, so logging out and back in
 * restores the same workspace.
 */
export async function signInWithGoogle(): Promise<AuthSession> {
  // Deliberately slower than a normal request — this stands in for a redirect
  // to Google and back, and the sign-in button shows a loading state across it.
  await new Promise((resolve) => setTimeout(resolve, 1100));

  seedIfEmpty();

  const users = collection<User>("users");
  const existing = users
    .all()
    .find((user) => user.email === DEMO_IDENTITY.email);

  const user =
    existing ??
    users.insert({
      id: generateId("usr"),
      name: DEMO_IDENTITY.name,
      email: DEMO_IDENTITY.email,
      avatarUrl: null,
      role: "owner",
      status: "active",
      jobTitle: DEMO_IDENTITY.jobTitle,
      department: DEMO_IDENTITY.department,
      timezone: Intl.DateTimeFormat().resolvedOptions().timeZone,
      lastActiveAt: now(),
      createdAt: now(),
      updatedAt: now(),
    });

  users.update(user.id, { lastActiveAt: now() });

  const issuedAt = new Date();
  const expiresAt = new Date(
    issuedAt.getTime() + SESSION_TTL_DAYS * 24 * 60 * 60 * 1000,
  );

  const session: AuthSession = {
    userId: user.id,
    email: user.email,
    name: user.name,
    avatarUrl: user.avatarUrl,
    token: `mock.${generateId("tok")}.${issuedAt.getTime().toString(36)}`,
    issuedAt: issuedAt.toISOString(),
    expiresAt: expiresAt.toISOString(),
  };

  write("session", session);
  return session;
}

/**
 * Clears the session only. Workspace data is deliberately preserved so signing
 * back in returns the user to exactly where they left off.
 */
export async function signOut(): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, 320));
  write("session", null);
}

/** Wipes everything, including seeded demo data. Used by Settings → reset. */
export async function resetWorkspace(): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, 420));
  clearAll({ keepSession: false });
}

/** The full profile of the signed-in user. */
export function getCurrentUser(): Promise<User> {
  return request(() => {
    const session = getStoredSession();
    if (!session) throw new ApiError("Not authenticated", 401);

    const user = collection<User>("users").find(session.userId);
    if (!user) throw new ApiError("User profile not found", 404);

    return user;
  });
}

export function updateProfile(patch: Partial<User>): Promise<User> {
  return request(() => {
    const session = getStoredSession();
    if (!session) throw new ApiError("Not authenticated", 401);

    const updated = collection<User>("users").update(session.userId, {
      ...patch,
      updatedAt: now(),
    });
    if (!updated) throw new ApiError("User profile not found", 404);

    // Keep the cached session display fields in step with the profile.
    write("session", {
      ...session,
      name: updated.name,
      email: updated.email,
      avatarUrl: updated.avatarUrl,
    });

    return updated;
  });
}
