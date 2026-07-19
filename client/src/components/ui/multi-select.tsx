"use client";

import { Check, ChevronDown, Search, X } from "lucide-react";
import { Popover } from "radix-ui";
import { useMemo, useRef, useState } from "react";
import { cn } from "@/lib/utils/cn";

export interface MultiSelectOption {
  value: string;
  label: string;
  /** Secondary line, e.g. a job title or email. */
  hint?: string;
}

/**
 * Multi-select combobox.
 *
 * Selections render as removable chips in the trigger, and the panel filters as
 * you type. Backspace on an empty search removes the last chip — the behaviour
 * people expect from a token field, and the reason this isn't just a checkbox
 * list in a dropdown.
 */
export function MultiSelect({
  options,
  selected,
  onChange,
  id,
  placeholder = "Select…",
  searchPlaceholder = "Search…",
  emptyMessage = "No matches",
  maxChips = 3,
  disabled,
  invalid,
  className,
  "aria-describedby": describedBy,
}: {
  options: MultiSelectOption[];
  selected: string[];
  onChange: (next: string[]) => void;
  id?: string;
  placeholder?: string;
  searchPlaceholder?: string;
  emptyMessage?: string;
  /** Chips shown before collapsing the rest into a "+N" counter. */
  maxChips?: number;
  disabled?: boolean;
  invalid?: boolean;
  className?: string;
  "aria-describedby"?: string;
}) {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const searchRef = useRef<HTMLInputElement>(null);

  const byValue = useMemo(
    () => new Map(options.map((option) => [option.value, option])),
    [options],
  );

  const filtered = useMemo(() => {
    const needle = query.trim().toLowerCase();
    if (!needle) return options;
    return options.filter(
      (option) =>
        option.label.toLowerCase().includes(needle) ||
        option.hint?.toLowerCase().includes(needle),
    );
  }, [options, query]);

  function toggle(value: string) {
    onChange(
      selected.includes(value)
        ? selected.filter((entry) => entry !== value)
        : [...selected, value],
    );
  }

  const shownChips = selected.slice(0, maxChips);
  const overflow = selected.length - shownChips.length;

  return (
    <Popover.Root
      open={open}
      onOpenChange={(next) => {
        setOpen(next);
        if (!next) setQuery("");
      }}
    >
      <Popover.Trigger asChild>
        {/* A div with role="combobox" rather than a <button>: the chips contain
            their own remove buttons, and a button cannot legally nest a button.
            Radix supplies aria-expanded and the click handling. */}
        <div
          id={id}
          role="combobox"
          aria-expanded={open}
          aria-haspopup="listbox"
          tabIndex={disabled ? -1 : 0}
          aria-disabled={disabled || undefined}
          aria-invalid={invalid || undefined}
          aria-describedby={describedBy}
          className={cn(
            "flex min-h-9 w-full items-center gap-1.5 rounded-control border border-border bg-surface px-2 py-1 text-left",
            "transition-colors hover:border-border-strong",
            "focus:outline-none focus-visible:border-accent focus-visible:ring-2 focus-visible:ring-accent/25",
            "disabled:cursor-not-allowed disabled:bg-surface-sunken",
            invalid &&
              "border-danger focus-visible:border-danger focus-visible:ring-danger/25",
            className,
          )}
        >
          <span className="flex min-w-0 flex-1 flex-wrap items-center gap-1">
            {selected.length === 0 ? (
              <span className="px-1 text-body text-subtle">{placeholder}</span>
            ) : (
              <>
                {shownChips.map((value) => (
                  <span
                    key={value}
                    className="flex items-center gap-1 rounded-[4px] bg-surface-raised py-0.5 pl-1.5 pr-0.5 text-label text-foreground"
                  >
                    {byValue.get(value)?.label ?? value}
                    <button
                      type="button"
                      aria-label={`Remove ${byValue.get(value)?.label ?? value}`}
                      onClick={(event) => {
                        // Removing a chip must not also open the panel.
                        event.stopPropagation();
                        toggle(value);
                      }}
                      className="flex size-4 items-center justify-center rounded-[3px] text-subtle hover:bg-border hover:text-foreground"
                    >
                      <X className="size-3" />
                    </button>
                  </span>
                ))}
                {overflow > 0 ? (
                  <span className="px-1 text-label text-muted tabular">
                    +{overflow}
                  </span>
                ) : null}
              </>
            )}
          </span>

          <ChevronDown className="size-4 shrink-0 text-subtle" aria-hidden />
        </div>
      </Popover.Trigger>

      <Popover.Portal>
        <Popover.Content
          align="start"
          sideOffset={6}
          onOpenAutoFocus={(event) => {
            // Land in the search field, not on the first option.
            event.preventDefault();
            searchRef.current?.focus();
          }}
          className="z-50 w-[var(--radix-popover-trigger-width)] min-w-56 overflow-hidden rounded-surface border border-border bg-surface shadow-md"
        >
          <div className="flex items-center gap-2 border-b border-border px-2.5">
            <Search className="size-3.5 shrink-0 text-subtle" aria-hidden />
            <input
              ref={searchRef}
              value={query}
              onChange={(event) => setQuery(event.target.value)}
              onKeyDown={(event) => {
                // Token-field convention: backspace on an empty query pops the
                // last chip.
                if (
                  event.key === "Backspace" &&
                  query === "" &&
                  selected.length > 0
                ) {
                  onChange(selected.slice(0, -1));
                }
              }}
              placeholder={searchPlaceholder}
              aria-label={searchPlaceholder}
              className="h-9 w-full bg-transparent text-body text-foreground placeholder:text-subtle focus:outline-none"
            />
          </div>

          <div className="max-h-56 overflow-y-auto scrollbar-thin p-1">
            {filtered.length === 0 ? (
              <p className="px-2 py-4 text-center text-caption text-subtle">
                {emptyMessage}
              </p>
            ) : (
              filtered.map((option) => {
                const isSelected = selected.includes(option.value);

                return (
                  <button
                    key={option.value}
                    type="button"
                    onClick={() => toggle(option.value)}
                    aria-pressed={isSelected}
                    className="flex w-full items-center gap-2 rounded-control px-2 py-1.5 text-left transition-colors hover:bg-surface-raised"
                  >
                    <span
                      aria-hidden
                      className={cn(
                        "flex size-4 shrink-0 items-center justify-center rounded-[4px] border",
                        isSelected
                          ? "border-accent bg-accent text-accent-foreground"
                          : "border-border-strong",
                      )}
                    >
                      {isSelected ? (
                        <Check className="size-3" strokeWidth={3} />
                      ) : null}
                    </span>

                    <span className="min-w-0 flex-1">
                      <span className="block truncate text-body text-foreground">
                        {option.label}
                      </span>
                      {option.hint ? (
                        <span className="block truncate text-label text-subtle">
                          {option.hint}
                        </span>
                      ) : null}
                    </span>
                  </button>
                );
              })
            )}
          </div>

          {selected.length > 0 ? (
            <div className="border-t border-border p-1">
              <button
                type="button"
                onClick={() => onChange([])}
                className="w-full rounded-control px-2 py-1.5 text-left text-caption text-muted transition-colors hover:bg-surface-raised hover:text-foreground"
              >
                Clear all ({selected.length})
              </button>
            </div>
          ) : null}
        </Popover.Content>
      </Popover.Portal>
    </Popover.Root>
  );
}
