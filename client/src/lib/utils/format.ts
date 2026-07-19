/**
 * Display formatting.
 *
 * Centralised so a date or duration looks identical everywhere it appears —
 * inconsistent formatting is one of the fastest ways for an enterprise UI to
 * feel unfinished.
 */

import {
  differenceInCalendarDays,
  format,
  formatDistanceToNowStrict,
  isThisYear,
  isToday,
  isTomorrow,
  isYesterday,
} from "date-fns";

function toDate(value: string | Date): Date {
  return typeof value === "string" ? new Date(value) : value;
}

/** "14:30" — 24-hour, used in dense lists and calendar grids. */
export function formatTime(value: string | Date): string {
  return format(toDate(value), "HH:mm");
}

/** "12 Mar 2026", dropping the year when it is the current one. */
export function formatDate(value: string | Date): string {
  const date = toDate(value);
  return format(date, isThisYear(date) ? "d MMM" : "d MMM yyyy");
}

/** "Today", "Tomorrow", "Yesterday", otherwise a short date. */
export function formatDateLabel(value: string | Date): string {
  const date = toDate(value);
  if (isToday(date)) return "Today";
  if (isTomorrow(date)) return "Tomorrow";
  if (isYesterday(date)) return "Yesterday";
  return formatDate(date);
}

/** "Today · 14:30" — the header line on meeting cards. */
export function formatDateTime(value: string | Date): string {
  return `${formatDateLabel(value)} · ${formatTime(value)}`;
}

/** "3 hours ago", "2 days ago" — for activity feeds and notifications. */
export function formatRelative(value: string | Date): string {
  const date = toDate(value);
  const seconds = Math.abs(Date.now() - date.getTime()) / 1000;

  // Anything inside a minute reads better as "just now" than "43 seconds ago".
  if (seconds < 45) return "just now";

  return `${formatDistanceToNowStrict(date)}${
    date.getTime() > Date.now() ? " from now" : " ago"
  }`;
}

/**
 * Compact duration from seconds: "45m", "1h 12m", "2h".
 * Used for meeting length, where precision below a minute is noise.
 */
export function formatDuration(totalSeconds: number): string {
  if (totalSeconds <= 0) return "—";

  const minutes = Math.round(totalSeconds / 60);
  if (minutes < 60) return `${minutes}m`;

  const hours = Math.floor(minutes / 60);
  const remainder = minutes % 60;
  return remainder === 0 ? `${hours}h` : `${hours}h ${remainder}m`;
}

/**
 * Clock-style elapsed time: "07:42", "1:03:18".
 * Used for the live meeting timer and transcript offsets, where every second
 * matters and the value must not jump width as it counts.
 */
export function formatTimecode(totalSeconds: number): string {
  const safe = Math.max(0, Math.floor(totalSeconds));
  const hours = Math.floor(safe / 3600);
  const minutes = Math.floor((safe % 3600) / 60);
  const seconds = safe % 60;

  const mm = String(minutes).padStart(2, "0");
  const ss = String(seconds).padStart(2, "0");

  return hours > 0 ? `${hours}:${mm}:${ss}` : `${mm}:${ss}`;
}

/** "2.4 MB" — binary units, one decimal place above kilobytes. */
export function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;

  const units = ["KB", "MB", "GB", "TB"];
  let value = bytes / 1024;
  let unitIndex = 0;

  while (value >= 1024 && unitIndex < units.length - 1) {
    value /= 1024;
    unitIndex += 1;
  }

  return `${value.toFixed(value >= 10 ? 0 : 1)} ${units[unitIndex]}`;
}

/** "1,284" — thousands separated, locale-aware. */
export function formatNumber(value: number): string {
  return new Intl.NumberFormat().format(value);
}

/** "+12%" / "−4%" — signed, for trend indicators. */
export function formatTrend(percent: number): string {
  if (percent === 0) return "0%";
  return `${percent > 0 ? "+" : "−"}${Math.abs(percent)}%`;
}

/**
 * Due-date phrasing that makes lateness obvious: "Overdue by 2 days",
 * "Due today", "Due in 5 days".
 */
export function formatDueDate(value: string | null): {
  label: string;
  overdue: boolean;
} {
  if (!value) return { label: "No due date", overdue: false };

  const date = toDate(value);
  const days = differenceInCalendarDays(date, new Date());

  if (days < 0) {
    const magnitude = Math.abs(days);
    return {
      label: `Overdue by ${magnitude} ${magnitude === 1 ? "day" : "days"}`,
      overdue: true,
    };
  }
  if (days === 0) return { label: "Due today", overdue: false };
  if (days === 1) return { label: "Due tomorrow", overdue: false };
  return { label: `Due in ${days} days`, overdue: false };
}

/** Turns an enum-ish value into a readable label: "in_progress" → "In progress". */
export function humanize(value: string): string {
  const spaced = value.replace(/_/g, " ");
  return spaced.charAt(0).toUpperCase() + spaced.slice(1);
}

/** Possessive-free greeting appropriate to the local hour. */
export function greetingFor(date = new Date()): string {
  const hour = date.getHours();
  if (hour < 12) return "Good morning";
  if (hour < 18) return "Good afternoon";
  return "Good evening";
}
