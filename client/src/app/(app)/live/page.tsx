"use client";

import {
  Bookmark as BookmarkIcon,
  Check,
  MessageSquare,
  Mic,
  MicOff,
  Pause,
  PhoneOff,
  Play,
  Radio,
  Send,
  Sparkles,
  StickyNote,
  Users,
  X,
} from "lucide-react";
import { useRouter } from "next/navigation";
import { useEffect, useMemo, useRef, useState } from "react";
import { AudioMeter } from "@/components/live/audio-meter";
import { PriorityBadge } from "@/components/meetings/status";
import { useSession } from "@/components/providers/auth-provider";
import { PageContainer } from "@/components/shell/page-header";
import { Avatar } from "@/components/ui/avatar";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { ConfirmDialog } from "@/components/ui/dialog";
import { EmptyState, Skeleton } from "@/components/ui/feedback";
import { Input } from "@/components/ui/input";
import { Tabs, TabsCount, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { useToast } from "@/components/ui/toast";
import { Tooltip } from "@/components/ui/tooltip";
import { endLiveMeeting, getLiveParticipants } from "@/lib/api/live";
import { useAsync } from "@/lib/hooks/use-async";
import { useLiveMeeting } from "@/lib/live/use-live-meeting";
import { cn } from "@/lib/utils/cn";
import { formatTimecode } from "@/lib/utils/format";

const MEETING_TITLE = "Platform Architecture Review";
const MEETING_TAGS = ["engineering", "architecture"];

export default function LiveMeetingPage() {
  const session = useSession();
  const router = useRouter();
  const { toast } = useToast();

  const roster = useAsync(
    () => getLiveParticipants(session.userId, 4),
    [session.userId],
  );
  const participants = useMemo(() => roster.data ?? [], [roster.data]);

  const live = useLiveMeeting(participants);

  const [sidePanel, setSidePanel] = useState<"ai" | "chat">("ai");
  const [noteDraft, setNoteDraft] = useState("");
  const [chatDraft, setChatDraft] = useState("");
  const [bookmarkDraft, setBookmarkDraft] = useState("");
  const [confirmEnd, setConfirmEnd] = useState(false);
  const [saving, setSaving] = useState(false);

  const transcriptRef = useRef<HTMLDivElement>(null);
  const chatRef = useRef<HTMLDivElement>(null);

  // Follow the transcript as it grows, the way a live caption panel does.
  // Keyed on the count alone: the effect scrolls, it doesn't read the segments.
  // biome-ignore lint/correctness/useExhaustiveDependencies: new-line count is the trigger
  useEffect(() => {
    transcriptRef.current?.scrollTo({
      top: transcriptRef.current.scrollHeight,
      behavior: "smooth",
    });
  }, [live.segments.length]);

  // biome-ignore lint/correctness/useExhaustiveDependencies: new-message count is the trigger
  useEffect(() => {
    chatRef.current?.scrollTo({ top: chatRef.current.scrollHeight });
  }, [live.chatMessages.length]);

  // Nudge the user to wrap up once the discussion has actually finished.
  useEffect(() => {
    if (live.scriptExhausted && live.status === "recording") {
      toast({
        tone: "info",
        title: "Discussion has wrapped up",
        description:
          "End the meeting to generate its summary and action items.",
      });
    }
  }, [live.scriptExhausted, live.status, toast]);

  const pendingActions = live.detectedActions.filter(
    (action) => action.status === "pending",
  );
  const acceptedActions = live.detectedActions.filter(
    (action) => action.status === "accepted",
  );

  const isRunning = live.status === "recording" || live.status === "paused";

  async function handleEnd() {
    setSaving(true);
    live.end();

    try {
      const meeting = await endLiveMeeting({
        title: MEETING_TITLE,
        participants,
        organizerId: participants[0]?.userId ?? session.userId,
        startedAt: new Date(
          Date.now() - live.elapsedSeconds * 1000,
        ).toISOString(),
        durationSeconds: Math.floor(live.elapsedSeconds),
        segments: live.segments.map((segment) => ({
          speakerId: segment.speakerId,
          speakerName: segment.speakerName,
          startSeconds: segment.startSeconds,
          endSeconds: segment.endSeconds,
          text: segment.text,
          isActionItem: segment.isActionItem,
        })),
        acceptedActions: acceptedActions.map((action) => ({
          title: action.title,
          assigneeId: participants[action.assigneeIndex]?.userId ?? null,
          priority: action.priority,
          sourceSegmentIndex: action.segmentIndex,
        })),
        aiNotes: live.aiNotes.map((note) => note.text),
        bookmarks: live.bookmarks,
        quickNotes: live.quickNotes,
        tags: MEETING_TAGS,
      });

      setConfirmEnd(false);
      toast({
        tone: "success",
        title: "Meeting saved",
        description: `${acceptedActions.length} action ${
          acceptedActions.length === 1 ? "item" : "items"
        } created.`,
      });
      router.push(`/meetings/${meeting.id}`);
    } catch (error) {
      setSaving(false);
      toast({
        tone: "error",
        title: "Could not save the meeting",
        description:
          error instanceof Error ? error.message : "Please try again.",
      });
    }
  }

  if (roster.loading) {
    return (
      <PageContainer>
        <Skeleton className="h-[70vh] w-full" />
      </PageContainer>
    );
  }

  /* --- Pre-start ---------------------------------------------------------- */

  if (live.status === "idle") {
    return (
      <PageContainer>
        <div className="mx-auto max-w-lg py-12">
          <Card>
            <div className="flex flex-col items-center gap-5 p-8 text-center">
              <div className="flex size-11 items-center justify-center rounded-surface border border-border bg-surface-raised">
                <Radio className="size-5 text-subtle" aria-hidden />
              </div>

              <div className="space-y-1.5">
                <h1 className="text-heading text-foreground">
                  {MEETING_TITLE}
                </h1>
                <p className="text-body text-muted">
                  Cadence will transcribe the discussion, label speakers and
                  surface commitments as they are made.
                </p>
              </div>

              <div className="flex items-center gap-2">
                {participants.map((participant) => (
                  <Avatar
                    key={participant.userId}
                    name={participant.name}
                    size="md"
                  />
                ))}
                <span className="ml-1 text-caption text-muted tabular">
                  {participants.length} joining
                </span>
              </div>

              <Button variant="primary" size="lg" onClick={live.start}>
                <Radio />
                Start recording
              </Button>

              <p className="text-caption text-subtle">
                This is a simulation. A scripted discussion plays out in real
                time so every control behaves as it would in a real call.
              </p>
            </div>
          </Card>
        </div>
      </PageContainer>
    );
  }

  /* --- Live --------------------------------------------------------------- */

  return (
    <div className="flex h-[calc(100dvh-3.5rem)] flex-col">
      {/* Status bar */}
      <div className="flex shrink-0 flex-wrap items-center gap-3 border-b border-border bg-surface px-4 py-2.5">
        <span
          className={cn(
            "flex items-center gap-2 rounded-control px-2 py-1",
            live.status === "recording"
              ? "bg-danger-subtle"
              : "bg-warning-subtle",
          )}
        >
          <span
            aria-hidden
            className={cn(
              "size-2 rounded-full",
              live.status === "recording"
                ? "anim-pulse-live bg-live"
                : "bg-warning",
            )}
          />
          <span
            className={cn(
              "text-label font-semibold uppercase",
              live.status === "recording"
                ? "text-danger-foreground"
                : "text-warning-foreground",
            )}
          >
            {live.status === "recording" ? "Recording" : "Paused"}
          </span>
        </span>

        {/* role="timer" carries the label; a bare span cannot take aria-label.
            Not a live region — announcing every tick would be unusable. */}
        <span
          role="timer"
          aria-label="Elapsed recording time"
          className="font-mono text-subheading text-foreground tabular"
        >
          {formatTimecode(live.elapsedSeconds)}
        </span>

        <p className="min-w-0 truncate text-body font-medium text-foreground">
          {MEETING_TITLE}
        </p>

        <span className="ml-auto flex items-center gap-1.5 text-caption text-muted">
          <Users className="size-3.5" aria-hidden />
          <span className="tabular">{participants.length}</span>
        </span>
      </div>

      {/* Body */}
      <div className="grid min-h-0 flex-1 grid-cols-1 lg:grid-cols-[1fr_20rem]">
        {/* Transcript */}
        <div className="flex min-h-0 flex-col border-border lg:border-r">
          <div className="flex shrink-0 items-center gap-2 border-b border-border px-4 py-2">
            <h2 className="text-caption font-semibold text-foreground">
              Live transcript
            </h2>
            <span className="text-label text-subtle tabular">
              {live.segments.length} lines
            </span>
          </div>

          <div
            ref={transcriptRef}
            className="min-h-0 flex-1 space-y-3 overflow-y-auto scrollbar-thin p-4"
          >
            {live.segments.length === 0 ? (
              <p className="py-10 text-center text-caption text-subtle">
                Listening…
              </p>
            ) : (
              live.segments.map((segment, index) => {
                const isLatest = index === live.segments.length - 1;

                return (
                  <div key={segment.id} className="flex gap-3">
                    <Avatar
                      name={segment.speakerName}
                      size="md"
                      className="mt-0.5 shrink-0"
                    />

                    <div className="min-w-0 flex-1">
                      <div className="flex flex-wrap items-center gap-2">
                        <span className="text-caption font-medium text-foreground">
                          {segment.speakerName}
                        </span>
                        <span className="font-mono text-label text-subtle tabular">
                          {formatTimecode(segment.startSeconds)}
                        </span>
                        {segment.isActionItem ? (
                          <Badge tone="accent" size="sm">
                            <Sparkles className="size-2.5" />
                            Commitment
                          </Badge>
                        ) : null}
                      </div>

                      <p
                        className={cn(
                          "mt-0.5 text-body",
                          // The newest line is emphasised so the eye tracks it.
                          isLatest && live.status === "recording"
                            ? "text-foreground"
                            : "text-muted",
                        )}
                      >
                        {segment.text}
                      </p>
                    </div>
                  </div>
                );
              })
            )}
          </div>

          {/* Participants with live audio meters */}
          <div className="shrink-0 border-t border-border px-4 py-2.5">
            <div className="flex flex-wrap items-center gap-3">
              {participants.map((participant, index) => {
                const speaking = live.currentSpeakerIndex === index;
                const isSelf = participant.userId === session.userId;

                return (
                  <div
                    key={participant.userId}
                    className={cn(
                      "flex items-center gap-2 rounded-control px-2 py-1 transition-colors",
                      speaking && "bg-success-subtle",
                    )}
                  >
                    <Avatar name={participant.name} size="sm" />
                    <span
                      className={cn(
                        "text-caption",
                        speaking ? "font-medium text-foreground" : "text-muted",
                      )}
                    >
                      {participant.name}
                      {isSelf ? " (you)" : ""}
                    </span>

                    {isSelf && live.isMuted ? (
                      <MicOff
                        className="size-3 text-danger"
                        aria-label="Muted"
                      />
                    ) : (
                      <AudioMeter
                        level={live.audioLevels[index] ?? 0}
                        active={speaking && live.status === "recording"}
                      />
                    )}
                  </div>
                );
              })}
            </div>
          </div>
        </div>

        {/* Side panel */}
        <aside className="flex min-h-0 flex-col border-t border-border lg:border-t-0">
          <Tabs
            value={sidePanel}
            onValueChange={(value) => setSidePanel(value as "ai" | "chat")}
            className="flex min-h-0 flex-1 flex-col"
          >
            <TabsList className="shrink-0 px-2">
              <TabsTrigger value="ai">
                AI notes
                {pendingActions.length > 0 ? (
                  <TabsCount>{pendingActions.length}</TabsCount>
                ) : null}
              </TabsTrigger>
              <TabsTrigger value="chat">
                Chat
                {live.chatMessages.length > 0 ? (
                  <TabsCount>{live.chatMessages.length}</TabsCount>
                ) : null}
              </TabsTrigger>
            </TabsList>

            {sidePanel === "ai" ? (
              <div className="min-h-0 flex-1 overflow-y-auto scrollbar-thin">
                {/* Detected commitments awaiting review */}
                {pendingActions.length > 0 ? (
                  <section className="border-b border-border p-3">
                    <h3 className="mb-2 flex items-center gap-1.5 text-overline uppercase text-subtle">
                      <Sparkles className="size-3 text-accent" aria-hidden />
                      Detected action items
                    </h3>

                    <div className="space-y-2">
                      {pendingActions.map((action) => (
                        <div
                          key={action.id}
                          className="rounded-control border border-accent/40 bg-accent-subtle/40 p-2.5"
                        >
                          <p className="text-caption text-foreground">
                            {action.title}
                          </p>

                          <div className="mt-1.5 flex items-center gap-1.5">
                            <Avatar
                              name={
                                participants[action.assigneeIndex]?.name ??
                                "Unassigned"
                              }
                              size="xs"
                            />
                            <span className="text-label text-muted">
                              {participants[action.assigneeIndex]?.name}
                            </span>
                            <PriorityBadge priority={action.priority} />
                          </div>

                          {/* Extraction is imperfect, so nothing is saved
                              without a human confirming it. */}
                          <div className="mt-2 flex items-center gap-1.5">
                            <Button
                              variant="primary"
                              size="sm"
                              onClick={() => live.acceptAction(action.id)}
                            >
                              <Check />
                              Accept
                            </Button>
                            <Button
                              variant="ghost"
                              size="sm"
                              onClick={() => live.dismissAction(action.id)}
                            >
                              <X />
                              Dismiss
                            </Button>
                          </div>
                        </div>
                      ))}
                    </div>
                  </section>
                ) : null}

                {acceptedActions.length > 0 ? (
                  <section className="border-b border-border p-3">
                    <h3 className="mb-2 text-overline uppercase text-subtle">
                      Accepted ({acceptedActions.length})
                    </h3>
                    <ul className="space-y-1.5">
                      {acceptedActions.map((action) => (
                        <li
                          key={action.id}
                          className="flex items-start gap-1.5 text-caption text-muted"
                        >
                          <Check
                            className="mt-0.5 size-3 shrink-0 text-success"
                            aria-hidden
                          />
                          {action.title}
                        </li>
                      ))}
                    </ul>
                  </section>
                ) : null}

                {/* Running AI observations */}
                <section className="p-3">
                  <h3 className="mb-2 text-overline uppercase text-subtle">
                    Notes
                  </h3>

                  {live.aiNotes.length === 0 ? (
                    <p className="text-caption text-subtle">
                      Observations will appear here as the discussion develops.
                    </p>
                  ) : (
                    <ul className="space-y-2.5">
                      {live.aiNotes.map((note) => (
                        <li key={note.id} className="flex gap-2">
                          <span
                            aria-hidden
                            className="mt-1.5 size-1 shrink-0 rounded-full bg-border-strong"
                          />
                          <div className="min-w-0">
                            <p className="text-caption text-muted">
                              {note.text}
                            </p>
                            <p className="mt-0.5 font-mono text-label text-subtle tabular">
                              {formatTimecode(note.atSeconds)}
                            </p>
                          </div>
                        </li>
                      ))}
                    </ul>
                  )}
                </section>

                {live.quickNotes.length > 0 ? (
                  <section className="border-t border-border p-3">
                    <h3 className="mb-2 flex items-center gap-1.5 text-overline uppercase text-subtle">
                      <StickyNote className="size-3" aria-hidden />
                      Your notes
                    </h3>
                    <ul className="space-y-1.5">
                      {live.quickNotes.map((note, index) => (
                        <li
                          // biome-ignore lint/suspicious/noArrayIndexKey: notes are append-only
                          key={index}
                          className="rounded-control bg-surface-sunken px-2 py-1.5 text-caption text-muted"
                        >
                          {note}
                        </li>
                      ))}
                    </ul>
                  </section>
                ) : null}

                {live.bookmarks.length > 0 ? (
                  <section className="border-t border-border p-3">
                    <h3 className="mb-2 flex items-center gap-1.5 text-overline uppercase text-subtle">
                      <BookmarkIcon className="size-3" aria-hidden />
                      Bookmarks
                    </h3>
                    <ul className="space-y-1.5">
                      {live.bookmarks.map((bookmark) => (
                        <li
                          key={bookmark.id}
                          className="flex items-start gap-2 text-caption"
                        >
                          <span className="font-mono text-label text-accent tabular">
                            {formatTimecode(bookmark.atSeconds)}
                          </span>
                          <span className="text-muted">{bookmark.label}</span>
                        </li>
                      ))}
                    </ul>
                  </section>
                ) : null}
              </div>
            ) : (
              <div className="flex min-h-0 flex-1 flex-col">
                <div
                  ref={chatRef}
                  className="min-h-0 flex-1 space-y-3 overflow-y-auto scrollbar-thin p-3"
                >
                  {live.chatMessages.length === 0 ? (
                    <EmptyState
                      icon={MessageSquare}
                      title="No messages"
                      description="Chat is visible to everyone on the call."
                      className="py-8"
                    />
                  ) : (
                    live.chatMessages.map((message) => (
                      <div key={message.id} className="flex gap-2">
                        <Avatar
                          name={
                            participants[message.authorIndex]?.name ?? "You"
                          }
                          size="sm"
                          className="mt-0.5 shrink-0"
                        />
                        <div className="min-w-0">
                          <div className="flex items-center gap-1.5">
                            <span className="text-label font-medium text-foreground">
                              {participants[message.authorIndex]?.name}
                            </span>
                            <span className="font-mono text-label text-subtle tabular">
                              {formatTimecode(message.atSeconds)}
                            </span>
                          </div>
                          <p className="text-caption text-muted">
                            {message.body}
                          </p>
                        </div>
                      </div>
                    ))
                  )}
                </div>

                <form
                  className="flex shrink-0 items-center gap-1.5 border-t border-border p-2"
                  onSubmit={(event) => {
                    event.preventDefault();
                    const selfIndex = participants.findIndex(
                      (p) => p.userId === session.userId,
                    );
                    live.sendChatMessage(
                      chatDraft,
                      selfIndex === -1 ? 0 : selfIndex,
                    );
                    setChatDraft("");
                  }}
                >
                  <Input
                    value={chatDraft}
                    onChange={(event) => setChatDraft(event.target.value)}
                    placeholder="Message everyone…"
                    aria-label="Chat message"
                    className="h-8 text-caption"
                  />
                  <Button
                    variant="primary"
                    size="icon-sm"
                    type="submit"
                    disabled={!chatDraft.trim()}
                    aria-label="Send message"
                  >
                    <Send />
                  </Button>
                </form>
              </div>
            )}
          </Tabs>
        </aside>
      </div>

      {/* Control bar */}
      <div className="shrink-0 border-t border-border bg-surface px-4 py-2.5">
        <div className="flex flex-wrap items-center gap-2">
          <Tooltip label={live.isMuted ? "Unmute" : "Mute"}>
            <Button
              variant={live.isMuted ? "danger" : "secondary"}
              size="icon"
              onClick={live.toggleMute}
              aria-label={
                live.isMuted ? "Unmute microphone" : "Mute microphone"
              }
              aria-pressed={live.isMuted}
            >
              {live.isMuted ? <MicOff /> : <Mic />}
            </Button>
          </Tooltip>

          {live.status === "recording" ? (
            <Button variant="secondary" size="md" onClick={live.pause}>
              <Pause />
              Pause recording
            </Button>
          ) : (
            <Button variant="secondary" size="md" onClick={live.resume}>
              <Play />
              Resume recording
            </Button>
          )}

          {/* Quick note */}
          <form
            className="flex items-center gap-1.5"
            onSubmit={(event) => {
              event.preventDefault();
              live.addQuickNote(noteDraft);
              setNoteDraft("");
              toast({ tone: "success", title: "Note added" });
            }}
          >
            <Input
              value={noteDraft}
              onChange={(event) => setNoteDraft(event.target.value)}
              placeholder="Add a note…"
              aria-label="Quick note"
              className="h-9 w-40 text-caption sm:w-52"
            />
            <Button
              variant="secondary"
              size="icon"
              type="submit"
              disabled={!noteDraft.trim()}
              aria-label="Save note"
            >
              <StickyNote />
            </Button>
          </form>

          {/* Bookmark */}
          <form
            className="flex items-center gap-1.5"
            onSubmit={(event) => {
              event.preventDefault();
              live.addBookmark(bookmarkDraft);
              setBookmarkDraft("");
              toast({
                tone: "success",
                title: "Moment bookmarked",
                description: formatTimecode(live.elapsedSeconds),
              });
            }}
          >
            <Input
              value={bookmarkDraft}
              onChange={(event) => setBookmarkDraft(event.target.value)}
              placeholder="Bookmark label…"
              aria-label="Bookmark label"
              className="h-9 w-36 text-caption sm:w-44"
            />
            <Button
              variant="secondary"
              size="icon"
              type="submit"
              aria-label="Bookmark this moment"
            >
              <BookmarkIcon />
            </Button>
          </form>

          <Button
            variant="danger"
            size="md"
            className="ml-auto"
            onClick={() => setConfirmEnd(true)}
            disabled={!isRunning}
          >
            <PhoneOff />
            End meeting
          </Button>
        </div>
      </div>

      <ConfirmDialog
        open={confirmEnd}
        onOpenChange={setConfirmEnd}
        title="End this meeting?"
        description={
          pendingActions.length > 0
            ? `Recording stops and the summary is generated. ${
                pendingActions.length
              } detected action ${
                pendingActions.length === 1 ? "item is" : "items are"
              } still unreviewed and will be discarded.`
            : "Recording stops, the transcript is saved and the summary is generated from what was discussed."
        }
        confirmLabel="End and save"
        destructive
        loading={saving}
        onConfirm={handleEnd}
      />
    </div>
  );
}
