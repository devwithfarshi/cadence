"use client";

import { ListTodo, Plus, Trash2 } from "lucide-react";
import { useState } from "react";
import { PriorityBadge, TaskStatusBadge } from "@/components/meetings/status";
import { Avatar } from "@/components/ui/avatar";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { DatePicker } from "@/components/ui/date-picker";
import { EmptyState, Skeleton } from "@/components/ui/feedback";
import { Field, Input } from "@/components/ui/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { useToast } from "@/components/ui/toast";
import { createTask, deleteTask, updateTask } from "@/lib/api/tasks";
import { cn } from "@/lib/utils/cn";
import { formatDueDate } from "@/lib/utils/format";
import type {
  ActionItem,
  TaskPriority,
  TaskStatus,
  User,
} from "@/types/domain";

const PRIORITIES: TaskPriority[] = ["low", "medium", "high", "urgent"];
const STATUSES: TaskStatus[] = ["todo", "in_progress", "blocked", "done"];

export function ActionItemsPanel({
  meetingId,
  items,
  users,
  currentUserId,
  loading,
  onChanged,
}: {
  meetingId: string;
  items: ActionItem[];
  users: User[];
  currentUserId: string;
  loading: boolean;
  onChanged: () => void;
}) {
  const { toast } = useToast();

  const [adding, setAdding] = useState(false);
  const [newTitle, setNewTitle] = useState("");
  const [newAssignee, setNewAssignee] = useState<string>(currentUserId);
  const [newPriority, setNewPriority] = useState<TaskPriority>("medium");
  const [newDueDate, setNewDueDate] = useState("");
  const [titleError, setTitleError] = useState<string>();
  const [submitting, setSubmitting] = useState(false);

  const userById = new Map(users.map((user) => [user.id, user]));

  async function handleToggle(task: ActionItem) {
    const nextStatus: TaskStatus = task.status === "done" ? "todo" : "done";
    try {
      await updateTask(task.id, { status: nextStatus });
      onChanged();
    } catch {
      toast({ tone: "error", title: "Could not update task" });
    }
  }

  async function handleFieldChange(
    task: ActionItem,
    patch: Partial<ActionItem>,
  ) {
    try {
      await updateTask(task.id, patch);
      onChanged();
    } catch {
      toast({ tone: "error", title: "Could not update task" });
    }
  }

  async function handleDelete(task: ActionItem) {
    try {
      await deleteTask(task.id);
      onChanged();
      toast({
        tone: "info",
        title: "Action item deleted",
        description: task.title,
      });
    } catch {
      toast({ tone: "error", title: "Could not delete task" });
    }
  }

  async function handleCreate(event: React.FormEvent) {
    event.preventDefault();

    if (!newTitle.trim()) {
      setTitleError("Describe what needs to happen.");
      return;
    }

    setSubmitting(true);
    try {
      await createTask({
        title: newTitle,
        assigneeId: newAssignee || null,
        creatorId: currentUserId,
        dueDate: newDueDate ? new Date(newDueDate).toISOString() : null,
        priority: newPriority,
        meetingId,
      });

      setNewTitle("");
      setNewDueDate("");
      setNewPriority("medium");
      setTitleError(undefined);
      setAdding(false);
      onChanged();
      toast({ tone: "success", title: "Action item added" });
    } catch (error) {
      toast({
        tone: "error",
        title: "Could not add action item",
        description:
          error instanceof Error ? error.message : "Please try again.",
      });
    } finally {
      setSubmitting(false);
    }
  }

  if (loading) {
    return (
      <div className="space-y-3 p-4">
        {[0, 1, 2].map((row) => (
          <Skeleton key={row} className="h-14 w-full" />
        ))}
      </div>
    );
  }

  const open = items.filter((item) => item.status !== "done");
  const done = items.filter((item) => item.status === "done");

  return (
    <div className="flex flex-col">
      <div className="flex items-center justify-between gap-2 border-b border-border px-4 py-3">
        <p className="text-caption text-muted tabular">
          {open.length} open · {done.length} completed
        </p>
        <Button
          variant="secondary"
          size="sm"
          onClick={() => setAdding((current) => !current)}
        >
          <Plus />
          Add action item
        </Button>
      </div>

      {adding ? (
        <form
          onSubmit={handleCreate}
          className="space-y-3 border-b border-border bg-surface-sunken px-4 py-3"
        >
          <Field label="What needs to happen?" required error={titleError}>
            {(props) => (
              <Input
                {...props}
                value={newTitle}
                onChange={(event) => {
                  setNewTitle(event.target.value);
                  if (titleError) setTitleError(undefined);
                }}
                placeholder="Confirm the October close dates with both champions"
                autoFocus
              />
            )}
          </Field>

          <div className="grid gap-3 sm:grid-cols-3">
            <Field label="Assignee">
              {() => (
                <Select value={newAssignee} onValueChange={setNewAssignee}>
                  <SelectTrigger className="w-full" size="sm">
                    <SelectValue placeholder="Unassigned" />
                  </SelectTrigger>
                  <SelectContent>
                    {users.map((user) => (
                      <SelectItem key={user.id} value={user.id}>
                        {user.name}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            </Field>

            <Field label="Priority">
              {() => (
                <Select
                  value={newPriority}
                  onValueChange={(value) =>
                    setNewPriority(value as TaskPriority)
                  }
                >
                  <SelectTrigger className="w-full" size="sm">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {PRIORITIES.map((priority) => (
                      <SelectItem key={priority} value={priority}>
                        {priority.charAt(0).toUpperCase() + priority.slice(1)}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            </Field>

            <Field label="Due date">
              {(props) => (
                <DatePicker
                  {...props}
                  value={newDueDate || null}
                  onChange={(next) => setNewDueDate(next ?? "")}
                />
              )}
            </Field>
          </div>

          <div className="flex justify-end gap-2">
            <Button
              variant="ghost"
              size="sm"
              type="button"
              onClick={() => {
                setAdding(false);
                setTitleError(undefined);
              }}
            >
              Cancel
            </Button>
            <Button
              variant="primary"
              size="sm"
              type="submit"
              loading={submitting}
            >
              Add item
            </Button>
          </div>
        </form>
      ) : null}

      {items.length === 0 ? (
        <EmptyState
          icon={ListTodo}
          title="No action items"
          description="Nothing was extracted from this meeting, and nothing has been added by hand."
          action={
            <Button
              variant="secondary"
              size="sm"
              onClick={() => setAdding(true)}
            >
              <Plus />
              Add the first one
            </Button>
          }
        />
      ) : (
        <ul className="divide-y divide-border">
          {[...open, ...done].map((task) => {
            const assignee = task.assigneeId
              ? userById.get(task.assigneeId)
              : undefined;
            const due = formatDueDate(task.dueDate);
            const isDone = task.status === "done";

            return (
              <li
                key={task.id}
                className="group flex items-start gap-3 px-4 py-3 transition-colors hover:bg-surface-raised/40"
              >
                <Checkbox
                  checked={isDone}
                  onCheckedChange={() => handleToggle(task)}
                  aria-label={`Mark "${task.title}" as ${isDone ? "not done" : "done"}`}
                  className="mt-0.5"
                />

                <div className="min-w-0 flex-1">
                  <p
                    className={cn(
                      "text-body",
                      isDone ? "text-subtle line-through" : "text-foreground",
                    )}
                  >
                    {task.title}
                  </p>

                  <div className="mt-1.5 flex flex-wrap items-center gap-2">
                    {assignee ? (
                      <span className="flex items-center gap-1.5">
                        <Avatar name={assignee.name} size="xs" />
                        <span className="text-caption text-muted">
                          {assignee.name}
                        </span>
                      </span>
                    ) : (
                      <span className="text-caption text-subtle">
                        Unassigned
                      </span>
                    )}

                    <PriorityBadge priority={task.priority} />
                    <TaskStatusBadge status={task.status} />

                    <span
                      className={cn(
                        "text-caption",
                        due.overdue && !isDone
                          ? "font-medium text-danger"
                          : "text-subtle",
                      )}
                    >
                      {due.label}
                    </span>
                  </div>
                </div>

                {/* Inline status control plus delete, revealed on hover/focus */}
                <div className="flex shrink-0 items-center gap-1.5 opacity-0 transition-opacity focus-within:opacity-100 group-hover:opacity-100">
                  <Select
                    value={task.status}
                    onValueChange={(value) =>
                      handleFieldChange(task, { status: value as TaskStatus })
                    }
                  >
                    <SelectTrigger size="sm" className="w-32">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {STATUSES.map((status) => (
                        <SelectItem key={status} value={status}>
                          {status === "in_progress"
                            ? "In progress"
                            : status === "todo"
                              ? "To do"
                              : status.charAt(0).toUpperCase() +
                                status.slice(1)}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>

                  <Button
                    variant="ghost"
                    size="icon-sm"
                    aria-label={`Delete "${task.title}"`}
                    onClick={() => handleDelete(task)}
                  >
                    <Trash2 />
                  </Button>
                </div>
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}
