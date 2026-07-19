import { cn } from "@/lib/utils/cn";
import type { DocumentType, KnowledgeItemKind } from "@/types/domain";

/**
 * Type chip for a file.
 *
 * The extension itself is the clearest identifier — a generic page glyph tells
 * the reader nothing a three-letter label doesn't say better. Colour is tone
 * only, never the categorical series palette.
 */
const TYPE_TONE: Record<DocumentType, string> = {
  pdf: "bg-danger-subtle text-danger-foreground",
  docx: "bg-info-subtle text-info-foreground",
  pptx: "bg-warning-subtle text-warning-foreground",
  csv: "bg-success-subtle text-success-foreground",
  txt: "bg-surface-raised text-muted",
  image: "bg-accent-subtle text-accent-subtle-foreground",
};

export function FileTypeChip({
  type,
  className,
}: {
  type: DocumentType;
  className?: string;
}) {
  return (
    <span
      className={cn(
        "flex size-9 shrink-0 items-center justify-center rounded-control text-overline uppercase font-semibold",
        TYPE_TONE[type],
        className,
      )}
    >
      {type === "image" ? "IMG" : type}
    </span>
  );
}

const KIND_LABELS: Record<KnowledgeItemKind, string> = {
  document: "Document",
  meeting_note: "Meeting note",
  ai_summary: "AI summary",
  link: "Link",
};

export function knowledgeKindLabel(kind: KnowledgeItemKind): string {
  return KIND_LABELS[kind];
}
