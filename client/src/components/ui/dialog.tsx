"use client";

import { X } from "lucide-react";
import { Dialog as DialogPrimitive } from "radix-ui";
import type { ComponentProps, ReactNode } from "react";
import { cn } from "@/lib/utils/cn";
import { Button } from "./button";

export const Dialog = DialogPrimitive.Root;
export const DialogTrigger = DialogPrimitive.Trigger;
export const DialogClose = DialogPrimitive.Close;

function Overlay({
  className,
  ...props
}: ComponentProps<typeof DialogPrimitive.Overlay>) {
  return (
    <DialogPrimitive.Overlay
      className={cn("anim-overlay fixed inset-0 z-50 bg-overlay", className)}
      {...props}
    />
  );
}

/**
 * Centred modal. Radix handles focus trapping, scroll locking and Escape;
 * a title is required for screen readers, so `DialogContent` always renders one.
 */
export function DialogContent({
  title,
  description,
  children,
  footer,
  size = "md",
  className,
  ...props
}: Omit<ComponentProps<typeof DialogPrimitive.Content>, "title"> & {
  title: string;
  description?: string;
  footer?: ReactNode;
  size?: "sm" | "md" | "lg";
}) {
  const widths = {
    sm: "max-w-sm",
    md: "max-w-lg",
    lg: "max-w-2xl",
  } as const;

  return (
    <DialogPrimitive.Portal>
      <Overlay />
      <DialogPrimitive.Content
        className={cn(
          "anim-dialog fixed left-1/2 top-1/2 z-50 w-[calc(100vw-2rem)] -translate-x-1/2 -translate-y-1/2",
          "rounded-surface border border-border bg-surface shadow-lg",
          "focus:outline-none",
          widths[size],
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
            <Button variant="ghost" size="icon-sm" aria-label="Close dialog">
              <X />
            </Button>
          </DialogPrimitive.Close>
        </div>

        <div className="max-h-[70vh] overflow-y-auto scrollbar-thin px-5 py-4">
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

/**
 * Confirmation prompt for destructive or irreversible actions.
 * Controlled so the caller can keep the trigger row's state in sync.
 */
export function ConfirmDialog({
  open,
  onOpenChange,
  title,
  description,
  confirmLabel = "Confirm",
  cancelLabel = "Cancel",
  destructive = false,
  loading = false,
  onConfirm,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  description: string;
  confirmLabel?: string;
  cancelLabel?: string;
  destructive?: boolean;
  loading?: boolean;
  onConfirm: () => void;
}) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        title={title}
        size="sm"
        footer={
          <>
            <DialogClose asChild>
              <Button variant="secondary" size="sm">
                {cancelLabel}
              </Button>
            </DialogClose>
            <Button
              variant={destructive ? "danger" : "primary"}
              size="sm"
              loading={loading}
              onClick={onConfirm}
            >
              {confirmLabel}
            </Button>
          </>
        }
      >
        <p className="text-body text-muted">{description}</p>
      </DialogContent>
    </Dialog>
  );
}
