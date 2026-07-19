"use client";

import {
  ArrowLeftRight,
  CalendarPlus,
  Check,
  CircleCheck,
  ListPlus,
  Mail,
  MessageSquare,
  Minus,
  MoreHorizontal,
  Plus,
  Sparkles,
  Trash2,
  Upload,
  UserPlus,
  UserX,
  X,
} from "lucide-react";
import Link from "next/link";
import { useCallback, useState } from "react";
import { useSession } from "@/components/providers/auth-provider";
import { PageContainer, PageHeader } from "@/components/shell/page-header";
import { Avatar } from "@/components/ui/avatar";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  ConfirmDialog,
  Dialog,
  DialogClose,
  DialogContent,
} from "@/components/ui/dialog";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import {
  EmptyState,
  ErrorState,
  Skeleton,
  SkeletonRows,
} from "@/components/ui/feedback";
import { FilterMenu } from "@/components/ui/filter-menu";
import { Field, Input, SearchInput } from "@/components/ui/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
  TableWrapper,
} from "@/components/ui/table";
import { Tabs, TabsCount, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Timeline } from "@/components/ui/timeline";
import { useToast } from "@/components/ui/toast";
import {
  createOrganization,
  deleteOrganization,
  inviteMember,
  listInvitations,
  listMembers,
  listOrganizations,
  ROLE_PERMISSIONS,
  removeMember,
  resendInvitation,
  revokeInvitation,
  setMemberStatus,
  switchOrganization,
  updateMemberRole,
} from "@/lib/api/team";
import { listActivity } from "@/lib/api/workspace";
import { useAsync, useDebounced } from "@/lib/hooks/use-async";
import { cn } from "@/lib/utils/cn";
import { formatRelative, humanize } from "@/lib/utils/format";
import type {
  ActivityKind,
  OrganizationPlan,
  User,
  UserRole,
} from "@/types/domain";

const ROLE_OPTIONS: { value: UserRole; label: string }[] = [
  { value: "owner", label: "Owner" },
  { value: "admin", label: "Admin" },
  { value: "member", label: "Member" },
  { value: "guest", label: "Guest" },
];

/** Icon and tone per activity kind, so the timeline reads at a glance. */
const ACTIVITY_ICONS: Record<ActivityKind, typeof Check> = {
  meeting_created: CalendarPlus,
  meeting_completed: CircleCheck,
  summary_generated: Sparkles,
  task_created: ListPlus,
  task_completed: CircleCheck,
  document_uploaded: Upload,
  member_joined: UserPlus,
  comment_added: MessageSquare,
};

const ACTIVITY_TONES: Record<
  ActivityKind,
  "default" | "accent" | "success" | "warning" | "danger"
> = {
  meeting_created: "default",
  meeting_completed: "success",
  summary_generated: "accent",
  task_created: "default",
  task_completed: "success",
  document_uploaded: "default",
  member_joined: "accent",
  comment_added: "default",
};

const ROLE_TONE = {
  owner: "accent",
  admin: "info",
  member: "neutral",
  guest: "outline",
} as const;

const STATUS_TONE = {
  active: "success",
  invited: "warning",
  suspended: "danger",
} as const;

type Tab =
  | "members"
  | "organizations"
  | "invitations"
  | "permissions"
  | "activity";

const PLAN_LABELS: Record<OrganizationPlan, string> = {
  free: "Free",
  team: "Team",
  business: "Business",
  enterprise: "Enterprise",
};

export default function TeamPage() {
  const session = useSession();
  const { toast } = useToast();

  const [tab, setTab] = useState<Tab>("members");
  const [search, setSearch] = useState("");
  const debouncedSearch = useDebounced(search, 250);
  const [roles, setRoles] = useState<UserRole[]>([]);
  const [statuses, setStatuses] = useState<User["status"][]>([]);

  const [inviteOpen, setInviteOpen] = useState(false);
  const [orgDialogOpen, setOrgDialogOpen] = useState(false);
  const [pendingRemove, setPendingRemove] = useState<User | null>(null);
  const [mutating, setMutating] = useState(false);

  const members = useAsync(
    () =>
      listMembers({ search: debouncedSearch, role: roles, status: statuses }),
    [debouncedSearch, roles, statuses],
  );
  const invitations = useAsync(() => listInvitations(), []);
  const organizations = useAsync(() => listOrganizations(), []);
  const activity = useAsync(() => listActivity(20), []);

  const refresh = useCallback(() => {
    members.refetch();
    invitations.refetch();
    organizations.refetch();
    activity.refetch();
  }, [members, invitations, organizations, activity]);

  const pendingInvites = (invitations.data ?? []).filter(
    (invite) => invite.status === "pending",
  );

  async function handleRoleChange(user: User, role: UserRole) {
    try {
      await updateMemberRole(user.id, role);
      refresh();
      toast({
        tone: "success",
        title: "Role updated",
        description: `${user.name} is now ${role}.`,
      });
    } catch (error) {
      toast({
        tone: "error",
        title: "Could not change role",
        description:
          error instanceof Error ? error.message : "Please try again.",
      });
    }
  }

  async function handleStatusChange(user: User, status: User["status"]) {
    try {
      await setMemberStatus(user.id, status);
      refresh();
      toast({
        tone: "success",
        title:
          status === "suspended" ? "Member suspended" : "Member reactivated",
        description: user.name,
      });
    } catch {
      toast({ tone: "error", title: "Could not update member" });
    }
  }

  async function handleRemove() {
    if (!pendingRemove) return;
    setMutating(true);
    try {
      await removeMember(pendingRemove.id);
      setPendingRemove(null);
      refresh();
      toast({ tone: "success", title: "Member removed" });
    } catch (error) {
      toast({
        tone: "error",
        title: "Could not remove member",
        description:
          error instanceof Error ? error.message : "Please try again.",
      });
    } finally {
      setMutating(false);
    }
  }

  return (
    <PageContainer>
      <PageHeader
        title="Team"
        description="Members, roles, permissions and workspace activity."
        actions={
          <Button
            variant="primary"
            size="md"
            onClick={() => setInviteOpen(true)}
          >
            <Plus />
            Invite member
          </Button>
        }
      />

      <Tabs
        value={tab}
        onValueChange={(value) => setTab(value as Tab)}
        className="mb-4"
      >
        <TabsList>
          <TabsTrigger value="members">
            Members
            {members.data ? <TabsCount>{members.data.length}</TabsCount> : null}
          </TabsTrigger>
          <TabsTrigger value="organizations">
            Organizations
            {organizations.data ? (
              <TabsCount>{organizations.data.length}</TabsCount>
            ) : null}
          </TabsTrigger>
          <TabsTrigger value="invitations">
            Invitations
            {pendingInvites.length > 0 ? (
              <TabsCount>{pendingInvites.length}</TabsCount>
            ) : null}
          </TabsTrigger>
          <TabsTrigger value="permissions">Permissions</TabsTrigger>
          <TabsTrigger value="activity">Activity</TabsTrigger>
        </TabsList>
      </Tabs>

      {tab === "members" ? (
        <>
          <div className="mb-3 flex flex-wrap items-center gap-2">
            <SearchInput
              value={search}
              onValueChange={setSearch}
              placeholder="Search members…"
              className="w-full sm:w-64"
            />
            <FilterMenu
              label="Role"
              options={ROLE_OPTIONS}
              selected={roles}
              onChange={setRoles}
            />
            <FilterMenu
              label="Status"
              options={[
                { value: "active" as const, label: "Active" },
                { value: "invited" as const, label: "Invited" },
                { value: "suspended" as const, label: "Suspended" },
              ]}
              selected={statuses}
              onChange={setStatuses}
              icon={false}
            />
            {roles.length + statuses.length > 0 || search ? (
              <Button
                variant="ghost"
                size="sm"
                onClick={() => {
                  setRoles([]);
                  setStatuses([]);
                  setSearch("");
                }}
              >
                <X />
                Clear
              </Button>
            ) : null}
          </div>

          {members.error ? (
            <ErrorState
              description={members.error.message}
              onRetry={members.refetch}
            />
          ) : members.loading && !members.data ? (
            <div className="rounded-surface border border-border bg-surface">
              <SkeletonRows rows={6} columns={4} />
            </div>
          ) : (members.data ?? []).length === 0 ? (
            <EmptyState
              icon={UserX}
              title="No members match your filters"
              description="Try loosening a filter or clearing your search."
              className="rounded-surface border border-border bg-surface"
            />
          ) : (
            <TableWrapper>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Member</TableHead>
                    <TableHead>Role</TableHead>
                    <TableHead>Department</TableHead>
                    <TableHead>Status</TableHead>
                    <TableHead>Last active</TableHead>
                    <TableHead className="w-16 text-right">Actions</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {(members.data ?? []).map((user) => {
                    const isSelf = user.id === session.userId;

                    return (
                      <TableRow key={user.id}>
                        <TableCell>
                          <div className="flex items-center gap-2.5">
                            <Avatar name={user.name} size="md" />
                            <div className="min-w-0">
                              <p className="truncate font-medium text-foreground">
                                {user.name}
                                {isSelf ? (
                                  <span className="ml-1.5 text-label text-subtle">
                                    (you)
                                  </span>
                                ) : null}
                              </p>
                              <p className="truncate text-caption text-muted">
                                {user.email}
                              </p>
                            </div>
                          </div>
                        </TableCell>

                        <TableCell>
                          <Badge tone={ROLE_TONE[user.role]}>
                            {humanize(user.role)}
                          </Badge>
                        </TableCell>

                        <TableCell className="text-muted">
                          <p>{user.jobTitle}</p>
                          <p className="text-caption text-subtle">
                            {user.department}
                          </p>
                        </TableCell>

                        <TableCell>
                          <Badge tone={STATUS_TONE[user.status]}>
                            {humanize(user.status)}
                          </Badge>
                        </TableCell>

                        <TableCell className="whitespace-nowrap text-caption text-muted">
                          {formatRelative(user.lastActiveAt)}
                        </TableCell>

                        <TableCell className="text-right">
                          <DropdownMenu>
                            <DropdownMenuTrigger asChild>
                              <Button
                                variant="ghost"
                                size="icon-sm"
                                aria-label={`Actions for ${user.name}`}
                              >
                                <MoreHorizontal />
                              </Button>
                            </DropdownMenuTrigger>
                            <DropdownMenuContent>
                              <DropdownMenuLabel>Change role</DropdownMenuLabel>
                              {ROLE_OPTIONS.map((option) => (
                                <DropdownMenuItem
                                  key={option.value}
                                  onSelect={() =>
                                    handleRoleChange(user, option.value)
                                  }
                                >
                                  {user.role === option.value ? (
                                    <Check />
                                  ) : (
                                    <span className="size-4" />
                                  )}
                                  {option.label}
                                </DropdownMenuItem>
                              ))}

                              <DropdownMenuSeparator />

                              {user.status === "suspended" ? (
                                <DropdownMenuItem
                                  onSelect={() =>
                                    handleStatusChange(user, "active")
                                  }
                                >
                                  <Check />
                                  Reactivate
                                </DropdownMenuItem>
                              ) : (
                                <DropdownMenuItem
                                  onSelect={() =>
                                    handleStatusChange(user, "suspended")
                                  }
                                  // Suspending yourself would lock you out.
                                  disabled={isSelf}
                                >
                                  <Minus />
                                  Suspend
                                </DropdownMenuItem>
                              )}

                              <DropdownMenuItem
                                destructive
                                disabled={isSelf || user.role === "owner"}
                                onSelect={() => setPendingRemove(user)}
                              >
                                <Trash2 />
                                Remove from workspace
                              </DropdownMenuItem>
                            </DropdownMenuContent>
                          </DropdownMenu>
                        </TableCell>
                      </TableRow>
                    );
                  })}
                </TableBody>
              </Table>
            </TableWrapper>
          )}
        </>
      ) : null}

      {tab === "organizations" ? (
        <div className="space-y-3">
          {organizations.loading && !organizations.data ? (
            <Skeleton className="h-40 w-full" />
          ) : (
            (organizations.data ?? []).map((org) => (
              <div
                key={org.id}
                className={cn(
                  "flex flex-wrap items-center gap-4 rounded-surface border bg-surface p-4",
                  org.isCurrent ? "border-accent/50" : "border-border",
                )}
              >
                <span
                  aria-hidden
                  className="flex size-10 shrink-0 items-center justify-center rounded-surface border border-border bg-surface-raised text-body font-semibold text-muted"
                >
                  {org.name.slice(0, 2).toUpperCase()}
                </span>

                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-2">
                    <p className="truncate text-body font-medium text-foreground">
                      {org.name}
                    </p>
                    {org.isCurrent ? (
                      <Badge tone="accent" size="sm">
                        Current
                      </Badge>
                    ) : null}
                    <Badge tone="neutral" size="sm">
                      {PLAN_LABELS[org.plan]}
                    </Badge>
                  </div>
                  <p className="mt-0.5 text-caption text-muted">
                    {org.slug} · {org.memberIds.length}{" "}
                    {org.memberIds.length === 1 ? "member" : "members"}
                  </p>
                </div>

                <div className="flex shrink-0 items-center gap-2">
                  {org.isCurrent ? (
                    <span className="text-caption text-subtle">Active</span>
                  ) : (
                    <Button
                      variant="secondary"
                      size="sm"
                      onClick={async () => {
                        await switchOrganization(org.id);
                        refresh();
                        toast({
                          tone: "success",
                          title: `Switched to ${org.name}`,
                        });
                      }}
                    >
                      <ArrowLeftRight />
                      Switch
                    </Button>
                  )}
                  <Button
                    variant="ghost"
                    size="icon-sm"
                    aria-label={`Delete ${org.name}`}
                    disabled={org.isCurrent}
                    onClick={async () => {
                      try {
                        await deleteOrganization(org.id);
                        refresh();
                        toast({ tone: "info", title: "Organization deleted" });
                      } catch (error) {
                        toast({
                          tone: "error",
                          title: "Could not delete",
                          description:
                            error instanceof Error
                              ? error.message
                              : "Please try again.",
                        });
                      }
                    }}
                  >
                    <Trash2 />
                  </Button>
                </div>
              </div>
            ))
          )}

          <Button
            variant="secondary"
            size="sm"
            onClick={() => setOrgDialogOpen(true)}
          >
            <Plus />
            New organization
          </Button>
        </div>
      ) : null}

      {tab === "invitations" ? (
        (invitations.data ?? []).length === 0 ? (
          <EmptyState
            icon={Mail}
            title="No invitations"
            description="Invite someone and their pending invitation will appear here."
            action={
              <Button
                variant="primary"
                size="sm"
                onClick={() => setInviteOpen(true)}
              >
                <Plus />
                Invite member
              </Button>
            }
            className="rounded-surface border border-border bg-surface"
          />
        ) : (
          <TableWrapper>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Email</TableHead>
                  <TableHead>Role</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead>Sent</TableHead>
                  <TableHead>Expires</TableHead>
                  <TableHead className="text-right">Actions</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {(invitations.data ?? []).map((invite) => (
                  <TableRow key={invite.id}>
                    <TableCell className="font-medium text-foreground">
                      {invite.email}
                    </TableCell>
                    <TableCell>
                      <Badge tone={ROLE_TONE[invite.role]}>
                        {humanize(invite.role)}
                      </Badge>
                    </TableCell>
                    <TableCell>
                      <Badge
                        tone={
                          invite.status === "pending"
                            ? "warning"
                            : invite.status === "accepted"
                              ? "success"
                              : "neutral"
                        }
                      >
                        {humanize(invite.status)}
                      </Badge>
                    </TableCell>
                    <TableCell className="text-caption text-muted">
                      {formatRelative(invite.createdAt)}
                    </TableCell>
                    <TableCell className="text-caption text-muted">
                      {formatRelative(invite.expiresAt)}
                    </TableCell>
                    <TableCell className="text-right">
                      <div className="flex justify-end gap-1.5">
                        <Button
                          variant="secondary"
                          size="sm"
                          onClick={async () => {
                            await resendInvitation(invite.id);
                            refresh();
                            toast({
                              tone: "success",
                              title: "Invitation resent",
                            });
                          }}
                        >
                          Resend
                        </Button>
                        {invite.status === "pending" ? (
                          <Button
                            variant="ghost"
                            size="sm"
                            onClick={async () => {
                              await revokeInvitation(invite.id);
                              refresh();
                              toast({
                                tone: "info",
                                title: "Invitation revoked",
                              });
                            }}
                          >
                            Revoke
                          </Button>
                        ) : null}
                      </div>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableWrapper>
        )
      ) : null}

      {tab === "permissions" ? (
        <TableWrapper>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Capability</TableHead>
                {ROLE_OPTIONS.map((role) => (
                  <TableHead key={role.value} className="text-center">
                    {role.label}
                  </TableHead>
                ))}
              </TableRow>
            </TableHeader>
            <TableBody>
              {ROLE_PERMISSIONS.map((row) => (
                <TableRow key={row.capability}>
                  <TableCell>
                    <p className="font-medium text-foreground">
                      {row.capability}
                    </p>
                    <p className="text-caption text-muted">{row.description}</p>
                  </TableCell>
                  {ROLE_OPTIONS.map((role) => (
                    <TableCell key={role.value} className="text-center">
                      {row.roles[role.value] ? (
                        <Check
                          className="mx-auto size-4 text-success"
                          aria-label="Allowed"
                        />
                      ) : (
                        <Minus
                          className="mx-auto size-4 text-subtle"
                          aria-label="Not allowed"
                        />
                      )}
                    </TableCell>
                  ))}
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </TableWrapper>
      ) : null}

      {tab === "activity" ? (
        <div className="rounded-surface border border-border bg-surface">
          {activity.loading && !activity.data ? (
            <SkeletonRows rows={6} columns={2} />
          ) : (activity.data ?? []).length === 0 ? (
            <EmptyState
              icon={Mail}
              title="No activity yet"
              description="Workspace activity will show up here as your team works."
            />
          ) : (
            <div className="p-4">
              <Timeline
                entries={(activity.data ?? []).map((entry) => ({
                  id: entry.id,
                  title: entry.href ? (
                    <Link
                      href={entry.href}
                      className="hover:text-accent hover:underline"
                    >
                      {entry.summary}
                    </Link>
                  ) : (
                    entry.summary
                  ),
                  timestamp: formatRelative(entry.createdAt),
                  icon: ACTIVITY_ICONS[entry.kind],
                  tone: ACTIVITY_TONES[entry.kind],
                }))}
              />
            </div>
          )}
        </div>
      ) : null}

      <NewOrganizationDialog
        open={orgDialogOpen}
        onOpenChange={setOrgDialogOpen}
        ownerId={session.userId}
        onCreated={refresh}
      />

      <InviteDialog
        open={inviteOpen}
        onOpenChange={setInviteOpen}
        invitedById={session.userId}
        onInvited={() => {
          refresh();
          setTab("invitations");
        }}
      />

      <ConfirmDialog
        open={pendingRemove !== null}
        onOpenChange={(open) => {
          if (!open) setPendingRemove(null);
        }}
        title="Remove this member?"
        description={`${pendingRemove?.name ?? ""} will lose access to the workspace. Their meetings and action items are kept.`}
        confirmLabel="Remove member"
        destructive
        loading={mutating}
        onConfirm={handleRemove}
      />
    </PageContainer>
  );
}

function InviteDialog({
  open,
  onOpenChange,
  invitedById,
  onInvited,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  invitedById: string;
  onInvited: () => void;
}) {
  const { toast } = useToast();
  const [email, setEmail] = useState("");
  const [role, setRole] = useState<UserRole>("member");
  const [error, setError] = useState<string>();
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(event: React.FormEvent) {
    event.preventDefault();
    setSubmitting(true);
    setError(undefined);

    try {
      await inviteMember({ email, role, invitedById });
      setEmail("");
      setRole("member");
      onOpenChange(false);
      onInvited();
      toast({
        tone: "success",
        title: "Invitation sent",
        description: email,
      });
    } catch (caught) {
      setError(
        caught instanceof Error ? caught.message : "Could not send invitation.",
      );
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        title="Invite a member"
        description="They'll receive an email with a link to join this workspace."
        size="sm"
        footer={
          <>
            <DialogClose asChild>
              <Button variant="secondary" size="sm">
                Cancel
              </Button>
            </DialogClose>
            <Button
              variant="primary"
              size="sm"
              type="submit"
              form="invite-form"
              loading={submitting}
            >
              Send invitation
            </Button>
          </>
        }
      >
        <form
          id="invite-form"
          onSubmit={handleSubmit}
          className="space-y-4"
          noValidate
        >
          <Field label="Email address" required error={error}>
            {(props) => (
              <Input
                {...props}
                type="email"
                value={email}
                onChange={(event) => {
                  setEmail(event.target.value);
                  if (error) setError(undefined);
                }}
                placeholder="colleague@northwind.io"
                autoFocus
              />
            )}
          </Field>

          <Field
            label="Role"
            hint="Guests can read meetings but not record or manage tasks."
          >
            {() => (
              <Select
                value={role}
                onValueChange={(value) => setRole(value as UserRole)}
              >
                <SelectTrigger className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {ROLE_OPTIONS.filter((r) => r.value !== "owner").map(
                    (option) => (
                      <SelectItem key={option.value} value={option.value}>
                        {option.label}
                      </SelectItem>
                    ),
                  )}
                </SelectContent>
              </Select>
            )}
          </Field>
        </form>
      </DialogContent>
    </Dialog>
  );
}

function NewOrganizationDialog({
  open,
  onOpenChange,
  ownerId,
  onCreated,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  ownerId: string;
  onCreated: () => void;
}) {
  const { toast } = useToast();
  const [name, setName] = useState("");
  const [error, setError] = useState<string>();
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(event: React.FormEvent) {
    event.preventDefault();
    setSubmitting(true);
    setError(undefined);

    try {
      await createOrganization({ name, ownerId });
      setName("");
      onOpenChange(false);
      onCreated();
      toast({ tone: "success", title: "Organization created" });
    } catch (caught) {
      setError(
        caught instanceof Error ? caught.message : "Could not create it.",
      );
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        title="New organization"
        description="A separate workspace with its own members and meetings."
        size="sm"
        footer={
          <>
            <DialogClose asChild>
              <Button variant="secondary" size="sm">
                Cancel
              </Button>
            </DialogClose>
            <Button
              variant="primary"
              size="sm"
              type="submit"
              form="new-org-form"
              loading={submitting}
            >
              Create
            </Button>
          </>
        }
      >
        <form
          id="new-org-form"
          onSubmit={handleSubmit}
          className="space-y-4"
          noValidate
        >
          <Field label="Organization name" required error={error}>
            {(props) => (
              <Input
                {...props}
                value={name}
                onChange={(event) => {
                  setName(event.target.value);
                  if (error) setError(undefined);
                }}
                placeholder="Northwind Research"
                autoFocus
              />
            )}
          </Field>
        </form>
      </DialogContent>
    </Dialog>
  );
}
