/**
 * Mock transport.
 *
 * Every service call goes through `request`, which simulates network latency
 * and returns a Promise. Components therefore already handle loading and error
 * states correctly, and replacing this file with `fetch` calls against a real
 * API would not require touching a single component.
 */

import { seedIfEmpty } from "@/lib/db/seed";
import { isBrowser } from "@/lib/db/storage";
import type { ListQuery, Paginated, SortDirection } from "@/types/domain";

/** Latency range, in ms. Enough to make loading states real without dragging. */
const MIN_LATENCY = 90;
const MAX_LATENCY = 260;

export class ApiError extends Error {
  constructor(
    message: string,
    readonly status: number = 400,
  ) {
    super(message);
    this.name = "ApiError";
  }
}

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

/**
 * Runs `operation` against localStorage after a simulated round trip.
 *
 * The store is seeded lazily here rather than at module load, because module
 * evaluation can happen during SSR where localStorage does not exist.
 */
export async function request<T>(operation: () => T): Promise<T> {
  if (!isBrowser) {
    throw new ApiError("Data is only available in the browser", 503);
  }

  seedIfEmpty();
  await delay(MIN_LATENCY + Math.random() * (MAX_LATENCY - MIN_LATENCY));

  return operation();
}

/* -------------------------------------------------------------------------- */
/* Query helpers shared by every list endpoint                                */
/* -------------------------------------------------------------------------- */

/**
 * Case-insensitive substring match across the given fields.
 * An empty or whitespace-only term matches everything.
 */
export function matchesSearch<T>(
  item: T,
  term: string | undefined,
  fields: (item: T) => (string | null | undefined)[],
): boolean {
  const needle = term?.trim().toLowerCase();
  if (!needle) return true;

  return fields(item).some((value) =>
    (value ?? "").toLowerCase().includes(needle),
  );
}

/**
 * Stable sort by an extracted comparable value.
 *
 * Strings compare with `localeCompare` so accented names order correctly;
 * everything else compares numerically. Nulls always sort last regardless of
 * direction, which is what users expect from an empty due date.
 */
export function sortBy<T>(
  items: T[],
  extract: (item: T) => string | number | null | undefined,
  direction: SortDirection = "asc",
): T[] {
  const sign = direction === "asc" ? 1 : -1;

  return [...items].sort((a, b) => {
    const left = extract(a);
    const right = extract(b);

    const leftEmpty = left === null || left === undefined;
    const rightEmpty = right === null || right === undefined;
    if (leftEmpty && rightEmpty) return 0;
    if (leftEmpty) return 1;
    if (rightEmpty) return -1;

    if (typeof left === "string" && typeof right === "string") {
      return left.localeCompare(right) * sign;
    }
    return (Number(left) - Number(right)) * sign;
  });
}

export const DEFAULT_PAGE_SIZE = 10;

/** Slices a fully filtered and sorted list into a page envelope. */
export function paginate<T>(items: T[], query: ListQuery): Paginated<T> {
  const pageSize = Math.max(1, query.pageSize ?? DEFAULT_PAGE_SIZE);
  const totalPages = Math.max(1, Math.ceil(items.length / pageSize));
  // Clamp so a stale page number from a filter change can't render an empty page.
  const page = Math.min(Math.max(1, query.page ?? 1), totalPages);
  const start = (page - 1) * pageSize;

  return {
    items: items.slice(start, start + pageSize),
    total: items.length,
    page,
    pageSize,
    totalPages,
  };
}

/** ISO timestamp for "now" — centralised so records stay consistent. */
export function now(): string {
  return new Date().toISOString();
}

/** Collision-resistant enough for a demo, readable in the debugger. */
export function generateId(prefix: string): string {
  return `${prefix}_${Math.random().toString(36).slice(2, 10)}`;
}
