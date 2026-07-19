"use client";

import { ArrowDown, ArrowUp, ChevronsUpDown } from "lucide-react";
import type { ComponentProps } from "react";
import { cn } from "@/lib/utils/cn";
import type { SortDirection } from "@/types/domain";

/**
 * Table shell.
 *
 * The horizontal scroll lives on this wrapper rather than the page, so a wide
 * table never forces the whole layout sideways.
 */
export function TableWrapper({ className, ...props }: ComponentProps<"div">) {
  return (
    <div
      className={cn(
        "relative w-full overflow-x-auto scrollbar-thin rounded-surface border border-border bg-surface",
        className,
      )}
      {...props}
    />
  );
}

export function Table({ className, ...props }: ComponentProps<"table">) {
  return (
    <table
      className={cn("w-full border-collapse text-body", className)}
      {...props}
    />
  );
}

/** Sticky so column headers stay visible while scrolling long result sets. */
export function TableHeader({ className, ...props }: ComponentProps<"thead">) {
  return (
    <thead
      className={cn(
        "sticky top-0 z-10 bg-surface-raised [&_tr]:border-b [&_tr]:border-border",
        className,
      )}
      {...props}
    />
  );
}

export function TableBody({ className, ...props }: ComponentProps<"tbody">) {
  return (
    <tbody className={cn("[&_tr:last-child]:border-0", className)} {...props} />
  );
}

export function TableRow({
  className,
  selected,
  ...props
}: ComponentProps<"tr"> & { selected?: boolean }) {
  return (
    <tr
      data-selected={selected || undefined}
      className={cn(
        "border-b border-border transition-colors",
        "hover:bg-surface-raised/60",
        "data-[selected]:bg-accent-subtle/50",
        className,
      )}
      {...props}
    />
  );
}

export function TableHead({ className, ...props }: ComponentProps<"th">) {
  return (
    <th
      className={cn(
        "h-9 px-3 text-left align-middle text-overline uppercase text-subtle font-semibold",
        className,
      )}
      {...props}
    />
  );
}

export function TableCell({ className, ...props }: ComponentProps<"td">) {
  return (
    <td className={cn("px-3 py-2.5 align-middle", className)} {...props} />
  );
}

/**
 * Clickable column header that cycles asc → desc.
 * The arrow shows the direction only for the currently active column.
 */
export function SortableHead({
  label,
  columnKey,
  activeKey,
  direction,
  onSort,
  className,
  align = "left",
}: {
  label: string;
  columnKey: string;
  activeKey: string | undefined;
  direction: SortDirection | undefined;
  onSort: (key: string) => void;
  className?: string;
  align?: "left" | "right";
}) {
  const isActive = activeKey === columnKey;
  const Icon = !isActive
    ? ChevronsUpDown
    : direction === "asc"
      ? ArrowUp
      : ArrowDown;

  return (
    // `aria-sort` belongs on the column header itself, not on the control
    // inside it — screen readers announce it as a property of the column.
    <TableHead
      aria-sort={
        isActive ? (direction === "asc" ? "ascending" : "descending") : "none"
      }
      className={cn(align === "right" && "text-right", className)}
    >
      <button
        type="button"
        onClick={() => onSort(columnKey)}
        className={cn(
          "inline-flex items-center gap-1 rounded-control text-overline uppercase font-semibold transition-colors",
          "hover:text-foreground",
          isActive ? "text-foreground" : "text-subtle",
          align === "right" && "flex-row-reverse",
        )}
      >
        {label}
        <Icon className="size-3" aria-hidden />
      </button>
    </TableHead>
  );
}
