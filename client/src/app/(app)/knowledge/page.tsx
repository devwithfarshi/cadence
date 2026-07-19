"use client";

import {
  ExternalLink,
  LayoutGrid,
  Library,
  List as ListIcon,
  Plus,
  Star,
  Trash2,
  X,
} from "lucide-react";
import { useCallback, useEffect, useState } from "react";
import { knowledgeKindLabel } from "@/components/library/file-icon";
import { useSession } from "@/components/providers/auth-provider";
import { usePreferences } from "@/components/providers/preferences-provider";
import { PageContainer, PageHeader } from "@/components/shell/page-header";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  ConfirmDialog,
  Dialog,
  DialogClose,
  DialogContent,
} from "@/components/ui/dialog";
import { EmptyState, ErrorState, SkeletonRows } from "@/components/ui/feedback";
import { FilterMenu } from "@/components/ui/filter-menu";
import { Field, Input, SearchInput, Textarea } from "@/components/ui/input";
import { Pagination } from "@/components/ui/navigation";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { useToast } from "@/components/ui/toast";
import { Tooltip } from "@/components/ui/tooltip";
import {
  createKnowledgeItem,
  deleteKnowledgeItems,
  getKnowledgeFacets,
  type KnowledgeSortKey,
  listKnowledge,
  toggleKnowledgeFavorite,
  touchKnowledgeItem,
} from "@/lib/api/library";
import { useAsync, useDebounced } from "@/lib/hooks/use-async";
import { cn } from "@/lib/utils/cn";
import { formatRelative } from "@/lib/utils/format";
import type {
  KnowledgeItem,
  KnowledgeItemKind,
  ViewMode,
} from "@/types/domain";

const PAGE_SIZE = 12;

const KIND_OPTIONS: { value: KnowledgeItemKind; label: string }[] = [
  { value: "document", label: "Document" },
  { value: "meeting_note", label: "Meeting note" },
  { value: "ai_summary", label: "AI summary" },
  { value: "link", label: "Link" },
];

const KIND_TONE = {
  document: "info",
  meeting_note: "neutral",
  ai_summary: "accent",
  link: "success",
} as const;

export default function KnowledgePage() {
  const session = useSession();
  const { preferences, update } = usePreferences();
  const { toast } = useToast();

  const [search, setSearch] = useState("");
  const debouncedSearch = useDebounced(search, 250);
  const [kinds, setKinds] = useState<KnowledgeItemKind[]>([]);
  const [categories, setCategories] = useState<string[]>([]);
  const [tags, setTags] = useState<string[]>([]);
  const [favoritesOnly, setFavoritesOnly] = useState(false);
  const [sortBy, setSortBy] = useState<KnowledgeSortKey>("createdAt");
  const [page, setPage] = useState(1);

  const [createOpen, setCreateOpen] = useState(false);
  const [pendingDelete, setPendingDelete] = useState<KnowledgeItem | null>(
    null,
  );
  const [mutating, setMutating] = useState(false);

  const view: ViewMode = preferences.knowledgeView;

  const items = useAsync(
    () =>
      listKnowledge({
        search: debouncedSearch,
        kind: kinds,
        category: categories,
        tags,
        favoritesOnly,
        sortBy,
        page,
        pageSize: PAGE_SIZE,
      }),
    [debouncedSearch, kinds, categories, tags, favoritesOnly, sortBy, page],
  );

  const facets = useAsync(() => getKnowledgeFacets(), []);

  // Recently opened is its own rail, so it ignores the filters above.
  const recent = useAsync(
    () =>
      listKnowledge({
        sortBy: "lastOpenedAt",
        sortDir: "desc",
        pageSize: 4,
      }).then((result) =>
        result.items.filter((item) => item.lastOpenedAt !== null),
      ),
    [],
  );

  // biome-ignore lint/correctness/useExhaustiveDependencies: resetting on filter change is the intent
  useEffect(() => {
    setPage(1);
  }, [debouncedSearch, kinds, categories, tags, favoritesOnly]);

  const refresh = useCallback(() => {
    items.refetch();
    facets.refetch();
    recent.refetch();
  }, [items, facets, recent]);

  const list = items.data?.items ?? [];
  const activeFilters =
    kinds.length + categories.length + tags.length + (favoritesOnly ? 1 : 0);

  async function handleFavorite(item: KnowledgeItem) {
    items.setData((current) =>
      current
        ? {
            ...current,
            items: current.items.map((entry) =>
              entry.id === item.id
                ? { ...entry, isFavorite: !entry.isFavorite }
                : entry,
            ),
          }
        : current,
    );
    await toggleKnowledgeFavorite(item.id).catch(() => items.refetch());
  }

  async function handleOpen(item: KnowledgeItem) {
    await touchKnowledgeItem(item.id).catch(() => undefined);
    recent.refetch();

    if (item.sourceUrl) {
      window.open(item.sourceUrl, "_blank", "noopener,noreferrer");
    }
  }

  async function handleDelete() {
    if (!pendingDelete) return;
    setMutating(true);
    try {
      await deleteKnowledgeItems([pendingDelete.id]);
      setPendingDelete(null);
      refresh();
      toast({ tone: "success", title: "Removed from knowledge base" });
    } catch {
      toast({ tone: "error", title: "Could not remove item" });
    } finally {
      setMutating(false);
    }
  }

  function clearFilters() {
    setKinds([]);
    setCategories([]);
    setTags([]);
    setFavoritesOnly(false);
    setSearch("");
  }

  function ItemCard({ item }: { item: KnowledgeItem }) {
    return (
      <div className="group flex flex-col rounded-surface border border-border bg-surface p-4 transition-colors hover:border-border-strong">
        <div className="flex items-start justify-between gap-2">
          <Badge tone={KIND_TONE[item.kind]} size="sm">
            {knowledgeKindLabel(item.kind)}
          </Badge>

          <div className="flex shrink-0 items-center">
            <Button
              variant="ghost"
              size="icon-sm"
              aria-label={
                item.isFavorite ? "Remove from favourites" : "Add to favourites"
              }
              aria-pressed={item.isFavorite}
              onClick={() => handleFavorite(item)}
            >
              <Star
                className={cn(
                  item.isFavorite ? "fill-warning text-warning" : "text-subtle",
                )}
              />
            </Button>
            <Button
              variant="ghost"
              size="icon-sm"
              aria-label={`Remove ${item.title}`}
              className="opacity-0 transition-opacity focus-visible:opacity-100 group-hover:opacity-100"
              onClick={() => setPendingDelete(item)}
            >
              <Trash2 />
            </Button>
          </div>
        </div>

        <button
          type="button"
          onClick={() => handleOpen(item)}
          className="mt-2 text-left"
        >
          <p className="text-body font-medium text-foreground hover:text-accent">
            {item.title}
            {item.sourceUrl ? (
              <ExternalLink
                className="ml-1 inline size-3 text-subtle"
                aria-label="Opens externally"
              />
            ) : null}
          </p>
        </button>

        <p className="mt-1.5 line-clamp-3 flex-1 text-caption text-muted">
          {item.excerpt}
        </p>

        <div className="mt-3 flex flex-wrap items-center gap-1.5 border-t border-border pt-2.5">
          <span className="text-label text-subtle">{item.category}</span>
          {item.tags.slice(0, 2).map((tag) => (
            <Badge key={tag} tone="outline" size="sm">
              {tag}
            </Badge>
          ))}
        </div>
      </div>
    );
  }

  return (
    <PageContainer>
      <PageHeader
        title="Knowledge Base"
        description="A searchable record of everything your team has discussed and written down."
        actions={
          <Button
            variant="primary"
            size="md"
            onClick={() => setCreateOpen(true)}
          >
            <Plus />
            Add entry
          </Button>
        }
      />

      {/* Recently opened — a rail, deliberately outside the filtered set. */}
      {recent.data && recent.data.length > 0 ? (
        <section className="mb-5">
          <h2 className="mb-2 text-overline uppercase text-subtle">
            Recently opened
          </h2>
          <div className="flex flex-wrap gap-2">
            {recent.data.map((item) => (
              <button
                key={item.id}
                type="button"
                onClick={() => handleOpen(item)}
                className="flex min-w-0 max-w-64 items-center gap-2 rounded-control border border-border bg-surface px-2.5 py-1.5 transition-colors hover:border-border-strong hover:bg-surface-raised/60"
              >
                <span className="min-w-0 truncate text-caption text-foreground">
                  {item.title}
                </span>
                <span className="shrink-0 text-label text-subtle">
                  {item.lastOpenedAt ? formatRelative(item.lastOpenedAt) : ""}
                </span>
              </button>
            ))}
          </div>
        </section>
      ) : null}

      <div className="mb-3 flex flex-wrap items-center gap-2">
        <SearchInput
          value={search}
          onValueChange={setSearch}
          placeholder="Search the knowledge base…"
          className="w-full sm:w-72"
        />
        <FilterMenu
          label="Type"
          options={KIND_OPTIONS}
          selected={kinds}
          onChange={setKinds}
        />
        {facets.data && facets.data.categories.length > 0 ? (
          <FilterMenu
            label="Category"
            options={facets.data.categories.map((c) => ({
              value: c,
              label: c,
            }))}
            selected={categories}
            onChange={setCategories}
            icon={false}
          />
        ) : null}
        {facets.data && facets.data.tags.length > 0 ? (
          <FilterMenu
            label="Tags"
            options={facets.data.tags.map((t) => ({ value: t, label: t }))}
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

        <Select
          value={sortBy}
          onValueChange={(value) => setSortBy(value as KnowledgeSortKey)}
        >
          <SelectTrigger size="sm" className="w-40">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="createdAt">Newest first</SelectItem>
            <SelectItem value="title">Title A–Z</SelectItem>
            <SelectItem value="lastOpenedAt">Recently opened</SelectItem>
          </SelectContent>
        </Select>

        {activeFilters > 0 || search ? (
          <Button variant="ghost" size="sm" onClick={clearFilters}>
            <X />
            Clear
          </Button>
        ) : null}

        <div className="ml-auto flex items-center gap-0.5 rounded-control border border-border p-0.5">
          {(
            [
              { mode: "grid" as const, icon: LayoutGrid, label: "Grid view" },
              { mode: "list" as const, icon: ListIcon, label: "List view" },
            ] as const
          ).map(({ mode, icon: Icon, label }) => (
            <Tooltip key={mode} label={label}>
              <button
                type="button"
                onClick={() => update({ knowledgeView: mode })}
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

      {items.error ? (
        <ErrorState description={items.error.message} onRetry={refresh} />
      ) : items.loading && !items.data ? (
        <div className="rounded-surface border border-border bg-surface">
          <SkeletonRows rows={5} columns={3} />
        </div>
      ) : list.length === 0 ? (
        <EmptyState
          icon={Library}
          title={
            activeFilters > 0 || search
              ? "Nothing matches your filters"
              : "Your knowledge base is empty"
          }
          description={
            activeFilters > 0 || search
              ? "Try loosening a filter or clearing your search."
              : "Meeting notes, AI summaries and reference documents collect here as your team works."
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
                onClick={() => setCreateOpen(true)}
              >
                <Plus />
                Add the first entry
              </Button>
            )
          }
          className="rounded-surface border border-border bg-surface"
        />
      ) : view === "grid" ? (
        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
          {list.map((item) => (
            <ItemCard key={item.id} item={item} />
          ))}
        </div>
      ) : (
        <div className="divide-y divide-border rounded-surface border border-border bg-surface">
          {list.map((item) => (
            <div
              key={item.id}
              className="group flex items-start gap-3 px-4 py-3 transition-colors hover:bg-surface-raised/50"
            >
              <div className="min-w-0 flex-1">
                <div className="flex flex-wrap items-center gap-2">
                  <button
                    type="button"
                    onClick={() => handleOpen(item)}
                    className="min-w-0 text-left"
                  >
                    <span className="truncate text-body font-medium text-foreground hover:text-accent">
                      {item.title}
                    </span>
                  </button>
                  <Badge tone={KIND_TONE[item.kind]} size="sm">
                    {knowledgeKindLabel(item.kind)}
                  </Badge>
                  <span className="text-label text-subtle">
                    {item.category}
                  </span>
                </div>
                <p className="mt-1 line-clamp-2 text-caption text-muted">
                  {item.excerpt}
                </p>
              </div>

              <div className="flex shrink-0 items-center">
                <Button
                  variant="ghost"
                  size="icon-sm"
                  aria-label={
                    item.isFavorite
                      ? "Remove from favourites"
                      : "Add to favourites"
                  }
                  aria-pressed={item.isFavorite}
                  onClick={() => handleFavorite(item)}
                >
                  <Star
                    className={cn(
                      item.isFavorite
                        ? "fill-warning text-warning"
                        : "text-subtle",
                    )}
                  />
                </Button>
                <Button
                  variant="ghost"
                  size="icon-sm"
                  aria-label={`Remove ${item.title}`}
                  className="opacity-0 transition-opacity focus-visible:opacity-100 group-hover:opacity-100"
                  onClick={() => setPendingDelete(item)}
                >
                  <Trash2 />
                </Button>
              </div>
            </div>
          ))}
        </div>
      )}

      {items.data && items.data.total > 0 ? (
        <Pagination
          className="mt-4"
          page={items.data.page}
          totalPages={items.data.totalPages}
          total={items.data.total}
          pageSize={items.data.pageSize}
          onPageChange={setPage}
        />
      ) : null}

      <NewEntryDialog
        open={createOpen}
        onOpenChange={setCreateOpen}
        ownerId={session.userId}
        categories={facets.data?.categories ?? []}
        onCreated={refresh}
      />

      <ConfirmDialog
        open={pendingDelete !== null}
        onOpenChange={(open) => {
          if (!open) setPendingDelete(null);
        }}
        title="Remove this entry?"
        description={`"${pendingDelete?.title ?? ""}" will be removed from the knowledge base. The underlying meeting or document is not deleted.`}
        confirmLabel="Remove"
        destructive
        loading={mutating}
        onConfirm={handleDelete}
      />
    </PageContainer>
  );
}

function NewEntryDialog({
  open,
  onOpenChange,
  ownerId,
  categories,
  onCreated,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  ownerId: string;
  categories: string[];
  onCreated: () => void;
}) {
  const { toast } = useToast();
  const [title, setTitle] = useState("");
  const [kind, setKind] = useState<KnowledgeItemKind>("meeting_note");
  const [category, setCategory] = useState("");
  const [excerpt, setExcerpt] = useState("");
  const [sourceUrl, setSourceUrl] = useState("");
  const [tagsInput, setTagsInput] = useState("");
  const [titleError, setTitleError] = useState<string>();
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    if (!open) return;
    setTitle("");
    setKind("meeting_note");
    setCategory(categories[0] ?? "");
    setExcerpt("");
    setSourceUrl("");
    setTagsInput("");
    setTitleError(undefined);
  }, [open, categories]);

  async function handleSubmit(event: React.FormEvent) {
    event.preventDefault();
    if (!title.trim()) {
      setTitleError("Give the entry a title.");
      return;
    }

    setSubmitting(true);
    try {
      await createKnowledgeItem({
        title,
        kind,
        category,
        excerpt,
        ownerId,
        sourceUrl: kind === "link" ? sourceUrl.trim() || null : null,
        tags: tagsInput
          .split(",")
          .map((t) => t.trim().toLowerCase())
          .filter(Boolean),
      });
      onOpenChange(false);
      onCreated();
      toast({ tone: "success", title: "Entry added", description: title });
    } catch (error) {
      toast({
        tone: "error",
        title: "Could not add entry",
        description:
          error instanceof Error ? error.message : "Please try again.",
      });
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        title="Add knowledge base entry"
        description="Capture something the team should be able to find later."
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
              type="submit"
              form="new-kb-form"
              loading={submitting}
            >
              Add entry
            </Button>
          </>
        }
      >
        <form
          id="new-kb-form"
          onSubmit={handleSubmit}
          className="space-y-4"
          noValidate
        >
          <Field label="Title" required error={titleError}>
            {(props) => (
              <Input
                {...props}
                value={title}
                onChange={(event) => {
                  setTitle(event.target.value);
                  if (titleError) setTitleError(undefined);
                }}
                placeholder="How we decide what to cut"
                autoFocus
              />
            )}
          </Field>

          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Type">
              {() => (
                <Select
                  value={kind}
                  onValueChange={(value) => setKind(value as KnowledgeItemKind)}
                >
                  <SelectTrigger className="w-full">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {KIND_OPTIONS.map((option) => (
                      <SelectItem key={option.value} value={option.value}>
                        {option.label}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            </Field>

            <Field label="Category" hint="Groups related entries together.">
              {(props) => (
                <Input
                  {...props}
                  value={category}
                  onChange={(event) => setCategory(event.target.value)}
                  placeholder="Product"
                  list="kb-categories"
                />
              )}
            </Field>
          </div>

          <datalist id="kb-categories">
            {categories.map((c) => (
              <option key={c} value={c} />
            ))}
          </datalist>

          {kind === "link" ? (
            <Field label="URL" hint="Opens in a new tab.">
              {(props) => (
                <Input
                  {...props}
                  type="url"
                  value={sourceUrl}
                  onChange={(event) => setSourceUrl(event.target.value)}
                  placeholder="https://example.com/article"
                />
              )}
            </Field>
          ) : null}

          <Field label="Summary" hint="What someone needs to know at a glance.">
            {(props) => (
              <Textarea
                {...props}
                value={excerpt}
                onChange={(event) => setExcerpt(event.target.value)}
                rows={3}
              />
            )}
          </Field>

          <Field label="Tags" hint="Comma separated.">
            {(props) => (
              <Input
                {...props}
                value={tagsInput}
                onChange={(event) => setTagsInput(event.target.value)}
                placeholder="process, playbook"
              />
            )}
          </Field>
        </form>
      </DialogContent>
    </Dialog>
  );
}
