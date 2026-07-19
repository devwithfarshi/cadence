"use client";

import {
  Archive,
  ArchiveRestore,
  Copy,
  LayoutGrid,
  List,
  MoreHorizontal,
  Plus,
  Star,
  Trash2,
  Video,
  X,
} from "lucide-react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { useCallback, useEffect, useMemo, useState } from "react";
import { MeetingGridCard } from "@/components/meetings/meeting-grid-card";
import { NewMeetingDialog } from "@/components/meetings/new-meeting-dialog";
import {
  MeetingStatusBadge,
  PLATFORM_LABELS,
  SummaryBadge,
} from "@/components/meetings/status";
import { usePreferences } from "@/components/providers/preferences-provider";
import { PageContainer, PageHeader } from "@/components/shell/page-header";
import { AvatarGroup } from "@/components/ui/avatar";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { ConfirmDialog } from "@/components/ui/dialog";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { EmptyState, ErrorState, SkeletonRows } from "@/components/ui/feedback";
import { FilterMenu } from "@/components/ui/filter-menu";
import { SearchInput } from "@/components/ui/input";
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
  deleteMeetings,
  duplicateMeeting,
  listMeetings,
  listMeetingTags,
  type MeetingSortKey,
  setArchived,
  toggleFavorite,
} from "@/lib/api/meetings";
import { useAsync, useDebounced } from "@/lib/hooks/use-async";
import { cn } from "@/lib/utils/cn";
import { formatDateTime, formatDuration } from "@/lib/utils/format";
import type {
  MeetingPlatform,
  MeetingStatus,
  SortDirection,
  SummaryStatus,
  ViewMode,
} from "@/types/domain";

const PAGE_SIZE = 10;

const STATUS_OPTIONS: { value: MeetingStatus; label: string }[] = [
  { value: "scheduled", label: "Scheduled" },
  { value: "live", label: "Live" },
  { value: "processing", label: "Processing" },
  { value: "completed", label: "Completed" },
  { value: "cancelled", label: "Cancelled" },
];

const PLATFORM_OPTIONS = Object.entries(PLATFORM_LABELS).map(
  ([value, label]) => ({ value: value as MeetingPlatform, label }),
);

const SUMMARY_OPTIONS: { value: SummaryStatus; label: string }[] = [
  { value: "ready", label: "Summary ready" },
  { value: "queued", label: "Queued" },
  { value: "generating", label: "Generating" },
  { value: "none", label: "No summary" },
  { value: "failed", label: "Failed" },
];

export default function MeetingsPage() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const { preferences, update } = usePreferences();
  const { toast } = useToast();

  /* --- Query state ------------------------------------------------------- */

  const [search, setSearch] = useState("");
  const debouncedSearch = useDebounced(search, 250);

  const [statuses, setStatuses] = useState<MeetingStatus[]>([]);
  const [platforms, setPlatforms] = useState<MeetingPlatform[]>([]);
  const [summaryStatuses, setSummaryStatuses] = useState<SummaryStatus[]>([]);
  const [tags, setTags] = useState<string[]>([]);
  const [favoritesOnly, setFavoritesOnly] = useState(false);
  const [includeArchived, setIncludeArchived] = useState(false);

  const [sortBy, setSortBy] = useState<MeetingSortKey>("startsAt");
  const [sortDir, setSortDir] = useState<SortDirection>("desc");
  const [page, setPage] = useState(1);

  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [mutating, setMutating] = useState(false);

  // `?new=1` from the sidebar and command palette opens the create dialog.
  const [createOpen, setCreateOpen] = useState(false);
  useEffect(() => {
    if (searchParams.get("new") === "1") {
      setCreateOpen(true);
      router.replace("/meetings");
    }
  }, [searchParams, router]);

  /* --- Data -------------------------------------------------------------- */

  const query = useMemo(
    () => ({
      search: debouncedSearch,
      status: statuses,
      platform: platforms,
      summaryStatus: summaryStatuses,
      tags,
      favoritesOnly,
      includeArchived,
      sortBy,
      sortDir,
      page,
      pageSize: PAGE_SIZE,
    }),
    [
      debouncedSearch,
      statuses,
      platforms,
      summaryStatuses,
      tags,
      favoritesOnly,
      includeArchived,
      sortBy,
      sortDir,
      page,
    ],
  );

  const meetings = useAsync(() => listMeetings(query), [query]);
  const availableTags = useAsync(() => listMeetingTags(), []);

  // Any change to the result set invalidates the current page number.
  // biome-ignore lint/correctness/useExhaustiveDependencies: resetting on filter change is the intent
  useEffect(() => {
    setPage(1);
    setSelectedIds([]);
  }, [
    debouncedSearch,
    statuses,
    platforms,
    summaryStatuses,
    tags,
    favoritesOnly,
    includeArchived,
  ]);

  const items = meetings.data?.items ?? [];
  const viewMode: ViewMode = preferences.meetingsView;

  const activeFilterCount =
    statuses.length +
    platforms.length +
    summaryStatuses.length +
    tags.length +
    (favoritesOnly ? 1 : 0) +
    (includeArchived ? 1 : 0);

  /* --- Selection --------------------------------------------------------- */

  const allOnPageSelected =
    items.length > 0 && items.every((item) => selectedIds.includes(item.id));
  const someOnPageSelected =
    items.some((item) => selectedIds.includes(item.id)) && !allOnPageSelected;

  const toggleSelectAll = () => {
    setSelectedIds((current) => {
      const pageIds = items.map((item) => item.id);
      return allOnPageSelected
        ? current.filter((id) => !pageIds.includes(id))
        : [...new Set([...current, ...pageIds])];
    });
  };

  const toggleSelect = (id: string, selected: boolean) => {
    setSelectedIds((current) =>
      selected ? [...current, id] : current.filter((entry) => entry !== id),
    );
  };

  /* --- Mutations --------------------------------------------------------- */

  const refresh = useCallback(() => {
    meetings.refetch();
    availableTags.refetch();
  }, [meetings, availableTags]);

  async function handleToggleFavorite(id: string, title: string) {
    // Optimistic — favouriting should feel instant.
    meetings.setData((current) =>
      current
        ? {
            ...current,
            items: current.items.map((item) =>
              item.id === id ? { ...item, isFavorite: !item.isFavorite } : item,
            ),
          }
        : current,
    );

    try {
      const updated = await toggleFavorite(id);
      toast({
        tone: "success",
        title: updated.isFavorite
          ? "Added to favourites"
          : "Removed from favourites",
        description: title,
      });
    } catch {
      meetings.refetch();
      toast({ tone: "error", title: "Could not update favourite" });
    }
  }

  async function handleArchive(ids: string[], archived: boolean) {
    setMutating(true);
    try {
      const count = await setArchived(ids, archived);
      setSelectedIds([]);
      refresh();
      toast({
        tone: "success",
        title: archived
          ? `Archived ${count} ${count === 1 ? "meeting" : "meetings"}`
          : `Restored ${count} ${count === 1 ? "meeting" : "meetings"}`,
        action: {
          label: "Undo",
          onClick: async () => {
            await setArchived(ids, !archived);
            refresh();
          },
        },
      });
    } catch {
      toast({ tone: "error", title: "Could not update meetings" });
    } finally {
      setMutating(false);
    }
  }

  async function handleDuplicate(id: string) {
    try {
      const copy = await duplicateMeeting(id);
      refresh();
      toast({
        tone: "success",
        title: "Meeting duplicated",
        description: copy.title,
      });
    } catch {
      toast({ tone: "error", title: "Could not duplicate meeting" });
    }
  }

  async function handleDelete() {
    setMutating(true);
    try {
      const count = await deleteMeetings(selectedIds);
      setSelectedIds([]);
      setConfirmDelete(false);
      refresh();
      toast({
        tone: "success",
        title: `Deleted ${count} ${count === 1 ? "meeting" : "meetings"}`,
      });
    } catch {
      toast({ tone: "error", title: "Could not delete meetings" });
    } finally {
      setMutating(false);
    }
  }

  function handleSort(key: string) {
    const typed = key as MeetingSortKey;
    if (sortBy === typed) {
      setSortDir((current) => (current === "asc" ? "desc" : "asc"));
    } else {
      setSortBy(typed);
      setSortDir(typed === "startsAt" ? "desc" : "asc");
    }
  }

  function clearFilters() {
    setStatuses([]);
    setPlatforms([]);
    setSummaryStatuses([]);
    setTags([]);
    setFavoritesOnly(false);
    setIncludeArchived(false);
    setSearch("");
  }

  /* --- Row actions menu -------------------------------------------------- */

  function RowActions({ id, isArchived }: { id: string; isArchived: boolean }) {
    return (
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <Button variant="ghost" size="icon-sm" aria-label="Meeting actions">
            <MoreHorizontal />
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent>
          <DropdownMenuItem asChild>
            <Link href={`/meetings/${id}`}>
              <Video />
              Open meeting
            </Link>
          </DropdownMenuItem>
          <DropdownMenuItem onSelect={() => handleDuplicate(id)}>
            <Copy />
            Duplicate
          </DropdownMenuItem>
          <DropdownMenuItem onSelect={() => handleArchive([id], !isArchived)}>
            {isArchived ? <ArchiveRestore /> : <Archive />}
            {isArchived ? "Restore" : "Archive"}
          </DropdownMenuItem>
          <DropdownMenuSeparator />
          <DropdownMenuItem
            destructive
            onSelect={() => {
              setSelectedIds([id]);
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

  /* --- Render ------------------------------------------------------------ */

  return (
    <PageContainer>
      <PageHeader
        title="Meetings"
        description="Every recorded, scheduled and archived meeting in your workspace."
        actions={
          <Button
            variant="primary"
            size="md"
            onClick={() => setCreateOpen(true)}
          >
            <Plus />
            New meeting
          </Button>
        }
      />

      {/* Toolbar */}
      <div className="mb-3 flex flex-wrap items-center gap-2">
        <SearchInput
          value={search}
          onValueChange={setSearch}
          placeholder="Search meetings, participants, tags…"
          className="w-full sm:w-72"
        />

        <FilterMenu
          label="Status"
          options={STATUS_OPTIONS}
          selected={statuses}
          onChange={setStatuses}
        />
        <FilterMenu
          label="Platform"
          options={PLATFORM_OPTIONS}
          selected={platforms}
          onChange={setPlatforms}
          icon={false}
        />
        <FilterMenu
          label="AI summary"
          options={SUMMARY_OPTIONS}
          selected={summaryStatuses}
          onChange={setSummaryStatuses}
          icon={false}
        />
        {availableTags.data && availableTags.data.length > 0 ? (
          <FilterMenu
            label="Tags"
            options={availableTags.data.map((tag) => ({
              value: tag,
              label: tag,
            }))}
            selected={tags}
            onChange={setTags}
            icon={false}
          />
        ) : null}

        <Button
          variant="secondary"
          size="sm"
          aria-pressed={favoritesOnly}
          onClick={() => setFavoritesOnly((current) => !current)}
          className={cn(favoritesOnly && "border-accent/40 bg-accent-subtle")}
        >
          <Star className={cn(favoritesOnly && "fill-warning text-warning")} />
          Favourites
        </Button>

        <Button
          variant="secondary"
          size="sm"
          aria-pressed={includeArchived}
          onClick={() => setIncludeArchived((current) => !current)}
          className={cn(includeArchived && "border-accent/40 bg-accent-subtle")}
        >
          <Archive />
          Archived
        </Button>

        {activeFilterCount > 0 ? (
          <Button variant="ghost" size="sm" onClick={clearFilters}>
            <X />
            Clear
          </Button>
        ) : null}

        {/* View toggle, persisted to preferences */}
        <div className="ml-auto flex items-center gap-0.5 rounded-control border border-border p-0.5">
          {[
            { mode: "list" as const, icon: List, label: "List view" },
            { mode: "grid" as const, icon: LayoutGrid, label: "Grid view" },
          ].map(({ mode, icon: Icon, label }) => (
            <Tooltip key={mode} label={label}>
              <button
                type="button"
                onClick={() => update({ meetingsView: mode })}
                aria-label={label}
                aria-pressed={viewMode === mode}
                className={cn(
                  "flex size-7 items-center justify-center rounded-[4px] transition-colors",
                  viewMode === mode
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

      {/* Bulk action bar */}
      {selectedIds.length > 0 ? (
        <div className="mb-3 flex flex-wrap items-center gap-2 rounded-surface border border-accent/40 bg-accent-subtle px-3 py-2">
          <p className="text-caption font-medium text-foreground tabular">
            {selectedIds.length} selected
          </p>
          <div className="ml-auto flex flex-wrap items-center gap-2">
            <Button
              variant="secondary"
              size="sm"
              onClick={() => handleArchive(selectedIds, !includeArchived)}
              loading={mutating}
            >
              {includeArchived ? <ArchiveRestore /> : <Archive />}
              {includeArchived ? "Restore" : "Archive"}
            </Button>
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

      {/* Results */}
      {meetings.error ? (
        <ErrorState
          description={meetings.error.message}
          onRetry={meetings.refetch}
        />
      ) : meetings.loading && !meetings.data ? (
        <div className="rounded-surface border border-border bg-surface">
          <SkeletonRows rows={6} columns={5} />
        </div>
      ) : items.length === 0 ? (
        <EmptyState
          icon={Video}
          title={
            activeFilterCount > 0 || search
              ? "No meetings match your filters"
              : "No meetings yet"
          }
          description={
            activeFilterCount > 0 || search
              ? "Try loosening a filter or clearing your search."
              : "Schedule a meeting and Cadence will record and summarise it for you."
          }
          action={
            activeFilterCount > 0 || search ? (
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
                New meeting
              </Button>
            )
          }
          className="rounded-surface border border-border bg-surface"
        />
      ) : viewMode === "grid" ? (
        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
          {items.map((meeting) => (
            <MeetingGridCard
              key={meeting.id}
              meeting={meeting}
              selected={selectedIds.includes(meeting.id)}
              onSelectChange={(value) => toggleSelect(meeting.id, value)}
              onToggleFavorite={() =>
                handleToggleFavorite(meeting.id, meeting.title)
              }
              onOpenMenu={() => router.push(`/meetings/${meeting.id}`)}
            />
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
                      allOnPageSelected
                        ? true
                        : someOnPageSelected
                          ? "indeterminate"
                          : false
                    }
                    onCheckedChange={toggleSelectAll}
                    aria-label="Select all meetings on this page"
                  />
                </TableHead>
                <SortableHead
                  label="Meeting"
                  columnKey="title"
                  activeKey={sortBy}
                  direction={sortDir}
                  onSort={handleSort}
                />
                <SortableHead
                  label="Date"
                  columnKey="startsAt"
                  activeKey={sortBy}
                  direction={sortDir}
                  onSort={handleSort}
                />
                <SortableHead
                  label="Duration"
                  columnKey="durationSeconds"
                  activeKey={sortBy}
                  direction={sortDir}
                  onSort={handleSort}
                />
                <SortableHead
                  label="Participants"
                  columnKey="participants"
                  activeKey={sortBy}
                  direction={sortDir}
                  onSort={handleSort}
                />
                <TableHead>Status</TableHead>
                <TableHead className="w-20 text-right">Actions</TableHead>
              </TableRow>
            </TableHeader>

            <TableBody>
              {items.map((meeting) => {
                const selected = selectedIds.includes(meeting.id);

                return (
                  <TableRow key={meeting.id} selected={selected}>
                    <TableCell>
                      <Checkbox
                        checked={selected}
                        onCheckedChange={(value) =>
                          toggleSelect(meeting.id, value === true)
                        }
                        aria-label={`Select ${meeting.title}`}
                      />
                    </TableCell>

                    <TableCell className="max-w-80">
                      <div className="flex items-start gap-2">
                        <button
                          type="button"
                          onClick={() =>
                            handleToggleFavorite(meeting.id, meeting.title)
                          }
                          aria-label={
                            meeting.isFavorite
                              ? "Remove from favourites"
                              : "Add to favourites"
                          }
                          aria-pressed={meeting.isFavorite}
                          className="mt-0.5 rounded-control"
                        >
                          <Star
                            className={cn(
                              "size-3.5",
                              meeting.isFavorite
                                ? "fill-warning text-warning"
                                : "text-subtle hover:text-muted",
                            )}
                          />
                        </button>

                        <div className="min-w-0">
                          <Link
                            href={`/meetings/${meeting.id}`}
                            className="block truncate font-medium text-foreground hover:text-accent"
                          >
                            {meeting.title}
                          </Link>
                          <div className="mt-1 flex flex-wrap items-center gap-1">
                            {meeting.isArchived ? (
                              <Badge tone="neutral" size="sm">
                                Archived
                              </Badge>
                            ) : null}
                            {meeting.tags.slice(0, 2).map((tag) => (
                              <Badge key={tag} tone="outline" size="sm">
                                {tag}
                              </Badge>
                            ))}
                          </div>
                        </div>
                      </div>
                    </TableCell>

                    <TableCell className="whitespace-nowrap text-muted">
                      {formatDateTime(meeting.startsAt)}
                    </TableCell>

                    <TableCell className="whitespace-nowrap text-muted tabular">
                      {formatDuration(meeting.durationSeconds)}
                    </TableCell>

                    <TableCell>
                      <AvatarGroup people={meeting.participants} max={3} />
                    </TableCell>

                    <TableCell>
                      <div className="flex flex-wrap items-center gap-1.5">
                        <MeetingStatusBadge status={meeting.status} />
                        {meeting.summaryStatus !== "none" ? (
                          <SummaryBadge status={meeting.summaryStatus} />
                        ) : null}
                      </div>
                    </TableCell>

                    <TableCell className="text-right">
                      <RowActions
                        id={meeting.id}
                        isArchived={meeting.isArchived}
                      />
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        </TableWrapper>
      )}

      {meetings.data && meetings.data.total > 0 ? (
        <Pagination
          className="mt-4"
          page={meetings.data.page}
          totalPages={meetings.data.totalPages}
          total={meetings.data.total}
          pageSize={meetings.data.pageSize}
          onPageChange={setPage}
        />
      ) : null}

      <NewMeetingDialog
        open={createOpen}
        onOpenChange={setCreateOpen}
        onCreated={refresh}
      />

      <ConfirmDialog
        open={confirmDelete}
        onOpenChange={setConfirmDelete}
        title={`Delete ${selectedIds.length} ${
          selectedIds.length === 1 ? "meeting" : "meetings"
        }?`}
        description="This removes the recording, transcript, summary and comments. Action items are kept but lose their link to the meeting. This cannot be undone."
        confirmLabel="Delete"
        destructive
        loading={mutating}
        onConfirm={handleDelete}
      />
    </PageContainer>
  );
}
