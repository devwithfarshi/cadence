"use client";

import {
  Area,
  AreaChart,
  Bar,
  BarChart,
  CartesianGrid,
  Line,
  LineChart,
  Tooltip as RechartsTooltip,
  ResponsiveContainer,
  XAxis,
  YAxis,
} from "recharts";
import { cn } from "@/lib/utils/cn";

/**
 * Chart chrome, fixed across every chart.
 *
 * Gridlines are solid hairlines one step off the surface and horizontal only —
 * vertical rules add ink without helping anyone read a value. Axis text wears a
 * text token, never a series colour.
 */
const AXIS_TICK = { fill: "var(--foreground-subtle)", fontSize: 11 } as const;
const GRID_STROKE = "var(--border)";
const SURFACE = "var(--surface)";

const axisProps = {
  stroke: "var(--border)",
  tickLine: false,
  axisLine: false,
  tick: AXIS_TICK,
} as const;

export interface SeriesSpec {
  key: string;
  label: string;
  color: string;
}

/* -------------------------------------------------------------------------- */
/* Tooltip                                                                    */
/* -------------------------------------------------------------------------- */

interface TooltipEntry {
  dataKey?: string | number;
  name?: string | number;
  value?: string | number;
  color?: string;
  /** The original datum, so the readout can use a fuller label than the axis. */
  payload?: Record<string, unknown>;
}

/**
 * Shared readout. The value leads and the series name follows — the reader
 * already knows which series they are pointing at and wants the number.
 */
function ChartTooltip({
  active,
  payload,
  label,
  series,
  unit,
}: {
  active?: boolean;
  payload?: readonly TooltipEntry[];
  label?: string | number;
  series: SeriesSpec[];
  unit?: string;
}) {
  if (!active || !payload || payload.length === 0) return null;

  // The axis label is abbreviated to fit; the tooltip can afford the
  // unambiguous form ("Week of 14 Jul 2026" rather than "14 Jul").
  const full = payload[0]?.payload?.fullLabel;
  const heading = typeof full === "string" ? full : String(label ?? "");

  return (
    <div className="rounded-control border border-border bg-surface px-2.5 py-2 shadow-md">
      <p className="mb-1 text-label text-subtle">{heading}</p>
      <ul className="space-y-0.5">
        {payload.map((entry) => {
          const spec = series.find((s) => s.key === entry.dataKey);
          return (
            <li
              key={String(entry.dataKey)}
              className="flex items-center gap-2 whitespace-nowrap"
            >
              <span
                aria-hidden
                className="h-0.5 w-3 shrink-0 rounded-full"
                style={{ backgroundColor: spec?.color ?? entry.color }}
              />
              <span className="text-caption font-medium text-foreground tabular">
                {entry.value}
                {unit ? <span className="text-subtle"> {unit}</span> : null}
              </span>
              <span className="text-label text-muted">
                {spec?.label ?? String(entry.name ?? "")}
              </span>
            </li>
          );
        })}
      </ul>
    </div>
  );
}

/* -------------------------------------------------------------------------- */
/* Time series — line or area                                                 */
/* -------------------------------------------------------------------------- */

export function TimeSeriesChart<T extends object>({
  data,
  series,
  xKey = "label",
  variant = "line",
  height = 200,
  unit,
  className,
}: {
  data: T[];
  series: SeriesSpec[];
  xKey?: string;
  variant?: "line" | "area";
  height?: number;
  unit?: string;
  className?: string;
}) {
  // Long ranges produce many buckets. These charts also render in narrow
  // small-multiple cards, so cap at ~5 ticks — 7 collided at card width.
  const tickInterval = Math.max(0, Math.ceil(data.length / 5) - 1);

  const shared = (
    <>
      <CartesianGrid stroke={GRID_STROKE} strokeWidth={1} vertical={false} />
      <XAxis dataKey={xKey} interval={tickInterval} {...axisProps} />
      <YAxis width={36} allowDecimals={false} {...axisProps} />
      <RechartsTooltip
        // The crosshair finds the X, so the reader aims at a date not a line.
        cursor={{ stroke: "var(--border-strong)", strokeWidth: 1 }}
        content={({ active, payload, label }) => (
          <ChartTooltip
            active={active}
            payload={payload as readonly TooltipEntry[]}
            label={label as string}
            series={series}
            unit={unit}
          />
        )}
      />
    </>
  );

  return (
    <div className={cn("w-full", className)} style={{ height }}>
      <ResponsiveContainer width="100%" height="100%">
        {variant === "area" ? (
          <AreaChart
            data={data}
            margin={{ top: 8, right: 12, bottom: 0, left: 0 }}
          >
            {shared}
            {series.map((spec) => (
              <Area
                key={spec.key}
                // Straight segments, not a spline: a monotone curve between
                // discrete daily counts overshoots and implies values that were
                // never measured.
                type="linear"
                dataKey={spec.key}
                name={spec.label}
                stroke={spec.color}
                strokeWidth={2}
                strokeLinecap="round"
                strokeLinejoin="round"
                // A wash, never a saturated block.
                fill={spec.color}
                fillOpacity={0.1}
                dot={false}
                activeDot={{ r: 4, strokeWidth: 2, stroke: SURFACE }}
              />
            ))}
          </AreaChart>
        ) : (
          <LineChart
            data={data}
            margin={{ top: 8, right: 12, bottom: 0, left: 0 }}
          >
            {shared}
            {series.map((spec) => (
              <Line
                key={spec.key}
                type="linear"
                dataKey={spec.key}
                name={spec.label}
                stroke={spec.color}
                strokeWidth={2}
                strokeLinecap="round"
                strokeLinejoin="round"
                dot={false}
                // The surface ring keeps the marker legible where lines cross.
                activeDot={{ r: 4, strokeWidth: 2, stroke: SURFACE }}
              />
            ))}
          </LineChart>
        )}
      </ResponsiveContainer>
    </div>
  );
}

/* -------------------------------------------------------------------------- */
/* Column chart — one nominal series                                          */
/* -------------------------------------------------------------------------- */

export function ColumnChart<T extends object>({
  data,
  series,
  xKey,
  height = 200,
  unit,
  className,
}: {
  data: T[];
  series: SeriesSpec;
  xKey: string;
  height?: number;
  unit?: string;
  className?: string;
}) {
  return (
    <div className={cn("w-full", className)} style={{ height }}>
      <ResponsiveContainer width="100%" height="100%">
        <BarChart
          data={data}
          margin={{ top: 8, right: 12, bottom: 0, left: 0 }}
        >
          <CartesianGrid
            stroke={GRID_STROKE}
            strokeWidth={1}
            vertical={false}
          />
          <XAxis dataKey={xKey} {...axisProps} />
          <YAxis width={36} allowDecimals={false} {...axisProps} />
          <RechartsTooltip
            // On bars the mark is the hit target, so no crosshair — just a wash.
            cursor={{ fill: "var(--surface-raised)" }}
            content={({ active, payload, label }) => (
              <ChartTooltip
                active={active}
                payload={payload as readonly TooltipEntry[]}
                label={label as string}
                series={[series]}
                unit={unit}
              />
            )}
          />
          <Bar
            dataKey={series.key}
            name={series.label}
            fill={series.color}
            // Capped so the bar never fills its band — the leftover is air.
            maxBarSize={24}
            // Rounded data-end, square at the baseline.
            radius={[4, 4, 0, 0]}
          />
        </BarChart>
      </ResponsiveContainer>
    </div>
  );
}

/* -------------------------------------------------------------------------- */
/* Horizontal grouped bars — ranked comparison                                */
/* -------------------------------------------------------------------------- */

export function HorizontalBarChart<T extends object>({
  data,
  series,
  yKey,
  height = 260,
  unit,
  className,
}: {
  data: T[];
  series: SeriesSpec[];
  yKey: string;
  height?: number;
  unit?: string;
  className?: string;
}) {
  return (
    <div className={cn("w-full", className)} style={{ height }}>
      <ResponsiveContainer width="100%" height="100%">
        <BarChart
          data={data}
          layout="vertical"
          margin={{ top: 4, right: 16, bottom: 0, left: 0 }}
          // The gap between grouped bars is surface showing through.
          barGap={2}
        >
          <CartesianGrid
            stroke={GRID_STROKE}
            strokeWidth={1}
            horizontal={false}
          />
          <XAxis type="number" allowDecimals={false} {...axisProps} />
          <YAxis type="category" dataKey={yKey} width={132} {...axisProps} />
          <RechartsTooltip
            cursor={{ fill: "var(--surface-raised)" }}
            content={({ active, payload, label }) => (
              <ChartTooltip
                active={active}
                payload={payload as readonly TooltipEntry[]}
                label={label as string}
                series={series}
                unit={unit}
              />
            )}
          />
          {series.map((spec) => (
            <Bar
              key={spec.key}
              dataKey={spec.key}
              name={spec.label}
              fill={spec.color}
              maxBarSize={12}
              radius={[0, 4, 4, 0]}
            />
          ))}
        </BarChart>
      </ResponsiveContainer>
    </div>
  );
}

/* -------------------------------------------------------------------------- */
/* 100% stacked share bar                                                     */
/* -------------------------------------------------------------------------- */

/**
 * Part-to-whole across a handful of categories.
 *
 * Hand-rolled rather than charted: a flex row gives exact 2px surface gaps
 * between segments and lets a label be rendered only when it genuinely fits,
 * which is fiddly to guarantee inside a charting library.
 */
export function ShareBar({
  segments,
  className,
}: {
  segments: { label: string; value: number; share: number; color: string }[];
  className?: string;
}) {
  const total = segments.reduce((sum, segment) => sum + segment.share, 0) || 1;

  return (
    <div className={cn("space-y-3", className)}>
      <div className="flex h-8 w-full gap-0.5 overflow-hidden rounded-control">
        {segments.map((segment) => {
          const width = (segment.share / total) * 100;
          // Only label inside the segment when the text genuinely fits;
          // otherwise the legend and the table carry it.
          const showLabel = width >= 12;

          return (
            <div
              key={segment.label}
              className="flex items-center justify-center"
              style={{ width: `${width}%`, backgroundColor: segment.color }}
              title={`${segment.label}: ${segment.share}%`}
            >
              {showLabel ? (
                // Set on a filled mark, so the text takes white for contrast.
                <span className="px-1 text-label font-medium text-white tabular">
                  {Math.round(segment.share)}%
                </span>
              ) : null}
            </div>
          );
        })}
      </div>

      <ul className="grid gap-x-4 gap-y-1.5 sm:grid-cols-2">
        {segments.map((segment) => (
          <li key={segment.label} className="flex items-center gap-2">
            <span
              aria-hidden
              className="size-2.5 shrink-0 rounded-[2px]"
              style={{ backgroundColor: segment.color }}
            />
            <span className="min-w-0 flex-1 truncate text-caption text-muted">
              {segment.label}
            </span>
            <span className="shrink-0 text-caption text-foreground tabular">
              {segment.share}%
            </span>
          </li>
        ))}
      </ul>
    </div>
  );
}

/** The validated categorical slots, referenced by name rather than by index. */
export const CHART_COLORS = {
  slot1: "var(--chart-1)",
  slot2: "var(--chart-2)",
  slot3: "var(--chart-3)",
  slot4: "var(--chart-4)",
  slot5: "var(--chart-5)",
  slot6: "var(--chart-6)",
  muted: "var(--chart-muted)",
} as const;

/** Assigned in fixed order — never cycled, never reordered by rank. */
export const CHART_SLOTS = [
  CHART_COLORS.slot1,
  CHART_COLORS.slot2,
  CHART_COLORS.slot3,
  CHART_COLORS.slot4,
  CHART_COLORS.slot5,
  CHART_COLORS.slot6,
];
