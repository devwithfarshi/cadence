"use client";

import { Tooltip as Primitive } from "radix-ui";
import type { ComponentProps, ReactNode } from "react";
import { cn } from "@/lib/utils/cn";

export const TooltipProvider = Primitive.Provider;

/**
 * Convenience wrapper — the trigger/content pairing is identical everywhere,
 * so call sites only supply the label and the element it describes.
 */
export function Tooltip({
  label,
  children,
  side = "top",
  align = "center",
  className,
  ...props
}: {
  label: ReactNode;
  children: ReactNode;
  side?: ComponentProps<typeof Primitive.Content>["side"];
  align?: ComponentProps<typeof Primitive.Content>["align"];
  className?: string;
} & Omit<ComponentProps<typeof Primitive.Root>, "children">) {
  return (
    <Primitive.Root {...props}>
      <Primitive.Trigger asChild>{children}</Primitive.Trigger>
      <Primitive.Portal>
        <Primitive.Content
          side={side}
          align={align}
          sideOffset={6}
          className={cn(
            "z-50 rounded-control bg-foreground px-2 py-1 text-label text-inverted shadow-md",
            "max-w-64",
            className,
          )}
        >
          {label}
          <Primitive.Arrow className="fill-foreground" width={10} height={5} />
        </Primitive.Content>
      </Primitive.Portal>
    </Primitive.Root>
  );
}
