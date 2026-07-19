"use client";

import { cn } from "@/lib/utils/cn";

const BARS = 4;

/**
 * Compact audio activity indicator.
 *
 * Bars light up in proportion to `level`, so a glance tells you who is actually
 * speaking. Hidden from assistive tech — the current speaker is already
 * announced in the transcript, and a flickering meter would be pure noise.
 */
export function AudioMeter({
  level,
  active,
  className,
}: {
  /** 0–1 activity. */
  level: number;
  active: boolean;
  className?: string;
}) {
  const lit = Math.round(level * BARS);

  return (
    <div aria-hidden className={cn("flex items-end gap-0.5", className)}>
      {Array.from({ length: BARS }).map((_, index) => (
        <span
          // biome-ignore lint/suspicious/noArrayIndexKey: fixed-length positional bars
          key={index}
          className={cn(
            "w-0.5 rounded-full transition-all duration-150",
            // Each bar is progressively taller so the meter reads as a level.
            index === 0 && "h-1",
            index === 1 && "h-2",
            index === 2 && "h-2.5",
            index === 3 && "h-3.5",
            active && index < lit ? "bg-success" : "bg-border-strong",
          )}
        />
      ))}
    </div>
  );
}
