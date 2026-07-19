"use client";

import { X } from "lucide-react";
import { Dialog as DialogPrimitive } from "radix-ui";
import type { ComponentProps, ReactNode } from "react";
import { cn } from "@/lib/utils/cn";
import { Button } from "./button";

/**
 * Edge-anchored drawer.
 *
 * Built on Dialog rather than a separate primitive so it inherits the same
 * focus trap, scroll lock and Escape handling — a drawer is a modal that
 * happens to slide in from the side.
 */
export const Sheet = DialogPrimitive.Root;
export const SheetTrigger = DialogPrimitive.Trigger;
export const SheetClose = DialogPrimitive.Close;

export function SheetContent({
  side = "right",
  title,
  description,
  children,
  footer,
  className,
  ...props
}: Omit<ComponentProps<typeof DialogPrimitive.Content>, "title"> & {
  side?: "right" | "left";
  title: string;
  description?: string;
  footer?: ReactNode;
}) {
  return (
    <DialogPrimitive.Portal>
      <DialogPrimitive.Overlay className="anim-overlay fixed inset-0 z-50 bg-overlay" />
      <DialogPrimitive.Content
        className={cn(
          "fixed inset-y-0 z-50 flex w-full max-w-md flex-col border-border bg-surface focus:outline-none",
          side === "right"
            ? "anim-sheet-right right-0 border-l"
            : "anim-sheet-left left-0 border-r",
          className,
        )}
        {...props}
      >
        <div className="flex items-start justify-between gap-4 border-b border-border px-5 py-4">
          <div className="space-y-1">
            <DialogPrimitive.Title className="text-subheading text-foreground">
              {title}
            </DialogPrimitive.Title>
            {description ? (
              <DialogPrimitive.Description className="text-caption text-muted">
                {description}
              </DialogPrimitive.Description>
            ) : null}
          </div>
          <DialogPrimitive.Close asChild>
            <Button variant="ghost" size="icon-sm" aria-label="Close panel">
              <X />
            </Button>
          </DialogPrimitive.Close>
        </div>

        <div className="flex-1 overflow-y-auto scrollbar-thin px-5 py-4">
          {children}
        </div>

        {footer ? (
          <div className="flex items-center justify-end gap-2 border-t border-border px-5 py-3">
            {footer}
          </div>
        ) : null}
      </DialogPrimitive.Content>
    </DialogPrimitive.Portal>
  );
}
