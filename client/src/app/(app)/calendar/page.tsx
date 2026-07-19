"use client";

import {
  addDays,
  addMonths,
  eachDayOfInterval,
  endOfDay,
  endOfMonth,
  endOfWeek,
  format,
  isSameDay,
  isSameMonth,
  isToday,
  startOfDay,
  startOfMonth,
  startOfWeek,
  subDays,
  subMonths,
} from "date-fns";
import {
  CalendarDays,
  ChevronLeft,
  ChevronRight,
  ExternalLink,
  Plus,
  Radio,
  Users,
  X,
} from "lucide-react";
import Link from "next/link";
import { useCallback, useMemo, useState } from "react";
import { NewMeetingDialog } from "@/components/meetings/new-meeting-dialog";
import {
  MeetingStatusBadge,
  PLATFORM_LABELS,
  SummaryBadge,
} from "@/components/meetings/status";
import { usePreferences } from "@/components/providers/preferences-provider";
import { PageContainer, PageHeader } from "@/components/shell/page-header";
import { Avatar } from "@/components/ui/avatar";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { ErrorState, Skeleton } from "@/components/ui/feedback";
import { FilterMenu } from "@/components/ui/filter-menu";
import { SearchInput } from "@/components/ui/input";
import { Sheet, SheetContent } from "@/components/ui/sheet";
import { queryMeetings } from "@/lib/api/meetings";
import { useAsync, useDebounced } from "@/lib/hooks/use-async";
import { cn } from "@/lib/utils/cn";
import {
  formatDate,
  formatDateTime,
  formatDuration,
  formatTime,
} from "@/lib/utils/format";
import type {
  CalendarView,
  Meeting,
  MeetingPlatform,
  MeetingStatus,
} from "@/types/domain";

const STATUS_OPTIONS: { value: MeetingStatus; label: string }[] = [
  { value: "scheduled", label: "Scheduled" },
  { value: "live", label: "Live" },
  { value: "completed", label: "Completed" },
  { value: "cancelled", label: "Cancelled" },
];

const PLATFORM_OPTIONS = Object.entries(PLATFORM_LABELS).map(
  ([value, label]) => ({ value: value as MeetingPlatform, label }),
);

/** Hours rendered in the week and day grids. */
const DAY_START_HOUR = 7;
const DAY_END_HOUR = 21;
const HOUR_HEIGHT = 48;

export default function CalendarPage() {
  const { preferences, update } = usePreferences();
  const view: CalendarView = preferences.calendarView;

  const [anchor, setAnchor] = useState(() => new Date());
  const [search, setSearch] = useState("");
  const debouncedSearch = useDebounced(search, 250);
  const [statuses, setStatuses] = useState<MeetingStatus[]>([]);
  const [platforms, setPlatforms] = useState<MeetingPlatform[]>([]);
  const [selected, setSelected] = useState<Meeting | null>(null);
  const [createOpen, setCreateOpen] = useState(false);

  /** The window the current view needs, padded to whole weeks for the month grid. */
  const range = useMemo(() => {
    if (view === "month") {
      return {
        from: startOfWeek(startOfMonth(anchor), { weekStartsOn: 1 }),
        to: endOfWeek(endOfMonth(anchor), { weekStartsOn: 1 }),
      };
    }
    if (view === "week") {
      return {
        from: startOfWeek(anchor, { weekStartsOn: 1 }),
        to: endOfWeek(anchor, { weekStartsOn: 1 }),
      };
    }
    return { from: startOfDay(anchor), to: endOfDay(anchor) };
  }, [view, anchor]);

  const meetings = useAsync(
    () =>
      queryMeetings({
        search: debouncedSearch,
        status: statuses,
        platform: platforms,
        from: range.from.toISOString(),
        to: range.to.toISOString(),
        sortBy: "startsAt",
        sortDir: "asc",
      }),
    [
      debouncedSearch,
      statuses,
      platforms,
      range.from.getTime(),
      range.to.getTime(),
    ],
  );

  const items = meetings.data ?? [];

  // Upcoming ignores the visible range — it's a standing "what's next" rail.
  const upcoming = useAsync(
    () =>
      queryMeetings({
        status: ["scheduled"],
        from: new Date().toISOString(),
        sortBy: "startsAt",
        sortDir: "asc",
      }).then((list) => list.slice(0, 5)),
    [],
  );

  const meetingsOn = useCallback(
    (day: Date) =>
      items.filter((meeting) => isSameDay(new Date(meeting.startsAt), day)),
    [items],
  );

  function step(direction: 1 | -1) {
    setAnchor((current) =>
      view === "month"
        ? direction === 1
          ? addMonths(current, 1)
          : subMonths(current, 1)
        : view === "week"
          ? addDays(current, direction * 7)
          : direction === 1
            ? addDays(current, 1)
            : subDays(current, 1),
    );
  }

  const heading =
    view === "month"
      ? format(anchor, "MMMM yyyy")
      : view === "week"
        ? `${format(range.from, "d MMM")} – ${format(range.to, "d MMM yyyy")}`
        : format(anchor, "EEEE d MMMM yyyy");

  const activeFilters = statuses.length + platforms.length;

  return (
    <PageContainer>
      <PageHeader
        title="Calendar"
        description="Everything scheduled and recorded, laid out in time."
        actions={
          <Button
            variant="primary"
            size="md"
            onClick={() => setCreateOpen(true)}
          >
            <Plus />
            New meeting
          </Button>
        }
      />

      {/* Controls */}
      <div className="mb-3 flex flex-wrap items-center gap-2">
        <div className="flex items-center gap-1">
          <Button
            variant="ghost"
            size="icon-sm"
            aria-label="Previous"
            onClick={() => step(-1)}
          >
            <ChevronLeft />
          </Button>
          <Button
            variant="secondary"
            size="sm"
            onClick={() => setAnchor(new Date())}
          >
            Today
          </Button>
          <Button
            variant="ghost"
            size="icon-sm"
            aria-label="Next"
            onClick={() => step(1)}
          >
            <ChevronRight />
          </Button>
        </div>

        <h2 className="text-subheading text-foreground">{heading}</h2>

        <SearchInput
          value={search}
          onValueChange={setSearch}
          placeholder="Search meetings…"
          className="w-full sm:w-56"
        />
        <FilterMenu
          label="Status"
          options={STATUS_OPTIONS}
          selected={statuses}
          onChange={setStatuses}
        />
        <FilterMenu
          label="Platform"
          options={PLATFORM_OPTIONS}
          selected={platforms}
          onChange={setPlatforms}
          icon={false}
        />
        {activeFilters > 0 || search ? (
          <Button
            variant="ghost"
            size="sm"
            onClick={() => {
              setStatuses([]);
              setPlatforms([]);
              setSearch("");
            }}
          >
            <X />
            Clear
          </Button>
        ) : null}

        <div className="ml-auto flex items-center gap-0.5 rounded-control border border-border p-0.5">
          {(["month", "week", "day"] as const).map((mode) => (
            <button
              key={mode}
              type="button"
              onClick={() => update({ calendarView: mode })}
              aria-pressed={view === mode}
              className={cn(
                "rounded-[4px] px-2.5 py-1 text-caption font-medium capitalize transition-colors",
                view === mode
                  ? "bg-surface-raised text-foreground"
                  : "text-muted hover:text-foreground",
              )}
            >
              {mode}
            </button>
          ))}
        </div>
      </div>

      <div className="grid gap-4 xl:grid-cols-[1fr_17rem]">
        <div className="min-w-0">
          {meetings.error ? (
            <ErrorState
              description={meetings.error.message}
              onRetry={meetings.refetch}
            />
          ) : meetings.loading && !meetings.data ? (
            <Skeleton className="h-[32rem] w-full" />
          ) : view === "month" ? (
            <MonthGrid
              anchor={anchor}
              range={range}
              meetingsOn={meetingsOn}
              onSelect={setSelected}
              onPickDay={(day) => {
                setAnchor(day);
                update({ calendarView: "day" });
              }}
            />
          ) : (
            <TimeGrid
              days={eachDayOfInterval({ start: range.from, end: range.to })}
              meetingsOn={meetingsOn}
              onSelect={setSelected}
            />
          )}
        </div>

        {/* Upcoming rail */}
        <aside className="rounded-surface border border-border bg-surface">
          <header className="border-b border-border px-3 py-2.5">
            <h2 className="text-caption font-semibold text-foreground">
              Upcoming
            </h2>
          </header>

          {upcoming.loading ? (
            <div className="space-y-2 p-3">
              {[0, 1, 2].map((i) => (
                <Skeleton key={i} className="h-12 w-full" />
              ))}
            </div>
          ) : (upcoming.data ?? []).length === 0 ? (
            <p className="px-3 py-6 text-center text-caption text-subtle">
              Nothing scheduled ahead.
            </p>
          ) : (
            <ul className="divide-y divide-border">
              {(upcoming.data ?? []).map((meeting) => (
                <li key={meeting.id}>
                  <button
                    type="button"
                    onClick={() => setSelected(meeting)}
                    className="w-full px-3 py-2.5 text-left transition-colors hover:bg-surface-raised/60"
                  >
                    <p className="truncate text-caption font-medium text-foreground">
                      {meeting.title}
                    </p>
                    <p className="mt-0.5 text-label text-muted">
                      {formatDateTime(meeting.startsAt)}
                    </p>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </aside>
      </div>

      <MeetingDrawer
        meeting={selected}
        onOpenChange={(open) => {
          if (!open) setSelected(null);
        }}
      />

      <NewMeetingDialog
        open={createOpen}
        onOpenChange={setCreateOpen}
        onCreated={() => {
          meetings.refetch();
          upcoming.refetch();
        }}
      />
    </PageContainer>
  );
}

/* -------------------------------------------------------------------------- */
/* Month grid                                                                 */
/* -------------------------------------------------------------------------- */

const WEEKDAYS = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

function MonthGrid({
  anchor,
  range,
  meetingsOn,
  onSelect,
  onPickDay,
}: {
  anchor: Date;
  range: { from: Date; to: Date };
  meetingsOn: (day: Date) => Meeting[];
  onSelect: (meeting: Meeting) => void;
  onPickDay: (day: Date) => void;
}) {
  const days = eachDayOfInterval({ start: range.from, end: range.to });

  return (
    <div className="overflow-hidden rounded-surface border border-border bg-surface">
      <div className="grid grid-cols-7 border-b border-border bg-surface-sunken">
        {WEEKDAYS.map((day) => (
          <div
            key={day}
            className="px-2 py-1.5 text-center text-overline uppercase text-subtle"
          >
            {day}
          </div>
        ))}
      </div>

      <div className="grid grid-cols-7">
        {days.map((day) => {
          const dayMeetings = meetingsOn(day);
          const outside = !isSameMonth(day, anchor);

          return (
            <div
              key={day.toISOString()}
              className={cn(
                "min-h-28 border-b border-r border-border p-1.5 last:border-r-0",
                outside && "bg-surface-sunken/50",
              )}
            >
              <button
                type="button"
                onClick={() => onPickDay(day)}
                className={cn(
                  "mb-1 flex size-5 items-center justify-center rounded-full text-label tabular transition-colors",
                  isToday(day)
                    ? "bg-accent font-semibold text-accent-foreground"
                    : outside
                      ? "text-subtle hover:bg-surface-raised"
                      : "text-muted hover:bg-surface-raised",
                )}
                aria-label={`View ${format(day, "d MMMM")}`}
              >
                {format(day, "d")}
              </button>

              <div className="space-y-1">
                {dayMeetings.slice(0, 3).map((meeting) => (
                  <button
                    key={meeting.id}
                    type="button"
                    onClick={() => onSelect(meeting)}
                    className={cn(
                      "flex w-full items-center gap-1 rounded-[4px] px-1 py-0.5 text-left transition-colors hover:bg-surface-raised",
                      meeting.status === "live" && "bg-danger-subtle",
                    )}
                  >
                    <span
                      aria-hidden
                      className={cn(
                        "size-1.5 shrink-0 rounded-full",
                        meeting.status === "live"
                          ? "anim-pulse-live bg-live"
                          : meeting.status === "completed"
                            ? "bg-success"
                            : "bg-accent",
                      )}
                    />
                    <span className="truncate text-label text-foreground">
                      {formatTime(meeting.startsAt)} {meeting.title}
                    </span>
                  </button>
                ))}

                {dayMeetings.length > 3 ? (
                  <button
                    type="button"
                    onClick={() => onPickDay(day)}
                    className="px-1 text-label text-subtle hover:text-foreground tabular"
                  >
                    +{dayMeetings.length - 3} more
                  </button>
                ) : null}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}

/* -------------------------------------------------------------------------- */
/* Week & day grid                                                            */
/* -------------------------------------------------------------------------- */

function TimeGrid({
  days,
  meetingsOn,
  onSelect,
}: {
  days: Date[];
  meetingsOn: (day: Date) => Meeting[];
  onSelect: (meeting: Meeting) => void;
}) {
  const hours = Array.from(
    { length: DAY_END_HOUR - DAY_START_HOUR + 1 },
    (_, index) => DAY_START_HOUR + index,
  );

  /** Absolute position for an event within the hour column. */
  function geometry(meeting: Meeting) {
    const start = new Date(meeting.startsAt);
    const end = new Date(meeting.endsAt);

    const startHours = start.getHours() + start.getMinutes() / 60;
    const endHours = end.getHours() + end.getMinutes() / 60;

    const top = (startHours - DAY_START_HOUR) * HOUR_HEIGHT;
    // Floored so a 15-minute meeting is still large enough to read and click.
    const height = Math.max(22, (endHours - startHours) * HOUR_HEIGHT);

    return { top, height };
  }

  return (
    <div className="overflow-hidden rounded-surface border border-border bg-surface">
      {/* Day headers */}
      <div
        className="grid border-b border-border bg-surface-sunken"
        style={{ gridTemplateColumns: `3.5rem repeat(${days.length}, 1fr)` }}
      >
        <div />
        {days.map((day) => (
          <div
            key={day.toISOString()}
            className="border-l border-border px-2 py-1.5 text-center"
          >
            <p className="text-overline uppercase text-subtle">
              {format(day, "EEE")}
            </p>
            <p
              className={cn(
                "mx-auto mt-0.5 flex size-5 items-center justify-center rounded-full text-label tabular",
                isToday(day)
                  ? "bg-accent font-semibold text-accent-foreground"
                  : "text-foreground",
              )}
            >
              {format(day, "d")}
            </p>
          </div>
        ))}
      </div>

      {/* Hour rows */}
      <div className="max-h-[34rem] overflow-y-auto scrollbar-thin">
        <div
          className="relative grid"
          style={{ gridTemplateColumns: `3.5rem repeat(${days.length}, 1fr)` }}
        >
          {/* Hour labels */}
          <div>
            {hours.map((hour) => (
              <div
                key={hour}
                style={{ height: HOUR_HEIGHT }}
                className="relative border-b border-border pr-2 text-right"
              >
                <span className="absolute right-2 -top-1.5 text-label text-subtle tabular">
                  {String(hour).padStart(2, "0")}:00
                </span>
              </div>
            ))}
          </div>

          {/* Day columns */}
          {days.map((day) => (
            <div
              key={day.toISOString()}
              className="relative border-l border-border"
            >
              {hours.map((hour) => (
                <div
                  key={hour}
                  style={{ height: HOUR_HEIGHT }}
                  className="border-b border-border"
                />
              ))}

              {meetingsOn(day).map((meeting) => {
                const { top, height } = geometry(meeting);

                return (
                  <button
                    key={meeting.id}
                    type="button"
                    onClick={() => onSelect(meeting)}
                    style={{ top, height }}
                    className={cn(
                      "absolute inset-x-1 overflow-hidden rounded-control border px-1.5 py-1 text-left transition-colors",
                      meeting.status === "live"
                        ? "border-danger/40 bg-danger-subtle hover:border-danger"
                        : meeting.status === "completed"
                          ? "border-border bg-surface-raised hover:border-border-strong"
                          : "border-accent/30 bg-accent-subtle hover:border-accent",
                    )}
                  >
                    <p className="truncate text-label font-medium text-foreground">
                      {meeting.title}
                    </p>
                    {height > 34 ? (
                      <p className="truncate text-label text-muted tabular">
                        {formatTime(meeting.startsAt)}–
                        {formatTime(meeting.endsAt)}
                      </p>
                    ) : null}
                  </button>
                );
              })}
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}

/* -------------------------------------------------------------------------- */
/* Details drawer                                                             */
/* -------------------------------------------------------------------------- */

function MeetingDrawer({
  meeting,
  onOpenChange,
}: {
  meeting: Meeting | null;
  onOpenChange: (open: boolean) => void;
}) {
  if (!meeting) return null;

  return (
    <Sheet open={meeting !== null} onOpenChange={onOpenChange}>
      <SheetContent
        title={meeting.title}
        description={formatDateTime(meeting.startsAt)}
        footer={
          <>
            {meeting.status === "live" ? (
              <Button variant="danger" size="sm" asChild>
                <Link href="/live">
                  <Radio />
                  Join now
                </Link>
              </Button>
            ) : null}
            <Button variant="primary" size="sm" asChild>
              <Link href={`/meetings/${meeting.id}`}>
                Open meeting
                <ExternalLink />
              </Link>
            </Button>
          </>
        }
      >
        <div className="space-y-5">
          <div className="flex flex-wrap items-center gap-2">
            <MeetingStatusBadge status={meeting.status} />
            {meeting.summaryStatus !== "none" ? (
              <SummaryBadge status={meeting.summaryStatus} />
            ) : null}
          </div>

          {meeting.description ? (
            <p className="text-body text-muted">{meeting.description}</p>
          ) : null}

          <dl className="space-y-2.5">
            <div className="flex items-center gap-2">
              <CalendarDays
                className="size-3.5 shrink-0 text-subtle"
                aria-hidden
              />
              <dt className="sr-only">Date</dt>
              <dd className="text-caption text-foreground">
                {formatDate(meeting.startsAt)} · {formatTime(meeting.startsAt)}–
                {formatTime(meeting.endsAt)}
              </dd>
            </div>

            {meeting.durationSeconds > 0 ? (
              <div className="flex items-center gap-2">
                <span className="size-3.5" aria-hidden />
                <dt className="sr-only">Duration</dt>
                <dd className="text-caption text-muted tabular">
                  Recorded {formatDuration(meeting.durationSeconds)}
                </dd>
              </div>
            ) : null}

            <div className="flex items-center gap-2">
              <ExternalLink
                className="size-3.5 shrink-0 text-subtle"
                aria-hidden
              />
              <dt className="sr-only">Platform</dt>
              <dd className="text-caption text-muted">
                {PLATFORM_LABELS[meeting.platform]}
              </dd>
            </div>
          </dl>

          {meeting.tags.length > 0 ? (
            <div className="flex flex-wrap gap-1.5">
              {meeting.tags.map((tag) => (
                <Badge key={tag} tone="outline" size="sm">
                  {tag}
                </Badge>
              ))}
            </div>
          ) : null}

          <div>
            <h3 className="mb-2 flex items-center gap-1.5 text-overline uppercase text-subtle">
              <Users className="size-3" aria-hidden />
              Participants ({meeting.participants.length})
            </h3>
            <ul className="space-y-2">
              {meeting.participants.map((participant) => (
                <li
                  key={participant.userId}
                  className="flex items-center gap-2"
                >
                  <Avatar name={participant.name} size="sm" />
                  <div className="min-w-0">
                    <p className="truncate text-caption text-foreground">
                      {participant.name}
                    </p>
                    <p className="text-label capitalize text-subtle">
                      {participant.role}
                    </p>
                  </div>
                </li>
              ))}
            </ul>
          </div>
        </div>
      </SheetContent>
    </Sheet>
  );
}
