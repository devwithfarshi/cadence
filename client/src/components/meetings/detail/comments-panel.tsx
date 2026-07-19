"use client";

import { MessageSquare, Reply, Trash2 } from "lucide-react";
import { useState } from "react";
import { Avatar } from "@/components/ui/avatar";
import { Button } from "@/components/ui/button";
import { EmptyState, Skeleton } from "@/components/ui/feedback";
import { Textarea } from "@/components/ui/input";
import { useToast } from "@/components/ui/toast";
import { addComment, deleteComment } from "@/lib/api/meetings";
import { formatRelative, formatTimecode } from "@/lib/utils/format";
import type { Comment, User } from "@/types/domain";

/** Renders @mentions as accented text so they read as references, not prose. */
function renderBody(body: string, users: User[]) {
  const names = users
    .map((user) => user.name)
    .sort((a, b) => b.length - a.length);
  if (names.length === 0) return body;

  const escaped = names.map((name) =>
    name.replace(/[.*+?^${}()|[\]\\]/g, "\\$&"),
  );
  const parts = body.split(new RegExp(`(@(?:${escaped.join("|")}))`, "g"));

  return parts.map((part, index) =>
    part.startsWith("@") && names.includes(part.slice(1)) ? (
      <span
        // biome-ignore lint/suspicious/noArrayIndexKey: split output is positional
        key={index}
        className="font-medium text-accent"
      >
        {part}
      </span>
    ) : (
      part
    ),
  );
}

function CommentBody({
  comment,
  author,
  users,
  onReply,
  onDelete,
  canDelete,
}: {
  comment: Comment;
  author: User | undefined;
  users: User[];
  onReply?: () => void;
  onDelete: () => void;
  canDelete: boolean;
}) {
  return (
    <div className="group flex gap-3">
      <Avatar name={author?.name ?? "Unknown"} size="md" className="mt-0.5" />

      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-2">
          <span className="text-caption font-medium text-foreground">
            {author?.name ?? "Unknown user"}
          </span>
          <span className="text-label text-subtle">
            {formatRelative(comment.createdAt)}
          </span>
          {comment.atSeconds !== null ? (
            <span className="font-mono text-label text-subtle tabular">
              at {formatTimecode(comment.atSeconds)}
            </span>
          ) : null}
        </div>

        <p className="mt-1 whitespace-pre-wrap text-body text-muted">
          {renderBody(comment.body, users)}
        </p>

        <div className="mt-1.5 flex items-center gap-1 opacity-0 transition-opacity focus-within:opacity-100 group-hover:opacity-100">
          {onReply ? (
            <Button variant="ghost" size="sm" onClick={onReply}>
              <Reply />
              Reply
            </Button>
          ) : null}
          {canDelete ? (
            <Button variant="ghost" size="sm" onClick={onDelete}>
              <Trash2 />
              Delete
            </Button>
          ) : null}
        </div>
      </div>
    </div>
  );
}

export function CommentsPanel({
  meetingId,
  comments,
  users,
  currentUserId,
  loading,
  onChanged,
}: {
  meetingId: string;
  comments: Comment[];
  users: User[];
  currentUserId: string;
  loading: boolean;
  onChanged: () => void;
}) {
  const { toast } = useToast();
  const [body, setBody] = useState("");
  const [replyTo, setReplyTo] = useState<string | null>(null);
  const [replyBody, setReplyBody] = useState("");
  const [submitting, setSubmitting] = useState(false);

  const userById = new Map(users.map((user) => [user.id, user]));

  const topLevel = comments.filter((comment) => comment.parentId === null);
  const repliesOf = (parentId: string) =>
    comments.filter((comment) => comment.parentId === parentId);

  async function submit(text: string, parentId: string | null) {
    if (!text.trim()) return;

    setSubmitting(true);
    try {
      await addComment({
        meetingId,
        authorId: currentUserId,
        body: text,
        parentId,
      });

      setBody("");
      setReplyBody("");
      setReplyTo(null);
      onChanged();
    } catch (error) {
      toast({
        tone: "error",
        title: "Could not post comment",
        description:
          error instanceof Error ? error.message : "Please try again.",
      });
    } finally {
      setSubmitting(false);
    }
  }

  async function handleDelete(id: string) {
    try {
      await deleteComment(id);
      onChanged();
      toast({ tone: "info", title: "Comment deleted" });
    } catch {
      toast({ tone: "error", title: "Could not delete comment" });
    }
  }

  if (loading) {
    return (
      <div className="space-y-4 p-4">
        {[0, 1].map((row) => (
          <div key={row} className="flex gap-3">
            <Skeleton className="size-8 shrink-0 rounded-full" />
            <div className="flex-1 space-y-2">
              <Skeleton className="h-3 w-32" />
              <Skeleton className="h-4 w-full" />
            </div>
          </div>
        ))}
      </div>
    );
  }

  return (
    <div className="flex flex-col">
      {/* Composer */}
      <div className="border-b border-border p-4">
        <Textarea
          value={body}
          onChange={(event) => setBody(event.target.value)}
          placeholder="Add a comment. Use @ to mention a teammate."
          rows={3}
          aria-label="New comment"
        />
        <div className="mt-2 flex items-center justify-between gap-2">
          <p className="text-label text-subtle">
            Mention with @name to notify someone.
          </p>
          <Button
            variant="primary"
            size="sm"
            loading={submitting}
            disabled={!body.trim()}
            onClick={() => submit(body, null)}
          >
            Post comment
          </Button>
        </div>
      </div>

      {topLevel.length === 0 ? (
        <EmptyState
          icon={MessageSquare}
          title="No comments yet"
          description="Start the discussion — comments are visible to everyone with access to this meeting."
        />
      ) : (
        <ul className="divide-y divide-border">
          {topLevel.map((comment) => {
            const replies = repliesOf(comment.id);

            return (
              <li key={comment.id} className="px-4 py-4">
                <CommentBody
                  comment={comment}
                  author={userById.get(comment.authorId)}
                  users={users}
                  canDelete={comment.authorId === currentUserId}
                  onReply={() =>
                    setReplyTo((current) =>
                      current === comment.id ? null : comment.id,
                    )
                  }
                  onDelete={() => handleDelete(comment.id)}
                />

                {/* Replies, indented under their parent */}
                {replies.length > 0 ? (
                  <ul className="ml-11 mt-4 space-y-4 border-l border-border pl-4">
                    {replies.map((reply) => (
                      <li key={reply.id}>
                        <CommentBody
                          comment={reply}
                          author={userById.get(reply.authorId)}
                          users={users}
                          canDelete={reply.authorId === currentUserId}
                          onDelete={() => handleDelete(reply.id)}
                        />
                      </li>
                    ))}
                  </ul>
                ) : null}

                {replyTo === comment.id ? (
                  <div className="ml-11 mt-3">
                    <Textarea
                      value={replyBody}
                      onChange={(event) => setReplyBody(event.target.value)}
                      placeholder="Write a reply…"
                      rows={2}
                      aria-label="Reply"
                      autoFocus
                    />
                    <div className="mt-2 flex justify-end gap-2">
                      <Button
                        variant="ghost"
                        size="sm"
                        onClick={() => {
                          setReplyTo(null);
                          setReplyBody("");
                        }}
                      >
                        Cancel
                      </Button>
                      <Button
                        variant="primary"
                        size="sm"
                        loading={submitting}
                        disabled={!replyBody.trim()}
                        onClick={() => submit(replyBody, comment.id)}
                      >
                        Reply
                      </Button>
                    </div>
                  </div>
                ) : null}
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}
