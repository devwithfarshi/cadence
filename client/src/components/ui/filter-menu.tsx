"use client";

import { ChevronDown, ListFilter } from "lucide-react";
import { DropdownMenu as Primitive } from "radix-ui";
import { cn } from "@/lib/utils/cn";
import { Button } from "./button";
import {
  DropdownMenuCheckboxItem,
  DropdownMenuContent,
  DropdownMenuLabel,
  DropdownMenuSeparator,
} from "./dropdown-menu";

export interface FilterOption<T extends string> {
  value: T;
  label: string;
}

/**
 * Multi-select dropdown filter.
 *
 * Controlled: the parent owns the selected array, which keeps filter state in
 * one place alongside search, sort and pagination.
 */
export function FilterMenu<T extends string>({
  label,
  options,
  selected,
  onChange,
  icon = true,
  className,
}: {
  label: string;
  options: FilterOption<T>[];
  selected: T[];
  onChange: (next: T[]) => void;
  icon?: boolean;
  className?: string;
}) {
  const toggle = (value: T) => {
    onChange(
      selected.includes(value)
        ? selected.filter((entry) => entry !== value)
        : [...selected, value],
    );
  };

  const active = selected.length > 0;

  return (
    <Primitive.Root>
      <Primitive.Trigger asChild>
        <Button
          variant="secondary"
          size="sm"
          className={cn(
            active && "border-accent/40 bg-accent-subtle",
            className,
          )}
        >
          {icon ? <ListFilter /> : null}
          {label}
          {active ? (
            <span className="rounded-full bg-accent px-1.5 text-overline font-semibold text-accent-foreground tabular">
              {selected.length}
            </span>
          ) : null}
          <ChevronDown />
        </Button>
      </Primitive.Trigger>

      <DropdownMenuContent align="start" className="w-52">
        <DropdownMenuLabel>{label}</DropdownMenuLabel>

        {options.map((option) => (
          <DropdownMenuCheckboxItem
            key={option.value}
            checked={selected.includes(option.value)}
            // Keep the menu open so several values can be picked in one go.
            onSelect={(event) => event.preventDefault()}
            onCheckedChange={() => toggle(option.value)}
          >
            {option.label}
          </DropdownMenuCheckboxItem>
        ))}

        {active ? (
          <>
            <DropdownMenuSeparator />
            <button
              type="button"
              onClick={() => onChange([])}
              className="w-full rounded-control px-2 py-1.5 text-left text-caption text-muted transition-colors hover:bg-surface-raised hover:text-foreground"
            >
              Clear selection
            </button>
          </>
        ) : null}
      </DropdownMenuContent>
    </Primitive.Root>
  );
}
