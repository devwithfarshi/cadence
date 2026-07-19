"use client";

import { Check, Minus } from "lucide-react";
import { Checkbox as Primitive } from "radix-ui";
import type { ComponentProps } from "react";
import { cn } from "@/lib/utils/cn";

/**
 * Supports the indeterminate state (`checked="indeterminate"`), which the
 * data-table header uses when only some rows on the page are selected.
 */
export function Checkbox({
  className,
  ...props
}: ComponentProps<typeof Primitive.Root>) {
  return (
    <Primitive.Root
      className={cn(
        "peer flex size-4 shrink-0 items-center justify-center rounded-[4px] border border-border-strong bg-surface",
        "transition-colors hover:border-accent",
        "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/25",
        "data-[state=checked]:border-accent data-[state=checked]:bg-accent",
        "data-[state=indeterminate]:border-accent data-[state=indeterminate]:bg-accent",
        "disabled:cursor-not-allowed disabled:opacity-50",
        className,
      )}
      {...props}
    >
      <Primitive.Indicator className="text-accent-foreground">
        {props.checked === "indeterminate" ? (
          <Minus className="size-3" strokeWidth={3} />
        ) : (
          <Check className="size-3" strokeWidth={3} />
        )}
      </Primitive.Indicator>
    </Primitive.Root>
  );
}
