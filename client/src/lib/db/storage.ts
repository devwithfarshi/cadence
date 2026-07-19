/**
 * localStorage-backed persistence engine.
 *
 * Everything here is synchronous and browser-only. The async, backend-shaped
 * surface lives in `src/lib/api` — UI code should talk to that, not to this.
 *
 * Three things this layer guarantees:
 *   1. SSR safety. Reads on the server return the fallback instead of throwing.
 *   2. Change notification. Writes emit an event so hooks can re-render, and
 *      `storage` events keep other tabs in sync.
 *   3. Versioning. A schema bump wipes stale demo data rather than crashing on
 *      a shape that no longer matches.
 */

const NAMESPACE = "ama";
// Bump whenever the seed shape or record structure changes, so existing demo
// workspaces are rebuilt rather than left on stale data.
const SCHEMA_VERSION = 2;
const VERSION_KEY = `${NAMESPACE}:schema_version`;

/** Event fired on same-tab writes; `storage` only fires for *other* tabs. */
const CHANGE_EVENT = "ama:storage-change";

export const isBrowser = typeof window !== "undefined";

export type StorageKey =
  | "users"
  | "meetings"
  | "transcripts"
  | "summaries"
  | "action_items"
  | "documents"
  | "knowledge"
  | "comments"
  | "notifications"
  | "activity"
  | "integrations"
  | "conversations"
  | "api_keys"
  | "invitations"
  | "preferences"
  | "session";

function fullKey(key: StorageKey): string {
  return `${NAMESPACE}:${key}`;
}

/* -------------------------------------------------------------------------- */
/* Core read / write                                                          */
/* -------------------------------------------------------------------------- */

export function read<T>(key: StorageKey, fallback: T): T {
  if (!isBrowser) return fallback;
  try {
    const raw = window.localStorage.getItem(fullKey(key));
    if (raw === null) return fallback;
    return JSON.parse(raw) as T;
  } catch {
    // Corrupt or unparseable entry — fall back rather than break the app.
    return fallback;
  }
}

export function write<T>(key: StorageKey, value: T): void {
  if (!isBrowser) return;
  try {
    window.localStorage.setItem(fullKey(key), JSON.stringify(value));
    notify(key);
  } catch (error) {
    // Most likely a quota error. Surface it in dev; never crash the UI.
    if (process.env.NODE_ENV !== "production") {
      console.error(`[storage] failed to write "${key}"`, error);
    }
  }
}

export function remove(key: StorageKey): void {
  if (!isBrowser) return;
  window.localStorage.removeItem(fullKey(key));
  notify(key);
}

/* -------------------------------------------------------------------------- */
/* Change notification                                                        */
/* -------------------------------------------------------------------------- */

function notify(key: StorageKey): void {
  window.dispatchEvent(new CustomEvent(CHANGE_EVENT, { detail: { key } }));
}

/**
 * Subscribe to changes for a single collection. Returns an unsubscribe fn.
 * Listens to both same-tab writes and cross-tab `storage` events.
 */
export function subscribe(key: StorageKey, onChange: () => void): () => void {
  if (!isBrowser) return () => {};

  const handleLocal = (event: Event) => {
    const detail = (event as CustomEvent<{ key: StorageKey }>).detail;
    if (detail?.key === key) onChange();
  };

  const handleCrossTab = (event: StorageEvent) => {
    if (event.key === fullKey(key)) onChange();
  };

  window.addEventListener(CHANGE_EVENT, handleLocal);
  window.addEventListener("storage", handleCrossTab);

  return () => {
    window.removeEventListener(CHANGE_EVENT, handleLocal);
    window.removeEventListener("storage", handleCrossTab);
  };
}

/* -------------------------------------------------------------------------- */
/* Schema versioning                                                          */
/* -------------------------------------------------------------------------- */

/**
 * Returns true when the store is empty or was written by an older schema, in
 * which case the caller should re-seed. Stale data is cleared here so seeding
 * always starts from a clean slate.
 */
export function needsSeed(): boolean {
  if (!isBrowser) return false;

  const stored = window.localStorage.getItem(VERSION_KEY);
  if (stored === String(SCHEMA_VERSION)) {
    // Same schema, but the payload may still have been cleared by the user.
    return window.localStorage.getItem(fullKey("meetings")) === null;
  }

  if (stored !== null) clearAll({ keepSession: false });
  return true;
}

export function markSeeded(): void {
  if (!isBrowser) return;
  window.localStorage.setItem(VERSION_KEY, String(SCHEMA_VERSION));
}

/**
 * Wipe application data.
 *
 * `keepSession` is what logout uses: the spec calls for clearing the session
 * while preserving the user's data, so signing back in restores their
 * workspace exactly as they left it.
 */
export function clearAll(options: { keepSession: boolean }): void {
  if (!isBrowser) return;

  const keys = Object.keys(window.localStorage).filter((k) =>
    k.startsWith(`${NAMESPACE}:`),
  );

  for (const k of keys) {
    if (options.keepSession && k === fullKey("session")) continue;
    window.localStorage.removeItem(k);
  }
}

/* -------------------------------------------------------------------------- */
/* Typed collection helper                                                    */
/* -------------------------------------------------------------------------- */

export interface Identifiable {
  id: string;
}

/**
 * A typed view over one localStorage array. Reads are cheap enough to do on
 * every call — these collections are demo-sized, and always re-reading avoids
 * an entire class of cache-invalidation bug.
 */
export function collection<T extends Identifiable>(key: StorageKey) {
  return {
    key,

    all(): T[] {
      return read<T[]>(key, []);
    },

    find(id: string): T | undefined {
      return read<T[]>(key, []).find((item) => item.id === id);
    },

    replaceAll(items: T[]): void {
      write(key, items);
    },

    insert(item: T): T {
      const items = read<T[]>(key, []);
      write(key, [item, ...items]);
      return item;
    },

    /** Shallow-merges `patch` into the matching record. No-op if absent. */
    update(id: string, patch: Partial<T>): T | undefined {
      const items = read<T[]>(key, []);
      const index = items.findIndex((item) => item.id === id);
      if (index === -1) return undefined;

      const next = { ...items[index], ...patch } as T;
      items[index] = next;
      write(key, items);
      return next;
    },

    remove(id: string): boolean {
      const items = read<T[]>(key, []);
      const next = items.filter((item) => item.id !== id);
      if (next.length === items.length) return false;
      write(key, next);
      return true;
    },

    removeMany(ids: string[]): number {
      const idSet = new Set(ids);
      const items = read<T[]>(key, []);
      const next = items.filter((item) => !idSet.has(item.id));
      const removed = items.length - next.length;
      if (removed > 0) write(key, next);
      return removed;
    },
  };
}
