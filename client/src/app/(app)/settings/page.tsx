"use client";

import {
  Check,
  Copy,
  KeyRound,
  Monitor,
  Moon,
  Plus,
  RotateCcw,
  Sun,
  Trash2,
} from "lucide-react";
import { useCallback, useEffect, useState } from "react";
import { useAuth, useSession } from "@/components/providers/auth-provider";
import { usePreferences } from "@/components/providers/preferences-provider";
import { PageContainer, PageHeader } from "@/components/shell/page-header";
import { Avatar } from "@/components/ui/avatar";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import {
  ConfirmDialog,
  Dialog,
  DialogClose,
  DialogContent,
} from "@/components/ui/dialog";
import { EmptyState, Skeleton } from "@/components/ui/feedback";
import { Field, Input } from "@/components/ui/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { useToast } from "@/components/ui/toast";
import { getCurrentUser, resetWorkspace, updateProfile } from "@/lib/api/auth";
import {
  createApiKey,
  deleteApiKey,
  listApiKeys,
  revokeApiKey,
} from "@/lib/api/team";
import { useAsync } from "@/lib/hooks/use-async";
import { cn } from "@/lib/utils/cn";
import { formatRelative } from "@/lib/utils/format";
import type {
  AiPreferences,
  ApiKey,
  NotificationKind,
  ThemeMode,
} from "@/types/domain";

type Section =
  | "profile"
  | "workspace"
  | "notifications"
  | "appearance"
  | "ai"
  | "security"
  | "api";

const SECTIONS: { value: Section; label: string }[] = [
  { value: "profile", label: "Profile" },
  { value: "workspace", label: "Workspace" },
  { value: "notifications", label: "Notifications" },
  { value: "appearance", label: "Appearance" },
  { value: "ai", label: "AI preferences" },
  { value: "security", label: "Security" },
  { value: "api", label: "API keys" },
];

const NOTIFICATION_KINDS: {
  value: NotificationKind;
  label: string;
  description: string;
}[] = [
  {
    value: "transcript_ready",
    label: "Transcript ready",
    description: "When a recording finishes transcribing",
  },
  {
    value: "summary_ready",
    label: "Summary ready",
    description: "When an AI summary has been generated",
  },
  {
    value: "meeting_reminder",
    label: "Meeting reminder",
    description: "Shortly before a scheduled meeting starts",
  },
  {
    value: "task_assigned",
    label: "Task assigned",
    description: "When an action item is assigned to you",
  },
  {
    value: "mention",
    label: "Mention",
    description: "When someone @mentions you in a comment",
  },
  {
    value: "document_uploaded",
    label: "Document uploaded",
    description: "When a file is added to the workspace",
  },
];

/** Section shell — a titled card with consistent padding. */
function SettingsCard({
  title,
  description,
  children,
  footer,
}: {
  title: string;
  description?: string;
  children: React.ReactNode;
  footer?: React.ReactNode;
}) {
  return (
    <section className="rounded-surface border border-border bg-surface">
      <header className="border-b border-border px-4 py-3">
        <h2 className="text-subheading text-foreground">{title}</h2>
        {description ? (
          <p className="mt-0.5 text-caption text-muted">{description}</p>
        ) : null}
      </header>
      <div className="p-4">{children}</div>
      {footer ? (
        <div className="flex items-center justify-end gap-2 border-t border-border px-4 py-3">
          {footer}
        </div>
      ) : null}
    </section>
  );
}

/** Label + description + control, the row shape used throughout settings. */
function SettingRow({
  label,
  description,
  control,
}: {
  label: string;
  description?: string;
  control: React.ReactNode;
}) {
  return (
    <div className="flex items-start justify-between gap-4 py-3">
      <div className="min-w-0">
        <p className="text-body text-foreground">{label}</p>
        {description ? (
          <p className="mt-0.5 text-caption text-muted">{description}</p>
        ) : null}
      </div>
      <div className="shrink-0">{control}</div>
    </div>
  );
}

export default function SettingsPage() {
  const session = useSession();
  const { refresh: refreshSession, signOut } = useAuth();
  const { preferences, update, resolvedTheme } = usePreferences();
  const { toast } = useToast();

  const [section, setSection] = useState<Section>("profile");
  const profile = useAsync(() => getCurrentUser(), []);
  const apiKeys = useAsync(() => listApiKeys(), []);

  return (
    <PageContainer>
      <PageHeader
        title="Settings"
        description="Profile, workspace, security and AI preferences."
      />

      <Tabs
        value={section}
        onValueChange={(value) => setSection(value as Section)}
        className="mb-4"
      >
        <TabsList>
          {SECTIONS.map((entry) => (
            <TabsTrigger key={entry.value} value={entry.value}>
              {entry.label}
            </TabsTrigger>
          ))}
        </TabsList>
      </Tabs>

      <div className="max-w-3xl space-y-4">
        {section === "profile" ? (
          <ProfileSection
            loading={profile.loading}
            name={profile.data?.name ?? session.name}
            email={profile.data?.email ?? session.email}
            jobTitle={profile.data?.jobTitle ?? ""}
            department={profile.data?.department ?? ""}
            timezone={profile.data?.timezone ?? ""}
            onSaved={() => {
              profile.refetch();
              refreshSession();
            }}
          />
        ) : null}

        {section === "workspace" ? (
          <>
            <SettingsCard
              title="Workspace"
              description="How this workspace is identified across the product."
            >
              <div className="divide-y divide-border">
                <SettingRow
                  label="Workspace name"
                  description="Shown in the sidebar and on shared links."
                  control={
                    <Input
                      defaultValue="Northwind"
                      className="w-56"
                      aria-label="Workspace name"
                    />
                  }
                />
                <SettingRow
                  label="Default meeting visibility"
                  description="Who can see a new recording by default."
                  control={
                    <Select defaultValue="team">
                      <SelectTrigger className="w-56">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="team">
                          Everyone in workspace
                        </SelectItem>
                        <SelectItem value="participants">
                          Participants only
                        </SelectItem>
                        <SelectItem value="private">Only me</SelectItem>
                      </SelectContent>
                    </Select>
                  }
                />
                <SettingRow
                  label="Retention"
                  description="How long recordings and transcripts are kept."
                  control={
                    <Select defaultValue="12m">
                      <SelectTrigger className="w-56">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="3m">3 months</SelectItem>
                        <SelectItem value="12m">12 months</SelectItem>
                        <SelectItem value="forever">Indefinitely</SelectItem>
                      </SelectContent>
                    </Select>
                  }
                />
              </div>
            </SettingsCard>

            <DangerZone
              onReset={async () => {
                await resetWorkspace();
                toast({
                  tone: "success",
                  title: "Workspace reset",
                  description: "Signing you out to rebuild the demo data.",
                });
                await signOut();
              }}
            />
          </>
        ) : null}

        {section === "notifications" ? (
          <SettingsCard
            title="Notifications"
            description="Choose what reaches you, and where."
          >
            <div className="mb-2 flex items-center justify-end gap-6 pr-1">
              <span className="text-overline uppercase text-subtle">
                In app
              </span>
              <span className="text-overline uppercase text-subtle">Email</span>
            </div>

            <div className="divide-y divide-border">
              {NOTIFICATION_KINDS.map((kind) => (
                <div
                  key={kind.value}
                  className="flex items-center justify-between gap-4 py-3"
                >
                  <div className="min-w-0">
                    <p className="text-body text-foreground">{kind.label}</p>
                    <p className="mt-0.5 text-caption text-muted">
                      {kind.description}
                    </p>
                  </div>

                  <div className="flex shrink-0 items-center gap-10 pr-3">
                    <Checkbox
                      checked={preferences.notifications.inApp.includes(
                        kind.value,
                      )}
                      aria-label={`${kind.label} in app`}
                      onCheckedChange={(value) =>
                        update({
                          notifications: {
                            ...preferences.notifications,
                            inApp:
                              value === true
                                ? [
                                    ...preferences.notifications.inApp,
                                    kind.value,
                                  ]
                                : preferences.notifications.inApp.filter(
                                    (k) => k !== kind.value,
                                  ),
                          },
                        })
                      }
                    />
                    <Checkbox
                      checked={preferences.notifications.email.includes(
                        kind.value,
                      )}
                      aria-label={`${kind.label} by email`}
                      onCheckedChange={(value) =>
                        update({
                          notifications: {
                            ...preferences.notifications,
                            email:
                              value === true
                                ? [
                                    ...preferences.notifications.email,
                                    kind.value,
                                  ]
                                : preferences.notifications.email.filter(
                                    (k) => k !== kind.value,
                                  ),
                          },
                        })
                      }
                    />
                  </div>
                </div>
              ))}
            </div>
          </SettingsCard>
        ) : null}

        {section === "appearance" ? (
          <SettingsCard
            title="Appearance"
            description="Changes apply immediately and are remembered on this device."
          >
            <div className="divide-y divide-border">
              <SettingRow
                label="Theme"
                description={`Currently rendering in ${resolvedTheme} mode.`}
                control={
                  <div className="flex items-center gap-0.5 rounded-control border border-border p-0.5">
                    {(
                      [
                        { value: "light" as const, icon: Sun, label: "Light" },
                        { value: "dark" as const, icon: Moon, label: "Dark" },
                        {
                          value: "system" as const,
                          icon: Monitor,
                          label: "System",
                        },
                      ] as const
                    ).map(({ value, icon: Icon, label }) => (
                      <button
                        key={value}
                        type="button"
                        onClick={() => update({ theme: value as ThemeMode })}
                        aria-pressed={preferences.theme === value}
                        className={cn(
                          "flex items-center gap-1.5 rounded-[4px] px-2.5 py-1 text-caption font-medium transition-colors",
                          preferences.theme === value
                            ? "bg-surface-raised text-foreground"
                            : "text-muted hover:text-foreground",
                        )}
                      >
                        <Icon className="size-3.5" />
                        {label}
                      </button>
                    ))}
                  </div>
                }
              />

              <SettingRow
                label="Density"
                description="Compact fits more rows on screen."
                control={
                  <Select
                    value={preferences.density}
                    onValueChange={(value) =>
                      update({
                        density: value as "comfortable" | "compact",
                      })
                    }
                  >
                    <SelectTrigger className="w-44">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="comfortable">Comfortable</SelectItem>
                      <SelectItem value="compact">Compact</SelectItem>
                    </SelectContent>
                  </Select>
                }
              />

              <SettingRow
                label="Language"
                description="Interface language. Content is never translated."
                control={
                  <Select
                    value={preferences.language}
                    onValueChange={(value) => update({ language: value })}
                  >
                    <SelectTrigger className="w-44">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="en">English</SelectItem>
                      <SelectItem value="de">Deutsch</SelectItem>
                      <SelectItem value="fr">Français</SelectItem>
                      <SelectItem value="es">Español</SelectItem>
                    </SelectContent>
                  </Select>
                }
              />

              <SettingRow
                label="Collapse sidebar"
                description="Start with the navigation collapsed."
                control={
                  <Checkbox
                    checked={preferences.sidebarCollapsed}
                    aria-label="Collapse sidebar by default"
                    onCheckedChange={(value) =>
                      update({ sidebarCollapsed: value === true })
                    }
                  />
                }
              />
            </div>
          </SettingsCard>
        ) : null}

        {section === "ai" ? (
          <SettingsCard
            title="AI preferences"
            description="How Cadence summarises meetings and extracts commitments."
          >
            <div className="divide-y divide-border">
              <SettingRow
                label="Summary length"
                description="How much detail generated summaries carry."
                control={
                  <Select
                    value={preferences.ai.summaryLength}
                    onValueChange={(value) =>
                      update({
                        ai: {
                          ...preferences.ai,
                          summaryLength:
                            value as AiPreferences["summaryLength"],
                        },
                      })
                    }
                  >
                    <SelectTrigger className="w-44">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="brief">Brief</SelectItem>
                      <SelectItem value="standard">Standard</SelectItem>
                      <SelectItem value="detailed">Detailed</SelectItem>
                    </SelectContent>
                  </Select>
                }
              />

              <SettingRow
                label="Summarise automatically"
                description="Generate a summary as soon as a recording finishes."
                control={
                  <Checkbox
                    checked={preferences.ai.autoSummarise}
                    aria-label="Summarise automatically"
                    onCheckedChange={(value) =>
                      update({
                        ai: {
                          ...preferences.ai,
                          autoSummarise: value === true,
                        },
                      })
                    }
                  />
                }
              />

              <SettingRow
                label="Detect action items"
                description="Flag commitments spoken during a meeting."
                control={
                  <Checkbox
                    checked={preferences.ai.autoExtractActionItems}
                    aria-label="Detect action items"
                    onCheckedChange={(value) =>
                      update({
                        ai: {
                          ...preferences.ai,
                          autoExtractActionItems: value === true,
                        },
                      })
                    }
                  />
                }
              />

              <SettingRow
                label="Review before creating"
                description="Hold detected items for confirmation instead of creating them outright. Extraction is imperfect, so this is on by default."
                control={
                  <Checkbox
                    checked={preferences.ai.requireActionItemReview}
                    aria-label="Require review before creating action items"
                    disabled={!preferences.ai.autoExtractActionItems}
                    onCheckedChange={(value) =>
                      update({
                        ai: {
                          ...preferences.ai,
                          requireActionItemReview: value === true,
                        },
                      })
                    }
                  />
                }
              />

              <SettingRow
                label="Output language"
                description="The language summaries are written in."
                control={
                  <Select
                    value={preferences.ai.outputLanguage}
                    onValueChange={(value) =>
                      update({
                        ai: { ...preferences.ai, outputLanguage: value },
                      })
                    }
                  >
                    <SelectTrigger className="w-44">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="en">English</SelectItem>
                      <SelectItem value="de">Deutsch</SelectItem>
                      <SelectItem value="fr">Français</SelectItem>
                      <SelectItem value="es">Español</SelectItem>
                    </SelectContent>
                  </Select>
                }
              />
            </div>
          </SettingsCard>
        ) : null}

        {section === "security" ? (
          <SettingsCard
            title="Security"
            description="How you sign in and where your session is active."
          >
            <div className="divide-y divide-border">
              <SettingRow
                label="Sign-in method"
                description="Google is the only method enabled for this workspace."
                control={<Badge tone="success">Google SSO</Badge>}
              />
              <SettingRow
                label="Two-factor authentication"
                description="Managed by your Google account, not by Cadence."
                control={
                  <Button variant="secondary" size="sm" asChild>
                    <a
                      href="https://myaccount.google.com/security"
                      target="_blank"
                      rel="noopener noreferrer"
                    >
                      Manage in Google
                    </a>
                  </Button>
                }
              />
              <SettingRow
                label="Active session"
                description={`Signed in ${formatRelative(session.issuedAt)} · expires ${formatRelative(session.expiresAt)}`}
                control={
                  <Button variant="danger-outline" size="sm" onClick={signOut}>
                    Sign out
                  </Button>
                }
              />
            </div>
          </SettingsCard>
        ) : null}

        {section === "api" ? (
          <ApiKeysSection
            keys={apiKeys.data ?? []}
            loading={apiKeys.loading}
            onChanged={apiKeys.refetch}
          />
        ) : null}
      </div>
    </PageContainer>
  );
}

/* -------------------------------------------------------------------------- */
/* Profile                                                                    */
/* -------------------------------------------------------------------------- */

function ProfileSection({
  loading,
  name,
  email,
  jobTitle,
  department,
  timezone,
  onSaved,
}: {
  loading: boolean;
  name: string;
  email: string;
  jobTitle: string;
  department: string;
  timezone: string;
  onSaved: () => void;
}) {
  const { toast } = useToast();
  const [draft, setDraft] = useState({ name, jobTitle, department, timezone });
  const [saving, setSaving] = useState(false);
  const [nameError, setNameError] = useState<string>();

  // Re-seed the form once the profile actually loads.
  useEffect(() => {
    setDraft({ name, jobTitle, department, timezone });
  }, [name, jobTitle, department, timezone]);

  const dirty =
    draft.name !== name ||
    draft.jobTitle !== jobTitle ||
    draft.department !== department ||
    draft.timezone !== timezone;

  async function handleSave() {
    if (!draft.name.trim()) {
      setNameError("Name cannot be empty.");
      return;
    }

    setSaving(true);
    try {
      await updateProfile({
        name: draft.name.trim(),
        jobTitle: draft.jobTitle.trim(),
        department: draft.department.trim(),
        timezone: draft.timezone.trim(),
      });
      onSaved();
      toast({ tone: "success", title: "Profile updated" });
    } catch (error) {
      toast({
        tone: "error",
        title: "Could not save profile",
        description:
          error instanceof Error ? error.message : "Please try again.",
      });
    } finally {
      setSaving(false);
    }
  }

  if (loading) return <Skeleton className="h-80 w-full" />;

  return (
    <SettingsCard
      title="Profile"
      description="How you appear to everyone else in the workspace."
      footer={
        <Button
          variant="primary"
          size="sm"
          loading={saving}
          disabled={!dirty}
          onClick={handleSave}
        >
          Save changes
        </Button>
      }
    >
      <div className="space-y-4">
        <div className="flex items-center gap-4">
          <Avatar name={draft.name || name} size="xl" />
          <div className="min-w-0">
            <p className="text-body font-medium text-foreground">{email}</p>
            <p className="text-caption text-muted">
              Your avatar and email come from Google and can&apos;t be changed
              here.
            </p>
          </div>
        </div>

        <Field label="Full name" required error={nameError}>
          {(props) => (
            <Input
              {...props}
              value={draft.name}
              onChange={(event) => {
                setDraft((d) => ({ ...d, name: event.target.value }));
                if (nameError) setNameError(undefined);
              }}
            />
          )}
        </Field>

        <div className="grid gap-4 sm:grid-cols-2">
          <Field label="Job title">
            {(props) => (
              <Input
                {...props}
                value={draft.jobTitle}
                onChange={(event) =>
                  setDraft((d) => ({ ...d, jobTitle: event.target.value }))
                }
              />
            )}
          </Field>

          <Field label="Department">
            {(props) => (
              <Input
                {...props}
                value={draft.department}
                onChange={(event) =>
                  setDraft((d) => ({ ...d, department: event.target.value }))
                }
              />
            )}
          </Field>
        </div>

        <Field label="Timezone" hint="Used when displaying meeting times.">
          {(props) => (
            <Input
              {...props}
              value={draft.timezone}
              onChange={(event) =>
                setDraft((d) => ({ ...d, timezone: event.target.value }))
              }
            />
          )}
        </Field>
      </div>
    </SettingsCard>
  );
}

/* -------------------------------------------------------------------------- */
/* API keys                                                                   */
/* -------------------------------------------------------------------------- */

function ApiKeysSection({
  keys,
  loading,
  onChanged,
}: {
  keys: ApiKey[];
  loading: boolean;
  onChanged: () => void;
}) {
  const { toast } = useToast();
  const [createOpen, setCreateOpen] = useState(false);
  const [name, setName] = useState("");
  const [scopes, setScopes] = useState<("read" | "write")[]>(["read"]);
  const [error, setError] = useState<string>();
  const [submitting, setSubmitting] = useState(false);
  // Held only until the dialog closes — never re-derivable from the list.
  const [revealed, setRevealed] = useState<ApiKey | null>(null);
  const [pendingDelete, setPendingDelete] = useState<ApiKey | null>(null);

  const handleCreate = useCallback(
    async (event: React.FormEvent) => {
      event.preventDefault();
      setSubmitting(true);
      setError(undefined);

      try {
        const created = await createApiKey({ name, scopes });
        setRevealed(created);
        setCreateOpen(false);
        setName("");
        setScopes(["read"]);
        onChanged();
      } catch (caught) {
        setError(
          caught instanceof Error ? caught.message : "Could not create key.",
        );
      } finally {
        setSubmitting(false);
      }
    },
    [name, scopes, onChanged],
  );

  return (
    <>
      <SettingsCard
        title="API keys"
        description="Programmatic access to your workspace data."
        footer={
          <Button
            variant="primary"
            size="sm"
            onClick={() => setCreateOpen(true)}
          >
            <Plus />
            Create key
          </Button>
        }
      >
        {loading ? (
          <Skeleton className="h-24 w-full" />
        ) : keys.length === 0 ? (
          <EmptyState
            icon={KeyRound}
            title="No API keys"
            description="Create a key to access meetings, transcripts and action items from your own tools."
            className="py-10"
          />
        ) : (
          <ul className="divide-y divide-border">
            {keys.map((key) => {
              const revoked = key.revokedAt !== null;

              return (
                <li
                  key={key.id}
                  className="flex items-center justify-between gap-4 py-3"
                >
                  <div className="min-w-0">
                    <div className="flex flex-wrap items-center gap-2">
                      <p
                        className={cn(
                          "text-body font-medium",
                          revoked
                            ? "text-subtle line-through"
                            : "text-foreground",
                        )}
                      >
                        {key.name}
                      </p>
                      {revoked ? (
                        <Badge tone="danger" size="sm">
                          Revoked
                        </Badge>
                      ) : (
                        key.scopes.map((scope) => (
                          <Badge key={scope} tone="neutral" size="sm">
                            {scope}
                          </Badge>
                        ))
                      )}
                    </div>

                    <p className="mt-0.5 font-mono text-caption text-muted">
                      {key.prefix}
                      <span className="text-subtle">••••••••••••</span>
                    </p>
                    <p className="mt-0.5 text-label text-subtle">
                      Created {formatRelative(key.createdAt)}
                      {key.lastUsedAt
                        ? ` · last used ${formatRelative(key.lastUsedAt)}`
                        : " · never used"}
                    </p>
                  </div>

                  <div className="flex shrink-0 items-center gap-1.5">
                    {!revoked ? (
                      <Button
                        variant="secondary"
                        size="sm"
                        onClick={async () => {
                          await revokeApiKey(key.id);
                          onChanged();
                          toast({ tone: "info", title: "Key revoked" });
                        }}
                      >
                        Revoke
                      </Button>
                    ) : null}
                    <Button
                      variant="ghost"
                      size="icon-sm"
                      aria-label={`Delete ${key.name}`}
                      onClick={() => setPendingDelete(key)}
                    >
                      <Trash2 />
                    </Button>
                  </div>
                </li>
              );
            })}
          </ul>
        )}
      </SettingsCard>

      {/* Create */}
      <Dialog open={createOpen} onOpenChange={setCreateOpen}>
        <DialogContent
          title="Create API key"
          description="The secret is shown once and cannot be retrieved later."
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
                form="api-key-form"
                loading={submitting}
              >
                Create key
              </Button>
            </>
          }
        >
          <form
            id="api-key-form"
            onSubmit={handleCreate}
            className="space-y-4"
            noValidate
          >
            <Field label="Key name" required error={error}>
              {(props) => (
                <Input
                  {...props}
                  value={name}
                  onChange={(event) => {
                    setName(event.target.value);
                    if (error) setError(undefined);
                  }}
                  placeholder="CI pipeline"
                  autoFocus
                />
              )}
            </Field>

            <Field
              label="Scopes"
              hint="Write access can create and modify records."
            >
              {() => (
                <div className="space-y-2">
                  {(["read", "write"] as const).map((scope) => (
                    <label
                      key={scope}
                      htmlFor={`scope-${scope}`}
                      className="flex cursor-pointer items-center gap-2.5 rounded-control px-1 py-1"
                    >
                      <Checkbox
                        id={`scope-${scope}`}
                        checked={scopes.includes(scope)}
                        onCheckedChange={(value) =>
                          setScopes((current) =>
                            value === true
                              ? [...current, scope]
                              : current.filter((s) => s !== scope),
                          )
                        }
                      />
                      <span className="text-body capitalize text-foreground">
                        {scope}
                      </span>
                    </label>
                  ))}
                </div>
              )}
            </Field>
          </form>
        </DialogContent>
      </Dialog>

      {/* Reveal — the only time the full secret is ever shown. */}
      <Dialog
        open={revealed !== null}
        onOpenChange={(open) => {
          if (!open) setRevealed(null);
        }}
      >
        <DialogContent
          title="Copy your API key"
          description="This is the only time the full key will be shown. Store it somewhere safe."
          size="sm"
          footer={
            <DialogClose asChild>
              <Button variant="primary" size="sm">
                <Check />
                Done
              </Button>
            </DialogClose>
          }
        >
          <div className="flex items-center gap-2 rounded-control border border-border bg-surface-sunken p-2.5">
            <code className="min-w-0 flex-1 break-all font-mono text-caption text-foreground">
              {revealed?.secret}
            </code>
            <Button
              variant="secondary"
              size="icon-sm"
              aria-label="Copy key"
              onClick={async () => {
                if (!revealed) return;
                await navigator.clipboard.writeText(revealed.secret);
                toast({ tone: "success", title: "Key copied to clipboard" });
              }}
            >
              <Copy />
            </Button>
          </div>
        </DialogContent>
      </Dialog>

      <ConfirmDialog
        open={pendingDelete !== null}
        onOpenChange={(open) => {
          if (!open) setPendingDelete(null);
        }}
        title="Delete this API key?"
        description={`Any integration still using "${pendingDelete?.name ?? ""}" will immediately stop working.`}
        confirmLabel="Delete key"
        destructive
        onConfirm={async () => {
          if (!pendingDelete) return;
          await deleteApiKey(pendingDelete.id);
          setPendingDelete(null);
          onChanged();
          toast({ tone: "success", title: "Key deleted" });
        }}
      />
    </>
  );
}

/* -------------------------------------------------------------------------- */
/* Danger zone                                                                */
/* -------------------------------------------------------------------------- */

function DangerZone({ onReset }: { onReset: () => Promise<void> }) {
  const [confirming, setConfirming] = useState(false);
  const [working, setWorking] = useState(false);

  return (
    <>
      <section className="rounded-surface border border-danger/40 bg-surface">
        <header className="border-b border-danger/30 px-4 py-3">
          <h2 className="text-subheading text-foreground">Danger zone</h2>
          <p className="mt-0.5 text-caption text-muted">
            Irreversible actions affecting the whole workspace.
          </p>
        </header>

        <div className="flex items-center justify-between gap-4 p-4">
          <div className="min-w-0">
            <p className="text-body text-foreground">Reset demo data</p>
            <p className="mt-0.5 text-caption text-muted">
              Deletes every meeting, task, document and preference, then
              rebuilds the seeded workspace from scratch.
            </p>
          </div>
          <Button
            variant="danger"
            size="sm"
            className="shrink-0"
            onClick={() => setConfirming(true)}
          >
            <RotateCcw />
            Reset
          </Button>
        </div>
      </section>

      <ConfirmDialog
        open={confirming}
        onOpenChange={setConfirming}
        title="Reset the entire workspace?"
        description="Every meeting, transcript, task, document and setting will be deleted and the demo data regenerated. You will be signed out. This cannot be undone."
        confirmLabel="Reset everything"
        destructive
        loading={working}
        onConfirm={async () => {
          setWorking(true);
          await onReset();
          setWorking(false);
        }}
      />
    </>
  );
}
