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
  startOfMonth,
  startOfWeek,
  subMonths,
} from "date-fns";
import { CalendarOff, ChevronLeft, ChevronRight } from "lucide-react";
import { useMemo, useState } from "react";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/feedback";
import { cn } from "@/lib/utils/cn";
import type { ActionItem } from "@/types/domain";

const PRIORITY_DOT = {
  low: "bg-subtle",
  medium: "bg-info",
  high: "bg-warning",
  urgent: "bg-danger",
} as const;

const WEEKDAYS = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

export function TaskCalendar({
  tasks,
  loading,
  onOpenTask,
}: {
  tasks: ActionItem[];
  loading: boolean;
  onOpenTask: (task: ActionItem) => void;
}) {
  const [month, setMonth] = useState(() => startOfMonth(new Date()));

  // Weeks start Monday, which is the convention for a work calendar.
  const days = useMemo(
    () =>
      eachDayOfInterval({
        start: startOfWeek(startOfMonth(month), { weekStartsOn: 1 }),
        end: endOfWeek(endOfMonth(month), { weekStartsOn: 1 }),
      }),
    [month],
  );

  const dated = tasks.filter((task) => task.dueDate !== null);
  const undated = tasks.filter((task) => task.dueDate === null);

  if (loading) {
    return <Skeleton className="h-[32rem] w-full" />;
  }

  return (
    <div className="space-y-4">
      <div className="overflow-hidden rounded-surface border border-border bg-surface">
        {/* Month navigation */}
        <div className="flex items-center justify-between gap-2 border-b border-border px-3 py-2.5">
          <h3 className="text-subheading text-foreground">
            {format(month, "MMMM yyyy")}
          </h3>

          <div className="flex items-center gap-1">
            <Button
              variant="ghost"
              size="icon-sm"
              aria-label="Previous month"
              onClick={() => setMonth((m) => subMonths(m, 1))}
            >
              <ChevronLeft />
            </Button>
            <Button
              variant="secondary"
              size="sm"
              onClick={() => setMonth(startOfMonth(new Date()))}
            >
              Today
            </Button>
            <Button
              variant="ghost"
              size="icon-sm"
              aria-label="Next month"
              onClick={() => setMonth((m) => addMonths(m, 1))}
            >
              <ChevronRight />
            </Button>
          </div>
        </div>

        {/* Weekday header */}
        <div className="grid grid-cols-7 border-b border-border bg-surface-sunken">
          {WEEKDAYS.map((day) => (
            <div
              key={day}
              className="px-2 py-1.5 text-center text-overline uppercase text-subtle"
            >
              {day}
            </div>
          ))}
        </div>

        {/* Day grid */}
        <div className="grid grid-cols-7">
          {days.map((day) => {
            const dayTasks = dated.filter((task) =>
              isSameDay(new Date(task.dueDate as string), day),
            );
            const outside = !isSameMonth(day, month);

            return (
              <div
                key={day.toISOString()}
                className={cn(
                  "min-h-24 border-b border-r border-border p-1.5 last:border-r-0",
                  outside && "bg-surface-sunken/50",
                )}
              >
                <div className="mb-1 flex items-center justify-between">
                  <span
                    className={cn(
                      "flex size-5 items-center justify-center rounded-full text-label tabular",
                      isToday(day)
                        ? "bg-accent font-semibold text-accent-foreground"
                        : outside
                          ? "text-subtle"
                          : "text-muted",
                    )}
                  >
                    {format(day, "d")}
                  </span>
                </div>

                <div className="space-y-1">
                  {dayTasks.slice(0, 3).map((task) => (
                    <button
                      key={task.id}
                      type="button"
                      onClick={() => onOpenTask(task)}
                      className={cn(
                        "flex w-full items-center gap-1.5 rounded-[4px] px-1 py-0.5 text-left transition-colors hover:bg-surface-raised",
                        task.status === "done" && "opacity-50",
                      )}
                    >
                      <span
                        aria-hidden
                        className={cn(
                          "size-1.5 shrink-0 rounded-full",
                          PRIORITY_DOT[task.priority],
                        )}
                      />
                      <span
                        className={cn(
                          "truncate text-label",
                          task.status === "done"
                            ? "text-subtle line-through"
                            : "text-foreground",
                        )}
                      >
                        {task.title}
                      </span>
                    </button>
                  ))}

                  {dayTasks.length > 3 ? (
                    <p className="px-1 text-label text-subtle tabular">
                      +{dayTasks.length - 3} more
                    </p>
                  ) : null}
                </div>
              </div>
            );
          })}
        </div>
      </div>

      {/* Undated tasks would otherwise be invisible in a calendar view. */}
      <div className="rounded-surface border border-border bg-surface">
        <div className="flex items-center gap-2 border-b border-border px-3 py-2">
          <CalendarOff className="size-3.5 text-subtle" aria-hidden />
          <h3 className="text-caption font-semibold text-foreground">
            No due date
          </h3>
          <span className="ml-auto text-label text-subtle tabular">
            {undated.length}
          </span>
        </div>

        {undated.length === 0 ? (
          <p className="px-3 py-4 text-caption text-subtle">
            Every task in this view has a due date.
          </p>
        ) : (
          <ul className="divide-y divide-border">
            {undated.map((task) => (
              <li key={task.id}>
                <button
                  type="button"
                  onClick={() => onOpenTask(task)}
                  className="flex w-full items-center gap-2 px-3 py-2 text-left transition-colors hover:bg-surface-raised/60"
                >
                  <span
                    aria-hidden
                    className={cn(
                      "size-1.5 shrink-0 rounded-full",
                      PRIORITY_DOT[task.priority],
                    )}
                  />
                  <span
                    className={cn(
                      "truncate text-body",
                      task.status === "done"
                        ? "text-subtle line-through"
                        : "text-foreground",
                    )}
                  >
                    {task.title}
                  </span>
                </button>
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  );
}
