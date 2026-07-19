"use client";

import {
  CheckSquare,
  Clock,
  FileText,
  Library,
  Sparkles,
  Users,
  Video,
} from "lucide-react";
import { useState } from "react";
import { ChartCard, ChartLegend } from "@/components/charts/chart-card";
import {
  CHART_COLORS,
  CHART_SLOTS,
  ColumnChart,
  HorizontalBarChart,
  ShareBar,
  TimeSeriesChart,
} from "@/components/charts/charts";
import { StatCard } from "@/components/dashboard/stat-card";
import { PageContainer, PageHeader } from "@/components/shell/page-header";
import { ErrorState, Skeleton } from "@/components/ui/feedback";
import {
  getAnalytics,
  RANGE_LABELS,
  type RangePreset,
} from "@/lib/api/analytics";
import { useAsync } from "@/lib/hooks/use-async";
import { cn } from "@/lib/utils/cn";
import { formatNumber } from "@/lib/utils/format";

const PRESETS: RangePreset[] = ["7d", "30d", "90d", "12m"];

export default function AnalyticsPage() {
  const [range, setRange] = useState<RangePreset>("30d");
  const analytics = useAsync(() => getAnalytics(range), [range]);

  const data = analytics.data;
  // Charts hold their previous render while refetching rather than flashing.
  const stale = analytics.loading && data !== undefined;

  if (analytics.error) {
    return (
      <PageContainer>
        <PageHeader title="Analytics" />
        <ErrorState
          description={analytics.error.message}
          onRetry={analytics.refetch}
        />
      </PageContainer>
    );
  }

  return (
    <PageContainer>
      <PageHeader
        title="Analytics"
        description="Meeting volume, recording hours, AI usage and team productivity."
      />

      {/* One filter row, above everything it scopes. Presets before any custom
          range — nobody fights a calendar grid for "last 30 days". */}
      <div className="mb-5 flex flex-wrap items-center gap-2">
        <fieldset className="flex flex-wrap items-center gap-2">
          <legend className="sr-only">Date range</legend>
          <span aria-hidden className="text-caption text-muted">
            Period
          </span>

          <div className="flex items-center gap-0.5 rounded-control border border-border p-0.5">
            {PRESETS.map((preset) => (
              <button
                key={preset}
                type="button"
                onClick={() => setRange(preset)}
                aria-pressed={range === preset}
                className={cn(
                  "rounded-[4px] px-2.5 py-1 text-caption font-medium transition-colors",
                  range === preset
                    ? "bg-surface-raised text-foreground"
                    : "text-muted hover:text-foreground",
                )}
              >
                {RANGE_LABELS[preset]}
              </button>
            ))}
          </div>
        </fieldset>

        {data ? (
          <span className="text-caption text-subtle">
            Bucketed by {data.granularity}
          </span>
        ) : null}
      </div>

      {/* KPI row — headline numbers are figures, not one-bar charts. */}
      <section aria-label="Summary" className="mb-4">
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <StatCard
            label="Meetings"
            value={data ? formatNumber(data.summary.meetings) : "—"}
            icon={Video}
            hint={
              data && data.summary.averageDurationMinutes > 0
                ? `${data.summary.averageDurationMinutes}m average length`
                : "No recordings in range"
            }
            loading={analytics.loading && !data}
          />
          <StatCard
            label="Hours recorded"
            value={data ? data.summary.hours : "—"}
            icon={Clock}
            hint="Across completed meetings"
            loading={analytics.loading && !data}
          />
          <StatCard
            label="AI summaries"
            value={data ? formatNumber(data.summary.summaries) : "—"}
            icon={Sparkles}
            hint="Generated in this period"
            loading={analytics.loading && !data}
          />
          <StatCard
            label="Task completion"
            value={data ? `${data.summary.completionRate}%` : "—"}
            icon={CheckSquare}
            hint={
              data
                ? `${data.summary.tasksCompleted} of ${data.summary.tasksCreated} created`
                : undefined
            }
            loading={analytics.loading && !data}
          />
        </div>

        <div className="mt-3 grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
          <StatCard
            label="Documents added"
            value={data ? formatNumber(data.summary.documents) : "—"}
            icon={FileText}
            loading={analytics.loading && !data}
          />
          <StatCard
            label="Knowledge items"
            value={data ? formatNumber(data.summary.knowledgeItems) : "—"}
            icon={Library}
            loading={analytics.loading && !data}
          />
          <StatCard
            label="Active members"
            value={data ? formatNumber(data.summary.activeMembers) : "—"}
            icon={Users}
            loading={analytics.loading && !data}
          />
        </div>
      </section>

      {!data ? (
        <div className="grid gap-4 lg:grid-cols-2">
          {[0, 1, 2, 3].map((index) => (
            <Skeleton key={index} className="h-64 w-full" />
          ))}
        </div>
      ) : (
        <div className="space-y-4">
          {/* Small multiples: three single-series charts sharing an x-axis,
              rather than one chart carrying three different y-scales. */}
          <div className="grid gap-4 lg:grid-cols-3">
            <ChartCard
              title="Meeting volume"
              description="Meetings held per bucket"
              stale={stale}
              rows={data.overTime}
              columns={[
                { key: "label", label: "Period", value: (r) => r.fullLabel },
                {
                  key: "meetings",
                  label: "Meetings",
                  numeric: true,
                  value: (r) => r.meetings,
                },
              ]}
            >
              <TimeSeriesChart
                data={data.overTime}
                series={[
                  {
                    key: "meetings",
                    label: "Meetings",
                    color: CHART_COLORS.slot1,
                  },
                ]}
              />
            </ChartCard>

            <ChartCard
              title="Recording hours"
              description="Hours of audio captured"
              stale={stale}
              rows={data.overTime}
              columns={[
                { key: "label", label: "Period", value: (r) => r.fullLabel },
                {
                  key: "hours",
                  label: "Hours",
                  numeric: true,
                  value: (r) => r.hours,
                },
              ]}
            >
              <TimeSeriesChart
                data={data.overTime}
                variant="area"
                unit="h"
                series={[
                  { key: "hours", label: "Hours", color: CHART_COLORS.slot4 },
                ]}
              />
            </ChartCard>

            <ChartCard
              title="AI usage"
              description="Summaries generated"
              stale={stale}
              rows={data.overTime}
              columns={[
                { key: "label", label: "Period", value: (r) => r.fullLabel },
                {
                  key: "summaries",
                  label: "Summaries",
                  numeric: true,
                  value: (r) => r.summaries,
                },
              ]}
            >
              <TimeSeriesChart
                data={data.overTime}
                variant="area"
                series={[
                  {
                    key: "summaries",
                    label: "Summaries",
                    color: CHART_COLORS.slot3,
                  },
                ]}
              />
            </ChartCard>
          </div>

          <div className="grid gap-4 lg:grid-cols-2">
            {/* Two series, so a legend is mandatory. */}
            <ChartCard
              title="Task throughput"
              description="Action items created against action items completed"
              stale={stale}
              legend={
                <ChartLegend
                  items={[
                    { label: "Created", color: CHART_COLORS.slot1 },
                    { label: "Completed", color: CHART_COLORS.slot2 },
                  ]}
                />
              }
              rows={data.overTime}
              columns={[
                { key: "label", label: "Period", value: (r) => r.fullLabel },
                {
                  key: "created",
                  label: "Created",
                  numeric: true,
                  value: (r) => r.tasksCreated,
                },
                {
                  key: "completed",
                  label: "Completed",
                  numeric: true,
                  value: (r) => r.tasksCompleted,
                },
              ]}
            >
              <TimeSeriesChart
                data={data.overTime}
                series={[
                  {
                    key: "tasksCreated",
                    label: "Created",
                    color: CHART_COLORS.slot1,
                  },
                  {
                    key: "tasksCompleted",
                    label: "Completed",
                    color: CHART_COLORS.slot2,
                  },
                ]}
              />
            </ChartCard>

            {/* Nominal categories: every bar takes the same slot-1 hue rather
                than a value ramp, which would re-encode bar length as colour. */}
            <ChartCard
              title="Meeting frequency"
              description="When meetings actually happen"
              stale={stale}
              rows={data.byWeekday}
              columns={[
                { key: "weekday", label: "Day", value: (r) => r.weekday },
                {
                  key: "meetings",
                  label: "Meetings",
                  numeric: true,
                  value: (r) => r.meetings,
                },
              ]}
            >
              <ColumnChart
                data={data.byWeekday}
                xKey="weekday"
                series={{
                  key: "meetings",
                  label: "Meetings",
                  color: CHART_COLORS.slot1,
                }}
              />
            </ChartCard>
          </div>

          <div className="grid gap-4 lg:grid-cols-2">
            <ChartCard
              title="Team productivity"
              description="Meetings attended and tasks completed, per member"
              stale={stale}
              legend={
                <ChartLegend
                  items={[
                    {
                      label: "Meetings attended",
                      color: CHART_COLORS.slot1,
                      shape: "rect",
                    },
                    {
                      label: "Tasks completed",
                      color: CHART_COLORS.slot2,
                      shape: "rect",
                    },
                  ]}
                />
              }
              rows={data.byMember}
              columns={[
                { key: "name", label: "Member", value: (r) => r.name },
                {
                  key: "meetings",
                  label: "Meetings",
                  numeric: true,
                  value: (r) => r.meetings,
                },
                {
                  key: "completed",
                  label: "Completed",
                  numeric: true,
                  value: (r) => r.tasksCompleted,
                },
                {
                  key: "open",
                  label: "Open",
                  numeric: true,
                  value: (r) => r.tasksOpen,
                },
              ]}
            >
              <HorizontalBarChart
                data={data.byMember}
                yKey="name"
                height={Math.max(200, data.byMember.length * 34)}
                series={[
                  {
                    key: "meetings",
                    label: "Meetings attended",
                    color: CHART_COLORS.slot1,
                  },
                  {
                    key: "tasksCompleted",
                    label: "Tasks completed",
                    color: CHART_COLORS.slot2,
                  },
                ]}
              />
            </ChartCard>

            <ChartCard
              title="Speaker distribution"
              description="Share of speaking time, weighted by meeting length"
              stale={stale}
              rows={data.speakerDistribution}
              columns={[
                { key: "name", label: "Speaker", value: (r) => r.name },
                {
                  key: "minutes",
                  label: "Minutes",
                  numeric: true,
                  value: (r) => r.minutes,
                },
                {
                  key: "share",
                  label: "Share",
                  numeric: true,
                  value: (r) => `${r.share}%`,
                },
              ]}
            >
              {data.speakerDistribution.length === 0 ? (
                <p className="px-2 py-12 text-center text-caption text-subtle">
                  No recorded speaking time in this period.
                </p>
              ) : (
                <ShareBar
                  className="p-2"
                  segments={data.speakerDistribution.map((speaker, index) => ({
                    label: speaker.name,
                    value: speaker.minutes,
                    share: speaker.share,
                    // Fixed order, assigned by position — never by rank.
                    color: CHART_SLOTS[index % CHART_SLOTS.length],
                  }))}
                />
              )}
            </ChartCard>
          </div>
        </div>
      )}
    </PageContainer>
  );
}
