/**
 * AI chat.
 *
 * The "model" here is a retrieval-and-template routine, not a language model.
 * It only ever answers from records actually in the store, and every citation
 * points at something real — a fabricated answer with a plausible-looking
 * source would be far more misleading in a demo than an honest "I don't know".
 */

import { collection } from "@/lib/db/storage";
import type {
  ActionItem,
  AISummary,
  ChatConversation,
  ChatMessage,
  ChatSource,
  DocumentFile,
  KnowledgeItem,
  Meeting,
} from "@/types/domain";
import { ApiError, generateId, now, request } from "./client";

const conversations = collection<ChatConversation>("conversations");
const meetings = collection<Meeting>("meetings");
const summaries = collection<AISummary>("summaries");
const tasks = collection<ActionItem>("action_items");
const documents = collection<DocumentFile>("documents");
const knowledge = collection<KnowledgeItem>("knowledge");

export const PROMPT_SUGGESTIONS = [
  "What did we decide in the last roadmap review?",
  "What are the open risks across recent meetings?",
  "Which action items are overdue, and who owns them?",
  "Generate a weekly report",
  "Rewrite the last summary as a brief",
];

export function listConversations(): Promise<ChatConversation[]> {
  return request(() =>
    conversations
      .all()
      .sort(
        (a, b) =>
          new Date(b.updatedAt).getTime() - new Date(a.updatedAt).getTime(),
      ),
  );
}

export function getConversation(id: string): Promise<ChatConversation> {
  return request(() => {
    const conversation = conversations.find(id);
    if (!conversation) throw new ApiError("Conversation not found", 404);
    return conversation;
  });
}

export function createConversation(): Promise<ChatConversation> {
  return request(() => {
    const timestamp = now();
    return conversations.insert({
      id: generateId("cnv"),
      title: "New conversation",
      messages: [],
      createdAt: timestamp,
      updatedAt: timestamp,
    });
  });
}

export function deleteConversation(id: string): Promise<void> {
  return request(() => {
    conversations.remove(id);
  });
}

export function renameConversation(
  id: string,
  title: string,
): Promise<ChatConversation> {
  return request(() => {
    const updated = conversations.update(id, {
      title: title.trim() || "Untitled",
      updatedAt: now(),
    });
    if (!updated) throw new ApiError("Conversation not found", 404);
    return updated;
  });
}

/* -------------------------------------------------------------------------- */
/* Retrieval                                                                  */
/* -------------------------------------------------------------------------- */

/** Words too common to be worth matching on. */
const STOPWORDS = new Set([
  "the",
  "a",
  "an",
  "and",
  "or",
  "but",
  "what",
  "which",
  "who",
  "whom",
  "whose",
  "did",
  "do",
  "does",
  "was",
  "were",
  "is",
  "are",
  "be",
  "been",
  "in",
  "on",
  "at",
  "to",
  "for",
  "of",
  "about",
  "we",
  "our",
  "us",
  "i",
  "me",
  "my",
  "you",
  "it",
  "its",
  "this",
  "that",
  "these",
  "those",
  "how",
  "when",
  "where",
  "why",
  "can",
  "could",
  "would",
  "should",
  "there",
  "their",
  "from",
  "with",
  "last",
  "recent",
  "any",
  "all",
  "some",
  "me",
  "give",
  "tell",
  "show",
]);

function keywordsOf(question: string): string[] {
  return question
    .toLowerCase()
    .replace(/[^\w\s]/g, " ")
    .split(/\s+/)
    .filter((word) => word.length > 2 && !STOPWORDS.has(word));
}

function scoreText(text: string, keywords: string[]): number {
  const haystack = text.toLowerCase();
  return keywords.reduce(
    (score, word) => score + (haystack.includes(word) ? 1 : 0),
    0,
  );
}

interface Retrieved {
  meetings: Meeting[];
  summaries: AISummary[];
  documents: DocumentFile[];
  knowledge: KnowledgeItem[];
  overdueTasks: ActionItem[];
}

function retrieve(question: string): Retrieved {
  const keywords = keywordsOf(question);
  const nowMs = Date.now();

  const rankedMeetings = meetings
    .all()
    .filter((m) => !m.isArchived)
    .map((meeting) => ({
      meeting,
      score:
        scoreText(
          `${meeting.title} ${meeting.description} ${meeting.tags.join(" ")}`,
          keywords,
        ) * 2,
    }))
    .filter((entry) => entry.score > 0)
    .sort((a, b) => b.score - a.score)
    .slice(0, 3)
    .map((entry) => entry.meeting);

  // Summaries are searched by body so a question can match what was *said*,
  // not only what the meeting was called.
  const rankedSummaries = summaries
    .all()
    .map((summary) => ({
      summary,
      score: scoreText(
        `${summary.executiveSummary} ${summary.keyPoints.join(" ")} ${summary.highlights
          .map((h) => h.text)
          .join(" ")}`,
        keywords,
      ),
    }))
    .filter((entry) => entry.score > 0)
    .sort((a, b) => b.score - a.score)
    .slice(0, 3)
    .map((entry) => entry.summary);

  return {
    meetings: rankedMeetings,
    summaries: rankedSummaries,
    documents: documents
      .all()
      .filter((doc) => scoreText(`${doc.name} ${doc.excerpt}`, keywords) > 0)
      .slice(0, 3),
    knowledge: knowledge
      .all()
      .filter(
        (item) => scoreText(`${item.title} ${item.excerpt}`, keywords) > 0,
      )
      .slice(0, 3),
    overdueTasks: tasks
      .all()
      .filter(
        (task) =>
          task.status !== "done" &&
          task.dueDate !== null &&
          new Date(task.dueDate).getTime() < nowMs,
      )
      .slice(0, 5),
  };
}

/* -------------------------------------------------------------------------- */
/* Answer composition                                                         */
/* -------------------------------------------------------------------------- */

function sourcesFrom(found: Retrieved): ChatSource[] {
  const sources: ChatSource[] = [];

  for (const meeting of found.meetings) {
    sources.push({
      id: meeting.id,
      label: meeting.title,
      kind: "meeting",
      href: `/meetings/${meeting.id}`,
    });
  }
  // A summary's own meeting is the useful destination, and only if not already cited.
  for (const summary of found.summaries) {
    const meeting = meetings.find(summary.meetingId);
    if (meeting && !sources.some((s) => s.id === meeting.id)) {
      sources.push({
        id: meeting.id,
        label: meeting.title,
        kind: "meeting",
        href: `/meetings/${meeting.id}`,
      });
    }
  }
  for (const doc of found.documents) {
    sources.push({
      id: doc.id,
      label: doc.name,
      kind: "document",
      href: "/documents",
    });
  }
  for (const item of found.knowledge) {
    sources.push({
      id: item.id,
      label: item.title,
      kind: "knowledge",
      href: "/knowledge",
    });
  }

  return sources.slice(0, 5);
}

/**
 * Builds a status report from the workspace.
 *
 * Every number is counted from stored records rather than estimated, so the
 * report can be checked against the Analytics page and will agree with it.
 */
function composeReport(question: string): string {
  const lower = question.toLowerCase();
  const days = lower.includes("month")
    ? 30
    : lower.includes("quarter")
      ? 90
      : 7;
  const since = Date.now() - days * 24 * 60 * 60 * 1000;
  const period = days === 7 ? "week" : days === 30 ? "month" : "quarter";

  const inPeriod = meetings
    .all()
    .filter(
      (meeting) =>
        !meeting.isArchived && new Date(meeting.startsAt).getTime() >= since,
    );

  const held = inPeriod.filter((meeting) => meeting.status === "completed");
  const hours = held.reduce((sum, m) => sum + m.durationSeconds / 3600, 0);

  const allTasks = tasks.all();
  const created = allTasks.filter(
    (task) => new Date(task.createdAt).getTime() >= since,
  );
  const completed = allTasks.filter(
    (task) =>
      task.completedAt !== null &&
      new Date(task.completedAt).getTime() >= since,
  );
  const overdue = allTasks.filter(
    (task) =>
      task.status !== "done" &&
      task.dueDate !== null &&
      new Date(task.dueDate).getTime() < Date.now(),
  );

  if (inPeriod.length === 0 && created.length === 0) {
    return `There is no activity in the last ${period} to report on. Try a longer period, such as "generate a monthly report".`;
  }

  const decisions = summaries
    .all()
    .filter((summary) => new Date(summary.generatedAt).getTime() >= since)
    .flatMap((summary) =>
      summary.highlights.filter((h) => h.kind === "decision"),
    )
    .slice(0, 5);

  const risks = summaries
    .all()
    .filter((summary) => new Date(summary.generatedAt).getTime() >= since)
    .flatMap((summary) => summary.highlights.filter((h) => h.kind === "risk"))
    .slice(0, 3);

  const sections = [
    `ACTIVITY REPORT — LAST ${period.toUpperCase()}`,
    `${held.length} ${held.length === 1 ? "meeting" : "meetings"} held, ${hours.toFixed(1)} hours recorded. ${created.length} action ${created.length === 1 ? "item" : "items"} created and ${completed.length} completed.`,
  ];

  if (decisions.length > 0) {
    sections.push(
      `Decisions made:\n${decisions.map((d) => `• ${d.text}`).join("\n")}`,
    );
  }
  if (risks.length > 0) {
    sections.push(
      `Risks raised:\n${risks.map((r) => `• ${r.text}`).join("\n")}`,
    );
  }
  if (overdue.length > 0) {
    sections.push(
      `${overdue.length} action ${overdue.length === 1 ? "item is" : "items are"} now overdue:\n${overdue
        .slice(0, 5)
        .map((task) => `• ${task.title}`)
        .join("\n")}`,
    );
  } else {
    sections.push("Nothing is currently overdue.");
  }

  return sections.join("\n\n");
}

/**
 * Rewrites an existing summary at a requested length or tone.
 *
 * Works only from the stored summary text — it re-frames what is already
 * recorded and never adds claims that were not in the original.
 */
function composeRewrite(question: string, found: Retrieved): string {
  const lower = question.toLowerCase();

  // Prefer a summary the question actually matched; otherwise the newest one.
  const target =
    found.summaries[0] ??
    [...summaries.all()].sort(
      (a, b) =>
        new Date(b.generatedAt).getTime() - new Date(a.generatedAt).getTime(),
    )[0];

  if (!target) {
    return "There are no summaries in this workspace to rewrite yet. Record a meeting and generate a summary first.";
  }

  const meeting = meetings.find(target.meetingId);
  const title = meeting ? `"${meeting.title}"` : "the meeting";

  if (
    lower.includes("brief") ||
    lower.includes("short") ||
    lower.includes("shorter")
  ) {
    // First sentence only — the shortest honest reduction.
    const firstSentence =
      target.executiveSummary.split(/(?<=\.)\s/)[0] ?? target.executiveSummary;
    const decisions = target.highlights.filter((h) => h.kind === "decision");

    return `Brief version of the ${title} summary:\n\n${firstSentence}${
      decisions.length > 0 ? `\n\nKey decision: ${decisions[0].text}` : ""
    }`;
  }

  if (lower.includes("bullet") || lower.includes("bullets")) {
    return `${title} summary as bullets:\n\n${target.keyPoints
      .map((point) => `• ${point}`)
      .join("\n")}`;
  }

  if (
    lower.includes("detail") ||
    lower.includes("longer") ||
    lower.includes("expand")
  ) {
    const parts = [target.executiveSummary];
    if (target.keyPoints.length > 0) {
      parts.push(
        `Key points:\n${target.keyPoints.map((p) => `• ${p}`).join("\n")}`,
      );
    }
    for (const kind of ["decision", "risk", "question"] as const) {
      const entries = target.highlights.filter((h) => h.kind === kind);
      if (entries.length === 0) continue;
      const heading =
        kind === "decision"
          ? "Decisions"
          : kind === "risk"
            ? "Risks"
            : "Open questions";
      parts.push(
        `${heading}:\n${entries.map((e) => `• ${e.text}`).join("\n")}`,
      );
    }
    return `Detailed version of the ${title} summary:\n\n${parts.join("\n\n")}`;
  }

  // No length named, so state the options rather than guessing.
  return `I can rewrite the ${title} summary as a brief, as bullets, or in more detail — say which and I'll do it.\n\nCurrent summary:\n\n${target.executiveSummary}`;
}

function composeAnswer(question: string, found: Retrieved): string {
  const lower = question.toLowerCase();

  // Report generation and rewriting are commands, not retrieval questions.
  if (
    /\b(report|summarise the week|weekly summary|status update)\b/.test(lower)
  ) {
    return composeReport(question);
  }
  if (/\b(rewrite|reword|rephrase|shorten|condense)\b/.test(lower)) {
    return composeRewrite(question, found);
  }

  // Overdue work is a direct lookup, not a retrieval problem.
  if (lower.includes("overdue")) {
    if (found.overdueTasks.length === 0) {
      return "Nothing is overdue right now — every open action item is still within its due date.";
    }
    const lines = found.overdueTasks.map((task) => {
      const due = new Date(task.dueDate as string).toLocaleDateString();
      return `• ${task.title} — was due ${due}`;
    });
    return `${found.overdueTasks.length} action ${
      found.overdueTasks.length === 1 ? "item is" : "items are"
    } overdue:\n\n${lines.join("\n")}\n\nYou can reassign or reschedule these from the Tasks page.`;
  }

  const hasEvidence =
    found.summaries.length > 0 ||
    found.meetings.length > 0 ||
    found.documents.length > 0 ||
    found.knowledge.length > 0;

  // Saying so is better than inventing something that reads well.
  if (!hasEvidence) {
    return "I couldn't find anything in this workspace that answers that.\n\nI can only answer from meetings, summaries, documents and knowledge base entries that have been recorded here — I don't have outside knowledge. Try naming a meeting, a person, or a topic that was discussed.";
  }

  const parts: string[] = [];

  if (found.summaries.length > 0) {
    const summary = found.summaries[0];
    const meeting = meetings.find(summary.meetingId);
    parts.push(
      `From ${meeting ? `"${meeting.title}"` : "a recorded meeting"}:\n\n${summary.executiveSummary}`,
    );

    const decisions = summary.highlights.filter((h) => h.kind === "decision");
    if (decisions.length > 0) {
      parts.push(
        `Decisions recorded:\n${decisions.map((d) => `• ${d.text}`).join("\n")}`,
      );
    }

    const risks = summary.highlights.filter((h) => h.kind === "risk");
    if (risks.length > 0 && /risk|concern|problem|worry/.test(lower)) {
      parts.push(`Open risks:\n${risks.map((r) => `• ${r.text}`).join("\n")}`);
    }
  } else if (found.meetings.length > 0) {
    const list = found.meetings
      .map(
        (meeting) =>
          `• ${meeting.title} — ${new Date(meeting.startsAt).toLocaleDateString()}`,
      )
      .join("\n");
    parts.push(
      `I found ${found.meetings.length} related ${
        found.meetings.length === 1 ? "meeting" : "meetings"
      }, but no summary has been generated for them yet:\n\n${list}`,
    );
  }

  if (found.documents.length > 0) {
    parts.push(
      `Related documents:\n${found.documents
        .map((doc) => `• ${doc.name} — ${doc.excerpt}`)
        .join("\n")}`,
    );
  }

  if (found.knowledge.length > 0 && found.summaries.length === 0) {
    parts.push(
      `From the knowledge base:\n${found.knowledge
        .map((item) => `• ${item.title} — ${item.excerpt}`)
        .join("\n")}`,
    );
  }

  return parts.join("\n\n");
}

/**
 * Appends the user's message and the assistant's reply.
 *
 * Deliberately slower than a normal request — a reply that lands instantly
 * reads as canned, and the UI's streaming state needs somewhere to live.
 */
export async function sendMessage(
  conversationId: string,
  content: string,
): Promise<ChatConversation> {
  const question = content.trim();
  if (!question) throw new ApiError("Message cannot be empty", 422);

  await new Promise((resolve) => setTimeout(resolve, 700));

  return request(() => {
    const conversation = conversations.find(conversationId);
    if (!conversation) throw new ApiError("Conversation not found", 404);

    const found = retrieve(question);
    const timestamp = now();

    const userMessage: ChatMessage = {
      id: generateId("msg"),
      role: "user",
      content: question,
      createdAt: timestamp,
      sources: [],
    };

    const assistantMessage: ChatMessage = {
      id: generateId("msg"),
      role: "assistant",
      content: composeAnswer(question, found),
      createdAt: new Date(Date.now() + 1).toISOString(),
      sources: sourcesFrom(found),
    };

    const updated = conversations.update(conversationId, {
      // The first question names the conversation, as it does in every chat UI.
      title:
        conversation.messages.length === 0
          ? question.slice(0, 60)
          : conversation.title,
      messages: [...conversation.messages, userMessage, assistantMessage],
      updatedAt: timestamp,
    });

    return updated as ChatConversation;
  });
}

/** Follow-ups derived from what was actually cited. */
export function suggestFollowUps(conversation: ChatConversation): string[] {
  const last = [...conversation.messages]
    .reverse()
    .find((message) => message.role === "assistant");

  if (!last || last.sources.length === 0) return PROMPT_SUGGESTIONS.slice(0, 3);

  const meetingSource = last.sources.find((s) => s.kind === "meeting");
  return [
    meetingSource
      ? `What action items came out of "${meetingSource.label}"?`
      : "Which action items are still open?",
    "What risks were raised?",
    "Who was involved in these discussions?",
  ];
}
