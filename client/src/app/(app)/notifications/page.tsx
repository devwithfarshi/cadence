"use client";

import { isToday, isYesterday } from "date-fns";
import {
  Archive,
  ArchiveRestore,
  AtSign,
  Bell,
  CalendarClock,
  CheckCheck,
  FileText,
  ListChecks,
  Sparkles,
  Trash2,
} from "lucide-react";
import Link from "next/link";
import { useCallback, useMemo, useState } from "react";
import { PageContainer, PageHeader } from "@/components/shell/page-header";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { ConfirmDialog } from "@/components/ui/dialog";
import { EmptyState, ErrorState, SkeletonRows } from "@/components/ui/feedback";
import { FilterMenu } from "@/components/ui/filter-menu";
import { Tabs, TabsCount, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { useToast } from "@/components/ui/toast";
import {
  archiveNotification,
  deleteNotification,
  listNotifications,
  markAllNotificationsRead,
  markNotificationRead,
} from "@/lib/api/workspace";
import { useAsync } from "@/lib/hooks/use-async";
import { cn } from "@/lib/utils/cn";
import { formatDate, formatRelative } from "@/lib/utils/format";
import type { AppNotification, NotificationKind } from "@/types/domain";

const KIND_ICONS: Record<NotificationKind, typeof Bell> = {
  transcript_ready: FileText,
  summary_ready: Sparkles,
  meeting_reminder: CalendarClock,
  task_assigned: ListChecks,
  mention: AtSign,
  document_uploaded: FileText,
};

const KIND_OPTIONS: { value: NotificationKind; label: string }[] = [
  { value: "transcript_ready", label: "Transcript ready" },
  { value: "summary_ready", label: "Summary ready" },
  { value: "meeting_reminder", label: "Meeting reminder" },
  { value: "task_assigned", label: "Task assigned" },
  { value: "mention", label: "Mention" },
  { value: "document_uploaded", label: "Document uploaded" },
];

type Tab = "inbox" | "unread" | "archived";

/** Groups by day so a long inbox stays scannable. */
function groupByDay(items: AppNotification[]) {
  const groups = new Map<string, AppNotification[]>();

  for (const item of items) {
    const date = new Date(item.createdAt);
    const label = isToday(date)
      ? "Today"
      : isYesterday(date)
        ? "Yesterday"
        : formatDate(date);

    const bucket = groups.get(label) ?? [];
    bucket.push(item);
    groups.set(label, bucket);
  }

  return [...groups.entries()];
}

export default function NotificationsPage() {
  const { toast } = useToast();

  const [tab, setTab] = useState<Tab>("inbox");
  const [kinds, setKinds] = useState<NotificationKind[]>([]);
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [mutating, setMutating] = useState(false);

  // Everything is fetched once and split client-side, so the tab counts always
  // agree with the lists beneath them.
  const all = useAsync(() => listNotifications({ includeArchived: true }), []);

  const { inbox, unread, archived } = useMemo(() => {
    const items = all.data ?? [];
    const byKind = kinds.length
      ? items.filter((item) => kinds.includes(item.kind))
      : items;

    return {
      inbox: byKind.filter((item) => !item.isArchived),
      unread: byKind.filter((item) => !item.isArchived && !item.isRead),
      archived: byKind.filter((item) => item.isArchived),
    };
  }, [all.data, kinds]);

  const visible =
    tab === "unread" ? unread : tab === "archived" ? archived : inbox;
  const grouped = groupByDay(visible);

  const refresh = useCallback(() => {
    all.refetch();
    setSelectedIds([]);
  }, [all]);

  async function handleMarkRead(ids: string[], isRead: boolean) {
    setMutating(true);
    try {
      await Promise.all(ids.map((id) => markNotificationRead(id, isRead)));
      refresh();
      toast({
        tone: "success",
        title: `Marked ${ids.length} as ${isRead ? "read" : "unread"}`,
      });
    } catch {
      toast({ tone: "error", title: "Could not update notifications" });
    } finally {
      setMutating(false);
    }
  }

  async function handleArchive(ids: string[], isArchived: boolean) {
    setMutating(true);
    try {
      await Promise.all(ids.map((id) => archiveNotification(id, isArchived)));
      refresh();
      toast({
        tone: "success",
        title: isArchived
          ? `Archived ${ids.length}`
          : `Restored ${ids.length} to inbox`,
      });
    } catch {
      toast({ tone: "error", title: "Could not update notifications" });
    } finally {
      setMutating(false);
    }
  }

  async function handleDelete() {
    setMutating(true);
    try {
      await Promise.all(selectedIds.map((id) => deleteNotification(id)));
      setConfirmDelete(false);
      refresh();
      toast({ tone: "success", title: `Deleted ${selectedIds.length}` });
    } catch {
      toast({ tone: "error", title: "Could not delete notifications" });
    } finally {
      setMutating(false);
    }
  }

  const allVisibleSelected =
    visible.length > 0 &&
    visible.every((item) => selectedIds.includes(item.id));

  return (
    <PageContainer>
      <PageHeader
        title="Notifications"
        description="Everything Cadence has flagged for your attention."
        actions={
          <Button
            variant="secondary"
            size="md"
            disabled={unread.length === 0}
            onClick={async () => {
              const count = await markAllNotificationsRead();
              refresh();
              toast({
                tone: "success",
                title: `Marked ${count} as read`,
              });
            }}
          >
            <CheckCheck />
            Mark all read
          </Button>
        }
      />

      <Tabs
        value={tab}
        onValueChange={(value) => {
          setTab(value as Tab);
          setSelectedIds([]);
        }}
        className="mb-4"
      >
        <TabsList>
          <TabsTrigger value="inbox">
            Inbox
            <TabsCount>{inbox.length}</TabsCount>
          </TabsTrigger>
          <TabsTrigger value="unread">
            Unread
            {unread.length > 0 ? <TabsCount>{unread.length}</TabsCount> : null}
          </TabsTrigger>
          <TabsTrigger value="archived">
            Archived
            {archived.length > 0 ? (
              <TabsCount>{archived.length}</TabsCount>
            ) : null}
          </TabsTrigger>
        </TabsList>
      </Tabs>

      <div className="mb-3 flex flex-wrap items-center gap-2">
        <FilterMenu
          label="Type"
          options={KIND_OPTIONS}
          selected={kinds}
          onChange={setKinds}
        />

        {visible.length > 0 ? (
          <label
            // Explicitly bound: the Radix checkbox renders a button, which
            // implicit label wrapping does not associate with.
            htmlFor="select-all-notifications"
            className="flex cursor-pointer items-center gap-2 rounded-control px-2 py-1 text-caption text-muted hover:text-foreground"
          >
            <Checkbox
              id="select-all-notifications"
              checked={
                allVisibleSelected
                  ? true
                  : selectedIds.length > 0
                    ? "indeterminate"
                    : false
              }
              onCheckedChange={() =>
                setSelectedIds(
                  allVisibleSelected ? [] : visible.map((item) => item.id),
                )
              }
            />
            Select all
          </label>
        ) : null}
      </div>

      {selectedIds.length > 0 ? (
        <div className="mb-3 flex flex-wrap items-center gap-2 rounded-surface border border-accent/40 bg-accent-subtle px-3 py-2">
          <p className="text-caption font-medium text-foreground tabular">
            {selectedIds.length} selected
          </p>
          <div className="ml-auto flex flex-wrap items-center gap-2">
            <Button
              variant="secondary"
              size="sm"
              loading={mutating}
              onClick={() => handleMarkRead(selectedIds, true)}
            >
              <CheckCheck />
              Mark read
            </Button>
            <Button
              variant="secondary"
              size="sm"
              loading={mutating}
              onClick={() => handleArchive(selectedIds, tab !== "archived")}
            >
              {tab === "archived" ? <ArchiveRestore /> : <Archive />}
              {tab === "archived" ? "Restore" : "Archive"}
            </Button>
            <Button
              variant="danger-outline"
              size="sm"
              onClick={() => setConfirmDelete(true)}
            >
              <Trash2 />
              Delete
            </Button>
            <Button
              variant="ghost"
              size="sm"
              onClick={() => setSelectedIds([])}
            >
              Clear
            </Button>
          </div>
        </div>
      ) : null}

      {all.error ? (
        <ErrorState description={all.error.message} onRetry={all.refetch} />
      ) : all.loading && !all.data ? (
        <div className="rounded-surface border border-border bg-surface">
          <SkeletonRows rows={6} columns={2} />
        </div>
      ) : visible.length === 0 ? (
        <EmptyState
          icon={Bell}
          title={
            tab === "unread"
              ? "All caught up"
              : tab === "archived"
                ? "Nothing archived"
                : "No notifications"
          }
          description={
            tab === "unread"
              ? "You have read everything in your inbox."
              : tab === "archived"
                ? "Archived notifications are kept here in case you need them."
                : "Activity from your meetings, tasks and documents will appear here."
          }
          className="rounded-surface border border-border bg-surface"
        />
      ) : (
        <div className="space-y-5">
          {grouped.map(([day, items]) => (
            <section key={day}>
              <h2 className="mb-2 text-overline uppercase text-subtle">
                {day}
              </h2>

              <ul className="divide-y divide-border rounded-surface border border-border bg-surface">
                {items.map((notification) => {
                  const Icon = KIND_ICONS[notification.kind];
                  const selected = selectedIds.includes(notification.id);

                  return (
                    <li
                      key={notification.id}
                      className={cn(
                        "group flex gap-3 px-4 py-3 transition-colors",
                        selected
                          ? "bg-accent-subtle/50"
                          : !notification.isRead
                            ? "bg-accent-subtle/20 hover:bg-surface-raised/50"
                            : "hover:bg-surface-raised/50",
                      )}
                    >
                      <Checkbox
                        checked={selected}
                        onCheckedChange={(value) =>
                          setSelectedIds((current) =>
                            value === true
                              ? [...current, notification.id]
                              : current.filter((id) => id !== notification.id),
                          )
                        }
                        aria-label={`Select "${notification.title}"`}
                        className="mt-1"
                      />

                      <Icon
                        className="mt-0.5 size-4 shrink-0 text-subtle"
                        aria-hidden
                      />

                      <div className="min-w-0 flex-1">
                        <div className="flex items-center gap-2">
                          {notification.href ? (
                            <Link
                              href={notification.href}
                              onClick={() =>
                                !notification.isRead &&
                                markNotificationRead(notification.id).then(
                                  refresh,
                                )
                              }
                              className="min-w-0 truncate text-body font-medium text-foreground hover:text-accent"
                            >
                              {notification.title}
                            </Link>
                          ) : (
                            <span className="truncate text-body font-medium text-foreground">
                              {notification.title}
                            </span>
                          )}
                          {!notification.isRead ? (
                            <>
                              {/* The dot is decorative; the state is announced
                                  as text so it isn't colour-only. */}
                              <span
                                aria-hidden
                                className="size-1.5 shrink-0 rounded-full bg-accent"
                              />
                              <span className="sr-only">Unread</span>
                            </>
                          ) : null}
                        </div>

                        <p className="mt-0.5 text-caption text-muted">
                          {notification.body}
                        </p>
                        <p className="mt-1 text-label text-subtle">
                          {formatRelative(notification.createdAt)}
                        </p>
                      </div>

                      <div className="flex shrink-0 items-start gap-0.5 opacity-0 transition-opacity focus-within:opacity-100 group-hover:opacity-100">
                        <Button
                          variant="ghost"
                          size="icon-sm"
                          aria-label={
                            notification.isRead
                              ? "Mark as unread"
                              : "Mark as read"
                          }
                          onClick={() =>
                            handleMarkRead(
                              [notification.id],
                              !notification.isRead,
                            )
                          }
                        >
                          <CheckCheck />
                        </Button>
                        <Button
                          variant="ghost"
                          size="icon-sm"
                          aria-label={
                            notification.isArchived ? "Restore" : "Archive"
                          }
                          onClick={() =>
                            handleArchive(
                              [notification.id],
                              !notification.isArchived,
                            )
                          }
                        >
                          {notification.isArchived ? (
                            <ArchiveRestore />
                          ) : (
                            <Archive />
                          )}
                        </Button>
                        <Button
                          variant="ghost"
                          size="icon-sm"
                          aria-label="Delete notification"
                          onClick={() => {
                            setSelectedIds([notification.id]);
                            setConfirmDelete(true);
                          }}
                        >
                          <Trash2 />
                        </Button>
                      </div>
                    </li>
                  );
                })}
              </ul>
            </section>
          ))}
        </div>
      )}

      <ConfirmDialog
        open={confirmDelete}
        onOpenChange={setConfirmDelete}
        title={`Delete ${selectedIds.length} ${
          selectedIds.length === 1 ? "notification" : "notifications"
        }?`}
        description="These will be permanently removed. This cannot be undone."
        confirmLabel="Delete"
        destructive
        loading={mutating}
        onConfirm={handleDelete}
      />
    </PageContainer>
  );
}
