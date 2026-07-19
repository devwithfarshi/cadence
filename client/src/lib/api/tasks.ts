/**
 * Action items / tasks service. Backs both the meeting detail page's task list
 * and the standalone Tasks pages.
 */

import { collection } from "@/lib/db/storage";
import type {
  ActionItem,
  ListQuery,
  Paginated,
  TaskPriority,
  TaskStatus,
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

const tasks = collection<ActionItem>("action_items");

/** Ordering for priority, which sorts by severity rather than alphabetically. */
const PRIORITY_RANK: Record<TaskPriority, number> = {
  low: 0,
  medium: 1,
  high: 2,
  urgent: 3,
};

export type TaskSortKey =
  | "dueDate"
  | "priority"
  | "title"
  | "createdAt"
  | "status";

export interface TaskQuery extends ListQuery<TaskSortKey> {
  status?: TaskStatus[];
  priority?: TaskPriority[];
  assigneeId?: string;
  creatorId?: string;
  meetingId?: string;
  tags?: string[];
  /** Only tasks with no assignee. Distinct from `assigneeId` being absent. */
  unassignedOnly?: boolean;
  /** Only tasks whose due date has passed and which are not done. */
  overdueOnly?: boolean;
  /** Restrict to tasks due within this window; tasks with no due date drop out. */
  dueFrom?: string;
  dueTo?: string;
}

function applyFilters(all: ActionItem[], query: TaskQuery): ActionItem[] {
  const nowMs = Date.now();

  return all.filter((task) => {
    if (query.status?.length && !query.status.includes(task.status))
      return false;
    if (query.priority?.length && !query.priority.includes(task.priority)) {
      return false;
    }
    if (query.assigneeId && task.assigneeId !== query.assigneeId) return false;
    if (query.creatorId && task.creatorId !== query.creatorId) return false;
    if (query.meetingId && task.meetingId !== query.meetingId) return false;
    if (query.unassignedOnly && task.assigneeId !== null) return false;

    if (query.tags?.length && !query.tags.some((t) => task.tags.includes(t))) {
      return false;
    }

    if (query.overdueOnly) {
      const overdue =
        task.status !== "done" &&
        task.dueDate !== null &&
        new Date(task.dueDate).getTime() < nowMs;
      if (!overdue) return false;
    }

    if (query.dueFrom || query.dueTo) {
      // An undated task can't fall inside a date window.
      if (task.dueDate === null) return false;

      const due = new Date(task.dueDate).getTime();
      if (query.dueFrom && due < new Date(query.dueFrom).getTime())
        return false;
      if (query.dueTo && due > new Date(query.dueTo).getTime()) return false;
    }

    return matchesSearch(task, query.search, (t) => [
      t.title,
      t.description,
      ...t.tags,
    ]);
  });
}

function applySort(items: ActionItem[], query: TaskQuery): ActionItem[] {
  const key = query.sortBy ?? "dueDate";
  const dir = query.sortDir ?? (key === "priority" ? "desc" : "asc");

  switch (key) {
    case "priority":
      return sortBy(items, (t) => PRIORITY_RANK[t.priority], dir);
    case "title":
      return sortBy(items, (t) => t.title, dir);
    case "status":
      return sortBy(items, (t) => t.status, dir);
    case "createdAt":
      return sortBy(items, (t) => new Date(t.createdAt).getTime(), dir);
    default:
      return sortBy(
        items,
        (t) => (t.dueDate ? new Date(t.dueDate).getTime() : null),
        dir,
      );
  }
}

export function listTasks(
  query: TaskQuery = {},
): Promise<Paginated<ActionItem>> {
  return request(() =>
    paginate(applySort(applyFilters(tasks.all(), query), query), query),
  );
}

export function queryTasks(query: TaskQuery = {}): Promise<ActionItem[]> {
  return request(() => applySort(applyFilters(tasks.all(), query), query));
}

export interface CreateTaskInput {
  title: string;
  description?: string;
  assigneeId?: string | null;
  creatorId: string;
  dueDate?: string | null;
  priority?: TaskPriority;
  meetingId?: string | null;
  sourceSegmentId?: string | null;
  tags?: string[];
}

export function createTask(input: CreateTaskInput): Promise<ActionItem> {
  return request(() => {
    if (!input.title.trim()) throw new ApiError("Title is required", 422);

    const timestamp = now();
    return tasks.insert({
      id: generateId("act"),
      title: input.title.trim(),
      description: input.description?.trim() ?? "",
      assigneeId: input.assigneeId ?? null,
      creatorId: input.creatorId,
      dueDate: input.dueDate ?? null,
      priority: input.priority ?? "medium",
      status: "todo",
      meetingId: input.meetingId ?? null,
      sourceSegmentId: input.sourceSegmentId ?? null,
      completedAt: null,
      tags: input.tags ?? [],
      createdAt: timestamp,
      updatedAt: timestamp,
    });
  });
}

export function updateTask(
  id: string,
  patch: Partial<ActionItem>,
): Promise<ActionItem> {
  return request(() => {
    // Keep completedAt consistent with status without callers having to.
    const derived: Partial<ActionItem> = { ...patch, updatedAt: now() };
    if (patch.status === "done") {
      derived.completedAt = patch.completedAt ?? now();
    } else if (patch.status !== undefined) {
      derived.completedAt = null;
    }

    const updated = tasks.update(id, derived);
    if (!updated) throw new ApiError("Task not found", 404);
    return updated;
  });
}

export function setTaskStatus(
  ids: string[],
  status: TaskStatus,
): Promise<number> {
  return request(() => {
    const completedAt = status === "done" ? now() : null;
    let changed = 0;

    for (const id of ids) {
      if (tasks.update(id, { status, completedAt, updatedAt: now() })) {
        changed += 1;
      }
    }
    return changed;
  });
}

export function deleteTask(id: string): Promise<void> {
  return request(() => {
    if (!tasks.remove(id)) throw new ApiError("Task not found", 404);
  });
}

export function deleteTasks(ids: string[]): Promise<number> {
  return request(() => tasks.removeMany(ids));
}

/** Bulk reassignment. `assigneeId` of null unassigns. */
export function assignTasks(
  ids: string[],
  assigneeId: string | null,
): Promise<number> {
  return request(() => {
    let changed = 0;
    for (const id of ids) {
      if (tasks.update(id, { assigneeId, updatedAt: now() })) changed += 1;
    }
    return changed;
  });
}

export function setTaskPriority(
  ids: string[],
  priority: TaskPriority,
): Promise<number> {
  return request(() => {
    let changed = 0;
    for (const id of ids) {
      if (tasks.update(id, { priority, updatedAt: now() })) changed += 1;
    }
    return changed;
  });
}

/**
 * Moves a task to a different Kanban column.
 *
 * Board position is derived from the sort order rather than stored, so this is
 * just a status change — but it is a named operation because the board calls it
 * for a specific reason, and a future backend would likely need ordering here.
 */
export function moveTaskToStatus(
  id: string,
  status: TaskStatus,
): Promise<ActionItem> {
  return updateTask(id, { status });
}

/** Distinct tags across all tasks, for populating filter menus. */
export function listTaskTags(): Promise<string[]> {
  return request(() => [...new Set(tasks.all().flatMap((t) => t.tags))].sort());
}

export interface TaskCounts {
  all: number;
  assigned: number;
  created: number;
  completed: number;
  overdue: number;
  byStatus: Record<TaskStatus, number>;
}

/**
 * Counts for the view tabs and board column headers.
 *
 * Computed in one pass so the tab badges cannot disagree with the list beneath
 * them — separate queries per tab would drift as data changes between calls.
 */
export function getTaskCounts(userId: string): Promise<TaskCounts> {
  return request(() => {
    const all = tasks.all();
    const nowMs = Date.now();

    const byStatus: Record<TaskStatus, number> = {
      todo: 0,
      in_progress: 0,
      blocked: 0,
      done: 0,
    };
    for (const task of all) byStatus[task.status] += 1;

    return {
      all: all.length,
      assigned: all.filter(
        (t) => t.assigneeId === userId && t.status !== "done",
      ).length,
      created: all.filter((t) => t.creatorId === userId).length,
      completed: byStatus.done,
      overdue: all.filter(
        (t) =>
          t.status !== "done" &&
          t.dueDate !== null &&
          new Date(t.dueDate).getTime() < nowMs,
      ).length,
      byStatus,
    };
  });
}
