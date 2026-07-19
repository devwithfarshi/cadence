"use client";

import { ChevronDown } from "lucide-react";
import { Accordion as Primitive } from "radix-ui";
import type { ComponentProps } from "react";
import { cn } from "@/lib/utils/cn";

/**
 * Disclosure list.
 *
 * Radix handles the roving focus and `aria-expanded` wiring; the styling here
 * keeps it recessive — a hairline divider between rows rather than a stack of
 * boxes, so a long list of sections doesn't read as heavy.
 */
export const Accordion = Primitive.Root;

export function AccordionItem({
  className,
  ...props
}: ComponentProps<typeof Primitive.Item>) {
  return (
    <Primitive.Item
      className={cn("border-b border-border last:border-0", className)}
      {...props}
    />
  );
}

export function AccordionTrigger({
  className,
  children,
  ...props
}: ComponentProps<typeof Primitive.Trigger>) {
  return (
    <Primitive.Header className="flex">
      <Primitive.Trigger
        className={cn(
          "group flex flex-1 items-center justify-between gap-3 py-3 text-left",
          "text-body font-medium text-foreground transition-colors hover:text-accent",
          "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/25",
          className,
        )}
        {...props}
      >
        {children}
        <ChevronDown
          aria-hidden
          className="size-4 shrink-0 text-subtle transition-transform duration-200 group-data-[state=open]:rotate-180"
        />
      </Primitive.Trigger>
    </Primitive.Header>
  );
}

export function AccordionContent({
  className,
  children,
  ...props
}: ComponentProps<typeof Primitive.Content>) {
  return (
    <Primitive.Content
      // Radix exposes the measured height as a CSS variable, which is what
      // makes an open/close transition possible without a fixed height.
      className="overflow-hidden data-[state=closed]:anim-collapse data-[state=open]:anim-expand"
      {...props}
    >
      <div className={cn("pb-3 text-body text-muted", className)}>
        {children}
      </div>
    </Primitive.Content>
  );
}
