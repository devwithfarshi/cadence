"use client";

import {
  Archive,
  ArchiveRestore,
  Bookmark,
  Calendar,
  Clock,
  Copy,
  ExternalLink,
  Paperclip,
  Star,
  Trash2,
  Users,
} from "lucide-react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { use, useEffect, useState } from "react";
import { ActionItemsPanel } from "@/components/meetings/detail/action-items-panel";
import { CommentsPanel } from "@/components/meetings/detail/comments-panel";
import { SummaryPanel } from "@/components/meetings/detail/summary-panel";
import { TranscriptPanel } from "@/components/meetings/detail/transcript-panel";
import {
  MeetingStatusBadge,
  PLATFORM_LABELS,
  RecordingBadge,
  SummaryBadge,
} from "@/components/meetings/status";
import { useSession } from "@/components/providers/auth-provider";
import { useSetBreadcrumbs } from "@/components/shell/breadcrumb-context";
import { PageContainer } from "@/components/shell/page-header";
import { Avatar } from "@/components/ui/avatar";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { ConfirmDialog } from "@/components/ui/dialog";
import {
  EmptyState,
  ErrorState,
  Progress,
  Skeleton,
} from "@/components/ui/feedback";
import {
  Tabs,
  TabsContent,
  TabsCount,
  TabsList,
  TabsTrigger,
} from "@/components/ui/tabs";
import { useToast } from "@/components/ui/toast";
import { Tooltip } from "@/components/ui/tooltip";
import {
  deleteMeeting,
  getComments,
  getMeeting,
  getMeetingActionItems,
  getSummary,
  getTranscript,
  setArchived,
  toggleFavorite,
} from "@/lib/api/meetings";
import { listUsers, recordRecentMeeting } from "@/lib/api/workspace";
import { collection } from "@/lib/db/storage";
import { useAsync } from "@/lib/hooks/use-async";
import { cn } from "@/lib/utils/cn";
import {
  formatDate,
  formatDuration,
  formatFileSize,
  formatTime,
  formatTimecode,
} from "@/lib/utils/format";
import type { DocumentFile } from "@/types/domain";

export default function MeetingDetailPage({
  params,
}: {
  // Next 16 delivers route params as a Promise, unwrapped here with `use`.
  params: Promise<{ id: string }>;
}) {
  const { id } = use(params);
  const router = useRouter();
  const session = useSession();
  const { toast } = useToast();

  const [confirmDelete, setConfirmDelete] = useState(false);
  const [deleting, setDeleting] = useState(false);

  const meeting = useAsync(() => getMeeting(id), [id]);
  const transcript = useAsync(() => getTranscript(id), [id]);
  const summary = useAsync(() => getSummary(id), [id]);
  const actionItems = useAsync(() => getMeetingActionItems(id), [id]);
  const comments = useAsync(() => getComments(id), [id]);
  const users = useAsync(() => listUsers(), []);

  // Attachments come straight from the store — there is no document service
  // in this pass, and the detail page only needs a read.
  const attachments = useAsync(
    async () =>
      collection<DocumentFile>("documents")
        .all()
        .filter((document) => document.meetingId === id),
    [id],
  );

  useSetBreadcrumbs(
    meeting.data
      ? [
          { label: "Meetings", href: "/meetings" },
          { label: meeting.data.title },
        ]
      : null,
  );

  // Feeds the "recently opened" list in preferences. Keyed on the id rather
  // than the loaded object so it records once per meeting, not per refetch.
  useEffect(() => {
    recordRecentMeeting(id);
  }, [id]);

  async function handleToggleFavorite() {
    if (!meeting.data) return;

    meeting.setData((current) =>
      current ? { ...current, isFavorite: !current.isFavorite } : current,
    );

    try {
      const updated = await toggleFavorite(id);
      toast({
        tone: "success",
        title: updated.isFavorite
          ? "Added to favourites"
          : "Removed from favourites",
      });
    } catch {
      meeting.refetch();
      toast({ tone: "error", title: "Could not update favourite" });
    }
  }

  async function handleArchive() {
    if (!meeting.data) return;
    const next = !meeting.data.isArchived;

    try {
      await setArchived([id], next);
      meeting.refetch();
      toast({
        tone: "success",
        title: next ? "Meeting archived" : "Meeting restored",
      });
    } catch {
      toast({ tone: "error", title: "Could not update meeting" });
    }
  }

  async function handleDelete() {
    setDeleting(true);
    try {
      await deleteMeeting(id);
      toast({ tone: "success", title: "Meeting deleted" });
      router.push("/meetings");
    } catch {
      setDeleting(false);
      toast({ tone: "error", title: "Could not delete meeting" });
    }
  }

  async function handleCopyLink() {
    try {
      await navigator.clipboard.writeText(window.location.href);
      toast({ tone: "success", title: "Link copied to clipboard" });
    } catch {
      toast({ tone: "error", title: "Could not copy link" });
    }
  }

  /* --- Loading and error --------------------------------------------------- */

  if (meeting.error) {
    return (
      <PageContainer>
        <ErrorState
          title="Meeting not found"
          description={meeting.error.message}
          onRetry={meeting.refetch}
        />
        <div className="flex justify-center">
          <Button variant="secondary" size="sm" asChild>
            <Link href="/meetings">Back to meetings</Link>
          </Button>
        </div>
      </PageContainer>
    );
  }

  if (!meeting.data) {
    return (
      <PageContainer>
        <div className="space-y-4">
          <Skeleton className="h-8 w-2/3 max-w-md" />
          <Skeleton className="h-4 w-1/3 max-w-xs" />
          <Skeleton className="h-64 w-full" />
        </div>
      </PageContainer>
    );
  }

  const data = meeting.data;
  const openActionItems = (actionItems.data ?? []).filter(
    (item) => item.status !== "done",
  );

  return (
    <PageContainer>
      {/* Header */}
      <div className="pb-5">
        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
          <div className="min-w-0 space-y-2">
            <div className="flex flex-wrap items-center gap-2">
              <MeetingStatusBadge status={data.status} />
              <RecordingBadge status={data.recordingStatus} />
              <SummaryBadge status={data.summaryStatus} />
              {data.isArchived ? <Badge tone="neutral">Archived</Badge> : null}
            </div>

            <h1 className="text-display text-foreground">{data.title}</h1>

            {data.description ? (
              <p className="max-w-3xl text-body text-muted">
                {data.description}
              </p>
            ) : null}

            {data.tags.length > 0 ? (
              <div className="flex flex-wrap gap-1.5">
                {data.tags.map((tag) => (
                  <Badge key={tag} tone="outline" size="sm">
                    {tag}
                  </Badge>
                ))}
              </div>
            ) : null}
          </div>

          <div className="flex shrink-0 items-center gap-1.5">
            <Tooltip
              label={
                data.isFavorite ? "Remove from favourites" : "Add to favourites"
              }
            >
              <Button
                variant="secondary"
                size="icon"
                onClick={handleToggleFavorite}
                aria-label={
                  data.isFavorite
                    ? "Remove from favourites"
                    : "Add to favourites"
                }
                aria-pressed={data.isFavorite}
              >
                <Star
                  className={cn(data.isFavorite && "fill-warning text-warning")}
                />
              </Button>
            </Tooltip>

            <Tooltip label="Copy link">
              <Button
                variant="secondary"
                size="icon"
                onClick={handleCopyLink}
                aria-label="Copy link to this meeting"
              >
                <Copy />
              </Button>
            </Tooltip>

            <Tooltip label={data.isArchived ? "Restore" : "Archive"}>
              <Button
                variant="secondary"
                size="icon"
                onClick={handleArchive}
                aria-label={
                  data.isArchived ? "Restore meeting" : "Archive meeting"
                }
              >
                {data.isArchived ? <ArchiveRestore /> : <Archive />}
              </Button>
            </Tooltip>

            <Tooltip label="Delete meeting">
              <Button
                variant="danger-outline"
                size="icon"
                onClick={() => setConfirmDelete(true)}
                aria-label="Delete meeting"
              >
                <Trash2 />
              </Button>
            </Tooltip>
          </div>
        </div>
      </div>

      <div className="grid gap-4 lg:grid-cols-[1fr_18rem]">
        {/* Tabs */}
        <div className="min-w-0">
          <Tabs defaultValue="summary">
            <TabsList>
              <TabsTrigger value="summary">AI Summary</TabsTrigger>
              <TabsTrigger value="transcript">
                Transcript
                {transcript.data ? (
                  <TabsCount>{transcript.data.length}</TabsCount>
                ) : null}
              </TabsTrigger>
              <TabsTrigger value="actions">
                Action items
                {openActionItems.length > 0 ? (
                  <TabsCount>{openActionItems.length}</TabsCount>
                ) : null}
              </TabsTrigger>
              <TabsTrigger value="attachments">
                Attachments
                {attachments.data && attachments.data.length > 0 ? (
                  <TabsCount>{attachments.data.length}</TabsCount>
                ) : null}
              </TabsTrigger>
              <TabsTrigger value="comments">
                Comments
                {comments.data && comments.data.length > 0 ? (
                  <TabsCount>{comments.data.length}</TabsCount>
                ) : null}
              </TabsTrigger>
            </TabsList>

            <div className="mt-4">
              <TabsContent value="summary">
                <Card>
                  <SummaryPanel
                    meeting={data}
                    summary={summary.data ?? null}
                    loading={summary.loading}
                    hasTranscript={(transcript.data ?? []).length > 0}
                    onRegenerated={() => {
                      summary.refetch();
                      meeting.refetch();
                    }}
                  />
                </Card>
              </TabsContent>

              <TabsContent value="transcript">
                <Card>
                  <TranscriptPanel
                    meeting={data}
                    segments={transcript.data ?? []}
                    loading={transcript.loading}
                  />
                </Card>
              </TabsContent>

              <TabsContent value="actions">
                <Card>
                  <ActionItemsPanel
                    meetingId={id}
                    items={actionItems.data ?? []}
                    users={users.data ?? []}
                    currentUserId={session.userId}
                    loading={actionItems.loading || users.loading}
                    onChanged={actionItems.refetch}
                  />
                </Card>
              </TabsContent>

              <TabsContent value="attachments">
                <Card>
                  {attachments.loading ? (
                    <div className="space-y-2 p-4">
                      {[0, 1].map((row) => (
                        <Skeleton key={row} className="h-12 w-full" />
                      ))}
                    </div>
                  ) : (attachments.data ?? []).length === 0 ? (
                    <EmptyState
                      icon={Paperclip}
                      title="No attachments"
                      description="Documents linked to this meeting will appear here."
                    />
                  ) : (
                    <ul className="divide-y divide-border">
                      {(attachments.data ?? []).map((document) => (
                        <li
                          key={document.id}
                          className="flex items-center gap-3 px-4 py-3"
                        >
                          <span className="flex size-9 shrink-0 items-center justify-center rounded-control border border-border bg-surface-raised text-overline uppercase text-subtle">
                            {document.type}
                          </span>

                          <div className="min-w-0 flex-1">
                            <p className="truncate text-body font-medium text-foreground">
                              {document.name}
                            </p>
                            <p className="text-caption text-muted tabular">
                              {formatFileSize(document.sizeBytes)}
                            </p>
                          </div>

                          <Badge
                            tone={
                              document.processingStatus === "indexed"
                                ? "success"
                                : document.processingStatus === "failed"
                                  ? "danger"
                                  : "info"
                            }
                          >
                            {document.processingStatus}
                          </Badge>
                        </li>
                      ))}
                    </ul>
                  )}
                </Card>
              </TabsContent>

              <TabsContent value="comments">
                <Card>
                  <CommentsPanel
                    meetingId={id}
                    comments={comments.data ?? []}
                    users={users.data ?? []}
                    currentUserId={session.userId}
                    loading={comments.loading || users.loading}
                    onChanged={comments.refetch}
                  />
                </Card>
              </TabsContent>
            </div>
          </Tabs>
        </div>

        {/* Contextual sidebar */}
        <aside className="space-y-4">
          <Card>
            <div className="space-y-3 p-4">
              <h2 className="text-overline uppercase text-subtle">Details</h2>

              <dl className="space-y-2.5">
                <div className="flex items-start gap-2">
                  <Calendar
                    className="mt-0.5 size-3.5 shrink-0 text-subtle"
                    aria-hidden
                  />
                  <div className="min-w-0">
                    <dt className="sr-only">Date</dt>
                    <dd className="text-caption text-foreground">
                      {formatDate(data.startsAt)}
                    </dd>
                    <dd className="text-caption text-muted tabular">
                      {formatTime(data.startsAt)} – {formatTime(data.endsAt)}
                    </dd>
                  </div>
                </div>

                <div className="flex items-start gap-2">
                  <Clock
                    className="mt-0.5 size-3.5 shrink-0 text-subtle"
                    aria-hidden
                  />
                  <div>
                    <dt className="sr-only">Duration</dt>
                    <dd className="text-caption text-foreground tabular">
                      {formatDuration(data.durationSeconds)}
                    </dd>
                  </div>
                </div>

                <div className="flex items-start gap-2">
                  <ExternalLink
                    className="mt-0.5 size-3.5 shrink-0 text-subtle"
                    aria-hidden
                  />
                  <div className="min-w-0">
                    <dt className="sr-only">Platform</dt>
                    <dd className="text-caption text-foreground">
                      {PLATFORM_LABELS[data.platform]}
                    </dd>
                  </div>
                </div>
              </dl>
            </div>
          </Card>

          {/* Participants with talk-time distribution */}
          <Card>
            <div className="space-y-3 p-4">
              <h2 className="flex items-center gap-1.5 text-overline uppercase text-subtle">
                <Users className="size-3" aria-hidden />
                Participants ({data.participants.length})
              </h2>

              <ul className="space-y-3">
                {[...data.participants]
                  .sort((a, b) => b.talkTimeRatio - a.talkTimeRatio)
                  .map((participant) => (
                    <li key={participant.userId} className="space-y-1.5">
                      <div className="flex items-center gap-2">
                        <Avatar name={participant.name} size="sm" />
                        <div className="min-w-0 flex-1">
                          <p className="truncate text-caption text-foreground">
                            {participant.name}
                          </p>
                          <p className="text-label capitalize text-subtle">
                            {participant.role}
                          </p>
                        </div>
                        {data.status === "completed" ? (
                          <span className="shrink-0 text-label text-muted tabular">
                            {Math.round(participant.talkTimeRatio * 100)}%
                          </span>
                        ) : null}
                      </div>

                      {data.status === "completed" ? (
                        <Progress
                          value={participant.talkTimeRatio * 100}
                          label={`${participant.name} spoke for ${Math.round(
                            participant.talkTimeRatio * 100,
                          )}% of the meeting`}
                        />
                      ) : null}
                    </li>
                  ))}
              </ul>
            </div>
          </Card>

          {/* Bookmarks */}
          {data.bookmarks.length > 0 ? (
            <Card>
              <div className="space-y-3 p-4">
                <h2 className="flex items-center gap-1.5 text-overline uppercase text-subtle">
                  <Bookmark className="size-3" aria-hidden />
                  Bookmarks
                </h2>
                <ul className="space-y-2">
                  {data.bookmarks.map((bookmark) => (
                    <li key={bookmark.id} className="flex items-start gap-2">
                      <span className="font-mono text-label text-accent tabular">
                        {formatTimecode(bookmark.atSeconds)}
                      </span>
                      <span className="text-caption text-muted">
                        {bookmark.label}
                      </span>
                    </li>
                  ))}
                </ul>
              </div>
            </Card>
          ) : null}
        </aside>
      </div>

      <ConfirmDialog
        open={confirmDelete}
        onOpenChange={setConfirmDelete}
        title="Delete this meeting?"
        description="This removes the recording, transcript, summary and comments. Action items are kept but lose their link to this meeting. This cannot be undone."
        confirmLabel="Delete meeting"
        destructive
        loading={deleting}
        onConfirm={handleDelete}
      />
    </PageContainer>
  );
}
