"use client";

import { endOfDay, startOfDay } from "date-fns";
import {
  ArrowRight,
  CalendarClock,
  CheckCircle2,
  CheckSquare,
  Clock,
  FileText,
  Library,
  ListTodo,
  Plus,
  Radio,
  Sparkles,
  Upload,
  Video,
} from "lucide-react";
import Link from "next/link";
import { useMemo } from "react";
import { StatCard } from "@/components/dashboard/stat-card";
import {
  MeetingStatusBadge,
  PriorityBadge,
  SummaryBadge,
} from "@/components/meetings/status";
import { useAuth } from "@/components/providers/auth-provider";
import { PageContainer, PageHeader } from "@/components/shell/page-header";
import { AvatarGroup } from "@/components/ui/avatar";
import { Button } from "@/components/ui/button";
import { Card, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { EmptyState, ErrorState, Skeleton } from "@/components/ui/feedback";
import { useToast } from "@/components/ui/toast";
import { queryMeetings } from "@/lib/api/meetings";
import { queryTasks, setTaskStatus } from "@/lib/api/tasks";
import { getDashboardStats, listActivity } from "@/lib/api/workspace";
import { useAsync } from "@/lib/hooks/use-async";
import { cn } from "@/lib/utils/cn";
import {
  formatDateTime,
  formatDueDate,
  formatDuration,
  formatNumber,
  formatRelative,
  formatTime,
  greetingFor,
} from "@/lib/utils/format";
import type { ActionItem, ActivityLog, Meeting } from "@/types/domain";

/* -------------------------------------------------------------------------- */
/* Meeting list rows                                                          */
/* -------------------------------------------------------------------------- */

function MeetingLine({ meeting }: { meeting: Meeting }) {
  return (
    <Link
      href={`/meetings/${meeting.id}`}
      className="flex items-center gap-3 px-4 py-3 transition-colors hover:bg-surface-raised/60"
    >
      <div className="min-w-0 flex-1">
        <p className="truncate text-body font-medium text-foreground">
          {meeting.title}
        </p>
        <p className="mt-0.5 flex items-center gap-1.5 text-caption text-muted">
          <span>{formatDateTime(meeting.startsAt)}</span>
          {meeting.durationSeconds > 0 ? (
            <>
              <span aria-hidden>·</span>
              <span className="tabular">
                {formatDuration(meeting.durationSeconds)}
              </span>
            </>
          ) : null}
        </p>
      </div>

      <AvatarGroup people={meeting.participants} max={3} />

      <div className="hidden shrink-0 sm:block">
        {meeting.status === "completed" ? (
          <SummaryBadge status={meeting.summaryStatus} />
        ) : (
          <MeetingStatusBadge status={meeting.status} />
        )}
      </div>
    </Link>
  );
}

function MeetingListCard({
  title,
  meetings,
  loading,
  error,
  onRetry,
  emptyTitle,
  emptyDescription,
  viewAllHref,
}: {
  title: string;
  meetings: Meeting[] | undefined;
  loading: boolean;
  error: Error | undefined;
  onRetry: () => void;
  emptyTitle: string;
  emptyDescription: string;
  viewAllHref?: string;
}) {
  return (
    <Card className="flex flex-col">
      <CardHeader>
        <CardTitle>{title}</CardTitle>
        {viewAllHref ? (
          <Link
            href={viewAllHref}
            className="shrink-0 text-caption font-medium text-accent underline-offset-4 hover:underline"
          >
            View all
          </Link>
        ) : null}
      </CardHeader>

      {error ? (
        <ErrorState description={error.message} onRetry={onRetry} />
      ) : loading ? (
        <div className="divide-y divide-border">
          {[0, 1, 2].map((row) => (
            <div key={row} className="flex items-center gap-3 px-4 py-3">
              <div className="flex-1 space-y-1.5">
                <Skeleton className="h-4 w-2/3" />
                <Skeleton className="h-3 w-1/3" />
              </div>
              <Skeleton className="h-6 w-20" />
            </div>
          ))}
        </div>
      ) : meetings && meetings.length > 0 ? (
        <div className="divide-y divide-border">
          {meetings.map((meeting) => (
            <MeetingLine key={meeting.id} meeting={meeting} />
          ))}
        </div>
      ) : (
        <EmptyState
          icon={CalendarClock}
          title={emptyTitle}
          description={emptyDescription}
          className="py-10"
        />
      )}
    </Card>
  );
}

/* -------------------------------------------------------------------------- */
/* Page                                                                       */
/* -------------------------------------------------------------------------- */

export default function DashboardPage() {
  const { session } = useAuth();
  const { toast } = useToast();

  const stats = useAsync(() => getDashboardStats(), []);
  const activity = useAsync(() => listActivity(6), []);

  // Today's window is computed once per mount; the dashboard is not long-lived
  // enough for a midnight rollover to matter.
  const todayRange = useMemo(() => {
    const now = new Date();
    return {
      from: startOfDay(now).toISOString(),
      to: endOfDay(now).toISOString(),
    };
  }, []);

  const todays = useAsync(
    () => queryMeetings({ from: todayRange.from, to: todayRange.to }),
    [todayRange.from, todayRange.to],
  );

  const upcoming = useAsync(
    () =>
      queryMeetings({
        status: ["scheduled"],
        from: new Date().toISOString(),
        sortBy: "startsAt",
        sortDir: "asc",
      }).then((items) => items.slice(0, 4)),
    [],
  );

  const recent = useAsync(
    () =>
      queryMeetings({
        status: ["completed"],
        to: new Date().toISOString(),
        sortBy: "startsAt",
        sortDir: "desc",
      }).then((items) => items.slice(0, 4)),
    [],
  );

  const openTasks = useAsync(
    () =>
      queryTasks({
        status: ["todo", "in_progress", "blocked"],
        sortBy: "dueDate",
        sortDir: "asc",
      }).then((items) => items.slice(0, 5)),
    [],
  );

  const liveMeeting = todays.data?.find((meeting) => meeting.status === "live");

  async function handleCompleteTask(task: ActionItem) {
    // Drop it from the list immediately; the dashboard only shows open work.
    openTasks.setData((current) =>
      (current ?? []).filter((item) => item.id !== task.id),
    );

    try {
      await setTaskStatus([task.id], "done");
      stats.refetch();
      toast({
        tone: "success",
        title: "Task completed",
        description: task.title,
        action: {
          label: "Undo",
          onClick: async () => {
            await setTaskStatus([task.id], "todo");
            openTasks.refetch();
            stats.refetch();
          },
        },
      });
    } catch (error) {
      openTasks.refetch();
      toast({
        tone: "error",
        title: "Could not complete task",
        description:
          error instanceof Error ? error.message : "Please try again.",
      });
    }
  }

  const firstName = session?.name.split(" ")[0] ?? "there";

  return (
    <PageContainer>
      <PageHeader
        title={`${greetingFor()}, ${firstName}`}
        description="Here's what's happening across your workspace today."
        actions={
          <>
            <Button variant="secondary" size="md" asChild>
              <Link href="/documents">
                <Upload />
                Upload
              </Link>
            </Button>
            <Button variant="primary" size="md" asChild>
              <Link href="/meetings?new=1">
                <Plus />
                New meeting
              </Link>
            </Button>
          </>
        }
      />

      {/* Live meeting banner — only when something is actually recording. */}
      {liveMeeting ? (
        <Link
          href="/live"
          className="mb-5 flex items-center gap-3 rounded-surface border border-danger/30 bg-danger-subtle px-4 py-3 transition-colors hover:border-danger/50"
        >
          <span className="relative flex size-2.5 shrink-0">
            <span className="anim-pulse-live absolute inline-flex size-full rounded-full bg-live" />
          </span>
          <div className="min-w-0 flex-1">
            <p className="truncate text-body font-medium text-foreground">
              {liveMeeting.title} is recording now
            </p>
            <p className="text-caption text-muted">
              Started at {formatTime(liveMeeting.startsAt)} ·{" "}
              {liveMeeting.participants.length} participants
            </p>
          </div>
          <Button variant="danger" size="sm" className="shrink-0" asChild>
            <span>
              <Radio />
              Join
            </span>
          </Button>
        </Link>
      ) : null}

      {/* Statistics */}
      <section aria-label="Workspace statistics" className="mb-6">
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <StatCard
            label="Total meetings"
            value={stats.data ? formatNumber(stats.data.totalMeetings) : "—"}
            icon={Video}
            trend={stats.data?.meetingTrendPct}
            hint="Scheduled, live and recorded"
            loading={stats.loading}
            href="/meetings"
          />
          <StatCard
            label="Hours recorded"
            value={stats.data ? stats.data.hoursRecorded : "—"}
            icon={Clock}
            hint="Across all completed meetings"
            loading={stats.loading}
          />
          <StatCard
            label="AI summaries"
            value={stats.data ? formatNumber(stats.data.aiSummaries) : "—"}
            icon={Sparkles}
            hint="Generated and ready to read"
            loading={stats.loading}
          />
          <StatCard
            label="Open tasks"
            value={stats.data ? formatNumber(stats.data.openTasks) : "—"}
            icon={ListTodo}
            hint={
              stats.data && stats.data.overdueTasks > 0
                ? `${stats.data.overdueTasks} overdue`
                : "Nothing overdue"
            }
            loading={stats.loading}
            href="/tasks"
          />
        </div>

        <div className="mt-3 grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
          <StatCard
            label="Completed tasks"
            value={stats.data ? formatNumber(stats.data.completedTasks) : "—"}
            icon={CheckSquare}
            loading={stats.loading}
            href="/tasks"
          />
          <StatCard
            label="Documents"
            value={stats.data ? formatNumber(stats.data.documents) : "—"}
            icon={FileText}
            loading={stats.loading}
            href="/documents"
          />
          <StatCard
            label="Knowledge base"
            value={stats.data ? formatNumber(stats.data.knowledgeItems) : "—"}
            icon={Library}
            loading={stats.loading}
            href="/knowledge"
          />
        </div>
      </section>

      {/* Main grid */}
      <div className="grid gap-4 lg:grid-cols-3">
        <div className="space-y-4 lg:col-span-2">
          <MeetingListCard
            title="Today"
            meetings={todays.data}
            loading={todays.loading}
            error={todays.error}
            onRetry={todays.refetch}
            emptyTitle="Nothing scheduled today"
            emptyDescription="Your calendar is clear. Enjoy the focus time."
            viewAllHref="/calendar"
          />

          <MeetingListCard
            title="Upcoming"
            meetings={upcoming.data}
            loading={upcoming.loading}
            error={upcoming.error}
            onRetry={upcoming.refetch}
            emptyTitle="No upcoming meetings"
            emptyDescription="Scheduled meetings will appear here as they're booked."
            viewAllHref="/calendar"
          />

          <MeetingListCard
            title="Recent"
            meetings={recent.data}
            loading={recent.loading}
            error={recent.error}
            onRetry={recent.refetch}
            emptyTitle="No recorded meetings yet"
            emptyDescription="Once a meeting is recorded it will show up here with its summary."
            viewAllHref="/meetings"
          />
        </div>

        <div className="space-y-4">
          {/* Pending action items */}
          <Card className="flex flex-col">
            <CardHeader>
              <CardTitle>Pending action items</CardTitle>
              <Link
                href="/tasks"
                className="shrink-0 text-caption font-medium text-accent underline-offset-4 hover:underline"
              >
                View all
              </Link>
            </CardHeader>

            {openTasks.error ? (
              <ErrorState
                description={openTasks.error.message}
                onRetry={openTasks.refetch}
              />
            ) : openTasks.loading ? (
              <div className="space-y-3 p-4">
                {[0, 1, 2].map((row) => (
                  <Skeleton key={row} className="h-10 w-full" />
                ))}
              </div>
            ) : openTasks.data && openTasks.data.length > 0 ? (
              <ul className="divide-y divide-border">
                {openTasks.data.map((task) => {
                  const due = formatDueDate(task.dueDate);

                  return (
                    <li key={task.id} className="flex gap-2.5 px-4 py-3">
                      <Checkbox
                        checked={false}
                        onCheckedChange={() => handleCompleteTask(task)}
                        aria-label={`Mark "${task.title}" as done`}
                        className="mt-0.5"
                      />

                      <div className="min-w-0 flex-1">
                        <p className="text-body text-foreground">
                          {task.title}
                        </p>
                        <div className="mt-1 flex flex-wrap items-center gap-2">
                          <PriorityBadge priority={task.priority} />
                          <span
                            className={cn(
                              "text-caption",
                              due.overdue
                                ? "font-medium text-danger"
                                : "text-muted",
                            )}
                          >
                            {due.label}
                          </span>
                        </div>
                      </div>
                    </li>
                  );
                })}
              </ul>
            ) : (
              <EmptyState
                icon={CheckCircle2}
                title="All clear"
                description="You have no open action items."
                className="py-10"
              />
            )}
          </Card>

          {/* Quick actions */}
          <Card>
            <CardHeader>
              <CardTitle>Quick actions</CardTitle>
            </CardHeader>
            <div className="grid grid-cols-2 gap-2 p-3">
              {[
                { label: "New meeting", href: "/meetings?new=1", icon: Plus },
                { label: "Start recording", href: "/live", icon: Radio },
                { label: "Ask the AI", href: "/chat", icon: Sparkles },
                { label: "Upload document", href: "/documents", icon: Upload },
              ].map((action) => (
                <Link
                  key={action.label}
                  href={action.href}
                  className="flex flex-col gap-2 rounded-control border border-border p-3 transition-colors hover:border-border-strong hover:bg-surface-raised/60"
                >
                  <action.icon className="size-4 text-subtle" aria-hidden />
                  <span className="text-caption font-medium text-foreground">
                    {action.label}
                  </span>
                </Link>
              ))}
            </div>
          </Card>

          {/* Recent activity */}
          <Card>
            <CardHeader>
              <CardTitle>Recent activity</CardTitle>
            </CardHeader>

            {activity.loading ? (
              <div className="space-y-3 p-4">
                {[0, 1, 2, 3].map((row) => (
                  <Skeleton key={row} className="h-8 w-full" />
                ))}
              </div>
            ) : activity.data && activity.data.length > 0 ? (
              <ul className="space-y-0 p-2">
                {activity.data.map((entry: ActivityLog) => (
                  <li key={entry.id}>
                    <ActivityRow entry={entry} />
                  </li>
                ))}
              </ul>
            ) : (
              <EmptyState
                icon={Clock}
                title="No activity yet"
                description="Workspace activity will show up here."
                className="py-8"
              />
            )}
          </Card>
        </div>
      </div>
    </PageContainer>
  );
}

function ActivityRow({ entry }: { entry: ActivityLog }) {
  const content = (
    <div className="flex gap-2.5 rounded-control px-2 py-2 transition-colors hover:bg-surface-raised/60">
      <span
        aria-hidden
        className="mt-1.5 size-1.5 shrink-0 rounded-full bg-border-strong"
      />
      <div className="min-w-0 flex-1">
        <p className="text-caption text-foreground">{entry.summary}</p>
        <p className="mt-0.5 text-label text-subtle">
          {formatRelative(entry.createdAt)}
        </p>
      </div>
      {entry.href ? (
        <ArrowRight
          className="mt-0.5 size-3.5 shrink-0 text-subtle"
          aria-hidden
        />
      ) : null}
    </div>
  );

  return entry.href ? <Link href={entry.href}>{content}</Link> : content;
}
