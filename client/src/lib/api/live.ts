/**
 * Live meeting persistence.
 *
 * The simulation itself runs entirely in React state — nothing is written until
 * the meeting ends, which mirrors how a real recorder buffers and then uploads.
 * Ending a session writes the meeting, its transcript, its summary and any
 * accepted action items in one go, so the result is indistinguishable from a
 * seeded meeting when it appears in the meetings list.
 */

import { collection } from "@/lib/db/storage";
import type {
  ActionItem,
  AISummary,
  Bookmark,
  Meeting,
  Participant,
  TaskPriority,
  TranscriptSegment,
  User,
} from "@/types/domain";
import { generateId, now, request } from "./client";

const meetings = collection<Meeting>("meetings");
const transcripts = collection<TranscriptSegment>("transcripts");
const summaries = collection<AISummary>("summaries");
const actionItems = collection<ActionItem>("action_items");

export interface LiveSegmentDraft {
  speakerId: string;
  speakerName: string;
  startSeconds: number;
  endSeconds: number;
  text: string;
  isActionItem: boolean;
}

export interface LiveActionDraft {
  title: string;
  assigneeId: string | null;
  priority: TaskPriority;
  /** Index into the emitted segment list, resolved to a real id on save. */
  sourceSegmentIndex: number | null;
}

export interface EndLiveMeetingInput {
  title: string;
  participants: Participant[];
  organizerId: string;
  startedAt: string;
  durationSeconds: number;
  segments: LiveSegmentDraft[];
  /** Only the action items the user accepted — dismissed ones are discarded. */
  acceptedActions: LiveActionDraft[];
  aiNotes: string[];
  bookmarks: Bookmark[];
  /** Free-text notes typed during the call, appended to the summary. */
  quickNotes: string[];
  tags: string[];
}

/**
 * Writes a finished live session to the store and returns the new meeting.
 *
 * Everything is created together because a half-written meeting — a transcript
 * with no meeting, or action items pointing at a segment that was never saved —
 * would be worse than not saving at all.
 */
export function endLiveMeeting(input: EndLiveMeetingInput): Promise<Meeting> {
  return request(() => {
    const meetingId = generateId("mtg");
    const timestamp = now();
    const endsAt = new Date(
      new Date(input.startedAt).getTime() + input.durationSeconds * 1000,
    ).toISOString();

    // Talk time is derived from what was actually said, not assigned upfront.
    const spokenSeconds = new Map<string, number>();
    for (const segment of input.segments) {
      const spoken = Math.max(0, segment.endSeconds - segment.startSeconds);
      spokenSeconds.set(
        segment.speakerId,
        (spokenSeconds.get(segment.speakerId) ?? 0) + spoken,
      );
    }
    const totalSpoken =
      [...spokenSeconds.values()].reduce((sum, value) => sum + value, 0) || 1;

    const participants: Participant[] = input.participants.map(
      (participant) => ({
        ...participant,
        attended: true,
        talkTimeRatio: Number(
          ((spokenSeconds.get(participant.userId) ?? 0) / totalSpoken).toFixed(
            4,
          ),
        ),
      }),
    );

    const meeting: Meeting = {
      id: meetingId,
      title: input.title,
      description: "Recorded live in Cadence.",
      startsAt: input.startedAt,
      endsAt,
      durationSeconds: input.durationSeconds,
      status: "completed",
      recordingStatus: "recorded",
      summaryStatus: "ready",
      platform: "google_meet",
      meetingUrl: null,
      organizerId: input.organizerId,
      participants,
      tags: input.tags,
      isFavorite: false,
      isArchived: false,
      bookmarks: input.bookmarks,
      createdAt: timestamp,
      updatedAt: timestamp,
    };
    meetings.insert(meeting);

    // Persist segments, keeping their emission order so indices stay valid.
    const savedSegments: TranscriptSegment[] = input.segments.map(
      (segment) => ({
        id: generateId("seg"),
        meetingId,
        speakerId: segment.speakerId,
        speakerName: segment.speakerName,
        startSeconds: segment.startSeconds,
        endSeconds: segment.endSeconds,
        text: segment.text,
        // Live capture, not a re-run of the model, so confidence is uniform.
        confidence: 0.96,
        isActionItem: segment.isActionItem,
      }),
    );
    transcripts.replaceAll([...savedSegments, ...transcripts.all()]);

    const summaryBody = [
      ...input.aiNotes,
      ...(input.quickNotes.length > 0
        ? [`Notes captured during the call: ${input.quickNotes.join(" ")}`]
        : []),
    ];

    const summary: AISummary = {
      id: generateId("sum"),
      meetingId,
      executiveSummary:
        summaryBody[0] ??
        `${input.title} was recorded live with ${participants.length} participants.`,
      keyPoints: summaryBody.slice(1),
      highlights: input.acceptedActions.map((action) => ({
        id: generateId("hl"),
        kind: "decision" as const,
        text: action.title,
        sourceSegmentId:
          action.sourceSegmentIndex !== null
            ? (savedSegments[action.sourceSegmentIndex]?.id ?? null)
            : null,
        atSeconds:
          action.sourceSegmentIndex !== null
            ? (savedSegments[action.sourceSegmentIndex]?.startSeconds ?? null)
            : null,
      })),
      model: "claude-opus-4-8",
      generatedAt: timestamp,
      createdAt: timestamp,
      updatedAt: timestamp,
    };
    summaries.insert(summary);

    const savedActions: ActionItem[] = input.acceptedActions.map((action) => ({
      id: generateId("act"),
      title: action.title,
      description: `Detected during "${input.title}".`,
      assigneeId: action.assigneeId,
      creatorId: input.organizerId,
      dueDate: null,
      priority: action.priority,
      status: "todo",
      meetingId,
      sourceSegmentId:
        action.sourceSegmentIndex !== null
          ? (savedSegments[action.sourceSegmentIndex]?.id ?? null)
          : null,
      completedAt: null,
      tags: input.tags.slice(0, 1),
      createdAt: timestamp,
      updatedAt: timestamp,
    }));
    if (savedActions.length > 0) {
      actionItems.replaceAll([...savedActions, ...actionItems.all()]);
    }

    return meeting;
  });
}

/**
 * The roster a new live session starts with.
 *
 * Picks the signed-in user plus a few active colleagues, so the simulation has
 * real names and avatars rather than placeholders.
 */
export function getLiveParticipants(
  organizerId: string,
  count = 4,
): Promise<Participant[]> {
  return request(() => {
    const users = collection<User>("users")
      .all()
      .filter((user) => user.status === "active");

    const organizer = users.find((user) => user.id === organizerId);
    const others = users.filter((user) => user.id !== organizerId);
    const roster = [...(organizer ? [organizer] : []), ...others].slice(
      0,
      count,
    );

    return roster.map((user, index) => ({
      userId: user.id,
      name: user.name,
      email: user.email,
      avatarUrl: user.avatarUrl,
      role: index === 0 ? ("host" as const) : ("attendee" as const),
      talkTimeRatio: 0,
      attended: true,
    }));
  });
}
