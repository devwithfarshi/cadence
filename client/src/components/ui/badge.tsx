import { cva, type VariantProps } from "class-variance-authority";
import type { ComponentProps } from "react";
import { cn } from "@/lib/utils/cn";

const badgeVariants = cva(
  "inline-flex items-center gap-1.5 rounded-control border font-medium whitespace-nowrap",
  {
    variants: {
      tone: {
        neutral: "border-border bg-surface-raised text-muted",
        accent:
          "border-transparent bg-accent-subtle text-accent-subtle-foreground",
        success: "border-transparent bg-success-subtle text-success-foreground",
        warning: "border-transparent bg-warning-subtle text-warning-foreground",
        danger: "border-transparent bg-danger-subtle text-danger-foreground",
        info: "border-transparent bg-info-subtle text-info-foreground",
        outline: "border-border-strong bg-transparent text-muted",
      },
      size: {
        sm: "px-1.5 py-0.5 text-overline uppercase",
        md: "px-2 py-0.5 text-label",
      },
    },
    defaultVariants: { tone: "neutral", size: "md" },
  },
);

export interface BadgeProps
  extends ComponentProps<"span">,
    VariantProps<typeof badgeVariants> {}

export function Badge({ className, tone, size, ...props }: BadgeProps) {
  return (
    <span className={cn(badgeVariants({ tone, size }), className)} {...props} />
  );
}

/** Small filled circle used to prefix status badges. */
export function Dot({ className }: { className?: string }) {
  return (
    <span
      aria-hidden
      className={cn("size-1.5 rounded-full bg-current", className)}
    />
  );
}
