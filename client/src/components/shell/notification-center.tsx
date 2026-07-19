"use client";

import {
  Archive,
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
import { Popover } from "radix-ui";
import { useCallback, useEffect, useState } from "react";
import { Button } from "@/components/ui/button";
import { EmptyState } from "@/components/ui/feedback";
import { useToast } from "@/components/ui/toast";
import { Tooltip } from "@/components/ui/tooltip";
import {
  archiveNotification,
  deleteNotification,
  listNotifications,
  markAllNotificationsRead,
  markNotificationRead,
} from "@/lib/api/workspace";
import { cn } from "@/lib/utils/cn";
import { formatRelative } from "@/lib/utils/format";
import type { AppNotification, NotificationKind } from "@/types/domain";

const KIND_ICONS: Record<NotificationKind, typeof Bell> = {
  transcript_ready: FileText,
  summary_ready: Sparkles,
  meeting_reminder: CalendarClock,
  task_assigned: ListChecks,
  mention: AtSign,
  document_uploaded: FileText,
};

type Filter = "all" | "unread";

export function NotificationCenter() {
  const [open, setOpen] = useState(false);
  const [filter, setFilter] = useState<Filter>("all");
  const [items, setItems] = useState<AppNotification[]>([]);
  const [loading, setLoading] = useState(true);
  const { toast } = useToast();

  const load = useCallback(() => {
    setLoading(true);
    listNotifications()
      .then(setItems)
      .catch(() => setItems([]))
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  const unreadCount = items.filter((item) => !item.isRead).length;
  const visible = filter === "unread" ? items.filter((i) => !i.isRead) : items;

  async function handleMarkRead(notification: AppNotification) {
    if (notification.isRead) return;
    // Optimistic: the badge should drop the moment the row is opened.
    setItems((current) =>
      current.map((item) =>
        item.id === notification.id ? { ...item, isRead: true } : item,
      ),
    );
    await markNotificationRead(notification.id).catch(load);
  }

  async function handleMarkAllRead() {
    setItems((current) => current.map((item) => ({ ...item, isRead: true })));
    const count = await markAllNotificationsRead().catch(() => 0);
    toast({
      tone: "success",
      title: count > 0 ? `Marked ${count} as read` : "Nothing to mark",
    });
  }

  async function handleArchive(id: string) {
    setItems((current) => current.filter((item) => item.id !== id));
    await archiveNotification(id).catch(load);
    toast({ tone: "info", title: "Notification archived" });
  }

  async function handleDelete(id: string) {
    const removed = items.find((item) => item.id === id);
    setItems((current) => current.filter((item) => item.id !== id));
    await deleteNotification(id).catch(load);

    toast({
      tone: "info",
      title: "Notification deleted",
      action: removed ? { label: "Undo", onClick: load } : undefined,
    });
  }

  return (
    <Popover.Root open={open} onOpenChange={setOpen}>
      <Tooltip label="Notifications">
        <Popover.Trigger asChild>
          <Button
            variant="ghost"
            size="icon"
            className="relative"
            aria-label={
              unreadCount > 0
                ? `Notifications, ${unreadCount} unread`
                : "Notifications"
            }
          >
            <Bell />
            {unreadCount > 0 ? (
              <span className="absolute right-1 top-1 flex min-w-4 items-center justify-center rounded-full bg-danger px-1 text-[0.5625rem] font-semibold leading-4 text-white tabular">
                {unreadCount > 9 ? "9+" : unreadCount}
              </span>
            ) : null}
          </Button>
        </Popover.Trigger>
      </Tooltip>

      <Popover.Portal>
        <Popover.Content
          align="end"
          sideOffset={8}
          className="z-50 flex w-96 max-w-[calc(100vw-2rem)] flex-col overflow-hidden rounded-surface border border-border bg-surface shadow-md"
        >
          <div className="flex items-center justify-between border-b border-border px-3 py-2.5">
            <p className="text-subheading text-foreground">Notifications</p>
            <Button
              variant="ghost"
              size="sm"
              onClick={handleMarkAllRead}
              disabled={unreadCount === 0}
            >
              <CheckCheck />
              Mark all read
            </Button>
          </div>

          <div className="flex items-center gap-1 border-b border-border px-2 py-1.5">
            {(["all", "unread"] as const).map((value) => (
              <button
                key={value}
                type="button"
                onClick={() => setFilter(value)}
                className={cn(
                  "rounded-control px-2 py-1 text-caption font-medium capitalize transition-colors",
                  filter === value
                    ? "bg-surface-raised text-foreground"
                    : "text-muted hover:text-foreground",
                )}
              >
                {value}
                {value === "unread" && unreadCount > 0 ? (
                  <span className="ml-1 tabular">({unreadCount})</span>
                ) : null}
              </button>
            ))}
          </div>

          <div className="max-h-96 overflow-y-auto scrollbar-thin">
            {loading ? (
              <p className="px-3 py-8 text-center text-caption text-muted">
                Loading…
              </p>
            ) : visible.length === 0 ? (
              <EmptyState
                icon={Bell}
                title={
                  filter === "unread" ? "All caught up" : "No notifications"
                }
                description={
                  filter === "unread"
                    ? "You have read everything in your inbox."
                    : "Activity from your meetings will appear here."
                }
                className="py-10"
              />
            ) : (
              <ul className="divide-y divide-border">
                {visible.map((notification) => {
                  const Icon = KIND_ICONS[notification.kind];

                  return (
                    <li
                      key={notification.id}
                      className={cn(
                        "group relative flex gap-2.5 px-3 py-2.5 transition-colors hover:bg-surface-raised/60",
                        !notification.isRead && "bg-accent-subtle/25",
                      )}
                    >
                      <Icon
                        className="mt-0.5 size-4 shrink-0 text-subtle"
                        aria-hidden
                      />

                      <div className="min-w-0 flex-1">
                        {notification.href ? (
                          <Link
                            href={notification.href}
                            onClick={() => {
                              handleMarkRead(notification);
                              setOpen(false);
                            }}
                            className="block"
                          >
                            <p className="text-body font-medium text-foreground">
                              {notification.title}
                            </p>
                          </Link>
                        ) : (
                          <p className="text-body font-medium text-foreground">
                            {notification.title}
                          </p>
                        )}

                        <p className="mt-0.5 line-clamp-2 text-caption text-muted">
                          {notification.body}
                        </p>
                        <p className="mt-1 text-label text-subtle">
                          {formatRelative(notification.createdAt)}
                        </p>
                      </div>

                      {/* Row actions stay hidden until hover or keyboard focus. */}
                      <div className="flex shrink-0 items-start gap-0.5 opacity-0 transition-opacity focus-within:opacity-100 group-hover:opacity-100">
                        <Button
                          variant="ghost"
                          size="icon-sm"
                          aria-label="Archive notification"
                          onClick={() => handleArchive(notification.id)}
                        >
                          <Archive />
                        </Button>
                        <Button
                          variant="ghost"
                          size="icon-sm"
                          aria-label="Delete notification"
                          onClick={() => handleDelete(notification.id)}
                        >
                          <Trash2 />
                        </Button>
                      </div>
                    </li>
                  );
                })}
              </ul>
            )}
          </div>

          <div className="border-t border-border px-3 py-2">
            <Link
              href="/notifications"
              onClick={() => setOpen(false)}
              className="text-caption font-medium text-accent underline-offset-4 hover:underline"
            >
              View all notifications
            </Link>
          </div>
        </Popover.Content>
      </Popover.Portal>
    </Popover.Root>
  );
}
