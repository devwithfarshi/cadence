"use client";

import { BarChart3, TableIcon } from "lucide-react";
import { type ReactNode, useState } from "react";
import { Tooltip } from "@/components/ui/tooltip";
import { cn } from "@/lib/utils/cn";

export interface TableColumn<T> {
  key: string;
  label: string;
  /** Right-aligned and tabular when the value is a number. */
  numeric?: boolean;
  value: (row: T) => string | number;
}

/**
 * Frame shared by every chart.
 *
 * Carries the chart/table toggle. The table is not a fallback — it is the
 * WCAG-clean twin, so no value is ever reachable only by hovering.
 */
export function ChartCard<T>({
  title,
  description,
  legend,
  children,
  rows,
  columns,
  /** Held at reduced opacity during refetch rather than flashing a skeleton. */
  stale = false,
  className,
}: {
  title: string;
  description?: string;
  legend?: ReactNode;
  children: ReactNode;
  rows: T[];
  columns: TableColumn<T>[];
  stale?: boolean;
  className?: string;
}) {
  const [view, setView] = useState<"chart" | "table">("chart");

  return (
    <section
      className={cn(
        "flex flex-col rounded-surface border border-border bg-surface",
        className,
      )}
    >
      <header className="flex items-start justify-between gap-3 border-b border-border px-4 py-3">
        <div className="min-w-0">
          <h3 className="text-subheading text-foreground">{title}</h3>
          {description ? (
            <p className="mt-0.5 text-caption text-muted">{description}</p>
          ) : null}
        </div>

        <div className="flex shrink-0 items-center gap-0.5 rounded-control border border-border p-0.5">
          {[
            { mode: "chart" as const, icon: BarChart3, label: "Chart view" },
            { mode: "table" as const, icon: TableIcon, label: "Table view" },
          ].map(({ mode, icon: Icon, label }) => (
            <Tooltip key={mode} label={label}>
              <button
                type="button"
                onClick={() => setView(mode)}
                aria-label={label}
                aria-pressed={view === mode}
                className={cn(
                  "flex size-6 items-center justify-center rounded-[4px] transition-colors",
                  view === mode
                    ? "bg-surface-raised text-foreground"
                    : "text-subtle hover:text-foreground",
                )}
              >
                <Icon className="size-3.5" />
              </button>
            </Tooltip>
          ))}
        </div>
      </header>

      {legend && view === "chart" ? (
        <div className="border-b border-border px-4 py-2">{legend}</div>
      ) : null}

      <div
        className={cn(
          "flex-1 transition-opacity",
          // Refetch holds the previous render — no skeleton, no layout jump.
          stale && "opacity-50",
        )}
      >
        {view === "chart" ? (
          <div className="p-2">{children}</div>
        ) : (
          <div className="max-h-80 overflow-auto scrollbar-thin">
            <table className="w-full text-body">
              <thead className="sticky top-0 bg-surface-raised">
                <tr className="border-b border-border">
                  {columns.map((column) => (
                    <th
                      key={column.key}
                      scope="col"
                      className={cn(
                        "px-3 py-2 text-overline uppercase text-subtle font-semibold",
                        column.numeric ? "text-right" : "text-left",
                      )}
                    >
                      {column.label}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {rows.map((row, index) => (
                  <tr
                    // biome-ignore lint/suspicious/noArrayIndexKey: chart rows are positional buckets
                    key={index}
                    className="border-b border-border last:border-0"
                  >
                    {columns.map((column) => (
                      <td
                        key={column.key}
                        className={cn(
                          "px-3 py-1.5",
                          column.numeric
                            ? "text-right text-muted tabular"
                            : "text-foreground",
                        )}
                      >
                        {column.value(row)}
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </section>
  );
}

/**
 * Legend for two or more series.
 *
 * Identity comes from the coloured key beside the label — the text itself
 * always wears a text token, never the series colour.
 */
export function ChartLegend({
  items,
}: {
  items: { label: string; color: string; shape?: "line" | "rect" }[];
}) {
  return (
    <ul className="flex flex-wrap items-center gap-x-4 gap-y-1.5">
      {items.map((item) => (
        <li key={item.label} className="flex items-center gap-1.5">
          <span
            aria-hidden
            className={cn(
              "shrink-0",
              // Legends mirror the mark: a line for lines, a swatch for fills.
              item.shape === "rect"
                ? "size-2.5 rounded-[2px]"
                : "h-0.5 w-3 rounded-full",
            )}
            style={{ backgroundColor: item.color }}
          />
          <span className="text-caption text-muted">{item.label}</span>
        </li>
      ))}
    </ul>
  );
}
