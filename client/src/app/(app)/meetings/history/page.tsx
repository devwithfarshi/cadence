"use client";

import { format, isSameMonth } from "date-fns";
import { ArchiveRestore, History, Star, Undo2 } from "lucide-react";
import Link from "next/link";
import { useMemo, useState } from "react";
import {
  MeetingStatusBadge,
  PLATFORM_LABELS,
  SummaryBadge,
} from "@/components/meetings/status";
import { useSetBreadcrumbs } from "@/components/shell/breadcrumb-context";
import { PageContainer, PageHeader } from "@/components/shell/page-header";
import { AvatarGroup } from "@/components/ui/avatar";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { EmptyState, ErrorState, Skeleton } from "@/components/ui/feedback";
import { FilterMenu } from "@/components/ui/filter-menu";
import { SearchInput } from "@/components/ui/input";
import { Timeline } from "@/components/ui/timeline";
import { useToast } from "@/components/ui/toast";
import { queryMeetings, setArchived } from "@/lib/api/meetings";
import { useAsync, useDebounced } from "@/lib/hooks/use-async";
import { formatDate, formatDuration, formatTime } from "@/lib/utils/format";
import type { Meeting, MeetingPlatform } from "@/types/domain";

const PLATFORM_OPTIONS = Object.entries(PLATFORM_LABELS).map(
  ([value, label]) => ({ value: value as MeetingPlatform, label }),
);

/** Groups meetings under a month heading so a long archive stays navigable. */
function groupByMonth(meetings: Meeting[]) {
  const groups: { label: string; meetings: Meeting[] }[] = [];

  for (const meeting of meetings) {
    const date = new Date(meeting.startsAt);
    const last = groups[groups.length - 1];

    if (last && isSameMonth(new Date(last.meetings[0].startsAt), date)) {
      last.meetings.push(meeting);
    } else {
      groups.push({ label: format(date, "MMMM yyyy"), meetings: [meeting] });
    }
  }

  return groups;
}

export default function MeetingHistoryPage() {
  const { toast } = useToast();

  const [search, setSearch] = useState("");
  const debouncedSearch = useDebounced(search, 250);
  const [platforms, setPlatforms] = useState<MeetingPlatform[]>([]);
  const [archivedOnly, setArchivedOnly] = useState(false);

  useSetBreadcrumbs([
    { label: "Meetings", href: "/meetings" },
    { label: "History" },
  ]);

  // History is everything already held, newest first — archived included, since
  // an archive that hides archived meetings would be useless.
  const meetings = useAsync(
    () =>
      queryMeetings({
        search: debouncedSearch,
        platform: platforms,
        status: ["completed", "cancelled"],
        includeArchived: true,
        to: new Date().toISOString(),
        sortBy: "startsAt",
        sortDir: "desc",
      }),
    [debouncedSearch, platforms],
  );

  const visible = useMemo(() => {
    const all = meetings.data ?? [];
    return archivedOnly ? all.filter((meeting) => meeting.isArchived) : all;
  }, [meetings.data, archivedOnly]);

  const groups = groupByMonth(visible);
  const archivedCount = (meetings.data ?? []).filter(
    (m) => m.isArchived,
  ).length;

  const totalHours = visible.reduce(
    (sum, meeting) => sum + meeting.durationSeconds / 3600,
    0,
  );

  async function handleRestore(meeting: Meeting) {
    try {
      await setArchived([meeting.id], false);
      meetings.refetch();
      toast({
        tone: "success",
        title: "Meeting restored",
        description: meeting.title,
      });
    } catch {
      toast({ tone: "error", title: "Could not restore meeting" });
    }
  }

  return (
    <PageContainer>
      <PageHeader
        title="Meeting history"
        description="Every meeting already held, newest first — including archived ones."
        actions={
          <Button variant="secondary" size="md" asChild>
            <Link href="/meetings">
              <Undo2 />
              Back to meetings
            </Link>
          </Button>
        }
      />

      <div className="mb-4 flex flex-wrap items-center gap-2">
        <SearchInput
          value={search}
          onValueChange={setSearch}
          placeholder="Search history…"
          className="w-full sm:w-64"
        />
        <FilterMenu
          label="Platform"
          options={PLATFORM_OPTIONS}
          selected={platforms}
          onChange={setPlatforms}
        />
        <Button
          variant="secondary"
          size="sm"
          aria-pressed={archivedOnly}
          onClick={() => setArchivedOnly((v) => !v)}
          className={archivedOnly ? "border-accent/40 bg-accent-subtle" : ""}
        >
          Archived only
          {archivedCount > 0 ? (
            <span className="tabular">({archivedCount})</span>
          ) : null}
        </Button>

        {visible.length > 0 ? (
          <p className="ml-auto text-caption text-muted tabular">
            {visible.length} {visible.length === 1 ? "meeting" : "meetings"} ·{" "}
            {totalHours.toFixed(1)}h recorded
          </p>
        ) : null}
      </div>

      {meetings.error ? (
        <ErrorState
          description={meetings.error.message}
          onRetry={meetings.refetch}
        />
      ) : meetings.loading && !meetings.data ? (
        <Skeleton className="h-96 w-full" />
      ) : visible.length === 0 ? (
        <EmptyState
          icon={History}
          title="Nothing in history yet"
          description={
            archivedOnly
              ? "No meetings have been archived."
              : "Meetings appear here once they have been held."
          }
          className="rounded-surface border border-border bg-surface"
        />
      ) : (
        <div className="space-y-6">
          {groups.map((group) => (
            <section key={group.label}>
              <h2 className="mb-3 text-overline uppercase text-subtle">
                {group.label}
              </h2>

              <div className="rounded-surface border border-border bg-surface p-4">
                <Timeline
                  entries={group.meetings.map((meeting) => ({
                    id: meeting.id,
                    tone: meeting.isArchived
                      ? "default"
                      : meeting.status === "cancelled"
                        ? "danger"
                        : "success",
                    title: (
                      <span className="flex flex-wrap items-center gap-2">
                        <Link
                          href={`/meetings/${meeting.id}`}
                          className="font-medium hover:text-accent hover:underline"
                        >
                          {meeting.title}
                        </Link>
                        {meeting.isFavorite ? (
                          <Star
                            className="size-3 fill-warning text-warning"
                            aria-label="Favourite"
                          />
                        ) : null}
                        {meeting.isArchived ? (
                          <Badge tone="neutral" size="sm">
                            Archived
                          </Badge>
                        ) : null}
                        <MeetingStatusBadge status={meeting.status} />
                        {meeting.summaryStatus !== "none" ? (
                          <SummaryBadge status={meeting.summaryStatus} />
                        ) : null}
                      </span>
                    ),
                    description: (
                      <span className="flex flex-wrap items-center gap-3">
                        <span className="tabular">
                          {formatTime(meeting.startsAt)} ·{" "}
                          {formatDuration(meeting.durationSeconds)}
                        </span>
                        <span>{PLATFORM_LABELS[meeting.platform]}</span>
                        <AvatarGroup
                          people={meeting.participants}
                          max={3}
                          size="xs"
                        />
                        {meeting.isArchived ? (
                          <button
                            type="button"
                            onClick={() => handleRestore(meeting)}
                            className="inline-flex items-center gap-1 text-caption text-accent hover:underline"
                          >
                            <ArchiveRestore className="size-3" />
                            Restore
                          </button>
                        ) : null}
                      </span>
                    ),
                    timestamp: formatDate(meeting.startsAt),
                  }))}
                />
              </div>
            </section>
          ))}
        </div>
      )}
    </PageContainer>
  );
}
