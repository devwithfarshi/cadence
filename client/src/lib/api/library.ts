/**
 * Documents and knowledge base.
 *
 * Both are "library" surfaces — searchable, taggable collections with the same
 * query shape — so they share a module rather than duplicating filter logic.
 */

import { collection } from "@/lib/db/storage";
import type {
  DocumentFile,
  DocumentType,
  KnowledgeItem,
  KnowledgeItemKind,
  ListQuery,
  Paginated,
  ProcessingStatus,
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

const documents = collection<DocumentFile>("documents");
const knowledge = collection<KnowledgeItem>("knowledge");

/* -------------------------------------------------------------------------- */
/* Documents                                                                  */
/* -------------------------------------------------------------------------- */

export type DocumentSortKey = "name" | "createdAt" | "sizeBytes" | "type";

export interface DocumentQuery extends ListQuery<DocumentSortKey> {
  type?: DocumentType[];
  processingStatus?: ProcessingStatus[];
  ownerId?: string;
  tags?: string[];
  favoritesOnly?: boolean;
}

export function listDocuments(
  query: DocumentQuery = {},
): Promise<Paginated<DocumentFile>> {
  return request(() => {
    const filtered = documents.all().filter((doc) => {
      if (query.favoritesOnly && !doc.isFavorite) return false;
      if (query.type?.length && !query.type.includes(doc.type)) return false;
      if (
        query.processingStatus?.length &&
        !query.processingStatus.includes(doc.processingStatus)
      ) {
        return false;
      }
      if (query.ownerId && doc.ownerId !== query.ownerId) return false;
      if (query.tags?.length && !query.tags.some((t) => doc.tags.includes(t))) {
        return false;
      }
      return matchesSearch(doc, query.search, (d) => [
        d.name,
        d.excerpt,
        ...d.tags,
      ]);
    });

    const key = query.sortBy ?? "createdAt";
    const dir = query.sortDir ?? (key === "createdAt" ? "desc" : "asc");

    const sorted =
      key === "name"
        ? sortBy(filtered, (d) => d.name, dir)
        : key === "sizeBytes"
          ? sortBy(filtered, (d) => d.sizeBytes, dir)
          : key === "type"
            ? sortBy(filtered, (d) => d.type, dir)
            : sortBy(filtered, (d) => new Date(d.createdAt).getTime(), dir);

    return paginate(sorted, query);
  });
}

/** File extension → the document type the UI groups by. */
function inferType(name: string): DocumentType {
  const ext = name.split(".").pop()?.toLowerCase() ?? "";
  if (ext === "pdf") return "pdf";
  if (ext === "docx" || ext === "doc") return "docx";
  if (ext === "pptx" || ext === "ppt") return "pptx";
  if (ext === "csv") return "csv";
  if (["png", "jpg", "jpeg", "gif", "webp", "svg"].includes(ext))
    return "image";
  return "txt";
}

/**
 * Registers an upload. The record starts as `processing` — the page then calls
 * `finishProcessing` on a timer, mirroring a real indexing pipeline where the
 * file is available immediately but searchable only once indexed.
 */
export function uploadDocument(input: {
  name: string;
  sizeBytes: number;
  ownerId: string;
  tags?: string[];
}): Promise<DocumentFile> {
  return request(() => {
    if (!input.name.trim()) throw new ApiError("File name is required", 422);

    const timestamp = now();
    return documents.insert({
      id: generateId("doc"),
      name: input.name.trim(),
      type: inferType(input.name),
      sizeBytes: input.sizeBytes,
      ownerId: input.ownerId,
      processingStatus: "processing",
      excerpt: "Indexing in progress — content will be searchable shortly.",
      tags: input.tags ?? [],
      isFavorite: false,
      meetingId: null,
      createdAt: timestamp,
      updatedAt: timestamp,
    });
  });
}

export function finishProcessing(id: string): Promise<DocumentFile> {
  return request(() => {
    const updated = documents.update(id, {
      processingStatus: "indexed",
      excerpt: "Indexed and searchable across the workspace.",
      updatedAt: now(),
    });
    if (!updated) throw new ApiError("Document not found", 404);
    return updated;
  });
}

export function renameDocument(
  id: string,
  name: string,
): Promise<DocumentFile> {
  return request(() => {
    const trimmed = name.trim();
    if (!trimmed) throw new ApiError("Name cannot be empty", 422);

    const existing = documents.find(id);
    if (!existing) throw new ApiError("Document not found", 404);

    // Renaming can change the extension, and therefore the type.
    return documents.update(id, {
      name: trimmed,
      type: inferType(trimmed),
      updatedAt: now(),
    }) as DocumentFile;
  });
}

export function toggleDocumentFavorite(id: string): Promise<DocumentFile> {
  return request(() => {
    const doc = documents.find(id);
    if (!doc) throw new ApiError("Document not found", 404);
    return documents.update(id, {
      isFavorite: !doc.isFavorite,
      updatedAt: now(),
    }) as DocumentFile;
  });
}

export function deleteDocuments(ids: string[]): Promise<number> {
  return request(() => documents.removeMany(ids));
}

export function listDocumentTags(): Promise<string[]> {
  return request(() =>
    [...new Set(documents.all().flatMap((d) => d.tags))].sort(),
  );
}

/* -------------------------------------------------------------------------- */
/* Knowledge base                                                             */
/* -------------------------------------------------------------------------- */

export type KnowledgeSortKey = "title" | "createdAt" | "lastOpenedAt";

export interface KnowledgeQuery extends ListQuery<KnowledgeSortKey> {
  kind?: KnowledgeItemKind[];
  category?: string[];
  tags?: string[];
  favoritesOnly?: boolean;
}

export function listKnowledge(
  query: KnowledgeQuery = {},
): Promise<Paginated<KnowledgeItem>> {
  return request(() => {
    const filtered = knowledge.all().filter((item) => {
      if (query.favoritesOnly && !item.isFavorite) return false;
      if (query.kind?.length && !query.kind.includes(item.kind)) return false;
      if (query.category?.length && !query.category.includes(item.category)) {
        return false;
      }
      if (
        query.tags?.length &&
        !query.tags.some((t) => item.tags.includes(t))
      ) {
        return false;
      }
      return matchesSearch(item, query.search, (k) => [
        k.title,
        k.excerpt,
        k.category,
        ...k.tags,
      ]);
    });

    const key = query.sortBy ?? "createdAt";
    const dir = query.sortDir ?? "desc";

    const sorted =
      key === "title"
        ? sortBy(filtered, (k) => k.title, dir)
        : key === "lastOpenedAt"
          ? sortBy(
              filtered,
              (k) =>
                k.lastOpenedAt ? new Date(k.lastOpenedAt).getTime() : null,
              dir,
            )
          : sortBy(filtered, (k) => new Date(k.createdAt).getTime(), dir);

    return paginate(sorted, query);
  });
}

export function getKnowledgeFacets(): Promise<{
  categories: string[];
  tags: string[];
}> {
  return request(() => {
    const all = knowledge.all();
    return {
      categories: [...new Set(all.map((k) => k.category))].sort(),
      tags: [...new Set(all.flatMap((k) => k.tags))].sort(),
    };
  });
}

export function toggleKnowledgeFavorite(id: string): Promise<KnowledgeItem> {
  return request(() => {
    const item = knowledge.find(id);
    if (!item) throw new ApiError("Item not found", 404);
    return knowledge.update(id, {
      isFavorite: !item.isFavorite,
      updatedAt: now(),
    }) as KnowledgeItem;
  });
}

/** Records a visit so the "recently opened" rail reflects real usage. */
export function touchKnowledgeItem(id: string): Promise<KnowledgeItem> {
  return request(() => {
    const updated = knowledge.update(id, { lastOpenedAt: now() });
    if (!updated) throw new ApiError("Item not found", 404);
    return updated;
  });
}

export function createKnowledgeItem(input: {
  title: string;
  kind: KnowledgeItemKind;
  category: string;
  excerpt: string;
  ownerId: string;
  tags?: string[];
  sourceUrl?: string | null;
}): Promise<KnowledgeItem> {
  return request(() => {
    if (!input.title.trim()) throw new ApiError("Title is required", 422);

    const timestamp = now();
    return knowledge.insert({
      id: generateId("kb"),
      title: input.title.trim(),
      kind: input.kind,
      category: input.category.trim() || "Uncategorised",
      excerpt: input.excerpt.trim(),
      tags: input.tags ?? [],
      isFavorite: false,
      ownerId: input.ownerId,
      sourceId: null,
      sourceUrl: input.sourceUrl ?? null,
      lastOpenedAt: null,
      createdAt: timestamp,
      updatedAt: timestamp,
    });
  });
}

export function deleteKnowledgeItems(ids: string[]): Promise<number> {
  return request(() => knowledge.removeMany(ids));
}
