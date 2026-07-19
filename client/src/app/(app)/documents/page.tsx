"use client";

import {
  Download,
  FileText,
  LayoutGrid,
  List as ListIcon,
  MoreHorizontal,
  Pencil,
  Share2,
  Star,
  Trash2,
  Upload,
  X,
} from "lucide-react";
import { useCallback, useEffect, useRef, useState } from "react";
import { FileTypeChip } from "@/components/library/file-icon";
import { useSession } from "@/components/providers/auth-provider";
import { PageContainer, PageHeader } from "@/components/shell/page-header";
import { Avatar } from "@/components/ui/avatar";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import {
  ConfirmDialog,
  Dialog,
  DialogClose,
  DialogContent,
} from "@/components/ui/dialog";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { EmptyState, ErrorState, SkeletonRows } from "@/components/ui/feedback";
import { FilterMenu } from "@/components/ui/filter-menu";
import { Field, Input, SearchInput } from "@/components/ui/input";
import { Pagination } from "@/components/ui/navigation";
import {
  SortableHead,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
  TableWrapper,
} from "@/components/ui/table";
import { useToast } from "@/components/ui/toast";
import { Tooltip } from "@/components/ui/tooltip";
import {
  type DocumentSortKey,
  deleteDocuments,
  finishProcessing,
  listDocuments,
  listDocumentTags,
  renameDocument,
  toggleDocumentFavorite,
  uploadDocument,
} from "@/lib/api/library";
import { listUsers } from "@/lib/api/workspace";
import { useAsync, useDebounced } from "@/lib/hooks/use-async";
import { cn } from "@/lib/utils/cn";
import { formatDate, formatFileSize, humanize } from "@/lib/utils/format";
import type {
  DocumentFile,
  DocumentType,
  ProcessingStatus,
  SortDirection,
  ViewMode,
} from "@/types/domain";

const PAGE_SIZE = 12;

const TYPE_OPTIONS: { value: DocumentType; label: string }[] = [
  { value: "pdf", label: "PDF" },
  { value: "docx", label: "Word" },
  { value: "pptx", label: "PowerPoint" },
  { value: "csv", label: "CSV" },
  { value: "txt", label: "Text" },
  { value: "image", label: "Image" },
];

const STATUS_OPTIONS: { value: ProcessingStatus; label: string }[] = [
  { value: "indexed", label: "Indexed" },
  { value: "processing", label: "Processing" },
  { value: "uploading", label: "Uploading" },
  { value: "failed", label: "Failed" },
];

const STATUS_TONE = {
  indexed: "success",
  processing: "info",
  uploading: "info",
  failed: "danger",
} as const;

export default function DocumentsPage() {
  const session = useSession();
  const { toast } = useToast();

  const [search, setSearch] = useState("");
  const debouncedSearch = useDebounced(search, 250);
  const [types, setTypes] = useState<DocumentType[]>([]);
  const [statuses, setStatuses] = useState<ProcessingStatus[]>([]);
  const [tags, setTags] = useState<string[]>([]);
  const [favoritesOnly, setFavoritesOnly] = useState(false);
  const [view, setView] = useState<ViewMode>("list");

  const [sortBy, setSortBy] = useState<DocumentSortKey>("createdAt");
  const [sortDir, setSortDir] = useState<SortDirection>("desc");
  const [page, setPage] = useState(1);

  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [mutating, setMutating] = useState(false);
  const [renaming, setRenaming] = useState<DocumentFile | null>(null);

  const fileInputRef = useRef<HTMLInputElement>(null);

  const docs = useAsync(
    () =>
      listDocuments({
        search: debouncedSearch,
        type: types,
        processingStatus: statuses,
        tags,
        favoritesOnly,
        sortBy,
        sortDir,
        page,
        pageSize: PAGE_SIZE,
      }),
    [
      debouncedSearch,
      types,
      statuses,
      tags,
      favoritesOnly,
      sortBy,
      sortDir,
      page,
    ],
  );

  const users = useAsync(() => listUsers(), []);
  const availableTags = useAsync(() => listDocumentTags(), []);

  // biome-ignore lint/correctness/useExhaustiveDependencies: resetting on filter change is the intent
  useEffect(() => {
    setPage(1);
    setSelectedIds([]);
  }, [debouncedSearch, types, statuses, tags, favoritesOnly]);

  const refresh = useCallback(() => {
    docs.refetch();
    availableTags.refetch();
  }, [docs, availableTags]);

  const items = docs.data?.items ?? [];
  const userById = new Map((users.data ?? []).map((u) => [u.id, u]));
  const activeFilters =
    types.length + statuses.length + tags.length + (favoritesOnly ? 1 : 0);

  /* --- Actions ----------------------------------------------------------- */

  async function handleUpload(files: FileList | null) {
    if (!files || files.length === 0) return;

    for (const file of Array.from(files)) {
      try {
        const created = await uploadDocument({
          name: file.name,
          sizeBytes: file.size,
          ownerId: session.userId,
        });
        refresh();

        toast({
          tone: "success",
          title: "Upload started",
          description: `${file.name} is being indexed.`,
        });

        // Indexing takes a moment in a real pipeline; reflect that rather than
        // flipping straight to "indexed".
        setTimeout(() => {
          finishProcessing(created.id)
            .then(() => {
              refresh();
              toast({
                tone: "success",
                title: "Indexed",
                description: `${file.name} is now searchable.`,
              });
            })
            .catch(() => undefined);
        }, 2600);
      } catch (error) {
        toast({
          tone: "error",
          title: "Upload failed",
          description:
            error instanceof Error ? error.message : "Please try again.",
        });
      }
    }

    if (fileInputRef.current) fileInputRef.current.value = "";
  }

  async function handleFavorite(doc: DocumentFile) {
    docs.setData((current) =>
      current
        ? {
            ...current,
            items: current.items.map((item) =>
              item.id === doc.id
                ? { ...item, isFavorite: !item.isFavorite }
                : item,
            ),
          }
        : current,
    );
    await toggleDocumentFavorite(doc.id).catch(() => docs.refetch());
  }

  async function handleRename(name: string) {
    if (!renaming) return;
    try {
      await renameDocument(renaming.id, name);
      setRenaming(null);
      refresh();
      toast({ tone: "success", title: "Document renamed" });
    } catch (error) {
      toast({
        tone: "error",
        title: "Could not rename",
        description:
          error instanceof Error ? error.message : "Please try again.",
      });
    }
  }

  async function handleDelete() {
    setMutating(true);
    try {
      const count = await deleteDocuments(selectedIds);
      setSelectedIds([]);
      setConfirmDelete(false);
      refresh();
      toast({
        tone: "success",
        title: `Deleted ${count} ${count === 1 ? "document" : "documents"}`,
      });
    } catch {
      toast({ tone: "error", title: "Could not delete documents" });
    } finally {
      setMutating(false);
    }
  }

  /**
   * Generates a file from the stored excerpt.
   *
   * There is no uploaded blob to serve — the store keeps metadata only — so the
   * download says so rather than pretending to return the original file.
   */
  function handleDownload(doc: DocumentFile) {
    const body = `${doc.name}\n\n${doc.excerpt}\n\n— Placeholder export from Cadence. The original file is not stored in this demo.\n`;
    const url = URL.createObjectURL(
      new Blob([body], { type: "text/plain;charset=utf-8" }),
    );

    const link = document.createElement("a");
    link.href = url;
    link.download = `${doc.name.replace(/\.[^.]+$/, "")}.txt`;
    link.click();
    URL.revokeObjectURL(url);

    toast({ tone: "info", title: "Placeholder file downloaded" });
  }

  async function handleShare(doc: DocumentFile) {
    try {
      await navigator.clipboard.writeText(
        `${window.location.origin}/documents#${doc.id}`,
      );
      toast({ tone: "success", title: "Share link copied" });
    } catch {
      toast({ tone: "error", title: "Could not copy link" });
    }
  }

  function handleSort(key: string) {
    const typed = key as DocumentSortKey;
    if (sortBy === typed) {
      setSortDir((d) => (d === "asc" ? "desc" : "asc"));
    } else {
      setSortBy(typed);
      setSortDir(typed === "createdAt" ? "desc" : "asc");
    }
  }

  function clearFilters() {
    setTypes([]);
    setStatuses([]);
    setTags([]);
    setFavoritesOnly(false);
    setSearch("");
  }

  function RowMenu({ doc }: { doc: DocumentFile }) {
    return (
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <Button
            variant="ghost"
            size="icon-sm"
            aria-label={`Actions for ${doc.name}`}
          >
            <MoreHorizontal />
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent>
          <DropdownMenuItem onSelect={() => setRenaming(doc)}>
            <Pencil />
            Rename
          </DropdownMenuItem>
          <DropdownMenuItem onSelect={() => handleDownload(doc)}>
            <Download />
            Download
          </DropdownMenuItem>
          <DropdownMenuItem onSelect={() => handleShare(doc)}>
            <Share2 />
            Copy share link
          </DropdownMenuItem>
          <DropdownMenuSeparator />
          <DropdownMenuItem
            destructive
            onSelect={() => {
              setSelectedIds([doc.id]);
              setConfirmDelete(true);
            }}
          >
            <Trash2 />
            Delete
          </DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>
    );
  }

  return (
    <PageContainer>
      <PageHeader
        title="Documents"
        description="Upload, organise and index files across the workspace."
        actions={
          <Button
            variant="primary"
            size="md"
            onClick={() => fileInputRef.current?.click()}
          >
            <Upload />
            Upload
          </Button>
        }
      />

      <input
        ref={fileInputRef}
        type="file"
        multiple
        className="sr-only"
        aria-label="Upload documents"
        onChange={(event) => handleUpload(event.target.files)}
      />

      <div className="mb-3 flex flex-wrap items-center gap-2">
        <SearchInput
          value={search}
          onValueChange={setSearch}
          placeholder="Search documents…"
          className="w-full sm:w-64"
        />
        <FilterMenu
          label="Type"
          options={TYPE_OPTIONS}
          selected={types}
          onChange={setTypes}
        />
        <FilterMenu
          label="Status"
          options={STATUS_OPTIONS}
          selected={statuses}
          onChange={setStatuses}
          icon={false}
        />
        {availableTags.data && availableTags.data.length > 0 ? (
          <FilterMenu
            label="Tags"
            options={availableTags.data.map((t) => ({ value: t, label: t }))}
            selected={tags}
            onChange={setTags}
            icon={false}
          />
        ) : null}

        <Button
          variant="secondary"
          size="sm"
          aria-pressed={favoritesOnly}
          onClick={() => setFavoritesOnly((v) => !v)}
          className={cn(favoritesOnly && "border-accent/40 bg-accent-subtle")}
        >
          <Star className={cn(favoritesOnly && "fill-warning text-warning")} />
          Favourites
        </Button>

        {activeFilters > 0 || search ? (
          <Button variant="ghost" size="sm" onClick={clearFilters}>
            <X />
            Clear
          </Button>
        ) : null}

        <div className="ml-auto flex items-center gap-0.5 rounded-control border border-border p-0.5">
          {(
            [
              { mode: "list" as const, icon: ListIcon, label: "List view" },
              { mode: "grid" as const, icon: LayoutGrid, label: "Grid view" },
            ] as const
          ).map(({ mode, icon: Icon, label }) => (
            <Tooltip key={mode} label={label}>
              <button
                type="button"
                onClick={() => setView(mode)}
                aria-label={label}
                aria-pressed={view === mode}
                className={cn(
                  "flex size-7 items-center justify-center rounded-[4px] transition-colors",
                  view === mode
                    ? "bg-surface-raised text-foreground"
                    : "text-subtle hover:text-foreground",
                )}
              >
                <Icon className="size-4" />
              </button>
            </Tooltip>
          ))}
        </div>
      </div>

      {selectedIds.length > 0 ? (
        <div className="mb-3 flex flex-wrap items-center gap-2 rounded-surface border border-accent/40 bg-accent-subtle px-3 py-2">
          <p className="text-caption font-medium text-foreground tabular">
            {selectedIds.length} selected
          </p>
          <div className="ml-auto flex items-center gap-2">
            <Button
              variant="danger-outline"
              size="sm"
              onClick={() => setConfirmDelete(true)}
            >
              <Trash2 />
              Delete
            </Button>
            <Button
              variant="ghost"
              size="sm"
              onClick={() => setSelectedIds([])}
            >
              Clear
            </Button>
          </div>
        </div>
      ) : null}

      {docs.error ? (
        <ErrorState description={docs.error.message} onRetry={refresh} />
      ) : docs.loading && !docs.data ? (
        <div className="rounded-surface border border-border bg-surface">
          <SkeletonRows rows={6} columns={4} />
        </div>
      ) : items.length === 0 ? (
        <EmptyState
          icon={FileText}
          title={
            activeFilters > 0 || search
              ? "No documents match your filters"
              : "No documents yet"
          }
          description={
            activeFilters > 0 || search
              ? "Try loosening a filter or clearing your search."
              : "Upload a file and Cadence will index it for search and AI retrieval."
          }
          action={
            activeFilters > 0 || search ? (
              <Button variant="secondary" size="sm" onClick={clearFilters}>
                Clear filters
              </Button>
            ) : (
              <Button
                variant="primary"
                size="sm"
                onClick={() => fileInputRef.current?.click()}
              >
                <Upload />
                Upload a document
              </Button>
            )
          }
          className="rounded-surface border border-border bg-surface"
        />
      ) : view === "grid" ? (
        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
          {items.map((doc) => (
            <div
              key={doc.id}
              className="group flex flex-col rounded-surface border border-border bg-surface p-4 transition-colors hover:border-border-strong"
            >
              <div className="flex items-start gap-3">
                <FileTypeChip type={doc.type} />
                <div className="min-w-0 flex-1">
                  <p className="truncate text-body font-medium text-foreground">
                    {doc.name}
                  </p>
                  <p className="text-caption text-muted tabular">
                    {formatFileSize(doc.sizeBytes)} ·{" "}
                    {formatDate(doc.createdAt)}
                  </p>
                </div>
                <div className="flex shrink-0 items-center">
                  <Button
                    variant="ghost"
                    size="icon-sm"
                    aria-label={
                      doc.isFavorite
                        ? "Remove from favourites"
                        : "Add to favourites"
                    }
                    aria-pressed={doc.isFavorite}
                    onClick={() => handleFavorite(doc)}
                  >
                    <Star
                      className={cn(
                        doc.isFavorite
                          ? "fill-warning text-warning"
                          : "text-subtle",
                      )}
                    />
                  </Button>
                  <RowMenu doc={doc} />
                </div>
              </div>

              <p className="mt-3 line-clamp-2 flex-1 text-caption text-muted">
                {doc.excerpt}
              </p>

              <div className="mt-3 flex items-center justify-between gap-2 border-t border-border pt-2.5">
                <Badge tone={STATUS_TONE[doc.processingStatus]}>
                  {humanize(doc.processingStatus)}
                </Badge>
                {userById.get(doc.ownerId) ? (
                  <Avatar
                    name={userById.get(doc.ownerId)?.name ?? ""}
                    size="xs"
                  />
                ) : null}
              </div>
            </div>
          ))}
        </div>
      ) : (
        <TableWrapper>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead className="w-10">
                  <Checkbox
                    checked={
                      items.every((i) => selectedIds.includes(i.id))
                        ? true
                        : items.some((i) => selectedIds.includes(i.id))
                          ? "indeterminate"
                          : false
                    }
                    onCheckedChange={() =>
                      setSelectedIds((current) => {
                        const ids = items.map((i) => i.id);
                        return ids.every((id) => current.includes(id))
                          ? current.filter((id) => !ids.includes(id))
                          : [...new Set([...current, ...ids])];
                      })
                    }
                    aria-label="Select all documents on this page"
                  />
                </TableHead>
                <SortableHead
                  label="Name"
                  columnKey="name"
                  activeKey={sortBy}
                  direction={sortDir}
                  onSort={handleSort}
                />
                <SortableHead
                  label="Type"
                  columnKey="type"
                  activeKey={sortBy}
                  direction={sortDir}
                  onSort={handleSort}
                />
                <SortableHead
                  label="Size"
                  columnKey="sizeBytes"
                  activeKey={sortBy}
                  direction={sortDir}
                  onSort={handleSort}
                />
                <TableHead>Owner</TableHead>
                <SortableHead
                  label="Uploaded"
                  columnKey="createdAt"
                  activeKey={sortBy}
                  direction={sortDir}
                  onSort={handleSort}
                />
                <TableHead>Status</TableHead>
                <TableHead className="w-16 text-right">Actions</TableHead>
              </TableRow>
            </TableHeader>

            <TableBody>
              {items.map((doc) => {
                const owner = userById.get(doc.ownerId);
                const selected = selectedIds.includes(doc.id);

                return (
                  <TableRow key={doc.id} selected={selected}>
                    <TableCell>
                      <Checkbox
                        checked={selected}
                        onCheckedChange={(value) =>
                          setSelectedIds((current) =>
                            value === true
                              ? [...current, doc.id]
                              : current.filter((id) => id !== doc.id),
                          )
                        }
                        aria-label={`Select ${doc.name}`}
                      />
                    </TableCell>

                    <TableCell className="max-w-80">
                      <div className="flex items-center gap-2">
                        <button
                          type="button"
                          onClick={() => handleFavorite(doc)}
                          aria-label={
                            doc.isFavorite
                              ? "Remove from favourites"
                              : "Add to favourites"
                          }
                          aria-pressed={doc.isFavorite}
                          className="rounded-control"
                        >
                          <Star
                            className={cn(
                              "size-3.5",
                              doc.isFavorite
                                ? "fill-warning text-warning"
                                : "text-subtle hover:text-muted",
                            )}
                          />
                        </button>
                        <span className="min-w-0 truncate font-medium text-foreground">
                          {doc.name}
                        </span>
                      </div>
                    </TableCell>

                    <TableCell className="uppercase text-muted">
                      {doc.type}
                    </TableCell>

                    <TableCell className="whitespace-nowrap text-muted tabular">
                      {formatFileSize(doc.sizeBytes)}
                    </TableCell>

                    <TableCell>
                      {owner ? (
                        <span className="flex items-center gap-1.5">
                          <Avatar name={owner.name} size="xs" />
                          <span className="truncate text-caption text-muted">
                            {owner.name}
                          </span>
                        </span>
                      ) : (
                        <span className="text-caption text-subtle">
                          Unknown
                        </span>
                      )}
                    </TableCell>

                    <TableCell className="whitespace-nowrap text-muted">
                      {formatDate(doc.createdAt)}
                    </TableCell>

                    <TableCell>
                      <Badge tone={STATUS_TONE[doc.processingStatus]}>
                        {humanize(doc.processingStatus)}
                      </Badge>
                    </TableCell>

                    <TableCell className="text-right">
                      <RowMenu doc={doc} />
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        </TableWrapper>
      )}

      {docs.data && docs.data.total > 0 ? (
        <Pagination
          className="mt-4"
          page={docs.data.page}
          totalPages={docs.data.totalPages}
          total={docs.data.total}
          pageSize={docs.data.pageSize}
          onPageChange={setPage}
        />
      ) : null}

      <RenameDialog
        doc={renaming}
        onOpenChange={(open) => {
          if (!open) setRenaming(null);
        }}
        onSubmit={handleRename}
      />

      <ConfirmDialog
        open={confirmDelete}
        onOpenChange={setConfirmDelete}
        title={`Delete ${selectedIds.length} ${
          selectedIds.length === 1 ? "document" : "documents"
        }?`}
        description="These files and their indexed content will be permanently removed. This cannot be undone."
        confirmLabel="Delete"
        destructive
        loading={mutating}
        onConfirm={handleDelete}
      />
    </PageContainer>
  );
}

function RenameDialog({
  doc,
  onOpenChange,
  onSubmit,
}: {
  doc: DocumentFile | null;
  onOpenChange: (open: boolean) => void;
  onSubmit: (name: string) => void;
}) {
  const [name, setName] = useState("");

  useEffect(() => {
    if (doc) setName(doc.name);
  }, [doc]);

  return (
    <Dialog open={doc !== null} onOpenChange={onOpenChange}>
      <DialogContent
        title="Rename document"
        size="sm"
        description="Changing the extension also changes how the file is categorised."
        footer={
          <>
            <DialogClose asChild>
              <Button variant="secondary" size="sm">
                Cancel
              </Button>
            </DialogClose>
            <Button
              variant="primary"
              size="sm"
              onClick={() => onSubmit(name)}
              disabled={!name.trim()}
            >
              Rename
            </Button>
          </>
        }
      >
        <Field label="File name" required>
          {(props) => (
            <Input
              {...props}
              value={name}
              onChange={(event) => setName(event.target.value)}
              autoFocus
            />
          )}
        </Field>
      </DialogContent>
    </Dialog>
  );
}
