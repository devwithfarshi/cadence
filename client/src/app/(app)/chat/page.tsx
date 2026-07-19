"use client";

import {
  CheckSquare,
  FileText,
  Library,
  Plus,
  Send,
  Sparkles,
  Trash2,
  Video,
} from "lucide-react";
import Link from "next/link";
import { useCallback, useEffect, useRef, useState } from "react";
import { useSession } from "@/components/providers/auth-provider";
import { Avatar } from "@/components/ui/avatar";
import { Button } from "@/components/ui/button";
import { ConfirmDialog } from "@/components/ui/dialog";
import { EmptyState, ErrorState, Skeleton } from "@/components/ui/feedback";
import { Textarea } from "@/components/ui/input";
import { useToast } from "@/components/ui/toast";
import {
  createConversation,
  deleteConversation,
  listConversations,
  PROMPT_SUGGESTIONS,
  sendMessage,
  suggestFollowUps,
} from "@/lib/api/chat";
import { useAsync } from "@/lib/hooks/use-async";
import { cn } from "@/lib/utils/cn";
import { formatRelative } from "@/lib/utils/format";
import type { ChatConversation, ChatSource } from "@/types/domain";

const SOURCE_ICONS = {
  meeting: Video,
  document: FileText,
  knowledge: Library,
} as const;

function SourceChip({ source }: { source: ChatSource }) {
  const Icon = SOURCE_ICONS[source.kind] ?? CheckSquare;

  return (
    <Link
      href={source.href}
      className="flex max-w-64 items-center gap-1.5 rounded-control border border-border bg-surface px-2 py-1 transition-colors hover:border-border-strong hover:bg-surface-raised"
    >
      <Icon className="size-3 shrink-0 text-subtle" aria-hidden />
      <span className="truncate text-label text-muted">{source.label}</span>
    </Link>
  );
}

export default function ChatPage() {
  const session = useSession();
  const { toast } = useToast();

  const [activeId, setActiveId] = useState<string | null>(null);
  const [draft, setDraft] = useState("");
  const [sending, setSending] = useState(false);
  const [pendingDelete, setPendingDelete] = useState<ChatConversation | null>(
    null,
  );

  const conversations = useAsync(() => listConversations(), []);
  const threadRef = useRef<HTMLDivElement>(null);

  // Open the most recent conversation on first load so the page isn't blank.
  useEffect(() => {
    if (
      activeId === null &&
      conversations.data &&
      conversations.data.length > 0
    ) {
      setActiveId(conversations.data[0].id);
    }
  }, [conversations.data, activeId]);

  const active =
    conversations.data?.find((entry) => entry.id === activeId) ?? null;

  // biome-ignore lint/correctness/useExhaustiveDependencies: message count is the trigger
  useEffect(() => {
    threadRef.current?.scrollTo({
      top: threadRef.current.scrollHeight,
      behavior: "smooth",
    });
  }, [active?.messages.length, sending]);

  const handleSend = useCallback(
    async (text: string) => {
      const question = text.trim();
      if (!question || sending) return;

      setSending(true);
      setDraft("");

      try {
        // A brand-new thread is created lazily, on the first question.
        let conversationId = activeId;
        if (!conversationId) {
          const created = await createConversation();
          conversationId = created.id;
          setActiveId(created.id);
        }

        await sendMessage(conversationId, question);
        await conversations.refetch();
      } catch (error) {
        toast({
          tone: "error",
          title: "Could not send message",
          description:
            error instanceof Error ? error.message : "Please try again.",
        });
        setDraft(question);
      } finally {
        setSending(false);
      }
    },
    [activeId, sending, conversations, toast],
  );

  async function handleNew() {
    const created = await createConversation();
    await conversations.refetch();
    setActiveId(created.id);
    setDraft("");
  }

  async function handleDelete() {
    if (!pendingDelete) return;
    await deleteConversation(pendingDelete.id);
    if (activeId === pendingDelete.id) setActiveId(null);
    setPendingDelete(null);
    await conversations.refetch();
    toast({ tone: "info", title: "Conversation deleted" });
  }

  const suggestions = active ? suggestFollowUps(active) : PROMPT_SUGGESTIONS;

  return (
    <div className="flex h-[calc(100dvh-3.5rem)]">
      {/* Conversation list */}
      <aside className="hidden w-64 shrink-0 flex-col border-r border-border bg-surface md:flex">
        <div className="shrink-0 p-3">
          <Button
            variant="primary"
            size="md"
            className="w-full"
            onClick={handleNew}
          >
            <Plus />
            New conversation
          </Button>
        </div>

        <div className="min-h-0 flex-1 overflow-y-auto scrollbar-thin px-2 pb-3">
          {conversations.loading && !conversations.data ? (
            <div className="space-y-2 px-1">
              {[0, 1, 2].map((i) => (
                <Skeleton key={i} className="h-12 w-full" />
              ))}
            </div>
          ) : (conversations.data ?? []).length === 0 ? (
            <p className="px-2 py-6 text-center text-caption text-subtle">
              No conversations yet.
            </p>
          ) : (
            <ul className="space-y-0.5">
              {(conversations.data ?? []).map((conversation) => (
                <li key={conversation.id} className="group relative">
                  <button
                    type="button"
                    onClick={() => setActiveId(conversation.id)}
                    className={cn(
                      "w-full rounded-control px-2 py-2 pr-8 text-left transition-colors",
                      conversation.id === activeId
                        ? "bg-accent-subtle"
                        : "hover:bg-surface-raised",
                    )}
                  >
                    <p
                      className={cn(
                        "truncate text-caption",
                        conversation.id === activeId
                          ? "font-medium text-accent-subtle-foreground"
                          : "text-foreground",
                      )}
                    >
                      {conversation.title}
                    </p>
                    <p className="mt-0.5 text-label text-subtle">
                      {formatRelative(conversation.updatedAt)}
                    </p>
                  </button>

                  <Button
                    variant="ghost"
                    size="icon-sm"
                    aria-label={`Delete "${conversation.title}"`}
                    onClick={() => setPendingDelete(conversation)}
                    className="absolute right-1 top-1.5 opacity-0 transition-opacity focus-visible:opacity-100 group-hover:opacity-100"
                  >
                    <Trash2 />
                  </Button>
                </li>
              ))}
            </ul>
          )}
        </div>
      </aside>

      {/* Thread */}
      <div className="flex min-w-0 flex-1 flex-col">
        {conversations.error ? (
          <ErrorState
            description={conversations.error.message}
            onRetry={conversations.refetch}
          />
        ) : (
          <>
            <div
              ref={threadRef}
              className="min-h-0 flex-1 overflow-y-auto scrollbar-thin"
            >
              <div className="mx-auto w-full max-w-3xl px-4 py-6">
                {!active || active.messages.length === 0 ? (
                  <div className="py-10">
                    <EmptyState
                      icon={Sparkles}
                      title="Ask about your workspace"
                      description="I answer from meetings, summaries, documents and knowledge base entries recorded here — not from outside knowledge."
                    />
                  </div>
                ) : (
                  <div className="space-y-6">
                    {active.messages.map((message) => (
                      <div key={message.id} className="flex gap-3">
                        {message.role === "assistant" ? (
                          <span
                            aria-hidden
                            className="flex size-8 shrink-0 items-center justify-center rounded-full bg-accent-subtle"
                          >
                            <Sparkles className="size-4 text-accent" />
                          </span>
                        ) : (
                          <Avatar
                            name={session.name}
                            size="md"
                            className="shrink-0"
                          />
                        )}

                        <div className="min-w-0 flex-1">
                          <p className="mb-1 text-caption font-medium text-foreground">
                            {message.role === "assistant"
                              ? "Cadence AI"
                              : session.name}
                          </p>

                          <div className="whitespace-pre-wrap text-body text-muted">
                            {message.content}
                          </div>

                          {message.sources.length > 0 ? (
                            <div className="mt-3">
                              <p className="mb-1.5 text-overline uppercase text-subtle">
                                Sources
                              </p>
                              <div className="flex flex-wrap gap-1.5">
                                {message.sources.map((source) => (
                                  <SourceChip
                                    key={`${source.kind}-${source.id}`}
                                    source={source}
                                  />
                                ))}
                              </div>
                            </div>
                          ) : null}
                        </div>
                      </div>
                    ))}

                    {sending ? (
                      <div className="flex gap-3">
                        <span
                          aria-hidden
                          className="flex size-8 shrink-0 items-center justify-center rounded-full bg-accent-subtle"
                        >
                          <Sparkles className="size-4 animate-pulse text-accent" />
                        </span>
                        <div className="flex-1 space-y-2 pt-1.5">
                          <Skeleton className="h-3 w-2/3" />
                          <Skeleton className="h-3 w-1/2" />
                        </div>
                      </div>
                    ) : null}
                  </div>
                )}
              </div>
            </div>

            {/* Composer */}
            <div className="shrink-0 border-t border-border bg-surface">
              <div className="mx-auto w-full max-w-3xl px-4 py-3">
                {!sending ? (
                  <div className="mb-2 flex flex-wrap gap-1.5">
                    {suggestions.map((suggestion) => (
                      <button
                        key={suggestion}
                        type="button"
                        onClick={() => handleSend(suggestion)}
                        className="rounded-control border border-border px-2 py-1 text-label text-muted transition-colors hover:border-border-strong hover:bg-surface-raised hover:text-foreground"
                      >
                        {suggestion}
                      </button>
                    ))}
                  </div>
                ) : null}

                <form
                  onSubmit={(event) => {
                    event.preventDefault();
                    handleSend(draft);
                  }}
                  className="flex items-end gap-2"
                >
                  <Textarea
                    value={draft}
                    onChange={(event) => setDraft(event.target.value)}
                    onKeyDown={(event) => {
                      // Enter sends; Shift+Enter breaks the line.
                      if (event.key === "Enter" && !event.shiftKey) {
                        event.preventDefault();
                        handleSend(draft);
                      }
                    }}
                    placeholder="Ask about your meetings, documents or action items…"
                    aria-label="Message"
                    rows={2}
                    className="min-h-11 resize-none"
                  />
                  <Button
                    variant="primary"
                    size="icon"
                    type="submit"
                    disabled={!draft.trim() || sending}
                    aria-label="Send message"
                  >
                    <Send />
                  </Button>
                </form>

                <p className="mt-1.5 text-label text-subtle">
                  Answers are grounded in this workspace only. Always check the
                  cited source before acting on one.
                </p>
              </div>
            </div>
          </>
        )}
      </div>

      <ConfirmDialog
        open={pendingDelete !== null}
        onOpenChange={(open) => {
          if (!open) setPendingDelete(null);
        }}
        title="Delete this conversation?"
        description={`"${pendingDelete?.title ?? ""}" and its messages will be permanently removed.`}
        confirmLabel="Delete"
        destructive
        onConfirm={handleDelete}
      />
    </div>
  );
}
