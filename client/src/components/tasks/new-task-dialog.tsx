"use client";

import { useEffect, useState } from "react";
import { Button } from "@/components/ui/button";
import { Dialog, DialogClose, DialogContent } from "@/components/ui/dialog";
import { Field, Input, Textarea } from "@/components/ui/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { useToast } from "@/components/ui/toast";
import { createTask } from "@/lib/api/tasks";
import type { TaskPriority, User } from "@/types/domain";

const PRIORITIES: { value: TaskPriority; label: string }[] = [
  { value: "urgent", label: "Urgent" },
  { value: "high", label: "High" },
  { value: "medium", label: "Medium" },
  { value: "low", label: "Low" },
];

/** Radix Select cannot hold an empty value, so unassigned needs a sentinel. */
const UNASSIGNED = "unassigned";

export function NewTaskDialog({
  open,
  onOpenChange,
  creatorId,
  users,
  onCreated,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  creatorId: string;
  users: User[];
  onCreated: () => void;
}) {
  const { toast } = useToast();

  const [title, setTitle] = useState("");
  const [description, setDescription] = useState("");
  const [assigneeId, setAssigneeId] = useState(creatorId);
  const [priority, setPriority] = useState<TaskPriority>("medium");
  const [dueDate, setDueDate] = useState("");
  const [tagsInput, setTagsInput] = useState("");
  const [titleError, setTitleError] = useState<string>();
  const [submitting, setSubmitting] = useState(false);

  // Reset to defaults each time the dialog opens.
  useEffect(() => {
    if (!open) return;
    setTitle("");
    setDescription("");
    setAssigneeId(creatorId);
    setPriority("medium");
    setDueDate("");
    setTagsInput("");
    setTitleError(undefined);
  }, [open, creatorId]);

  async function handleSubmit(event: React.FormEvent) {
    event.preventDefault();

    if (!title.trim()) {
      setTitleError("Describe what needs to happen.");
      return;
    }

    setSubmitting(true);
    try {
      await createTask({
        title,
        description,
        creatorId,
        assigneeId: assigneeId === UNASSIGNED ? null : assigneeId,
        priority,
        dueDate: dueDate ? new Date(dueDate).toISOString() : null,
        tags: tagsInput
          .split(",")
          .map((tag) => tag.trim().toLowerCase())
          .filter(Boolean),
      });

      onOpenChange(false);
      onCreated();
      toast({ tone: "success", title: "Task created", description: title });
    } catch (error) {
      toast({
        tone: "error",
        title: "Could not create task",
        description:
          error instanceof Error ? error.message : "Please try again.",
      });
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        title="New task"
        description="Create a task directly, without linking it to a meeting."
        footer={
          <>
            <DialogClose asChild>
              <Button variant="secondary" size="sm">
                Cancel
              </Button>
            </DialogClose>
            <Button
              variant="primary"
              size="sm"
              type="submit"
              form="new-task-form"
              loading={submitting}
            >
              Create task
            </Button>
          </>
        }
      >
        <form
          id="new-task-form"
          onSubmit={handleSubmit}
          className="space-y-4"
          noValidate
        >
          <Field label="What needs to happen?" required error={titleError}>
            {(props) => (
              <Input
                {...props}
                value={title}
                onChange={(event) => {
                  setTitle(event.target.value);
                  if (titleError) setTitleError(undefined);
                }}
                placeholder="Draft the Q4 planning brief"
                autoFocus
              />
            )}
          </Field>

          <Field label="Description" hint="Optional detail or context.">
            {(props) => (
              <Textarea
                {...props}
                value={description}
                onChange={(event) => setDescription(event.target.value)}
                rows={2}
                placeholder="Anything the assignee needs to know."
              />
            )}
          </Field>

          <div className="grid gap-4 sm:grid-cols-3">
            <Field label="Assignee">
              {() => (
                <Select value={assigneeId} onValueChange={setAssigneeId}>
                  <SelectTrigger className="w-full">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value={UNASSIGNED}>Unassigned</SelectItem>
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
                  value={priority}
                  onValueChange={(value) => setPriority(value as TaskPriority)}
                >
                  <SelectTrigger className="w-full">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {PRIORITIES.map((option) => (
                      <SelectItem key={option.value} value={option.value}>
                        {option.label}
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
                  value={dueDate}
                  onChange={(event) => setDueDate(event.target.value)}
                />
              )}
            </Field>
          </div>

          <Field label="Tags" hint="Comma separated, e.g. planning, q4">
            {(props) => (
              <Input
                {...props}
                value={tagsInput}
                onChange={(event) => setTagsInput(event.target.value)}
                placeholder="planning, q4"
              />
            )}
          </Field>
        </form>
      </DialogContent>
    </Dialog>
  );
}
