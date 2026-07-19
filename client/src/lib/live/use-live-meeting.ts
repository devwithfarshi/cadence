"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import type { Bookmark, Participant, TaskPriority } from "@/types/domain";
import { LIVE_SCRIPT } from "./script";

export type LiveStatus = "idle" | "recording" | "paused" | "ended";

export interface LiveSegment {
  id: string;
  speakerIndex: number;
  speakerId: string;
  speakerName: string;
  startSeconds: number;
  endSeconds: number;
  text: string;
  isActionItem: boolean;
}

export interface DetectedAction {
  id: string;
  title: string;
  assigneeIndex: number;
  priority: TaskPriority;
  /** Index into `segments`, so the source line can be shown and linked. */
  segmentIndex: number;
  status: "pending" | "accepted" | "dismissed";
}

export interface AiNote {
  id: string;
  text: string;
  atSeconds: number;
}

export interface ChatMessage {
  id: string;
  authorIndex: number;
  body: string;
  atSeconds: number;
}

/** Ticks per second for the timer and audio meter. */
const TICK_MS = 250;

/**
 * How long each transcript line takes to "be spoken".
 *
 * Derived from word count so longer contributions genuinely take longer, then
 * clamped so the demo stays watchable — a real 30-minute review compressed to
 * roughly two minutes of wall time.
 */
function lineDurationSeconds(text: string): number {
  const words = text.split(/\s+/).length;
  return Math.min(9, Math.max(3, Math.round(words / 9)));
}

export interface LiveMeetingState {
  status: LiveStatus;
  elapsedSeconds: number;
  segments: LiveSegment[];
  /** Index of whoever is speaking right now, or null between lines. */
  currentSpeakerIndex: number | null;
  /** Per-participant audio activity 0–1, for the level meters. */
  audioLevels: number[];
  detectedActions: DetectedAction[];
  aiNotes: AiNote[];
  bookmarks: Bookmark[];
  quickNotes: string[];
  chatMessages: ChatMessage[];
  isMuted: boolean;
  /** True once the script runs out, so the UI can prompt to wrap up. */
  scriptExhausted: boolean;
}

export interface LiveMeetingControls {
  start: () => void;
  pause: () => void;
  resume: () => void;
  end: () => void;
  toggleMute: () => void;
  addBookmark: (label: string) => void;
  addQuickNote: (note: string) => void;
  sendChatMessage: (body: string, authorIndex: number) => void;
  acceptAction: (id: string) => void;
  dismissAction: (id: string) => void;
  reset: () => void;
}

let idCounter = 0;
function nextId(prefix: string): string {
  idCounter += 1;
  return `${prefix}_${idCounter}`;
}

/**
 * Drives the live meeting simulation.
 *
 * A single interval advances a clock; everything else is derived from elapsed
 * time. Using one timer rather than a chain of `setTimeout`s means pause and
 * resume are trivially correct, and the transcript can never get ahead of the
 * clock the user is watching.
 */
export function useLiveMeeting(
  participants: Participant[],
): LiveMeetingState & LiveMeetingControls {
  const [status, setStatus] = useState<LiveStatus>("idle");
  const [elapsedSeconds, setElapsedSeconds] = useState(0);
  const [segments, setSegments] = useState<LiveSegment[]>([]);
  const [currentSpeakerIndex, setCurrentSpeakerIndex] = useState<number | null>(
    null,
  );
  const [audioLevels, setAudioLevels] = useState<number[]>([]);
  const [detectedActions, setDetectedActions] = useState<DetectedAction[]>([]);
  const [aiNotes, setAiNotes] = useState<AiNote[]>([]);
  const [bookmarks, setBookmarks] = useState<Bookmark[]>([]);
  const [quickNotes, setQuickNotes] = useState<string[]>([]);
  const [chatMessages, setChatMessages] = useState<ChatMessage[]>([]);
  const [isMuted, setIsMuted] = useState(false);
  const [scriptExhausted, setScriptExhausted] = useState(false);

  // Cursor into LIVE_SCRIPT, and the wall-clock second the current line ends.
  const lineIndexRef = useRef(0);
  const lineEndsAtRef = useRef(0);
  const elapsedRef = useRef(0);
  const timerRef = useRef<ReturnType<typeof setInterval> | null>(null);
  // Mirrors `segments.length` so a segment's index is known before state commits.
  const segmentCountRef = useRef(0);

  const clearTimer = useCallback(() => {
    if (timerRef.current !== null) {
      clearInterval(timerRef.current);
      timerRef.current = null;
    }
  }, []);

  /** Emits the next scripted line and schedules when it finishes. */
  const emitNextLine = useCallback(
    (atSeconds: number) => {
      const index = lineIndexRef.current;

      if (index >= LIVE_SCRIPT.length) {
        setScriptExhausted(true);
        setCurrentSpeakerIndex(null);
        return;
      }

      const line = LIVE_SCRIPT[index];
      // Guard against a script that references more speakers than are present.
      const speakerIndex = line.speaker % Math.max(1, participants.length);
      const speaker = participants[speakerIndex];
      const duration = lineDurationSeconds(line.text);

      const segment: LiveSegment = {
        id: nextId("seg"),
        speakerIndex,
        speakerId: speaker?.userId ?? "",
        speakerName: speaker?.name ?? "Unknown",
        startSeconds: atSeconds,
        endSeconds: atSeconds + duration,
        text: line.text,
        isActionItem: Boolean(line.action),
      };

      // The index this segment will occupy, tracked in a ref rather than read
      // from state. Deriving it inside a state updater would break under
      // StrictMode, which may invoke updaters twice.
      const segmentIndex = segmentCountRef.current;
      segmentCountRef.current += 1;

      setSegments((current) => [...current, segment]);

      const { action, note } = line;

      if (action) {
        setDetectedActions((actions) => [
          ...actions,
          {
            id: nextId("act"),
            title: action.title,
            assigneeIndex: action.assignee % Math.max(1, participants.length),
            priority: action.priority,
            segmentIndex,
            status: "pending",
          },
        ]);
      }

      if (note) {
        setAiNotes((notes) => [
          ...notes,
          { id: nextId("note"), text: note, atSeconds },
        ]);
      }

      setCurrentSpeakerIndex(speakerIndex);
      lineIndexRef.current = index + 1;
      // A short beat between speakers, so the meter isn't permanently lit.
      lineEndsAtRef.current = atSeconds + duration + 1;
    },
    [participants],
  );

  const tick = useCallback(() => {
    const next = elapsedRef.current + TICK_MS / 1000;
    elapsedRef.current = next;
    setElapsedSeconds(next);

    const whole = Math.floor(next);
    if (whole >= lineEndsAtRef.current) emitNextLine(whole);

    // Audio levels: the active speaker fluctuates, everyone else sits near zero
    // with occasional low-level background movement.
    setAudioLevels(
      participants.map((_, index) =>
        index === currentSpeakerIndex
          ? 0.35 + Math.random() * 0.65
          : Math.random() * 0.08,
      ),
    );
  }, [emitNextLine, participants, currentSpeakerIndex]);

  // The interval is re-created when `tick` changes identity, which happens on
  // every speaker change. Clearing first keeps exactly one timer alive.
  useEffect(() => {
    if (status !== "recording") return;

    clearTimer();
    timerRef.current = setInterval(tick, TICK_MS);
    return clearTimer;
  }, [status, tick, clearTimer]);

  useEffect(() => clearTimer, [clearTimer]);

  /* --- Controls ---------------------------------------------------------- */

  const start = useCallback(() => {
    elapsedRef.current = 0;
    lineIndexRef.current = 0;
    lineEndsAtRef.current = 0;
    segmentCountRef.current = 0;

    setElapsedSeconds(0);
    setSegments([]);
    setDetectedActions([]);
    setAiNotes([]);
    setBookmarks([]);
    setQuickNotes([]);
    setChatMessages([]);
    setScriptExhausted(false);
    setCurrentSpeakerIndex(null);
    setStatus("recording");
  }, []);

  const pause = useCallback(() => {
    setStatus("paused");
    setCurrentSpeakerIndex(null);
    setAudioLevels(participants.map(() => 0));
  }, [participants]);

  const resume = useCallback(() => setStatus("recording"), []);

  const end = useCallback(() => {
    clearTimer();
    setStatus("ended");
    setCurrentSpeakerIndex(null);
  }, [clearTimer]);

  const reset = useCallback(() => {
    clearTimer();
    elapsedRef.current = 0;
    lineIndexRef.current = 0;
    lineEndsAtRef.current = 0;
    segmentCountRef.current = 0;

    setStatus("idle");
    setElapsedSeconds(0);
    setSegments([]);
    setDetectedActions([]);
    setAiNotes([]);
    setBookmarks([]);
    setQuickNotes([]);
    setChatMessages([]);
    setScriptExhausted(false);
    setCurrentSpeakerIndex(null);
    setAudioLevels([]);
  }, [clearTimer]);

  const toggleMute = useCallback(() => setIsMuted((current) => !current), []);

  const addBookmark = useCallback((label: string) => {
    setBookmarks((current) => [
      ...current,
      {
        id: nextId("bkm"),
        atSeconds: Math.floor(elapsedRef.current),
        label: label.trim() || "Bookmark",
        createdAt: new Date().toISOString(),
      },
    ]);
  }, []);

  const addQuickNote = useCallback((note: string) => {
    const trimmed = note.trim();
    if (!trimmed) return;
    setQuickNotes((current) => [...current, trimmed]);
  }, []);

  const sendChatMessage = useCallback((body: string, authorIndex: number) => {
    const trimmed = body.trim();
    if (!trimmed) return;

    setChatMessages((current) => [
      ...current,
      {
        id: nextId("msg"),
        authorIndex,
        body: trimmed,
        atSeconds: Math.floor(elapsedRef.current),
      },
    ]);
  }, []);

  const acceptAction = useCallback((id: string) => {
    setDetectedActions((current) =>
      current.map((action) =>
        action.id === id ? { ...action, status: "accepted" } : action,
      ),
    );
  }, []);

  const dismissAction = useCallback((id: string) => {
    setDetectedActions((current) =>
      current.map((action) =>
        action.id === id ? { ...action, status: "dismissed" } : action,
      ),
    );
  }, []);

  return {
    status,
    elapsedSeconds,
    segments,
    currentSpeakerIndex,
    audioLevels,
    detectedActions,
    aiNotes,
    bookmarks,
    quickNotes,
    chatMessages,
    isMuted,
    scriptExhausted,
    start,
    pause,
    resume,
    end,
    toggleMute,
    addBookmark,
    addQuickNote,
    sendChatMessage,
    acceptAction,
    dismissAction,
    reset,
  };
}
