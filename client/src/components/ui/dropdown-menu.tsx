"use client";

import { Check } from "lucide-react";
import { DropdownMenu as Primitive } from "radix-ui";
import type { ComponentProps } from "react";
import { cn } from "@/lib/utils/cn";

export const DropdownMenu = Primitive.Root;
export const DropdownMenuTrigger = Primitive.Trigger;
export const DropdownMenuGroup = Primitive.Group;
export const DropdownMenuSub = Primitive.Sub;
export const DropdownMenuRadioGroup = Primitive.RadioGroup;

const surfaceClasses = [
  "z-50 min-w-44 overflow-hidden rounded-surface border border-border bg-surface p-1",
  "shadow-md",
].join(" ");

export function DropdownMenuContent({
  className,
  sideOffset = 6,
  align = "end",
  ...props
}: ComponentProps<typeof Primitive.Content>) {
  return (
    <Primitive.Portal>
      <Primitive.Content
        sideOffset={sideOffset}
        align={align}
        className={cn(surfaceClasses, className)}
        {...props}
      />
    </Primitive.Portal>
  );
}

const itemClasses = [
  "relative flex cursor-pointer select-none items-center gap-2 rounded-control px-2 py-1.5",
  "text-body text-foreground outline-none",
  "data-[highlighted]:bg-surface-raised",
  "data-[disabled]:pointer-events-none data-[disabled]:opacity-50",
  "[&_svg]:size-4 [&_svg]:shrink-0 [&_svg]:text-muted",
].join(" ");

export function DropdownMenuItem({
  className,
  destructive,
  ...props
}: ComponentProps<typeof Primitive.Item> & { destructive?: boolean }) {
  return (
    <Primitive.Item
      className={cn(
        itemClasses,
        destructive &&
          "text-danger data-[highlighted]:bg-danger-subtle [&_svg]:text-danger",
        className,
      )}
      {...props}
    />
  );
}

export function DropdownMenuCheckboxItem({
  className,
  children,
  ...props
}: ComponentProps<typeof Primitive.CheckboxItem>) {
  return (
    <Primitive.CheckboxItem
      className={cn(itemClasses, "pr-8", className)}
      {...props}
    >
      {children}
      <Primitive.ItemIndicator className="absolute right-2">
        <Check className="size-3.5 text-accent" />
      </Primitive.ItemIndicator>
    </Primitive.CheckboxItem>
  );
}

export function DropdownMenuRadioItem({
  className,
  children,
  ...props
}: ComponentProps<typeof Primitive.RadioItem>) {
  return (
    <Primitive.RadioItem
      className={cn(itemClasses, "pr-8", className)}
      {...props}
    >
      {children}
      <Primitive.ItemIndicator className="absolute right-2">
        <Check className="size-3.5 text-accent" />
      </Primitive.ItemIndicator>
    </Primitive.RadioItem>
  );
}

export function DropdownMenuLabel({
  className,
  ...props
}: ComponentProps<typeof Primitive.Label>) {
  return (
    <Primitive.Label
      className={cn(
        "px-2 py-1.5 text-overline uppercase text-subtle",
        className,
      )}
      {...props}
    />
  );
}

export function DropdownMenuSeparator({
  className,
  ...props
}: ComponentProps<typeof Primitive.Separator>) {
  return (
    <Primitive.Separator
      className={cn("-mx-1 my-1 h-px bg-border", className)}
      {...props}
    />
  );
}

/** Right-aligned keyboard hint inside a menu item. */
export function DropdownMenuShortcut({
  className,
  ...props
}: ComponentProps<"span">) {
  return (
    <span
      className={cn("ml-auto text-label text-subtle tabular", className)}
      {...props}
    />
  );
}
