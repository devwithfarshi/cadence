import type { LucideIcon } from "lucide-react";
import type { ReactNode } from "react";
import { cn } from "@/lib/utils/cn";

export interface TimelineEntry {
  id: string;
  /** Primary line. Accepts nodes so a row can carry a link or emphasis. */
  title: ReactNode;
  description?: ReactNode;
  timestamp: string;
  icon?: LucideIcon;
  /** Marks the entry as noteworthy — a decision, an incident, a completion. */
  tone?: "default" | "accent" | "success" | "warning" | "danger";
}

const TONES = {
  default: "border-border-strong bg-surface text-subtle",
  accent: "border-accent/40 bg-accent-subtle text-accent",
  success: "border-success/40 bg-success-subtle text-success-foreground",
  warning: "border-warning/40 bg-warning-subtle text-warning-foreground",
  danger: "border-danger/40 bg-danger-subtle text-danger-foreground",
} as const;

/**
 * Vertical event sequence.
 *
 * The connecting rail is drawn per-item and suppressed on the last one, so the
 * line stops at the final marker instead of trailing into empty space.
 */
export function Timeline({
  entries,
  className,
}: {
  entries: TimelineEntry[];
  className?: string;
}) {
  return (
    <ol className={cn("relative", className)}>
      {entries.map((entry, index) => {
        const Icon = entry.icon;
        const isLast = index === entries.length - 1;

        return (
          <li key={entry.id} className="relative flex gap-3 pb-5 last:pb-0">
            {/* Rail */}
            {!isLast ? (
              <span
                aria-hidden
                className="absolute left-3 top-6 h-full w-px -translate-x-1/2 bg-border"
              />
            ) : null}

            {/* Marker */}
            <span
              aria-hidden
              className={cn(
                "relative z-10 flex size-6 shrink-0 items-center justify-center rounded-full border",
                TONES[entry.tone ?? "default"],
              )}
            >
              {Icon ? (
                <Icon className="size-3" />
              ) : (
                <span className="size-1.5 rounded-full bg-current" />
              )}
            </span>

            <div className="min-w-0 flex-1 pt-0.5">
              <div className="text-body text-foreground">{entry.title}</div>
              {entry.description ? (
                <div className="mt-0.5 text-caption text-muted">
                  {entry.description}
                </div>
              ) : null}
              <p className="mt-1 text-label text-subtle">{entry.timestamp}</p>
            </div>
          </li>
        );
      })}
    </ol>
  );
}
