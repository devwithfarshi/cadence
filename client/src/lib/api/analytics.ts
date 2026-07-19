/**
 * Analytics aggregations.
 *
 * Every series is computed from the same date-scoped slice, so the numbers on
 * one chart always agree with the numbers on another. Buckets are pre-filled
 * across the whole range — a week with no meetings must render as a zero, not
 * as a gap the line skips over.
 */

import {
  differenceInCalendarDays,
  eachDayOfInterval,
  eachWeekOfInterval,
  endOfDay,
  format,
  isWithinInterval,
  startOfDay,
  startOfWeek,
} from "date-fns";
import { collection } from "@/lib/db/storage";
import type {
  ActionItem,
  AISummary,
  DocumentFile,
  KnowledgeItem,
  Meeting,
  User,
} from "@/types/domain";
import { request } from "./client";

const meetings = collection<Meeting>("meetings");
const tasks = collection<ActionItem>("action_items");
const summaries = collection<AISummary>("summaries");
const documents = collection<DocumentFile>("documents");
const knowledge = collection<KnowledgeItem>("knowledge");
const users = collection<User>("users");

export type RangePreset = "7d" | "30d" | "90d" | "12m";

export const RANGE_LABELS: Record<RangePreset, string> = {
  "7d": "Last 7 days",
  "30d": "Last 30 days",
  "90d": "Last 90 days",
  "12m": "Last 12 months",
};

const RANGE_DAYS: Record<RangePreset, number> = {
  "7d": 7,
  "30d": 30,
  "90d": 90,
  "12m": 365,
};

export interface DateRange {
  from: Date;
  to: Date;
}

export function resolveRange(preset: RangePreset): DateRange {
  const to = endOfDay(new Date());
  const from = startOfDay(
    new Date(to.getTime() - (RANGE_DAYS[preset] - 1) * 24 * 60 * 60 * 1000),
  );
  return { from, to };
}

/**
 * Bucket width for a range.
 *
 * Daily points past ~45 days produce an unreadable comb, so longer ranges roll
 * up to weeks. The bucket is part of the answer, so it is returned alongside.
 */
function bucketingFor(range: DateRange): {
  granularity: "day" | "week";
  buckets: Date[];
} {
  const days = differenceInCalendarDays(range.to, range.from) + 1;

  if (days <= 45) {
    return {
      granularity: "day",
      buckets: eachDayOfInterval({ start: range.from, end: range.to }),
    };
  }
  return {
    granularity: "week",
    buckets: eachWeekOfInterval(
      { start: range.from, end: range.to },
      { weekStartsOn: 1 },
    ),
  };
}

function bucketKeyFor(date: Date, granularity: "day" | "week"): string {
  return granularity === "day"
    ? format(date, "yyyy-MM-dd")
    : format(startOfWeek(date, { weekStartsOn: 1 }), "yyyy-MM-dd");
}

function inRange(iso: string, range: DateRange): boolean {
  const date = new Date(iso);
  return isWithinInterval(date, { start: range.from, end: range.to });
}

/* -------------------------------------------------------------------------- */
/* Shapes                                                                     */
/* -------------------------------------------------------------------------- */

export interface TimePoint {
  /** ISO bucket start, used as the series key. */
  bucket: string;
  /**
   * Short axis label. Deliberately terse — narrow small-multiple cards cannot
   * fit a longer string without the ticks colliding.
   */
  label: string;
  /** Unambiguous label for the tooltip and the table view. */
  fullLabel: string;
  meetings: number;
  hours: number;
  summaries: number;
  tasksCreated: number;
  tasksCompleted: number;
}

export interface WeekdayPoint {
  weekday: string;
  meetings: number;
}

export interface MemberPoint {
  userId: string;
  name: string;
  meetings: number;
  tasksCompleted: number;
  tasksOpen: number;
}

export interface SpeakerPoint {
  name: string;
  /** Minutes of speaking time across the range. */
  minutes: number;
  share: number;
}

export interface AnalyticsSummary {
  meetings: number;
  hours: number;
  summaries: number;
  tasksCreated: number;
  tasksCompleted: number;
  documents: number;
  knowledgeItems: number;
  activeMembers: number;
  /** Completion rate 0–100 across the range. */
  completionRate: number;
  /** Average meeting length in minutes; 0 when nothing was recorded. */
  averageDurationMinutes: number;
}

export interface AnalyticsResult {
  range: { from: string; to: string };
  granularity: "day" | "week";
  summary: AnalyticsSummary;
  overTime: TimePoint[];
  byWeekday: WeekdayPoint[];
  byMember: MemberPoint[];
  speakerDistribution: SpeakerPoint[];
}

const WEEKDAY_ORDER = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

/* -------------------------------------------------------------------------- */
/* Aggregation                                                                */
/* -------------------------------------------------------------------------- */

export function getAnalytics(preset: RangePreset): Promise<AnalyticsResult> {
  return request(() => {
    const range = resolveRange(preset);
    const { granularity, buckets } = bucketingFor(range);

    const scopedMeetings = meetings
      .all()
      .filter((m) => !m.isArchived && inRange(m.startsAt, range));
    const allTasks = tasks.all();
    const scopedSummaries = summaries
      .all()
      .filter((s) => inRange(s.generatedAt, range));

    /* --- Time series ---------------------------------------------------- */

    // Pre-fill every bucket so quiet periods render as zero rather than a gap.
    const series = new Map<string, TimePoint>();
    for (const bucket of buckets) {
      const key = bucketKeyFor(bucket, granularity);
      series.set(key, {
        bucket: key,
        label: format(bucket, "d MMM"),
        fullLabel:
          granularity === "day"
            ? format(bucket, "d MMM yyyy")
            : `Week of ${format(bucket, "d MMM yyyy")}`,
        meetings: 0,
        hours: 0,
        summaries: 0,
        tasksCreated: 0,
        tasksCompleted: 0,
      });
    }

    const bump = (iso: string, apply: (point: TimePoint) => void) => {
      const point = series.get(bucketKeyFor(new Date(iso), granularity));
      if (point) apply(point);
    };

    for (const meeting of scopedMeetings) {
      bump(meeting.startsAt, (point) => {
        point.meetings += 1;
        point.hours += meeting.durationSeconds / 3600;
      });
    }
    for (const summary of scopedSummaries) {
      bump(summary.generatedAt, (point) => {
        point.summaries += 1;
      });
    }
    for (const task of allTasks) {
      if (inRange(task.createdAt, range)) {
        bump(task.createdAt, (point) => {
          point.tasksCreated += 1;
        });
      }
      if (task.completedAt && inRange(task.completedAt, range)) {
        bump(task.completedAt, (point) => {
          point.tasksCompleted += 1;
        });
      }
    }

    const overTime = [...series.values()].map((point) => ({
      ...point,
      hours: Number(point.hours.toFixed(2)),
    }));

    /* --- Weekday distribution ------------------------------------------- */

    const weekdayCounts = new Map(WEEKDAY_ORDER.map((day) => [day, 0]));
    for (const meeting of scopedMeetings) {
      const day = format(new Date(meeting.startsAt), "EEE");
      weekdayCounts.set(day, (weekdayCounts.get(day) ?? 0) + 1);
    }
    const byWeekday: WeekdayPoint[] = WEEKDAY_ORDER.map((weekday) => ({
      weekday,
      meetings: weekdayCounts.get(weekday) ?? 0,
    }));

    /* --- Per-member ------------------------------------------------------ */

    const activeUsers = users.all().filter((user) => user.status === "active");
    const byMember: MemberPoint[] = activeUsers
      .map((user) => ({
        userId: user.id,
        name: user.name,
        meetings: scopedMeetings.filter((meeting) =>
          meeting.participants.some((p) => p.userId === user.id),
        ).length,
        tasksCompleted: allTasks.filter(
          (task) =>
            task.assigneeId === user.id &&
            task.completedAt !== null &&
            inRange(task.completedAt, range),
        ).length,
        tasksOpen: allTasks.filter(
          (task) => task.assigneeId === user.id && task.status !== "done",
        ).length,
      }))
      .sort((a, b) => b.meetings - a.meetings);

    /* --- Speaker distribution -------------------------------------------- */

    // Talk-time ratios are per-meeting, so weight each by that meeting's length
    // before summing — otherwise a 5-minute standup counts as much as an hour.
    const spoken = new Map<string, number>();
    for (const meeting of scopedMeetings) {
      if (meeting.durationSeconds <= 0) continue;
      for (const participant of meeting.participants) {
        spoken.set(
          participant.name,
          (spoken.get(participant.name) ?? 0) +
            participant.talkTimeRatio * meeting.durationSeconds,
        );
      }
    }

    const totalSpoken =
      [...spoken.values()].reduce((sum, value) => sum + value, 0) || 1;
    const ranked = [...spoken.entries()].sort((a, b) => b[1] - a[1]);

    // Past six slots the palette runs out, so the tail folds into "Other"
    // rather than generating a seventh hue.
    const MAX_SLICES = 5;
    const head = ranked.slice(0, MAX_SLICES);
    const tail = ranked.slice(MAX_SLICES);
    const tailSeconds = tail.reduce((sum, [, value]) => sum + value, 0);

    const speakerDistribution: SpeakerPoint[] = [
      ...head.map(([name, seconds]) => ({
        name,
        minutes: Number((seconds / 60).toFixed(1)),
        share: Number(((seconds / totalSpoken) * 100).toFixed(1)),
      })),
      ...(tailSeconds > 0
        ? [
            {
              name: `Other (${tail.length})`,
              minutes: Number((tailSeconds / 60).toFixed(1)),
              share: Number(((tailSeconds / totalSpoken) * 100).toFixed(1)),
            },
          ]
        : []),
    ];

    /* --- Summary --------------------------------------------------------- */

    const totalSeconds = scopedMeetings.reduce(
      (sum, meeting) => sum + meeting.durationSeconds,
      0,
    );
    const recorded = scopedMeetings.filter((m) => m.durationSeconds > 0);

    const createdInRange = allTasks.filter((task) =>
      inRange(task.createdAt, range),
    ).length;
    const completedInRange = allTasks.filter(
      (task) => task.completedAt !== null && inRange(task.completedAt, range),
    ).length;

    const summary: AnalyticsSummary = {
      meetings: scopedMeetings.length,
      hours: Number((totalSeconds / 3600).toFixed(1)),
      summaries: scopedSummaries.length,
      tasksCreated: createdInRange,
      tasksCompleted: completedInRange,
      documents: documents.all().filter((d) => inRange(d.createdAt, range))
        .length,
      knowledgeItems: knowledge.all().filter((k) => inRange(k.createdAt, range))
        .length,
      activeMembers: activeUsers.length,
      completionRate:
        createdInRange === 0
          ? 0
          : Math.round((completedInRange / createdInRange) * 100),
      averageDurationMinutes:
        recorded.length === 0
          ? 0
          : Math.round(totalSeconds / recorded.length / 60),
    };

    return {
      range: { from: range.from.toISOString(), to: range.to.toISOString() },
      granularity,
      summary,
      overTime,
      byWeekday,
      byMember,
      speakerDistribution,
    };
  });
}
