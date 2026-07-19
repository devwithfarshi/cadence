"use client";

import { Check, ChevronDown } from "lucide-react";
import { Select as Primitive } from "radix-ui";
import type { ComponentProps } from "react";
import { cn } from "@/lib/utils/cn";

export const Select = Primitive.Root;
export const SelectGroup = Primitive.Group;
export const SelectValue = Primitive.Value;

export function SelectTrigger({
  className,
  children,
  size = "md",
  ...props
}: ComponentProps<typeof Primitive.Trigger> & { size?: "sm" | "md" }) {
  return (
    <Primitive.Trigger
      className={cn(
        "flex items-center justify-between gap-2 rounded-control border border-border bg-surface text-body text-foreground",
        "hover:border-border-strong transition-colors",
        "focus:outline-none focus-visible:border-accent focus-visible:ring-2 focus-visible:ring-accent/25",
        "disabled:cursor-not-allowed disabled:opacity-50",
        "data-[placeholder]:text-subtle",
        size === "sm" ? "h-8 px-2.5 text-caption" : "h-9 px-3",
        className,
      )}
      {...props}
    >
      {children}
      <Primitive.Icon asChild>
        <ChevronDown className="size-4 shrink-0 text-subtle" />
      </Primitive.Icon>
    </Primitive.Trigger>
  );
}

export function SelectContent({
  className,
  children,
  position = "popper",
  ...props
}: ComponentProps<typeof Primitive.Content>) {
  return (
    <Primitive.Portal>
      <Primitive.Content
        position={position}
        sideOffset={6}
        className={cn(
          "z-50 min-w-[8rem] overflow-hidden rounded-surface border border-border bg-surface shadow-md",
          // Match the trigger width so the menu doesn't jump around.
          position === "popper" && "w-[var(--radix-select-trigger-width)]",
          className,
        )}
        {...props}
      >
        <Primitive.Viewport className="max-h-72 p-1 scrollbar-thin">
          {children}
        </Primitive.Viewport>
      </Primitive.Content>
    </Primitive.Portal>
  );
}

export function SelectItem({
  className,
  children,
  ...props
}: ComponentProps<typeof Primitive.Item>) {
  return (
    <Primitive.Item
      className={cn(
        "relative flex cursor-pointer select-none items-center rounded-control py-1.5 pl-2 pr-8",
        "text-body text-foreground outline-none",
        "data-[highlighted]:bg-surface-raised",
        "data-[disabled]:pointer-events-none data-[disabled]:opacity-50",
        className,
      )}
      {...props}
    >
      <Primitive.ItemText>{children}</Primitive.ItemText>
      <Primitive.ItemIndicator className="absolute right-2">
        <Check className="size-3.5 text-accent" />
      </Primitive.ItemIndicator>
    </Primitive.Item>
  );
}

export function SelectLabel({
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

export function SelectSeparator({
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
