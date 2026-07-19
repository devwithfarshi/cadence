"use client";

import { cva, type VariantProps } from "class-variance-authority";
import { Loader2 } from "lucide-react";
import { Slot } from "radix-ui";
import type { ComponentProps } from "react";
import { cn } from "@/lib/utils/cn";

const buttonVariants = cva(
  [
    "inline-flex items-center justify-center gap-2 whitespace-nowrap",
    "rounded-control font-medium transition-colors",
    "disabled:pointer-events-none disabled:opacity-50",
    "[&_svg]:shrink-0 [&_svg]:size-4",
  ].join(" "),
  {
    variants: {
      variant: {
        primary:
          "bg-accent text-accent-foreground hover:bg-accent-hover active:bg-accent-active",
        secondary:
          "border border-border bg-surface text-foreground hover:bg-surface-raised hover:border-border-strong",
        ghost: "text-muted hover:bg-surface-raised hover:text-foreground",
        danger: "bg-danger text-white hover:opacity-90 active:opacity-80",
        // For destructive actions that still need to read as secondary weight.
        "danger-outline":
          "border border-danger/40 text-danger hover:bg-danger-subtle",
        link: "text-accent underline-offset-4 hover:underline",
      },
      size: {
        sm: "h-8 px-2.5 text-caption",
        md: "h-9 px-3.5 text-body",
        lg: "h-10 px-4 text-body",
        // Square icon-only buttons; caller supplies an aria-label.
        "icon-sm": "size-8",
        icon: "size-9",
      },
    },
    defaultVariants: { variant: "secondary", size: "md" },
  },
);

export interface ButtonProps
  extends ComponentProps<"button">,
    VariantProps<typeof buttonVariants> {
  /** Render as the single child element instead of a `<button>`. */
  asChild?: boolean;
  /** Shows a spinner and blocks interaction while true. */
  loading?: boolean;
}

export function Button({
  className,
  variant,
  size,
  asChild = false,
  loading = false,
  disabled,
  children,
  ...props
}: ButtonProps) {
  // Slot requires exactly one child, so the spinner is only ever added when
  // rendering a real <button>.
  if (asChild) {
    return (
      <Slot.Root
        className={cn(buttonVariants({ variant, size }), className)}
        {...props}
      >
        {children}
      </Slot.Root>
    );
  }

  return (
    <button
      className={cn(buttonVariants({ variant, size }), className)}
      disabled={disabled || loading}
      aria-busy={loading || undefined}
      {...props}
    >
      {loading ? <Loader2 className="animate-spin" aria-hidden /> : null}
      {children}
    </button>
  );
}

export { buttonVariants };
