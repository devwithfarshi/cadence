"use client";

import {
  CalendarDays,
  CheckCircle2,
  Columns3,
  List as ListIcon,
  ListTodo,
  Plus,
  Trash2,
  UserCheck,
  X,
} from "lucide-react";
import { useCallback, useEffect, useMemo, useState } from "react";
import { PriorityBadge, TaskStatusBadge } from "@/components/meetings/status";
import { useSession } from "@/components/providers/auth-provider";
import { usePreferences } from "@/components/providers/preferences-provider";
import { PageContainer, PageHeader } from "@/components/shell/page-header";
import { NewTaskDialog } from "@/components/tasks/new-task-dialog";
import { TaskBoard } from "@/components/tasks/task-board";
import { TaskCalendar } from "@/components/tasks/task-calendar";
import { TaskDrawer } from "@/components/tasks/task-drawer";
import { Avatar } from "@/components/ui/avatar";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { ConfirmDialog } from "@/components/ui/dialog";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
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
import { Tabs, TabsCount, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { useToast } from "@/components/ui/toast";
import { Tooltip } from "@/components/ui/tooltip";
import { getMeeting } from "@/lib/api/meetings";
import {
  assignTasks,
  deleteTasks,
  getTaskCounts,
  listTasks,
  listTaskTags,
  moveTaskToStatus,
  queryTasks,
  setTaskPriority,
  setTaskStatus,
  type TaskSortKey,
  updateTask,
} from "@/lib/api/tasks";
import { listUsers } from "@/lib/api/workspace";
import { collection } from "@/lib/db/storage";
import { useAsync, useDebounced } from "@/lib/hooks/use-async";
import { cn } from "@/lib/utils/cn";
import { formatDueDate, humanize } from "@/lib/utils/format";
import type {
  ActionItem,
  Meeting,
  SortDirection,
  TaskPriority,
  TaskStatus,
  TasksView,
  TranscriptSegment,
} from "@/types/domain";

const PAGE_SIZE = 12;

type Scope = "assigned" | "created" | "completed" | "all";

const OPEN_STATUSES: TaskStatus[] = ["todo", "in_progress", "blocked"];

const STATUS_OPTIONS: { value: TaskStatus; label: string }[] = [
  { value: "todo", label: "To do" },
  { value: "in_progress", label: "In progress" },
  { value: "blocked", label: "Blocked" },
  { value: "done", label: "Done" },
];

const PRIORITY_OPTIONS: { value: TaskPriority; label: string }[] = [
  { value: "urgent", label: "Urgent" },
  { value: "high", label: "High" },
  { value: "medium", label: "Medium" },
  { value: "low", label: "Low" },
];

export default function TasksPage() {
  const session = useSession();
  const { preferences, update } = usePreferences();
  const { toast } = useToast();

  const [scope, setScope] = useState<Scope>("assigned");
  const [search, setSearch] = useState("");
  const debouncedSearch = useDebounced(search, 250);

  const [statuses, setStatuses] = useState<TaskStatus[]>([]);
  const [priorities, setPriorities] = useState<TaskPriority[]>([]);
  const [tags, setTags] = useState<string[]>([]);
  const [overdueOnly, setOverdueOnly] = useState(false);

  const [sortBy, setSortBy] = useState<TaskSortKey>("dueDate");
  const [sortDir, setSortDir] = useState<SortDirection>("asc");
  const [page, setPage] = useState(1);

  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [mutating, setMutating] = useState(false);

  const [createOpen, setCreateOpen] = useState(false);
  const [openTask, setOpenTask] = useState<ActionItem | null>(null);

  const view: TasksView = preferences.tasksView;

  /* --- Query ------------------------------------------------------------- */

  const baseQuery = useMemo(() => {
    // Scope decides *whose* tasks these are, so it is more than a filter.
    // "Assigned to me" deliberately means open work, not a full history.
    const scoped =
      scope === "assigned"
        ? { assigneeId: session.userId, status: OPEN_STATUSES }
        : scope === "created"
          ? { creatorId: session.userId }
          : scope === "completed"
            ? { status: ["done"] as TaskStatus[] }
            : {};

    return {
      ...scoped,
      // An explicit status filter overrides the scope's implicit one.
      ...(statuses.length > 0 ? { status: statuses } : {}),
      search: debouncedSearch,
      priority: priorities,
      tags,
      overdueOnly,
      sortBy,
      sortDir,
    };
  }, [
    scope,
    session.userId,
    statuses,
    debouncedSearch,
    priorities,
    tags,
    overdueOnly,
    sortBy,
    sortDir,
  ]);

  // The list paginates; the board and calendar need the whole set at once.
  const paged = useAsync(
    () =>
      view === "list"
        ? listTasks({ ...baseQuery, page, pageSize: PAGE_SIZE })
        : Promise.resolve(null),
    [baseQuery, page, view],
  );

  const full = useAsync(
    () => (view === "list" ? Promise.resolve(null) : queryTasks(baseQuery)),
    [baseQuery, view],
  );

  const users = useAsync(() => listUsers(), []);
  const counts = useAsync(
    () => getTaskCounts(session.userId),
    [session.userId],
  );
  const availableTags = useAsync(() => listTaskTags(), []);

  // Provenance for the drawer: the meeting and transcript line behind a task.
  const provenance = useAsync(async (): Promise<{
    meeting: Meeting | null;
    segment: TranscriptSegment | null;
  }> => {
    if (!openTask?.meetingId) return { meeting: null, segment: null };

    const meeting = await getMeeting(openTask.meetingId).catch(() => null);
    const segment = openTask.sourceSegmentId
      ? (collection<TranscriptSegment>("transcripts").find(
          openTask.sourceSegmentId,
        ) ?? null)
      : null;

    return { meeting, segment };
  }, [openTask?.meetingId, openTask?.sourceSegmentId]);

  const items = view === "list" ? (paged.data?.items ?? []) : (full.data ?? []);
  const loading = view === "list" ? paged.loading : full.loading;
  const error = view === "list" ? paged.error : full.error;

  const refresh = useCallback(() => {
    paged.refetch();
    full.refetch();
    counts.refetch();
    availableTags.refetch();
  }, [paged, full, counts, availableTags]);

  // Filter or scope changes invalidate the page number and selection.
  // biome-ignore lint/correctness/useExhaustiveDependencies: resetting on filter change is the intent
  useEffect(() => {
    setPage(1);
    setSelectedIds([]);
  }, [scope, debouncedSearch, statuses, priorities, tags, overdueOnly]);

  // Keep an open drawer in step with refreshed data.
  useEffect(() => {
    if (!openTask) return;
    const fresh = items.find((item) => item.id === openTask.id);
    if (fresh && fresh.updatedAt !== openTask.updatedAt) setOpenTask(fresh);
  }, [items, openTask]);

  const activeFilterCount =
    statuses.length + priorities.length + tags.length + (overdueOnly ? 1 : 0);

  /* --- Mutations --------------------------------------------------------- */

  async function handleToggleDone(task: ActionItem) {
    const next: TaskStatus = task.status === "done" ? "todo" : "done";
    try {
      await updateTask(task.id, { status: next });
      refresh();
    } catch {
      toast({ tone: "error", title: "Could not update task" });
    }
  }

  async function handleMove(task: ActionItem, status: TaskStatus) {
    // Optimistic: a dropped card should land immediately, not after a round trip.
    full.setData((current) =>
      current?.map((item) =>
        item.id === task.id ? { ...item, status } : item,
      ),
    );

    try {
      await moveTaskToStatus(task.id, status);
      counts.refetch();
    } catch {
      full.refetch();
      toast({ tone: "error", title: "Could not move task" });
    }
  }

  async function handleBulkStatus(status: TaskStatus) {
    setMutating(true);
    try {
      const count = await setTaskStatus(selectedIds, status);
      setSelectedIds([]);
      refresh();
      toast({
        tone: "success",
        title: `Moved ${count} ${count === 1 ? "task" : "tasks"} to ${humanize(
          status,
        ).toLowerCase()}`,
      });
    } catch {
      toast({ tone: "error", title: "Could not update tasks" });
    } finally {
      setMutating(false);
    }
  }

  async function handleBulkAssign(assigneeId: string | null) {
    setMutating(true);
    try {
      const count = await assignTasks(selectedIds, assigneeId);
      setSelectedIds([]);
      refresh();
      toast({
        tone: "success",
        title: `Reassigned ${count} ${count === 1 ? "task" : "tasks"}`,
      });
    } catch {
      toast({ tone: "error", title: "Could not reassign tasks" });
    } finally {
      setMutating(false);
    }
  }

  async function handleBulkPriority(priority: TaskPriority) {
    setMutating(true);
    try {
      const count = await setTaskPriority(selectedIds, priority);
      setSelectedIds([]);
      refresh();
      toast({
        tone: "success",
        title: `Set ${count} ${count === 1 ? "task" : "tasks"} to ${priority}`,
      });
    } catch {
      toast({ tone: "error", title: "Could not update priority" });
    } finally {
      setMutating(false);
    }
  }

  async function handleDelete() {
    setMutating(true);
    try {
      const count = await deleteTasks(selectedIds);
      setSelectedIds([]);
      setConfirmDelete(false);
      refresh();
      toast({
        tone: "success",
        title: `Deleted ${count} ${count === 1 ? "task" : "tasks"}`,
      });
    } catch {
      toast({ tone: "error", title: "Could not delete tasks" });
    } finally {
      setMutating(false);
    }
  }

  function handleSort(key: string) {
    const typed = key as TaskSortKey;
    if (sortBy === typed) {
      setSortDir((current) => (current === "asc" ? "desc" : "asc"));
    } else {
      setSortBy(typed);
      setSortDir("asc");
    }
  }

  function clearFilters() {
    setStatuses([]);
    setPriorities([]);
    setTags([]);
    setOverdueOnly(false);
    setSearch("");
  }

  /* --- Selection --------------------------------------------------------- */

  const allSelected =
    items.length > 0 && items.every((item) => selectedIds.includes(item.id));
  const someSelected =
    items.some((item) => selectedIds.includes(item.id)) && !allSelected;

  const userById = new Map((users.data ?? []).map((user) => [user.id, user]));

  return (
    <PageContainer>
      <PageHeader
        title="Tasks"
        description="Every action item, whether extracted from a meeting or created by hand."
        actions={
          <Button
            variant="primary"
            size="md"
            onClick={() => setCreateOpen(true)}
          >
            <Plus />
            New task
          </Button>
        }
      />

      {/* Scope tabs */}
      <Tabs
        value={scope}
        onValueChange={(value) => setScope(value as Scope)}
        className="mb-4"
      >
        <TabsList>
          <TabsTrigger value="assigned">
            Assigned to me
            {counts.data ? <TabsCount>{counts.data.assigned}</TabsCount> : null}
          </TabsTrigger>
          <TabsTrigger value="created">
            Created by me
            {counts.data ? <TabsCount>{counts.data.created}</TabsCount> : null}
          </TabsTrigger>
          <TabsTrigger value="completed">
            Completed
            {counts.data ? (
              <TabsCount>{counts.data.completed}</TabsCount>
            ) : null}
          </TabsTrigger>
          <TabsTrigger value="all">
            All tasks
            {counts.data ? <TabsCount>{counts.data.all}</TabsCount> : null}
          </TabsTrigger>
        </TabsList>
      </Tabs>

      {/* Toolbar */}
      <div className="mb-3 flex flex-wrap items-center gap-2">
        <SearchInput
          value={search}
          onValueChange={setSearch}
          placeholder="Search tasks…"
          className="w-full sm:w-64"
        />

        <FilterMenu
          label="Status"
          options={STATUS_OPTIONS}
          selected={statuses}
          onChange={setStatuses}
        />
        <FilterMenu
          label="Priority"
          options={PRIORITY_OPTIONS}
          selected={priorities}
          onChange={setPriorities}
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
          aria-pressed={overdueOnly}
          onClick={() => setOverdueOnly((current) => !current)}
          className={cn(overdueOnly && "border-danger/40 bg-danger-subtle")}
        >
          Overdue
          {counts.data && counts.data.overdue > 0 ? (
            <span className="tabular">({counts.data.overdue})</span>
          ) : null}
        </Button>

        {activeFilterCount > 0 || search ? (
          <Button variant="ghost" size="sm" onClick={clearFilters}>
            <X />
            Clear
          </Button>
        ) : null}

        {/* View switcher, persisted to preferences */}
        <div className="ml-auto flex items-center gap-0.5 rounded-control border border-border p-0.5">
          {[
            { mode: "list" as const, icon: ListIcon, label: "List view" },
            { mode: "board" as const, icon: Columns3, label: "Board view" },
            {
              mode: "calendar" as const,
              icon: CalendarDays,
              label: "Calendar view",
            },
          ].map(({ mode, icon: Icon, label }) => (
            <Tooltip key={mode} label={label}>
              <button
                type="button"
                onClick={() => update({ tasksView: mode })}
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

      {/* Bulk actions — list view only, since only it has row selection */}
      {view === "list" && selectedIds.length > 0 ? (
        <div className="mb-3 flex flex-wrap items-center gap-2 rounded-surface border border-accent/40 bg-accent-subtle px-3 py-2">
          <p className="text-caption font-medium text-foreground tabular">
            {selectedIds.length} selected
          </p>

          <div className="ml-auto flex flex-wrap items-center gap-2">
            <Button
              variant="secondary"
              size="sm"
              loading={mutating}
              onClick={() => handleBulkStatus("done")}
            >
              <CheckCircle2 />
              Mark done
            </Button>

            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button variant="secondary" size="sm">
                  <UserCheck />
                  Assign
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent>
                <DropdownMenuLabel>Assign to</DropdownMenuLabel>
                <DropdownMenuItem onSelect={() => handleBulkAssign(null)}>
                  Unassigned
                </DropdownMenuItem>
                {(users.data ?? []).map((user) => (
                  <DropdownMenuItem
                    key={user.id}
                    onSelect={() => handleBulkAssign(user.id)}
                  >
                    {user.name}
                  </DropdownMenuItem>
                ))}
              </DropdownMenuContent>
            </DropdownMenu>

            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button variant="secondary" size="sm">
                  Priority
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent>
                <DropdownMenuLabel>Set priority</DropdownMenuLabel>
                {PRIORITY_OPTIONS.map((option) => (
                  <DropdownMenuItem
                    key={option.value}
                    onSelect={() => handleBulkPriority(option.value)}
                  >
                    {option.label}
                  </DropdownMenuItem>
                ))}
              </DropdownMenuContent>
            </DropdownMenu>

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
      {error ? (
        <ErrorState description={error.message} onRetry={refresh} />
      ) : loading && items.length === 0 ? (
        <div className="rounded-surface border border-border bg-surface">
          <SkeletonRows rows={6} columns={5} />
        </div>
      ) : items.length === 0 ? (
        <EmptyState
          icon={ListTodo}
          title={
            activeFilterCount > 0 || search
              ? "No tasks match your filters"
              : scope === "assigned"
                ? "Nothing assigned to you"
                : "No tasks yet"
          }
          description={
            activeFilterCount > 0 || search
              ? "Try loosening a filter or clearing your search."
              : "Action items extracted from meetings appear here, alongside anything you create."
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
                New task
              </Button>
            )
          }
          className="rounded-surface border border-border bg-surface"
        />
      ) : view === "board" ? (
        <TaskBoard
          tasks={items}
          users={users.data ?? []}
          loading={false}
          onOpenTask={setOpenTask}
          onMoveTask={handleMove}
        />
      ) : view === "calendar" ? (
        <TaskCalendar tasks={items} loading={false} onOpenTask={setOpenTask} />
      ) : (
        <TableWrapper>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead className="w-10">
                  <Checkbox
                    checked={
                      allSelected
                        ? true
                        : someSelected
                          ? "indeterminate"
                          : false
                    }
                    onCheckedChange={() =>
                      setSelectedIds((current) => {
                        const ids = items.map((item) => item.id);
                        return allSelected
                          ? current.filter((id) => !ids.includes(id))
                          : [...new Set([...current, ...ids])];
                      })
                    }
                    aria-label="Select all tasks on this page"
                  />
                </TableHead>
                <TableHead className="w-10">
                  <span className="sr-only">Done</span>
                </TableHead>
                <SortableHead
                  label="Task"
                  columnKey="title"
                  activeKey={sortBy}
                  direction={sortDir}
                  onSort={handleSort}
                />
                <TableHead>Assignee</TableHead>
                <SortableHead
                  label="Due"
                  columnKey="dueDate"
                  activeKey={sortBy}
                  direction={sortDir}
                  onSort={handleSort}
                />
                <SortableHead
                  label="Priority"
                  columnKey="priority"
                  activeKey={sortBy}
                  direction={sortDir}
                  onSort={handleSort}
                />
                <SortableHead
                  label="Status"
                  columnKey="status"
                  activeKey={sortBy}
                  direction={sortDir}
                  onSort={handleSort}
                />
              </TableRow>
            </TableHeader>

            <TableBody>
              {items.map((task) => {
                const selected = selectedIds.includes(task.id);
                const assignee = task.assigneeId
                  ? userById.get(task.assigneeId)
                  : undefined;
                const due = formatDueDate(task.dueDate);

                return (
                  <TableRow key={task.id} selected={selected}>
                    <TableCell>
                      <Checkbox
                        checked={selected}
                        onCheckedChange={(value) =>
                          setSelectedIds((current) =>
                            value === true
                              ? [...current, task.id]
                              : current.filter((id) => id !== task.id),
                          )
                        }
                        aria-label={`Select ${task.title}`}
                      />
                    </TableCell>

                    <TableCell>
                      <Checkbox
                        checked={task.status === "done"}
                        onCheckedChange={() => handleToggleDone(task)}
                        aria-label={`Mark "${task.title}" as ${
                          task.status === "done" ? "not done" : "done"
                        }`}
                      />
                    </TableCell>

                    <TableCell className="max-w-96">
                      <button
                        type="button"
                        onClick={() => setOpenTask(task)}
                        className="block w-full text-left"
                      >
                        <span
                          className={cn(
                            "block truncate font-medium hover:text-accent",
                            task.status === "done"
                              ? "text-subtle line-through"
                              : "text-foreground",
                          )}
                        >
                          {task.title}
                        </span>
                      </button>

                      {task.tags.length > 0 ? (
                        <div className="mt-1 flex flex-wrap gap-1">
                          {task.tags.slice(0, 2).map((tag) => (
                            <Badge key={tag} tone="outline" size="sm">
                              {tag}
                            </Badge>
                          ))}
                        </div>
                      ) : null}
                    </TableCell>

                    <TableCell>
                      {assignee ? (
                        <span className="flex items-center gap-1.5">
                          <Avatar name={assignee.name} size="xs" />
                          <span className="truncate text-caption text-muted">
                            {assignee.name}
                          </span>
                        </span>
                      ) : (
                        <span className="text-caption text-subtle">
                          Unassigned
                        </span>
                      )}
                    </TableCell>

                    <TableCell
                      className={cn(
                        "whitespace-nowrap text-caption",
                        due.overdue && task.status !== "done"
                          ? "font-medium text-danger"
                          : "text-muted",
                      )}
                    >
                      {due.label}
                    </TableCell>

                    <TableCell>
                      <PriorityBadge priority={task.priority} />
                    </TableCell>

                    <TableCell>
                      <TaskStatusBadge status={task.status} />
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        </TableWrapper>
      )}

      {view === "list" && paged.data && paged.data.total > 0 ? (
        <Pagination
          className="mt-4"
          page={paged.data.page}
          totalPages={paged.data.totalPages}
          total={paged.data.total}
          pageSize={paged.data.pageSize}
          onPageChange={setPage}
        />
      ) : null}

      <TaskDrawer
        task={openTask}
        users={users.data ?? []}
        meeting={provenance.data?.meeting ?? null}
        sourceSegment={provenance.data?.segment ?? null}
        open={openTask !== null}
        onOpenChange={(next) => {
          if (!next) setOpenTask(null);
        }}
        onChanged={refresh}
        onDeleted={refresh}
      />

      <NewTaskDialog
        open={createOpen}
        onOpenChange={setCreateOpen}
        creatorId={session.userId}
        users={users.data ?? []}
        onCreated={refresh}
      />

      <ConfirmDialog
        open={confirmDelete}
        onOpenChange={setConfirmDelete}
        title={`Delete ${selectedIds.length} ${
          selectedIds.length === 1 ? "task" : "tasks"
        }?`}
        description="These tasks will be permanently removed. This cannot be undone."
        confirmLabel="Delete"
        destructive
        loading={mutating}
        onConfirm={handleDelete}
      />
    </PageContainer>
  );
}
