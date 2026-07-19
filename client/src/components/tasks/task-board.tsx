"use client";

import { CircleDashed, MoveRight, Paperclip } from "lucide-react";
import { useState } from "react";
import { PriorityBadge } from "@/components/meetings/status";
import { Avatar } from "@/components/ui/avatar";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { Skeleton } from "@/components/ui/feedback";
import { cn } from "@/lib/utils/cn";
import { formatDueDate } from "@/lib/utils/format";
import type { ActionItem, TaskStatus, User } from "@/types/domain";

const COLUMNS: { status: TaskStatus; label: string; accent: string }[] = [
  { status: "todo", label: "To do", accent: "bg-subtle" },
  { status: "in_progress", label: "In progress", accent: "bg-info" },
  { status: "blocked", label: "Blocked", accent: "bg-danger" },
  { status: "done", label: "Done", accent: "bg-success" },
];

function TaskCard({
  task,
  users,
  onOpen,
  onMove,
  isDragging,
  onDragStart,
  onDragEnd,
}: {
  task: ActionItem;
  users: User[];
  onOpen: () => void;
  onMove: (status: TaskStatus) => void;
  isDragging: boolean;
  onDragStart: () => void;
  onDragEnd: () => void;
}) {
  const assignee = task.assigneeId
    ? users.find((user) => user.id === task.assigneeId)
    : undefined;
  const due = formatDueDate(task.dueDate);

  return (
    // Dragging is a pointer-only affordance layered on top of a fully
    // keyboard-operable card: the title is a button that opens the drawer, and
    // the move menu performs the same reordering without a mouse.
    // biome-ignore lint/a11y/noStaticElementInteractions: drag is an enhancement, not the only path
    <div
      draggable
      onDragStart={onDragStart}
      onDragEnd={onDragEnd}
      className={cn(
        "group rounded-surface border border-border bg-surface p-3 transition-colors",
        "hover:border-border-strong",
        isDragging && "opacity-40",
      )}
    >
      <div className="flex items-start gap-2">
        {/* The card body is a button so the drawer is reachable by keyboard. */}
        <button
          type="button"
          onClick={onOpen}
          className="min-w-0 flex-1 text-left"
        >
          <p
            className={cn(
              "text-body",
              task.status === "done"
                ? "text-subtle line-through"
                : "text-foreground",
            )}
          >
            {task.title}
          </p>
        </button>

        {/* Keyboard-accessible equivalent of dragging the card. */}
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button
              variant="ghost"
              size="icon-sm"
              aria-label={`Move "${task.title}" to another column`}
              className="shrink-0 opacity-0 transition-opacity focus-visible:opacity-100 group-hover:opacity-100"
            >
              <MoveRight />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent>
            <DropdownMenuLabel>Move to</DropdownMenuLabel>
            {COLUMNS.filter((column) => column.status !== task.status).map(
              (column) => (
                <DropdownMenuItem
                  key={column.status}
                  onSelect={() => onMove(column.status)}
                >
                  {column.label}
                </DropdownMenuItem>
              ),
            )}
          </DropdownMenuContent>
        </DropdownMenu>
      </div>

      <div className="mt-2.5 flex flex-wrap items-center gap-2">
        <PriorityBadge priority={task.priority} />

        {/* A due date on finished work is noise — the deadline no longer
            applies once the task is done. */}
        {task.dueDate && task.status !== "done" ? (
          <span
            className={cn(
              "text-label",
              due.overdue ? "font-medium text-danger" : "text-subtle",
            )}
          >
            {due.label}
          </span>
        ) : null}

        {task.meetingId ? (
          <Paperclip
            className="size-3 text-subtle"
            aria-label="From a meeting"
          />
        ) : null}

        <span className="ml-auto">
          {assignee ? (
            <Avatar name={assignee.name} size="xs" />
          ) : (
            <CircleDashed
              className="size-4 text-subtle"
              aria-label="Unassigned"
            />
          )}
        </span>
      </div>
    </div>
  );
}

export function TaskBoard({
  tasks,
  users,
  loading,
  onOpenTask,
  onMoveTask,
}: {
  tasks: ActionItem[];
  users: User[];
  loading: boolean;
  onOpenTask: (task: ActionItem) => void;
  onMoveTask: (task: ActionItem, status: TaskStatus) => void;
}) {
  const [draggingId, setDraggingId] = useState<string | null>(null);
  const [dropTarget, setDropTarget] = useState<TaskStatus | null>(null);

  function handleDrop(status: TaskStatus) {
    const task = tasks.find((item) => item.id === draggingId);
    setDraggingId(null);
    setDropTarget(null);

    // Dropping a card back into its own column is a no-op, not a save.
    if (task && task.status !== status) onMoveTask(task, status);
  }

  if (loading) {
    return (
      <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
        {COLUMNS.map((column) => (
          <div key={column.status} className="space-y-2">
            <Skeleton className="h-8 w-full" />
            <Skeleton className="h-24 w-full" />
            <Skeleton className="h-24 w-full" />
          </div>
        ))}
      </div>
    );
  }

  return (
    <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
      {COLUMNS.map((column) => {
        const columnTasks = tasks.filter(
          (task) => task.status === column.status,
        );
        const isTarget = dropTarget === column.status;

        return (
          <section
            key={column.status}
            aria-label={`${column.label} column`}
            onDragOver={(event) => {
              // Required for the drop event to fire at all.
              event.preventDefault();
              setDropTarget(column.status);
            }}
            onDragLeave={() =>
              setDropTarget((c) => (c === column.status ? null : c))
            }
            onDrop={(event) => {
              event.preventDefault();
              handleDrop(column.status);
            }}
            className={cn(
              "flex flex-col rounded-surface border bg-surface-sunken transition-colors",
              isTarget ? "border-accent bg-accent-subtle/40" : "border-border",
            )}
          >
            <header className="flex items-center gap-2 border-b border-border px-3 py-2">
              <span
                aria-hidden
                className={cn("size-1.5 rounded-full", column.accent)}
              />
              <h3 className="text-caption font-semibold text-foreground">
                {column.label}
              </h3>
              <span className="ml-auto text-label text-subtle tabular">
                {columnTasks.length}
              </span>
            </header>

            <div className="flex-1 space-y-2 p-2">
              {columnTasks.length === 0 ? (
                <p className="px-2 py-6 text-center text-caption text-subtle">
                  {isTarget ? "Drop here" : "Nothing here"}
                </p>
              ) : (
                columnTasks.map((task) => (
                  <TaskCard
                    key={task.id}
                    task={task}
                    users={users}
                    isDragging={draggingId === task.id}
                    onDragStart={() => setDraggingId(task.id)}
                    onDragEnd={() => {
                      setDraggingId(null);
                      setDropTarget(null);
                    }}
                    onOpen={() => onOpenTask(task)}
                    onMove={(status) => onMoveTask(task, status)}
                  />
                ))
              )}
            </div>
          </section>
        );
      })}
    </div>
  );
}
