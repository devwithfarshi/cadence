/**
 * Workspace-level services: users, notifications, activity, preferences and the
 * aggregated dashboard statistics.
 */

import { DEFAULT_PREFERENCES } from "@/lib/db/seed";
import { collection, read, write } from "@/lib/db/storage";
import type {
  ActionItem,
  ActivityLog,
  AISummary,
  AppNotification,
  DocumentFile,
  KnowledgeItem,
  Meeting,
  NotificationKind,
  Preferences,
  User,
} from "@/types/domain";
import { ApiError, generateId, now, request } from "./client";

const users = collection<User>("users");
const notifications = collection<AppNotification>("notifications");
const activity = collection<ActivityLog>("activity");
const meetings = collection<Meeting>("meetings");
const tasks = collection<ActionItem>("action_items");
const documents = collection<DocumentFile>("documents");
const knowledge = collection<KnowledgeItem>("knowledge");
const summaries = collection<AISummary>("summaries");

/* -------------------------------------------------------------------------- */
/* Users                                                                      */
/* -------------------------------------------------------------------------- */

export function listUsers(): Promise<User[]> {
  return request(() => users.all());
}

export function getUser(id: string): Promise<User> {
  return request(() => {
    const user = users.find(id);
    if (!user) throw new ApiError("User not found", 404);
    return user;
  });
}

/* -------------------------------------------------------------------------- */
/* Notifications                                                              */
/* -------------------------------------------------------------------------- */

export interface NotificationQuery {
  kind?: NotificationKind[];
  unreadOnly?: boolean;
  includeArchived?: boolean;
}

export function listNotifications(
  query: NotificationQuery = {},
): Promise<AppNotification[]> {
  return request(() =>
    notifications
      .all()
      .filter((notification) => {
        if (!query.includeArchived && notification.isArchived) return false;
        if (query.unreadOnly && notification.isRead) return false;
        if (query.kind?.length && !query.kind.includes(notification.kind)) {
          return false;
        }
        return true;
      })
      .sort(
        (a, b) =>
          new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime(),
      ),
  );
}

export function getUnreadCount(): Promise<number> {
  return request(
    () => notifications.all().filter((n) => !n.isRead && !n.isArchived).length,
  );
}

export function markNotificationRead(
  id: string,
  isRead = true,
): Promise<AppNotification> {
  return request(() => {
    const updated = notifications.update(id, { isRead, updatedAt: now() });
    if (!updated) throw new ApiError("Notification not found", 404);
    return updated;
  });
}

export function markAllNotificationsRead(): Promise<number> {
  return request(() => {
    const all = notifications.all();
    const unread = all.filter((n) => !n.isRead && !n.isArchived);

    notifications.replaceAll(
      all.map((n) => (n.isArchived ? n : { ...n, isRead: true })),
    );
    return unread.length;
  });
}

export function archiveNotification(
  id: string,
  isArchived = true,
): Promise<AppNotification> {
  return request(() => {
    const updated = notifications.update(id, { isArchived, updatedAt: now() });
    if (!updated) throw new ApiError("Notification not found", 404);
    return updated;
  });
}

export function deleteNotification(id: string): Promise<void> {
  return request(() => {
    notifications.remove(id);
  });
}

/** Used by the live-meeting simulation to push new notifications in real time. */
export function pushNotification(input: {
  kind: NotificationKind;
  title: string;
  body: string;
  href?: string | null;
  actorId?: string | null;
}): Promise<AppNotification> {
  return request(() => {
    const timestamp = now();
    return notifications.insert({
      id: generateId("ntf"),
      kind: input.kind,
      title: input.title,
      body: input.body,
      isRead: false,
      isArchived: false,
      href: input.href ?? null,
      actorId: input.actorId ?? null,
      createdAt: timestamp,
      updatedAt: timestamp,
    });
  });
}

/* -------------------------------------------------------------------------- */
/* Activity                                                                   */
/* -------------------------------------------------------------------------- */

export function listActivity(limit = 20): Promise<ActivityLog[]> {
  return request(() =>
    activity
      .all()
      .sort(
        (a, b) =>
          new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime(),
      )
      .slice(0, limit),
  );
}

export function logActivity(input: {
  kind: ActivityLog["kind"];
  actorId: string;
  summary: string;
  targetId?: string | null;
  href?: string | null;
}): Promise<ActivityLog> {
  return request(() => {
    const timestamp = now();
    return activity.insert({
      id: generateId("evt"),
      kind: input.kind,
      actorId: input.actorId,
      summary: input.summary,
      targetId: input.targetId ?? null,
      href: input.href ?? null,
      createdAt: timestamp,
      updatedAt: timestamp,
    });
  });
}

/* -------------------------------------------------------------------------- */
/* Preferences                                                                */
/* -------------------------------------------------------------------------- */

/** Synchronous read — the theme and sidebar state are needed before first paint. */
export function getStoredPreferences(): Preferences {
  return {
    ...DEFAULT_PREFERENCES,
    ...read<Partial<Preferences>>("preferences", {}),
  };
}

export function savePreferences(patch: Partial<Preferences>): Preferences {
  const next = { ...getStoredPreferences(), ...patch };
  write("preferences", next);
  return next;
}

const MAX_RECENT = 8;

/** Records a meeting visit, most-recent-first, without duplicates. */
export function recordRecentMeeting(meetingId: string): void {
  const current = getStoredPreferences().recentMeetingIds.filter(
    (id) => id !== meetingId,
  );
  savePreferences({
    recentMeetingIds: [meetingId, ...current].slice(0, MAX_RECENT),
  });
}

export function recordRecentSearch(term: string): void {
  const trimmed = term.trim();
  if (!trimmed) return;

  const current = getStoredPreferences().recentSearches.filter(
    (s) => s.toLowerCase() !== trimmed.toLowerCase(),
  );
  savePreferences({
    recentSearches: [trimmed, ...current].slice(0, MAX_RECENT),
  });
}

/* -------------------------------------------------------------------------- */
/* Dashboard statistics                                                       */
/* -------------------------------------------------------------------------- */

export interface DashboardStats {
  totalMeetings: number;
  hoursRecorded: number;
  aiSummaries: number;
  openTasks: number;
  completedTasks: number;
  documents: number;
  knowledgeItems: number;
  /**
   * Percentage change in meeting count, this 30 days vs the previous 30.
   *
   * Null when the previous period is too small to produce a meaningful ratio —
   * going from 1 meeting to 11 is technically "+1000%", but reporting that is
   * noise rather than insight.
   */
  meetingTrendPct: number | null;
  overdueTasks: number;
  teamMembers: number;
}

export function getDashboardStats(): Promise<DashboardStats> {
  return request(() => {
    const allMeetings = meetings.all().filter((m) => !m.isArchived);
    const allTasks = tasks.all();

    const nowMs = Date.now();
    const THIRTY_DAYS = 30 * 24 * 60 * 60 * 1000;

    const inWindow = (meeting: Meeting, startAgo: number, endAgo: number) => {
      const time = new Date(meeting.startsAt).getTime();
      return time >= nowMs - startAgo && time < nowMs - endAgo;
    };

    const recent = allMeetings.filter((m) =>
      inWindow(m, THIRTY_DAYS, 0),
    ).length;
    const previous = allMeetings.filter((m) =>
      inWindow(m, 2 * THIRTY_DAYS, THIRTY_DAYS),
    ).length;

    // A baseline this small makes the ratio meaningless, so report no trend
    // rather than a dramatic and misleading percentage.
    const MIN_BASELINE = 3;
    const meetingTrendPct =
      previous < MIN_BASELINE
        ? null
        : Math.round(((recent - previous) / previous) * 100);

    const totalSeconds = allMeetings.reduce(
      (sum, m) => sum + m.durationSeconds,
      0,
    );

    return {
      totalMeetings: allMeetings.length,
      hoursRecorded: Number((totalSeconds / 3600).toFixed(1)),
      aiSummaries: summaries.all().length,
      openTasks: allTasks.filter((t) => t.status !== "done").length,
      completedTasks: allTasks.filter((t) => t.status === "done").length,
      documents: documents.all().length,
      knowledgeItems: knowledge.all().length,
      meetingTrendPct,
      overdueTasks: allTasks.filter(
        (t) =>
          t.status !== "done" &&
          t.dueDate !== null &&
          new Date(t.dueDate).getTime() < nowMs,
      ).length,
      teamMembers: users.all().filter((u) => u.status === "active").length,
    };
  });
}

/* -------------------------------------------------------------------------- */
/* Global search                                                              */
/* -------------------------------------------------------------------------- */

export interface SearchResult {
  id: string;
  title: string;
  subtitle: string;
  kind: "meeting" | "task" | "document" | "knowledge" | "person";
  href: string;
}

/** Cross-entity search backing the top bar and the command palette. */
export function globalSearch(term: string, limit = 8): Promise<SearchResult[]> {
  return request(() => {
    const needle = term.trim().toLowerCase();
    if (!needle) return [];

    const results: SearchResult[] = [];

    for (const meeting of meetings.all()) {
      if (meeting.title.toLowerCase().includes(needle)) {
        results.push({
          id: meeting.id,
          title: meeting.title,
          subtitle: new Date(meeting.startsAt).toLocaleDateString(undefined, {
            month: "short",
            day: "numeric",
            year: "numeric",
          }),
          kind: "meeting",
          href: `/meetings/${meeting.id}`,
        });
      }
    }

    for (const task of tasks.all()) {
      if (task.title.toLowerCase().includes(needle)) {
        results.push({
          id: task.id,
          title: task.title,
          subtitle: task.status.replace("_", " "),
          kind: "task",
          href: "/tasks",
        });
      }
    }

    for (const doc of documents.all()) {
      if (doc.name.toLowerCase().includes(needle)) {
        results.push({
          id: doc.id,
          title: doc.name,
          subtitle: doc.type.toUpperCase(),
          kind: "document",
          href: "/documents",
        });
      }
    }

    for (const item of knowledge.all()) {
      if (item.title.toLowerCase().includes(needle)) {
        results.push({
          id: item.id,
          title: item.title,
          subtitle: item.category,
          kind: "knowledge",
          href: "/knowledge",
        });
      }
    }

    for (const user of users.all()) {
      // Match the email's local part rather than the whole address: everyone
      // shares the company domain, so matching that would return the entire
      // directory whenever someone searches the company name.
      const emailLocalPart = user.email.toLowerCase().split("@")[0];
      const matchesEmail = needle.includes("@")
        ? user.email.toLowerCase().includes(needle)
        : emailLocalPart.includes(needle);

      if (user.name.toLowerCase().includes(needle) || matchesEmail) {
        results.push({
          id: user.id,
          title: user.name,
          subtitle: user.jobTitle,
          kind: "person",
          href: "/team",
        });
      }
    }

    return results.slice(0, limit);
  });
}
