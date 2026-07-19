"use client";

import { Tabs as Primitive } from "radix-ui";
import type { ComponentProps } from "react";
import { cn } from "@/lib/utils/cn";

export const Tabs = Primitive.Root;

/** Underlined tab bar — reads as navigation rather than as a control group. */
export function TabsList({
  className,
  ...props
}: ComponentProps<typeof Primitive.List>) {
  return (
    <Primitive.List
      className={cn(
        "flex items-center gap-1 border-b border-border overflow-x-auto scrollbar-thin",
        className,
      )}
      {...props}
    />
  );
}

export function TabsTrigger({
  className,
  ...props
}: ComponentProps<typeof Primitive.Trigger>) {
  return (
    <Primitive.Trigger
      className={cn(
        "relative whitespace-nowrap px-3 py-2 text-body font-medium text-muted transition-colors",
        "hover:text-foreground",
        "data-[state=active]:text-foreground",
        // The indicator sits on the shared bottom border of the list.
        "after:absolute after:inset-x-0 after:-bottom-px after:h-0.5 after:bg-transparent",
        "data-[state=active]:after:bg-accent",
        className,
      )}
      {...props}
    />
  );
}

export function TabsContent({
  className,
  ...props
}: ComponentProps<typeof Primitive.Content>) {
  return (
    <Primitive.Content
      className={cn("focus-visible:outline-none", className)}
      {...props}
    />
  );
}

/** Count chip shown alongside a tab label. */
export function TabsCount({ children }: { children: number }) {
  return (
    <span className="ml-1.5 rounded-full bg-surface-raised px-1.5 py-0.5 text-overline text-muted tabular">
      {children}
    </span>
  );
}
