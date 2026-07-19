import { Badge, Dot } from "@/components/ui/badge";
import { cn } from "@/lib/utils/cn";
import type {
  MeetingPlatform,
  MeetingStatus,
  RecordingStatus,
  SummaryStatus,
  TaskPriority,
  TaskStatus,
} from "@/types/domain";

/* -------------------------------------------------------------------------- */
/* Meeting status                                                             */
/* -------------------------------------------------------------------------- */

const MEETING_STATUS = {
  scheduled: { label: "Scheduled", tone: "info" },
  live: { label: "Live", tone: "danger" },
  processing: { label: "Processing", tone: "warning" },
  completed: { label: "Completed", tone: "success" },
  cancelled: { label: "Cancelled", tone: "neutral" },
} as const satisfies Record<
  MeetingStatus,
  { label: string; tone: "info" | "danger" | "warning" | "success" | "neutral" }
>;

export function MeetingStatusBadge({ status }: { status: MeetingStatus }) {
  const config = MEETING_STATUS[status];

  return (
    <Badge tone={config.tone}>
      <Dot className={cn(status === "live" && "anim-pulse-live")} />
      {config.label}
    </Badge>
  );
}

/* -------------------------------------------------------------------------- */
/* Recording status                                                           */
/* -------------------------------------------------------------------------- */

const RECORDING_STATUS = {
  not_recorded: { label: "No recording", tone: "neutral" },
  recording: { label: "Recording", tone: "danger" },
  paused: { label: "Paused", tone: "warning" },
  recorded: { label: "Recorded", tone: "neutral" },
  failed: { label: "Recording failed", tone: "danger" },
} as const satisfies Record<
  RecordingStatus,
  { label: string; tone: "neutral" | "danger" | "warning" }
>;

export function RecordingBadge({ status }: { status: RecordingStatus }) {
  const config = RECORDING_STATUS[status];
  return <Badge tone={config.tone}>{config.label}</Badge>;
}

/* -------------------------------------------------------------------------- */
/* AI summary status                                                          */
/* -------------------------------------------------------------------------- */

const SUMMARY_STATUS = {
  none: { label: "No summary", tone: "neutral" },
  queued: { label: "Summary queued", tone: "info" },
  generating: { label: "Summarising", tone: "info" },
  ready: { label: "Summary ready", tone: "accent" },
  failed: { label: "Summary failed", tone: "danger" },
} as const satisfies Record<
  SummaryStatus,
  { label: string; tone: "neutral" | "info" | "accent" | "danger" }
>;

export function SummaryBadge({ status }: { status: SummaryStatus }) {
  const config = SUMMARY_STATUS[status];
  return <Badge tone={config.tone}>{config.label}</Badge>;
}

/* -------------------------------------------------------------------------- */
/* Platform                                                                   */
/* -------------------------------------------------------------------------- */

export const PLATFORM_LABELS = {
  zoom: "Zoom",
  google_meet: "Google Meet",
  teams: "Microsoft Teams",
  in_person: "In person",
} as const satisfies Record<MeetingPlatform, string>;

export function PlatformLabel({ platform }: { platform: MeetingPlatform }) {
  return (
    <span className="text-caption text-muted">{PLATFORM_LABELS[platform]}</span>
  );
}

/* -------------------------------------------------------------------------- */
/* Tasks                                                                      */
/* -------------------------------------------------------------------------- */

const TASK_PRIORITY = {
  low: { label: "Low", tone: "neutral" },
  medium: { label: "Medium", tone: "info" },
  high: { label: "High", tone: "warning" },
  urgent: { label: "Urgent", tone: "danger" },
} as const satisfies Record<
  TaskPriority,
  { label: string; tone: "neutral" | "info" | "warning" | "danger" }
>;

export function PriorityBadge({ priority }: { priority: TaskPriority }) {
  const config = TASK_PRIORITY[priority];
  return (
    <Badge tone={config.tone} size="sm">
      {config.label}
    </Badge>
  );
}

const TASK_STATUS = {
  todo: { label: "To do", tone: "neutral" },
  in_progress: { label: "In progress", tone: "info" },
  blocked: { label: "Blocked", tone: "danger" },
  done: { label: "Done", tone: "success" },
} as const satisfies Record<
  TaskStatus,
  { label: string; tone: "neutral" | "info" | "danger" | "success" }
>;

export function TaskStatusBadge({ status }: { status: TaskStatus }) {
  const config = TASK_STATUS[status];
  return <Badge tone={config.tone}>{config.label}</Badge>;
}

export const TASK_STATUS_LABELS = TASK_STATUS;
export const TASK_PRIORITY_LABELS = TASK_PRIORITY;
export const MEETING_STATUS_LABELS = MEETING_STATUS;
export const SUMMARY_STATUS_LABELS = SUMMARY_STATUS;
