"use client";

import { Check, Copy, Download, FileText, Sparkles } from "lucide-react";
import { useMemo, useState } from "react";
import { Avatar } from "@/components/ui/avatar";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { EmptyState, Skeleton } from "@/components/ui/feedback";
import { SearchInput } from "@/components/ui/input";
import { useToast } from "@/components/ui/toast";
import { Tooltip } from "@/components/ui/tooltip";
import { cn } from "@/lib/utils/cn";
import { formatTimecode } from "@/lib/utils/format";
import type { Meeting, TranscriptSegment } from "@/types/domain";

/**
 * Splits `text` on every case-insensitive occurrence of `term` and wraps the
 * matches, so search hits are visible without re-rendering the whole list.
 */
function highlight(text: string, term: string) {
  const needle = term.trim();
  if (!needle) return text;

  // Escape user input before it becomes a regular expression.
  const escaped = needle.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  const parts = text.split(new RegExp(`(${escaped})`, "gi"));

  return parts.map((part, index) =>
    part.toLowerCase() === needle.toLowerCase() ? (
      <mark
        // biome-ignore lint/suspicious/noArrayIndexKey: split output is positional
        key={index}
        className="rounded-[3px] bg-warning-subtle px-0.5 text-warning-foreground"
      >
        {part}
      </mark>
    ) : (
      part
    ),
  );
}

function toPlainText(meeting: Meeting, segments: TranscriptSegment[]): string {
  const header = [
    meeting.title,
    new Date(meeting.startsAt).toLocaleString(),
    `${meeting.participants.length} participants`,
    "",
  ].join("\n");

  const body = segments
    .map(
      (segment) =>
        `[${formatTimecode(segment.startSeconds)}] ${segment.speakerName}: ${segment.text}`,
    )
    .join("\n\n");

  return `${header}\n${body}\n`;
}

export function TranscriptPanel({
  meeting,
  segments,
  loading,
}: {
  meeting: Meeting;
  segments: TranscriptSegment[];
  loading: boolean;
}) {
  const [search, setSearch] = useState("");
  const [copied, setCopied] = useState(false);
  const [actionItemsOnly, setActionItemsOnly] = useState(false);
  const { toast } = useToast();

  const filtered = useMemo(() => {
    const needle = search.trim().toLowerCase();
    return segments.filter((segment) => {
      if (actionItemsOnly && !segment.isActionItem) return false;
      if (!needle) return true;
      return (
        segment.text.toLowerCase().includes(needle) ||
        segment.speakerName.toLowerCase().includes(needle)
      );
    });
  }, [segments, search, actionItemsOnly]);

  async function handleCopy() {
    try {
      await navigator.clipboard.writeText(toPlainText(meeting, segments));
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
      toast({ tone: "success", title: "Transcript copied to clipboard" });
    } catch {
      toast({
        tone: "error",
        title: "Could not copy",
        description: "Your browser blocked clipboard access.",
      });
    }
  }

  function handleExport() {
    const blob = new Blob([toPlainText(meeting, segments)], {
      type: "text/plain;charset=utf-8",
    });
    const url = URL.createObjectURL(blob);

    const link = document.createElement("a");
    link.href = url;
    link.download = `${meeting.title.replace(/[^\w\s-]/g, "").trim()} — transcript.txt`;
    link.click();

    // Release the object URL once the download has been handed to the browser.
    URL.revokeObjectURL(url);
    toast({ tone: "success", title: "Transcript exported" });
  }

  if (loading) {
    return (
      <div className="space-y-4 p-4">
        {[0, 1, 2, 3, 4].map((row) => (
          <div key={row} className="flex gap-3">
            <Skeleton className="size-8 shrink-0 rounded-full" />
            <div className="flex-1 space-y-2">
              <Skeleton className="h-3 w-32" />
              <Skeleton className="h-4 w-full" />
              <Skeleton className="h-4 w-4/5" />
            </div>
          </div>
        ))}
      </div>
    );
  }

  if (segments.length === 0) {
    return (
      <EmptyState
        icon={FileText}
        title="No transcript available"
        description="This meeting hasn't been recorded, or the transcript is still processing."
      />
    );
  }

  const actionItemCount = segments.filter((s) => s.isActionItem).length;

  return (
    <div className="flex flex-col">
      {/* Toolbar */}
      <div className="flex flex-wrap items-center gap-2 border-b border-border px-4 py-3">
        <SearchInput
          value={search}
          onValueChange={setSearch}
          placeholder="Search transcript…"
          className="w-full sm:w-64"
        />

        {actionItemCount > 0 ? (
          <Button
            variant="secondary"
            size="sm"
            aria-pressed={actionItemsOnly}
            onClick={() => setActionItemsOnly((current) => !current)}
            className={cn(
              actionItemsOnly && "border-accent/40 bg-accent-subtle",
            )}
          >
            <Sparkles />
            Commitments
            <span className="tabular">({actionItemCount})</span>
          </Button>
        ) : null}

        <div className="ml-auto flex items-center gap-1.5">
          <Tooltip label="Copy full transcript">
            <Button variant="secondary" size="sm" onClick={handleCopy}>
              {copied ? <Check /> : <Copy />}
              {copied ? "Copied" : "Copy"}
            </Button>
          </Tooltip>
          <Tooltip label="Download as .txt">
            <Button variant="secondary" size="sm" onClick={handleExport}>
              <Download />
              Export
            </Button>
          </Tooltip>
        </div>
      </div>

      {/* Result count while searching */}
      {search.trim() || actionItemsOnly ? (
        <p className="border-b border-border bg-surface-sunken px-4 py-1.5 text-caption text-muted tabular">
          {filtered.length} of {segments.length} lines
        </p>
      ) : null}

      {/* Segments */}
      {filtered.length === 0 ? (
        <EmptyState
          icon={FileText}
          title="No matching lines"
          description={`Nothing in this transcript matches “${search}”.`}
        />
      ) : (
        <ol className="divide-y divide-border">
          {filtered.map((segment) => (
            <li
              key={segment.id}
              className="flex gap-3 px-4 py-3 transition-colors hover:bg-surface-raised/40"
            >
              <Avatar name={segment.speakerName} size="md" className="mt-0.5" />

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
                  {/* Surface only genuinely uncertain lines, not every score. */}
                  {segment.confidence < 0.9 ? (
                    <Tooltip
                      label={`Transcription confidence ${Math.round(
                        segment.confidence * 100,
                      )}%`}
                    >
                      <span className="text-label text-warning-foreground">
                        low confidence
                      </span>
                    </Tooltip>
                  ) : null}
                </div>

                <p className="mt-1 text-body text-muted">
                  {highlight(segment.text, search)}
                </p>
              </div>
            </li>
          ))}
        </ol>
      )}
    </div>
  );
}
