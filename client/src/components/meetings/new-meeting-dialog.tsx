"use client";

import { addMinutes } from "date-fns";
import { useEffect, useState } from "react";
import { Button } from "@/components/ui/button";
import { DateTimePicker } from "@/components/ui/date-picker";
import { Dialog, DialogClose, DialogContent } from "@/components/ui/dialog";
import { Field, Input, Textarea } from "@/components/ui/input";
import { MultiSelect } from "@/components/ui/multi-select";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { useToast } from "@/components/ui/toast";
import { createMeeting } from "@/lib/api/meetings";
import { listUsers } from "@/lib/api/workspace";
import type { MeetingPlatform, User } from "@/types/domain";
import { PLATFORM_LABELS } from "./status";

interface FormErrors {
  title?: string;
  endsAt?: string;
  participants?: string;
}

export function NewMeetingDialog({
  open,
  onOpenChange,
  onCreated,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onCreated: () => void;
}) {
  const { toast } = useToast();

  const [title, setTitle] = useState("");
  const [description, setDescription] = useState("");
  const [startsAt, setStartsAt] = useState("");
  const [endsAt, setEndsAt] = useState("");
  const [platform, setPlatform] = useState<MeetingPlatform>("google_meet");
  const [participantIds, setParticipantIds] = useState<string[]>([]);
  const [tagsInput, setTagsInput] = useState("");

  const [users, setUsers] = useState<User[]>([]);
  const [errors, setErrors] = useState<FormErrors>({});
  const [submitting, setSubmitting] = useState(false);

  // Reset to a sensible default window each time the dialog opens.
  useEffect(() => {
    if (!open) return;

    const start = addMinutes(new Date(), 30);
    setTitle("");
    setDescription("");
    setStartsAt(start.toISOString());
    setEndsAt(addMinutes(start, 30).toISOString());
    setPlatform("google_meet");
    setParticipantIds([]);
    setTagsInput("");
    setErrors({});

    listUsers()
      .then((all) => setUsers(all.filter((user) => user.status === "active")))
      .catch(() => setUsers([]));
  }, [open]);

  function validate(): FormErrors {
    const next: FormErrors = {};

    if (!title.trim()) next.title = "Give the meeting a title.";
    if (new Date(endsAt) <= new Date(startsAt)) {
      next.endsAt = "End time must be after the start time.";
    }
    if (participantIds.length === 0) {
      next.participants = "Add at least one participant.";
    }

    return next;
  }

  async function handleSubmit(event: React.FormEvent) {
    event.preventDefault();

    const found = validate();
    setErrors(found);
    if (Object.keys(found).length > 0) return;

    setSubmitting(true);
    try {
      await createMeeting({
        title,
        description,
        startsAt,
        endsAt,
        platform,
        participantIds,
        tags: tagsInput
          .split(",")
          .map((tag) => tag.trim().toLowerCase())
          .filter(Boolean),
      });

      toast({
        tone: "success",
        title: "Meeting scheduled",
        description: title,
      });
      onOpenChange(false);
      onCreated();
    } catch (error) {
      toast({
        tone: "error",
        title: "Could not schedule meeting",
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
        title="Schedule a meeting"
        description="Cadence will join, record and summarise it automatically."
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
              form="new-meeting-form"
              loading={submitting}
            >
              Schedule meeting
            </Button>
          </>
        }
      >
        <form
          id="new-meeting-form"
          onSubmit={handleSubmit}
          className="space-y-4"
          noValidate
        >
          <Field label="Title" required error={errors.title}>
            {(props) => (
              <Input
                {...props}
                value={title}
                onChange={(event) => setTitle(event.target.value)}
                placeholder="Q3 Roadmap Review"
                autoFocus
              />
            )}
          </Field>

          <Field
            label="Description"
            hint="Optional context shown on the meeting page."
          >
            {(props) => (
              <Textarea
                {...props}
                value={description}
                onChange={(event) => setDescription(event.target.value)}
                placeholder="What needs to be decided?"
                rows={2}
              />
            )}
          </Field>

          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Starts" required>
              {(props) => (
                <DateTimePicker
                  {...props}
                  value={startsAt}
                  onChange={(next) => setStartsAt(next ?? "")}
                />
              )}
            </Field>

            <Field label="Ends" required error={errors.endsAt}>
              {(props) => (
                <DateTimePicker
                  {...props}
                  value={endsAt}
                  onChange={(next) => setEndsAt(next ?? "")}
                />
              )}
            </Field>
          </div>

          <Field label="Platform">
            {() => (
              <Select
                value={platform}
                onValueChange={(value) => setPlatform(value as MeetingPlatform)}
              >
                <SelectTrigger className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {Object.entries(PLATFORM_LABELS).map(([value, label]) => (
                    <SelectItem key={value} value={value}>
                      {label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            )}
          </Field>

          <Field
            label="Participants"
            required
            error={errors.participants}
            hint={
              participantIds.length > 0
                ? `${participantIds.length} selected`
                : undefined
            }
          >
            {(props) => (
              <MultiSelect
                {...props}
                options={users.map((user) => ({
                  value: user.id,
                  label: user.name,
                  hint: user.jobTitle,
                }))}
                selected={participantIds}
                onChange={setParticipantIds}
                placeholder="Add participants…"
                searchPlaceholder="Search team members…"
                emptyMessage={
                  users.length === 0 ? "Loading team members…" : "No matches"
                }
              />
            )}
          </Field>

          <Field label="Tags" hint="Comma separated, e.g. roadmap, quarterly">
            {(props) => (
              <Input
                {...props}
                value={tagsInput}
                onChange={(event) => setTagsInput(event.target.value)}
                placeholder="roadmap, quarterly"
              />
            )}
          </Field>
        </form>
      </DialogContent>
    </Dialog>
  );
}
