"use client";

import { ExternalLink, Quote, Trash2 } from "lucide-react";
import Link from "next/link";
import { useEffect, useState } from "react";
import { PriorityBadge, TaskStatusBadge } from "@/components/meetings/status";
import { Avatar } from "@/components/ui/avatar";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { ConfirmDialog } from "@/components/ui/dialog";
import { Field, Input, Textarea } from "@/components/ui/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Sheet, SheetContent } from "@/components/ui/sheet";
import { useToast } from "@/components/ui/toast";
import { deleteTask, updateTask } from "@/lib/api/tasks";
import { cn } from "@/lib/utils/cn";
import {
  formatDateTime,
  formatDueDate,
  formatRelative,
} from "@/lib/utils/format";
import type {
  ActionItem,
  Meeting,
  TaskPriority,
  TaskStatus,
  TranscriptSegment,
  User,
} from "@/types/domain";

const PRIORITIES: TaskPriority[] = ["low", "medium", "high", "urgent"];
const STATUSES: TaskStatus[] = ["todo", "in_progress", "blocked", "done"];

const STATUS_LABELS: Record<TaskStatus, string> = {
  todo: "To do",
  in_progress: "In progress",
  blocked: "Blocked",
  done: "Done",
};

/** `<input type="date">` needs `yyyy-MM-dd`; an ISO timestamp is rejected. */
function toDateInput(iso: string | null): string {
  if (!iso) return "";
  return new Date(iso).toISOString().slice(0, 10);
}

export function TaskDrawer({
  task,
  users,
  meeting,
  sourceSegment,
  open,
  onOpenChange,
  onChanged,
  onDeleted,
}: {
  task: ActionItem | null;
  users: User[];
  /** The meeting this was extracted from, when there is one. */
  meeting: Meeting | null;
  /** The transcript line the AI pulled this commitment from. */
  sourceSegment: TranscriptSegment | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onChanged: () => void;
  onDeleted: () => void;
}) {
  const { toast } = useToast();

  const [title, setTitle] = useState("");
  const [description, setDescription] = useState("");
  const [titleError, setTitleError] = useState<string>();
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [deleting, setDeleting] = useState(false);

  // Reset the local draft whenever a different task is opened.
  useEffect(() => {
    if (!task) return;
    setTitle(task.title);
    setDescription(task.description);
    setTitleError(undefined);
  }, [task]);

  if (!task) return null;

  const assignee = task.assigneeId
    ? users.find((user) => user.id === task.assigneeId)
    : undefined;
  const creator = users.find((user) => user.id === task.creatorId);
  const due = formatDueDate(task.dueDate);

  async function patch(changes: Partial<ActionItem>) {
    if (!task) return;
    try {
      await updateTask(task.id, changes);
      onChanged();
    } catch (error) {
      toast({
        tone: "error",
        title: "Could not save changes",
        description:
          error instanceof Error ? error.message : "Please try again.",
      });
    }
  }

  /** Text fields commit on blur rather than on every keystroke. */
  async function commitTitle() {
    if (!task) return;
    const trimmed = title.trim();

    if (!trimmed) {
      setTitleError("Title cannot be empty.");
      setTitle(task.title);
      return;
    }
    setTitleError(undefined);
    if (trimmed !== task.title) await patch({ title: trimmed });
  }

  async function commitDescription() {
    if (!task) return;
    if (description !== task.description) await patch({ description });
  }

  async function handleDelete() {
    if (!task) return;
    setDeleting(true);
    try {
      await deleteTask(task.id);
      setConfirmDelete(false);
      onOpenChange(false);
      onDeleted();
      toast({ tone: "info", title: "Task deleted", description: task.title });
    } catch {
      toast({ tone: "error", title: "Could not delete task" });
    } finally {
      setDeleting(false);
    }
  }

  return (
    <>
      <Sheet open={open} onOpenChange={onOpenChange}>
        <SheetContent
          title="Task details"
          description={`Created ${formatRelative(task.createdAt)}${
            creator ? ` by ${creator.name}` : ""
          }`}
          footer={
            <>
              <Button
                variant="danger-outline"
                size="sm"
                onClick={() => setConfirmDelete(true)}
              >
                <Trash2 />
                Delete
              </Button>
              <Button
                variant="secondary"
                size="sm"
                onClick={() => onOpenChange(false)}
              >
                Close
              </Button>
            </>
          }
        >
          <div className="space-y-5">
            <div className="flex flex-wrap items-center gap-2">
              <TaskStatusBadge status={task.status} />
              <PriorityBadge priority={task.priority} />
              {due.overdue && task.status !== "done" ? (
                <span className="text-caption font-medium text-danger">
                  {due.label}
                </span>
              ) : null}
            </div>

            <Field label="Title" required error={titleError}>
              {(props) => (
                <Input
                  {...props}
                  value={title}
                  onChange={(event) => setTitle(event.target.value)}
                  onBlur={commitTitle}
                />
              )}
            </Field>

            <Field
              label="Description"
              hint="Saved when you click away from the field."
            >
              {(props) => (
                <Textarea
                  {...props}
                  value={description}
                  onChange={(event) => setDescription(event.target.value)}
                  onBlur={commitDescription}
                  rows={3}
                  placeholder="Add context…"
                />
              )}
            </Field>

            <div className="grid gap-4 sm:grid-cols-2">
              <Field label="Status">
                {() => (
                  <Select
                    value={task.status}
                    onValueChange={(value) =>
                      patch({ status: value as TaskStatus })
                    }
                  >
                    <SelectTrigger className="w-full">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {STATUSES.map((status) => (
                        <SelectItem key={status} value={status}>
                          {STATUS_LABELS[status]}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                )}
              </Field>

              <Field label="Priority">
                {() => (
                  <Select
                    value={task.priority}
                    onValueChange={(value) =>
                      patch({ priority: value as TaskPriority })
                    }
                  >
                    <SelectTrigger className="w-full">
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

              <Field label="Assignee">
                {() => (
                  <Select
                    // Radix Select cannot hold an empty string value, so the
                    // unassigned case uses an explicit sentinel.
                    value={task.assigneeId ?? "unassigned"}
                    onValueChange={(value) =>
                      patch({
                        assigneeId: value === "unassigned" ? null : value,
                      })
                    }
                  >
                    <SelectTrigger className="w-full">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="unassigned">Unassigned</SelectItem>
                      {users.map((user) => (
                        <SelectItem key={user.id} value={user.id}>
                          {user.name}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                )}
              </Field>

              <Field label="Due date">
                {(props) => (
                  <Input
                    {...props}
                    type="date"
                    value={toDateInput(task.dueDate)}
                    onChange={(event) =>
                      patch({
                        dueDate: event.target.value
                          ? new Date(event.target.value).toISOString()
                          : null,
                      })
                    }
                  />
                )}
              </Field>
            </div>

            {assignee ? (
              <div className="flex items-center gap-2.5 rounded-control border border-border bg-surface-sunken px-3 py-2">
                <Avatar name={assignee.name} size="md" />
                <div className="min-w-0">
                  <p className="truncate text-caption text-foreground">
                    {assignee.name}
                  </p>
                  <p className="truncate text-label text-subtle">
                    {assignee.jobTitle}
                  </p>
                </div>
              </div>
            ) : null}

            {task.tags.length > 0 ? (
              <div className="space-y-1.5">
                <p className="text-overline uppercase text-subtle">Tags</p>
                <div className="flex flex-wrap gap-1.5">
                  {task.tags.map((tag) => (
                    <Badge key={tag} tone="outline" size="sm">
                      {tag}
                    </Badge>
                  ))}
                </div>
              </div>
            ) : null}

            {/* Provenance — where an extracted task actually came from. */}
            {meeting ? (
              <div className="space-y-2 border-t border-border pt-4">
                <p className="text-overline uppercase text-subtle">
                  Extracted from
                </p>

                <Link
                  href={`/meetings/${meeting.id}`}
                  className="flex items-start gap-2 rounded-control border border-border px-3 py-2 transition-colors hover:border-border-strong hover:bg-surface-raised/60"
                >
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-caption font-medium text-foreground">
                      {meeting.title}
                    </p>
                    <p className="text-label text-subtle">
                      {formatDateTime(meeting.startsAt)}
                    </p>
                  </div>
                  <ExternalLink
                    className="mt-0.5 size-3.5 shrink-0 text-subtle"
                    aria-hidden
                  />
                </Link>

                {sourceSegment ? (
                  <blockquote className="flex gap-2 rounded-control bg-surface-sunken px-3 py-2">
                    <Quote
                      className="mt-0.5 size-3 shrink-0 text-subtle"
                      aria-hidden
                    />
                    <div className="min-w-0">
                      <p className="text-caption italic text-muted">
                        {sourceSegment.text}
                      </p>
                      <p className="mt-1 text-label text-subtle">
                        {sourceSegment.speakerName}
                      </p>
                    </div>
                  </blockquote>
                ) : null}
              </div>
            ) : null}

            <div
              className={cn(
                "space-y-1 border-t border-border pt-4 text-label text-subtle",
              )}
            >
              <p>Created {formatRelative(task.createdAt)}</p>
              <p>Updated {formatRelative(task.updatedAt)}</p>
              {task.completedAt ? (
                <p>Completed {formatRelative(task.completedAt)}</p>
              ) : null}
            </div>
          </div>
        </SheetContent>
      </Sheet>

      <ConfirmDialog
        open={confirmDelete}
        onOpenChange={setConfirmDelete}
        title="Delete this task?"
        description={`"${task.title}" will be permanently removed. This cannot be undone.`}
        confirmLabel="Delete task"
        destructive
        loading={deleting}
        onConfirm={handleDelete}
      />
    </>
  );
}
