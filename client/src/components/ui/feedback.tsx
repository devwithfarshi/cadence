"use client";

import type { LucideIcon } from "lucide-react";
import type { ComponentProps, ReactNode } from "react";
import { cn } from "@/lib/utils/cn";

/* -------------------------------------------------------------------------- */
/* Skeleton                                                                   */
/* -------------------------------------------------------------------------- */

export function Skeleton({ className, ...props }: ComponentProps<"div">) {
  return (
    <div
      aria-hidden
      className={cn(
        "animate-pulse rounded-control bg-surface-raised",
        className,
      )}
      {...props}
    />
  );
}

/** Placeholder rows matching the shape of a data table while it loads. */
export function SkeletonRows({
  rows = 6,
  columns = 4,
}: {
  rows?: number;
  columns?: number;
}) {
  return (
    <div className="divide-y divide-border" aria-hidden>
      {Array.from({ length: rows }).map((_, rowIndex) => (
        <div
          // biome-ignore lint/suspicious/noArrayIndexKey: static placeholder list
          key={rowIndex}
          className="flex items-center gap-4 px-3 py-3"
        >
          {Array.from({ length: columns }).map((__, columnIndex) => (
            <Skeleton
              // biome-ignore lint/suspicious/noArrayIndexKey: static placeholder list
              key={columnIndex}
              className={cn("h-4", columnIndex === 0 ? "flex-[2]" : "flex-1")}
            />
          ))}
        </div>
      ))}
    </div>
  );
}

/* -------------------------------------------------------------------------- */
/* Empty state                                                                */
/* -------------------------------------------------------------------------- */

export function EmptyState({
  icon: Icon,
  title,
  description,
  action,
  className,
}: {
  icon: LucideIcon;
  title: string;
  description: string;
  action?: ReactNode;
  className?: string;
}) {
  return (
    <div
      className={cn(
        "flex flex-col items-center justify-center gap-3 px-6 py-14 text-center",
        className,
      )}
    >
      <div className="flex size-10 items-center justify-center rounded-surface border border-border bg-surface-raised">
        <Icon className="size-5 text-subtle" aria-hidden />
      </div>
      <div className="space-y-1">
        <p className="text-subheading text-foreground">{title}</p>
        <p className="mx-auto max-w-sm text-caption text-muted">
          {description}
        </p>
      </div>
      {action ? <div className="pt-1">{action}</div> : null}
    </div>
  );
}

/* -------------------------------------------------------------------------- */
/* Error state                                                                */
/* -------------------------------------------------------------------------- */

export function ErrorState({
  title = "Something went wrong",
  description,
  onRetry,
  className,
}: {
  title?: string;
  description: string;
  onRetry?: () => void;
  className?: string;
}) {
  return (
    <div
      role="alert"
      className={cn(
        "flex flex-col items-center justify-center gap-3 px-6 py-14 text-center",
        className,
      )}
    >
      <div className="space-y-1">
        <p className="text-subheading text-foreground">{title}</p>
        <p className="mx-auto max-w-sm text-caption text-muted">
          {description}
        </p>
      </div>
      {onRetry ? (
        <button
          type="button"
          onClick={onRetry}
          className="text-caption font-medium text-accent underline-offset-4 hover:underline"
        >
          Try again
        </button>
      ) : null}
    </div>
  );
}

/* -------------------------------------------------------------------------- */
/* Progress                                                                   */
/* -------------------------------------------------------------------------- */

export function Progress({
  value,
  max = 100,
  className,
  tone = "accent",
  label,
}: {
  value: number;
  max?: number;
  className?: string;
  tone?: "accent" | "success" | "warning" | "danger";
  label?: string;
}) {
  const pct = max <= 0 ? 0 : Math.min(100, Math.max(0, (value / max) * 100));
  const tones = {
    accent: "bg-accent",
    success: "bg-success",
    warning: "bg-warning",
    danger: "bg-danger",
  } as const;

  return (
    <div
      role="progressbar"
      aria-valuenow={Math.round(pct)}
      aria-valuemin={0}
      aria-valuemax={100}
      aria-label={label}
      className={cn(
        "h-1.5 w-full overflow-hidden rounded-full bg-surface-sunken",
        className,
      )}
    >
      <div
        className={cn("h-full rounded-full transition-all", tones[tone])}
        style={{ width: `${pct}%` }}
      />
    </div>
  );
}

/* -------------------------------------------------------------------------- */
/* Separator                                                                  */
/* -------------------------------------------------------------------------- */

export function Separator({
  orientation = "horizontal",
  className,
}: {
  orientation?: "horizontal" | "vertical";
  className?: string;
}) {
  // Purely decorative: the surrounding markup already conveys the grouping, so
  // this is hidden from assistive tech rather than announced as a separator.
  return (
    <div
      aria-hidden
      className={cn(
        "shrink-0 bg-border",
        orientation === "horizontal" ? "h-px w-full" : "h-full w-px",
        className,
      )}
    />
  );
}
