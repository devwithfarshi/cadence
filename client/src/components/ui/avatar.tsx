"use client";

import { Avatar as AvatarPrimitive } from "radix-ui";
import type { ComponentProps } from "react";
import { cn } from "@/lib/utils/cn";

const SIZES = {
  xs: "size-5 text-[0.5625rem]",
  sm: "size-6 text-overline",
  md: "size-8 text-label",
  lg: "size-10 text-caption",
  xl: "size-16 text-heading",
} as const;

/** First letters of the first and last name parts, e.g. "Amara Osei" → "AO". */
export function initialsOf(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean);
  if (parts.length === 0) return "?";
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase();
  return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
}

/**
 * Deterministically maps a name to one of the chart hues, so the same person is
 * always the same colour across the app without storing anything.
 */
function hueClassFor(name: string): string {
  const palette = [
    "bg-chart-1/15 text-chart-1",
    "bg-chart-2/15 text-chart-2",
    "bg-chart-3/20 text-chart-3",
    "bg-chart-4/15 text-chart-4",
    "bg-chart-5/15 text-chart-5",
    "bg-chart-6/15 text-chart-6",
  ];

  let hash = 0;
  for (let i = 0; i < name.length; i += 1) {
    hash = (hash * 31 + name.charCodeAt(i)) | 0;
  }
  return palette[Math.abs(hash) % palette.length];
}

export interface AvatarProps
  extends ComponentProps<typeof AvatarPrimitive.Root> {
  name: string;
  src?: string | null;
  size?: keyof typeof SIZES;
}

export function Avatar({
  name,
  src,
  size = "md",
  className,
  ...props
}: AvatarProps) {
  return (
    <AvatarPrimitive.Root
      className={cn(
        "relative flex shrink-0 overflow-hidden rounded-full",
        SIZES[size],
        className,
      )}
      {...props}
    >
      {src ? (
        <AvatarPrimitive.Image
          src={src}
          alt={name}
          className="size-full object-cover"
        />
      ) : null}
      <AvatarPrimitive.Fallback
        delayMs={src ? 300 : 0}
        className={cn(
          "flex size-full items-center justify-center font-semibold",
          hueClassFor(name),
        )}
      >
        {initialsOf(name)}
      </AvatarPrimitive.Fallback>
    </AvatarPrimitive.Root>
  );
}

/** Overlapping avatar row with a "+N" overflow chip. */
export function AvatarGroup({
  people,
  max = 4,
  size = "sm",
}: {
  people: { name: string; avatarUrl?: string | null }[];
  max?: number;
  size?: keyof typeof SIZES;
}) {
  const shown = people.slice(0, max);
  const overflow = people.length - shown.length;

  // Deliberately not an overlapping stack. These avatars fall back to two-letter
  // initials, and overlapping clips the second letter at the sizes used in
  // tables and list rows — legibility matters more here than the stack effect.
  return (
    <div className="flex items-center gap-1">
      {shown.map((person) => (
        <Avatar
          key={person.name}
          name={person.name}
          src={person.avatarUrl}
          size={size}
        />
      ))}
      {overflow > 0 ? (
        <span className="ml-0.5 text-label text-muted tabular">
          +{overflow}
        </span>
      ) : null}
    </div>
  );
}
