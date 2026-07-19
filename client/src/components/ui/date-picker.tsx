"use client";

import {
  addMonths,
  eachDayOfInterval,
  endOfMonth,
  endOfWeek,
  format,
  isSameDay,
  isSameMonth,
  isToday,
  isValid,
  parse,
  startOfMonth,
  startOfWeek,
  subMonths,
} from "date-fns";
import { CalendarIcon, ChevronLeft, ChevronRight, X } from "lucide-react";
import { Popover } from "radix-ui";
import { useEffect, useMemo, useState } from "react";
import { cn } from "@/lib/utils/cn";
import { Button } from "./button";

const WEEKDAYS = ["Mo", "Tu", "We", "Th", "Fr", "Sa", "Su"];
const INPUT_FORMAT = "dd/MM/yyyy";

/** ISO date (yyyy-MM-dd) ↔ Date, with no timezone drift from parsing. */
function fromISO(value: string | null): Date | null {
  if (!value) return null;
  const parsed = parse(value.slice(0, 10), "yyyy-MM-dd", new Date());
  return isValid(parsed) ? parsed : null;
}

function toISO(date: Date): string {
  return format(date, "yyyy-MM-dd");
}

/**
 * Calendar popover with a typed fallback.
 *
 * Replaces `<input type="date">`, which renders as an OS widget that ignores
 * the design system entirely and looks different on every platform. The text
 * field stays editable so keyboard users can type a date rather than being
 * forced through a grid.
 */
export function DatePicker({
  value,
  onChange,
  id,
  placeholder = "dd/mm/yyyy",
  clearable = true,
  disabled,
  className,
  "aria-describedby": describedBy,
  invalid,
}: {
  /** ISO date string (yyyy-MM-dd) or null. */
  value: string | null;
  onChange: (value: string | null) => void;
  id?: string;
  placeholder?: string;
  clearable?: boolean;
  disabled?: boolean;
  className?: string;
  "aria-describedby"?: string;
  invalid?: boolean;
}) {
  const selected = fromISO(value);

  const [open, setOpen] = useState(false);
  const [draft, setDraft] = useState(
    selected ? format(selected, INPUT_FORMAT) : "",
  );
  const [month, setMonth] = useState(() => selected ?? new Date());

  // Keyed on the raw `value` string, never on `selected`: the latter is a fresh
  // Date instance every render, so depending on it re-runs the effect forever.
  useEffect(() => {
    const next = fromISO(value);
    setDraft(next ? format(next, INPUT_FORMAT) : "");
    if (next) setMonth(next);
  }, [value]);

  const days = useMemo(
    () =>
      eachDayOfInterval({
        start: startOfWeek(startOfMonth(month), { weekStartsOn: 1 }),
        end: endOfWeek(endOfMonth(month), { weekStartsOn: 1 }),
      }),
    [month],
  );

  /** Commits typed text, reverting if it isn't a real date. */
  function commitDraft() {
    if (!draft.trim()) {
      onChange(null);
      return;
    }

    const parsed = parse(draft, INPUT_FORMAT, new Date());
    if (isValid(parsed)) {
      onChange(toISO(parsed));
      setMonth(parsed);
    } else {
      setDraft(selected ? format(selected, INPUT_FORMAT) : "");
    }
  }

  return (
    <div className={cn("relative", className)}>
      <input
        id={id}
        type="text"
        inputMode="numeric"
        value={draft}
        disabled={disabled}
        placeholder={placeholder}
        aria-invalid={invalid || undefined}
        aria-describedby={describedBy}
        onChange={(event) => setDraft(event.target.value)}
        onBlur={commitDraft}
        onKeyDown={(event) => {
          if (event.key === "Enter") {
            event.preventDefault();
            commitDraft();
          }
        }}
        className={cn(
          "h-9 w-full rounded-control border border-border bg-surface pl-3 text-body text-foreground",
          // Room for the calendar button, plus the clear button when present.
          // Reserving both unconditionally clipped the date in narrow columns.
          clearable ? "pr-16" : "pr-9",
          "placeholder:text-subtle transition-colors hover:border-border-strong",
          "focus:outline-none focus-visible:border-accent focus-visible:ring-2 focus-visible:ring-accent/25",
          "disabled:cursor-not-allowed disabled:bg-surface-sunken disabled:text-subtle",
          invalid &&
            "border-danger focus-visible:border-danger focus-visible:ring-danger/25",
        )}
      />

      <div className="absolute right-1 top-1/2 flex -translate-y-1/2 items-center">
        {clearable && value ? (
          <button
            type="button"
            onClick={() => onChange(null)}
            disabled={disabled}
            aria-label="Clear date"
            className="flex size-7 items-center justify-center rounded-control text-subtle hover:bg-surface-raised hover:text-foreground"
          >
            <X className="size-3.5" />
          </button>
        ) : null}

        <Popover.Root open={open} onOpenChange={setOpen}>
          <Popover.Trigger asChild>
            <button
              type="button"
              disabled={disabled}
              aria-label="Open calendar"
              className="flex size-7 items-center justify-center rounded-control text-subtle hover:bg-surface-raised hover:text-foreground"
            >
              <CalendarIcon className="size-4" />
            </button>
          </Popover.Trigger>

          <Popover.Portal>
            <Popover.Content
              align="end"
              sideOffset={6}
              className="z-50 rounded-surface border border-border bg-surface p-3 shadow-md"
            >
              <div className="mb-2 flex items-center justify-between gap-2">
                <Button
                  variant="ghost"
                  size="icon-sm"
                  aria-label="Previous month"
                  onClick={() => setMonth((m) => subMonths(m, 1))}
                >
                  <ChevronLeft />
                </Button>
                <p className="text-caption font-medium text-foreground">
                  {format(month, "MMMM yyyy")}
                </p>
                <Button
                  variant="ghost"
                  size="icon-sm"
                  aria-label="Next month"
                  onClick={() => setMonth((m) => addMonths(m, 1))}
                >
                  <ChevronRight />
                </Button>
              </div>

              <div className="grid grid-cols-7 gap-0.5">
                {WEEKDAYS.map((day) => (
                  <div
                    key={day}
                    className="py-1 text-center text-overline uppercase text-subtle"
                  >
                    {day}
                  </div>
                ))}

                {days.map((day) => {
                  const isSelected = selected
                    ? isSameDay(day, selected)
                    : false;
                  const outside = !isSameMonth(day, month);

                  return (
                    <button
                      key={day.toISOString()}
                      type="button"
                      onClick={() => {
                        onChange(toISO(day));
                        setOpen(false);
                      }}
                      aria-pressed={isSelected}
                      aria-label={format(day, "d MMMM yyyy")}
                      className={cn(
                        "flex size-8 items-center justify-center rounded-control text-caption tabular transition-colors",
                        isSelected
                          ? "bg-accent font-semibold text-accent-foreground"
                          : isToday(day)
                            ? "border border-accent/40 text-foreground hover:bg-surface-raised"
                            : outside
                              ? "text-subtle hover:bg-surface-raised"
                              : "text-foreground hover:bg-surface-raised",
                      )}
                    >
                      {format(day, "d")}
                    </button>
                  );
                })}
              </div>

              <div className="mt-2 flex items-center justify-between gap-2 border-t border-border pt-2">
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={() => {
                    onChange(toISO(new Date()));
                    setOpen(false);
                  }}
                >
                  Today
                </Button>
                {clearable ? (
                  <Button
                    variant="ghost"
                    size="sm"
                    onClick={() => {
                      onChange(null);
                      setOpen(false);
                    }}
                  >
                    Clear
                  </Button>
                ) : null}
              </div>
            </Popover.Content>
          </Popover.Portal>
        </Popover.Root>
      </div>
    </div>
  );
}

/**
 * Date + time, for scheduling.
 *
 * Composes the picker with a plain time field rather than reaching back to
 * `datetime-local`, so the date half keeps the designed calendar.
 */
export function DateTimePicker({
  value,
  onChange,
  id,
  disabled,
  invalid,
  className,
  "aria-describedby": describedBy,
}: {
  /** Full ISO timestamp, or null. */
  value: string | null;
  onChange: (value: string | null) => void;
  id?: string;
  disabled?: boolean;
  invalid?: boolean;
  className?: string;
  "aria-describedby"?: string;
}) {
  const current = value ? new Date(value) : null;
  const datePart = current ? format(current, "yyyy-MM-dd") : null;
  const timePart = current ? format(current, "HH:mm") : "09:00";

  function merge(nextDate: string | null, nextTime: string) {
    if (!nextDate) {
      onChange(null);
      return;
    }
    const combined = parse(
      `${nextDate} ${nextTime}`,
      "yyyy-MM-dd HH:mm",
      new Date(),
    );
    onChange(isValid(combined) ? combined.toISOString() : null);
  }

  return (
    <div className={cn("flex gap-2", className)}>
      <DatePicker
        id={id}
        value={datePart}
        onChange={(next) => merge(next, timePart)}
        disabled={disabled}
        invalid={invalid}
        clearable={false}
        aria-describedby={describedBy}
        className="flex-1"
      />
      <input
        type="time"
        value={timePart}
        disabled={disabled}
        aria-label="Time"
        onChange={(event) => merge(datePart, event.target.value)}
        className={cn(
          "h-9 w-28 shrink-0 rounded-control border border-border bg-surface px-2 text-body text-foreground",
          "transition-colors hover:border-border-strong",
          "focus:outline-none focus-visible:border-accent focus-visible:ring-2 focus-visible:ring-accent/25",
          "disabled:cursor-not-allowed disabled:bg-surface-sunken disabled:text-subtle",
        )}
      />
    </div>
  );
}
