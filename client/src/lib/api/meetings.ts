/**
 * Meetings service — list/query, CRUD, favourites, archive, and the related
 * transcript, summary, comment and action-item reads that the detail page needs.
 */

import { collection } from "@/lib/db/storage";
import type {
  ActionItem,
  AISummary,
  Bookmark,
  Comment,
  ListQuery,
  Meeting,
  MeetingPlatform,
  MeetingStatus,
  Paginated,
  SummaryStatus,
  TranscriptSegment,
  User,
} from "@/types/domain";
import {
  ApiError,
  generateId,
  matchesSearch,
  now,
  paginate,
  request,
  sortBy,
} from "./client";

const meetings = collection<Meeting>("meetings");
const transcripts = collection<TranscriptSegment>("transcripts");
const summaries = collection<AISummary>("summaries");
const actionItems = collection<ActionItem>("action_items");
const comments = collection<Comment>("comments");

export type MeetingSortKey =
  | "startsAt"
  | "title"
  | "durationSeconds"
  | "participants";

export interface MeetingQuery extends ListQuery<MeetingSortKey> {
  status?: MeetingStatus[];
  platform?: MeetingPlatform[];
  summaryStatus?: SummaryStatus[];
  tags?: string[];
  participantId?: string;
  favoritesOnly?: boolean;
  /** Archived meetings are excluded unless this is set. */
  includeArchived?: boolean;
  /** Restrict to meetings starting within this window. */
  from?: string;
  to?: string;
}

function applyFilters(all: Meeting[], query: MeetingQuery): Meeting[] {
  return all.filter((meeting) => {
    if (!query.includeArchived && meeting.isArchived) return false;
    if (query.favoritesOnly && !meeting.isFavorite) return false;

    if (query.status?.length && !query.status.includes(meeting.status)) {
      return false;
    }
    if (query.platform?.length && !query.platform.includes(meeting.platform)) {
      return false;
    }
    if (
      query.summaryStatus?.length &&
      !query.summaryStatus.includes(meeting.summaryStatus)
    ) {
      return false;
    }
    if (
      query.tags?.length &&
      !query.tags.some((t) => meeting.tags.includes(t))
    ) {
      return false;
    }
    if (
      query.participantId &&
      !meeting.participants.some((p) => p.userId === query.participantId)
    ) {
      return false;
    }

    const startsAt = new Date(meeting.startsAt).getTime();
    if (query.from && startsAt < new Date(query.from).getTime()) return false;
    if (query.to && startsAt > new Date(query.to).getTime()) return false;

    return matchesSearch(meeting, query.search, (m) => [
      m.title,
      m.description,
      ...m.tags,
      ...m.participants.map((p) => p.name),
    ]);
  });
}

function applySort(items: Meeting[], query: MeetingQuery): Meeting[] {
  // Most recent first is the sensible default for a meeting archive.
  const key = query.sortBy ?? "startsAt";
  const dir = query.sortDir ?? (key === "startsAt" ? "desc" : "asc");

  switch (key) {
    case "title":
      return sortBy(items, (m) => m.title, dir);
    case "durationSeconds":
      return sortBy(items, (m) => m.durationSeconds, dir);
    case "participants":
      return sortBy(items, (m) => m.participants.length, dir);
    default:
      return sortBy(items, (m) => new Date(m.startsAt).getTime(), dir);
  }
}

export function listMeetings(
  query: MeetingQuery = {},
): Promise<Paginated<Meeting>> {
  return request(() =>
    paginate(applySort(applyFilters(meetings.all(), query), query), query),
  );
}

/** Unpaginated variant for calendar and dashboard widgets. */
export function queryMeetings(query: MeetingQuery = {}): Promise<Meeting[]> {
  return request(() => applySort(applyFilters(meetings.all(), query), query));
}

export function getMeeting(id: string): Promise<Meeting> {
  return request(() => {
    const meeting = meetings.find(id);
    if (!meeting) throw new ApiError("Meeting not found", 404);
    return meeting;
  });
}

export interface CreateMeetingInput {
  title: string;
  description?: string;
  startsAt: string;
  endsAt: string;
  platform: MeetingPlatform;
  participantIds: string[];
  tags?: string[];
}

export function createMeeting(input: CreateMeetingInput): Promise<Meeting> {
  return request(() => {
    if (!input.title.trim()) {
      throw new ApiError("Title is required", 422);
    }
    if (new Date(input.endsAt) <= new Date(input.startsAt)) {
      throw new ApiError("End time must be after the start time", 422);
    }

    const users = collection<User>("users").all();
    const timestamp = now();

    const meeting: Meeting = {
      id: generateId("mtg"),
      title: input.title.trim(),
      description: input.description?.trim() ?? "",
      startsAt: input.startsAt,
      endsAt: input.endsAt,
      durationSeconds: 0,
      status: "scheduled",
      recordingStatus: "not_recorded",
      summaryStatus: "none",
      platform: input.platform,
      meetingUrl: null,
      organizerId: users[0]?.id ?? "",
      participants: input.participantIds.flatMap((userId) => {
        const user = users.find((u) => u.id === userId);
        if (!user) return [];
        return [
          {
            userId: user.id,
            name: user.name,
            email: user.email,
            avatarUrl: user.avatarUrl,
            role: "attendee" as const,
            talkTimeRatio: 0,
            attended: false,
          },
        ];
      }),
      tags: input.tags ?? [],
      isFavorite: false,
      isArchived: false,
      bookmarks: [],
      createdAt: timestamp,
      updatedAt: timestamp,
    };

    return meetings.insert(meeting);
  });
}

export function updateMeeting(
  id: string,
  patch: Partial<Meeting>,
): Promise<Meeting> {
  return request(() => {
    const updated = meetings.update(id, { ...patch, updatedAt: now() });
    if (!updated) throw new ApiError("Meeting not found", 404);
    return updated;
  });
}

/** Removes the meeting and everything that hangs off it. */
export function deleteMeeting(id: string): Promise<void> {
  return request(() => {
    if (!meetings.remove(id)) throw new ApiError("Meeting not found", 404);

    transcripts.replaceAll(transcripts.all().filter((s) => s.meetingId !== id));
    summaries.replaceAll(summaries.all().filter((s) => s.meetingId !== id));
    comments.replaceAll(comments.all().filter((c) => c.meetingId !== id));
    // Action items outlive their meeting — they just lose the back-reference.
    actionItems.replaceAll(
      actionItems
        .all()
        .map((item) =>
          item.meetingId === id ? { ...item, meetingId: null } : item,
        ),
    );
  });
}

export function deleteMeetings(ids: string[]): Promise<number> {
  return request(() => meetings.removeMany(ids));
}

export function toggleFavorite(id: string): Promise<Meeting> {
  return request(() => {
    const meeting = meetings.find(id);
    if (!meeting) throw new ApiError("Meeting not found", 404);

    const updated = meetings.update(id, {
      isFavorite: !meeting.isFavorite,
      updatedAt: now(),
    });
    return updated as Meeting;
  });
}

export function setArchived(ids: string[], archived: boolean): Promise<number> {
  return request(() => {
    let changed = 0;
    for (const id of ids) {
      if (meetings.update(id, { isArchived: archived, updatedAt: now() })) {
        changed += 1;
      }
    }
    return changed;
  });
}

/** Copies a meeting and its transcript/summary into a new draft record. */
export function duplicateMeeting(id: string): Promise<Meeting> {
  return request(() => {
    const source = meetings.find(id);
    if (!source) throw new ApiError("Meeting not found", 404);

    const timestamp = now();
    const copy: Meeting = {
      ...source,
      id: generateId("mtg"),
      title: `${source.title} (copy)`,
      status: "scheduled",
      recordingStatus: "not_recorded",
      summaryStatus: "none",
      isFavorite: false,
      isArchived: false,
      bookmarks: [],
      durationSeconds: 0,
      createdAt: timestamp,
      updatedAt: timestamp,
    };

    return meetings.insert(copy);
  });
}

export function addBookmark(
  meetingId: string,
  atSeconds: number,
  label: string,
): Promise<Meeting> {
  return request(() => {
    const meeting = meetings.find(meetingId);
    if (!meeting) throw new ApiError("Meeting not found", 404);

    const bookmark: Bookmark = {
      id: generateId("bkm"),
      atSeconds,
      label: label.trim() || "Bookmark",
      createdAt: now(),
    };

    const updated = meetings.update(meetingId, {
      bookmarks: [...meeting.bookmarks, bookmark].sort(
        (a, b) => a.atSeconds - b.atSeconds,
      ),
      updatedAt: now(),
    });
    return updated as Meeting;
  });
}

/* -------------------------------------------------------------------------- */
/* Related records                                                            */
/* -------------------------------------------------------------------------- */

export function getTranscript(meetingId: string): Promise<TranscriptSegment[]> {
  return request(() =>
    transcripts
      .all()
      .filter((segment) => segment.meetingId === meetingId)
      .sort((a, b) => a.startSeconds - b.startSeconds),
  );
}

export function getSummary(meetingId: string): Promise<AISummary | null> {
  return request(
    () => summaries.all().find((s) => s.meetingId === meetingId) ?? null,
  );
}

/**
 * Simulates an AI summarisation run over the stored transcript.
 *
 * The output is assembled from real transcript content — the longest lines and
 * the ones flagged as commitments — so a regenerated summary still reflects
 * what was actually said rather than inventing new claims.
 */
export function generateSummary(meetingId: string): Promise<AISummary> {
  return request(() => {
    const meeting = meetings.find(meetingId);
    if (!meeting) throw new ApiError("Meeting not found", 404);

    const segments = transcripts
      .all()
      .filter((s) => s.meetingId === meetingId)
      .sort((a, b) => a.startSeconds - b.startSeconds);

    if (segments.length === 0) {
      throw new ApiError("No transcript available to summarise", 422);
    }

    const substantive = [...segments]
      .sort((a, b) => b.text.length - a.text.length)
      .slice(0, 5);

    const timestamp = now();
    const summary: AISummary = {
      id: generateId("sum"),
      meetingId,
      executiveSummary: `${meeting.title} ran for ${Math.round(
        meeting.durationSeconds / 60,
      )} minutes with ${meeting.participants.length} participants. ${
        substantive[0]?.text ?? ""
      }`,
      keyPoints: substantive.map((s) => s.text),
      highlights: segments
        .filter((s) => s.isActionItem)
        .map((s) => ({
          id: generateId("hl"),
          kind: "decision" as const,
          text: s.text,
          sourceSegmentId: s.id,
          atSeconds: s.startSeconds,
        })),
      model: "claude-opus-4-8",
      generatedAt: timestamp,
      createdAt: timestamp,
      updatedAt: timestamp,
    };

    // Replace any previous summary so a meeting only ever has one.
    summaries.replaceAll([
      summary,
      ...summaries.all().filter((s) => s.meetingId !== meetingId),
    ]);
    meetings.update(meetingId, {
      summaryStatus: "ready",
      updatedAt: timestamp,
    });

    return summary;
  });
}

export function getMeetingActionItems(
  meetingId: string,
): Promise<ActionItem[]> {
  return request(() =>
    actionItems.all().filter((item) => item.meetingId === meetingId),
  );
}

export function getComments(meetingId: string): Promise<Comment[]> {
  return request(() =>
    comments
      .all()
      .filter((comment) => comment.meetingId === meetingId)
      .sort(
        (a, b) =>
          new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime(),
      ),
  );
}

export function addComment(input: {
  meetingId: string;
  authorId: string;
  body: string;
  parentId?: string | null;
  atSeconds?: number | null;
}): Promise<Comment> {
  return request(() => {
    const body = input.body.trim();
    if (!body) throw new ApiError("Comment cannot be empty", 422);

    // Resolve @mentions to user ids so they can be rendered as links later.
    const names = collection<User>("users").all();
    const mentions = names
      .filter((user) => body.includes(`@${user.name}`))
      .map((user) => user.id);

    const timestamp = now();
    return comments.insert({
      id: generateId("cmt"),
      meetingId: input.meetingId,
      authorId: input.authorId,
      body,
      mentions,
      parentId: input.parentId ?? null,
      atSeconds: input.atSeconds ?? null,
      createdAt: timestamp,
      updatedAt: timestamp,
    });
  });
}

export function deleteComment(id: string): Promise<void> {
  return request(() => {
    // Deleting a parent removes its replies too, rather than orphaning them.
    const remaining = comments
      .all()
      .filter((comment) => comment.id !== id && comment.parentId !== id);
    comments.replaceAll(remaining);
  });
}

/** Distinct tags across all meetings, for populating filter menus. */
export function listMeetingTags(): Promise<string[]> {
  return request(() =>
    [...new Set(meetings.all().flatMap((m) => m.tags))].sort(),
  );
}
