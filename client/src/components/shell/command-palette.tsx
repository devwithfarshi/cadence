"use client";

import { Command } from "cmdk";
import {
  CheckSquare,
  Clock,
  FileText,
  History,
  Library,
  Moon,
  PanelLeft,
  Plus,
  Search,
  Sun,
  User,
  Video,
} from "lucide-react";
import { useRouter } from "next/navigation";
import { Dialog as DialogPrimitive } from "radix-ui";
import { useCallback, useEffect, useState } from "react";
import { usePreferences } from "@/components/providers/preferences-provider";
import { useToast } from "@/components/ui/toast";
import {
  clearRecentSearches,
  getRecentMeetings,
  getStoredPreferences,
  globalSearch,
  recordRecentSearch,
  type SearchResult,
} from "@/lib/api/workspace";
import { useDebounced } from "@/lib/hooks/use-async";
import { ALL_NAV_ITEMS } from "@/lib/navigation";
import type { Meeting } from "@/types/domain";

const RESULT_ICONS = {
  meeting: Video,
  task: CheckSquare,
  document: FileText,
  knowledge: Library,
  person: User,
} as const;

export function CommandPalette({
  open,
  onOpenChange,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const router = useRouter();
  const { preferences, update, resolvedTheme } = usePreferences();
  const { toast } = useToast();

  const [query, setQuery] = useState("");
  const [results, setResults] = useState<SearchResult[]>([]);
  const debouncedQuery = useDebounced(query, 180);

  // Recency rails, shown only while the search box is empty.
  const [recentSearches, setRecentSearches] = useState<string[]>([]);
  const [recentMeetings, setRecentMeetings] = useState<Meeting[]>([]);

  // Re-read on each open: both lists change as the user works elsewhere.
  useEffect(() => {
    if (!open) return;
    setRecentSearches(getStoredPreferences().recentSearches);
    getRecentMeetings(4)
      .then(setRecentMeetings)
      .catch(() => setRecentMeetings([]));
  }, [open]);

  // Search records live in localStorage, so this queries on every keystroke
  // pause rather than filtering a preloaded list.
  useEffect(() => {
    if (!open) return;

    let cancelled = false;
    if (!debouncedQuery.trim()) {
      setResults([]);
      return;
    }

    globalSearch(debouncedQuery, 6)
      .then((found) => {
        if (!cancelled) setResults(found);
      })
      .catch(() => {
        if (!cancelled) setResults([]);
      });

    return () => {
      cancelled = true;
    };
  }, [debouncedQuery, open]);

  // Reset between openings so the palette never reopens mid-search.
  useEffect(() => {
    if (!open) {
      setQuery("");
      setResults([]);
    }
  }, [open]);

  const run = useCallback(
    (action: () => void) => {
      onOpenChange(false);
      action();
    },
    [onOpenChange],
  );

  /**
   * Opens a result and remembers the query that found it.
   *
   * Recorded on selection rather than on keystroke, so the history holds
   * searches that actually led somewhere instead of every partial word typed.
   */
  const openResult = useCallback(
    (href: string) => {
      recordRecentSearch(query);
      run(() => router.push(href));
    },
    [query, run, router],
  );

  return (
    <DialogPrimitive.Root open={open} onOpenChange={onOpenChange}>
      <DialogPrimitive.Portal>
        <DialogPrimitive.Overlay className="anim-overlay fixed inset-0 z-50 bg-overlay" />
        <DialogPrimitive.Content
          className="anim-dialog fixed left-1/2 top-[15%] z-50 w-[calc(100vw-2rem)] max-w-xl -translate-x-1/2 overflow-hidden rounded-surface border border-border bg-surface shadow-lg focus:outline-none"
          aria-label="Command palette"
        >
          <DialogPrimitive.Title className="sr-only">
            Search and commands
          </DialogPrimitive.Title>
          <DialogPrimitive.Description className="sr-only">
            Search meetings, documents and people, or run a command.
          </DialogPrimitive.Description>

          <Command
            // Results are already ranked by the search service, so cmdk's own
            // fuzzy scoring would only fight it.
            shouldFilter={false}
            loop
            className="flex flex-col"
          >
            <div className="flex items-center gap-2.5 border-b border-border px-3.5">
              <Search className="size-4 shrink-0 text-subtle" aria-hidden />
              <Command.Input
                value={query}
                onValueChange={setQuery}
                placeholder="Search meetings, tasks, documents…"
                className="h-12 w-full bg-transparent text-body text-foreground placeholder:text-subtle focus:outline-none"
              />
              <kbd className="hidden shrink-0 rounded border border-border px-1.5 py-0.5 text-overline text-subtle sm:block">
                ESC
              </kbd>
            </div>

            <Command.List className="max-h-80 overflow-y-auto scrollbar-thin p-2">
              <Command.Empty className="px-2 py-8 text-center text-caption text-muted">
                {query.trim()
                  ? `No results for “${query}”`
                  : "Type to search, or pick a command below."}
              </Command.Empty>

              {/* Recency rails: only useful before the user starts typing. */}
              {!query.trim() && recentMeetings.length > 0 ? (
                <Command.Group
                  heading="Recently opened"
                  className="[&_[cmdk-group-heading]]:px-2 [&_[cmdk-group-heading]]:pb-1 [&_[cmdk-group-heading]]:text-overline [&_[cmdk-group-heading]]:uppercase [&_[cmdk-group-heading]]:text-subtle"
                >
                  {recentMeetings.map((meeting) => (
                    <Command.Item
                      key={`recent-${meeting.id}`}
                      value={`recent-${meeting.id}`}
                      onSelect={() =>
                        run(() => router.push(`/meetings/${meeting.id}`))
                      }
                      className="flex cursor-pointer items-center gap-2.5 rounded-control px-2 py-2 text-body text-foreground data-[selected=true]:bg-surface-raised"
                    >
                      <Clock
                        className="size-4 shrink-0 text-subtle"
                        aria-hidden
                      />
                      <span className="min-w-0 flex-1 truncate">
                        {meeting.title}
                      </span>
                    </Command.Item>
                  ))}
                </Command.Group>
              ) : null}

              {!query.trim() && recentSearches.length > 0 ? (
                <Command.Group
                  heading="Recent searches"
                  className="[&_[cmdk-group-heading]]:px-2 [&_[cmdk-group-heading]]:pb-1 [&_[cmdk-group-heading]]:pt-2 [&_[cmdk-group-heading]]:text-overline [&_[cmdk-group-heading]]:uppercase [&_[cmdk-group-heading]]:text-subtle"
                >
                  {recentSearches.map((term) => (
                    <Command.Item
                      key={`search-${term}`}
                      value={`search-${term}`}
                      // Re-runs the search rather than navigating, which is what
                      // a search history entry should do.
                      onSelect={() => setQuery(term)}
                      className="flex cursor-pointer items-center gap-2.5 rounded-control px-2 py-2 text-body text-foreground data-[selected=true]:bg-surface-raised"
                    >
                      <History
                        className="size-4 shrink-0 text-subtle"
                        aria-hidden
                      />
                      <span className="min-w-0 flex-1 truncate">{term}</span>
                    </Command.Item>
                  ))}

                  <Command.Item
                    value="clear-recent-searches"
                    onSelect={() => {
                      clearRecentSearches();
                      setRecentSearches([]);
                    }}
                    className="flex cursor-pointer items-center gap-2.5 rounded-control px-2 py-2 text-caption text-muted data-[selected=true]:bg-surface-raised"
                  >
                    <span className="size-4 shrink-0" aria-hidden />
                    Clear search history
                  </Command.Item>
                </Command.Group>
              ) : null}

              {results.length > 0 ? (
                <Command.Group
                  heading="Results"
                  className="[&_[cmdk-group-heading]]:px-2 [&_[cmdk-group-heading]]:pb-1 [&_[cmdk-group-heading]]:text-overline [&_[cmdk-group-heading]]:uppercase [&_[cmdk-group-heading]]:text-subtle"
                >
                  {results.map((result) => {
                    const Icon = RESULT_ICONS[result.kind];
                    return (
                      <Command.Item
                        key={`${result.kind}-${result.id}`}
                        value={`${result.kind}-${result.id}`}
                        onSelect={() => openResult(result.href)}
                        className="flex cursor-pointer items-center gap-2.5 rounded-control px-2 py-2 text-body text-foreground data-[selected=true]:bg-surface-raised"
                      >
                        <Icon
                          className="size-4 shrink-0 text-subtle"
                          aria-hidden
                        />
                        <span className="min-w-0 flex-1 truncate">
                          {result.title}
                        </span>
                        <span className="shrink-0 text-label text-subtle">
                          {result.subtitle}
                        </span>
                      </Command.Item>
                    );
                  })}
                </Command.Group>
              ) : null}

              <Command.Group
                heading="Go to"
                className="[&_[cmdk-group-heading]]:px-2 [&_[cmdk-group-heading]]:pb-1 [&_[cmdk-group-heading]]:pt-2 [&_[cmdk-group-heading]]:text-overline [&_[cmdk-group-heading]]:uppercase [&_[cmdk-group-heading]]:text-subtle"
              >
                {ALL_NAV_ITEMS.filter((item) =>
                  item.label.toLowerCase().includes(query.trim().toLowerCase()),
                ).map((item) => (
                  <Command.Item
                    key={item.href}
                    value={`nav-${item.href}`}
                    onSelect={() => run(() => router.push(item.href))}
                    className="flex cursor-pointer items-center gap-2.5 rounded-control px-2 py-2 text-body text-foreground data-[selected=true]:bg-surface-raised"
                  >
                    <item.icon
                      className="size-4 shrink-0 text-subtle"
                      aria-hidden
                    />
                    <span className="flex-1 truncate">{item.label}</span>
                    {item.upcoming ? (
                      <span className="text-overline uppercase text-subtle">
                        Soon
                      </span>
                    ) : null}
                  </Command.Item>
                ))}
              </Command.Group>

              <Command.Group
                heading="Actions"
                className="[&_[cmdk-group-heading]]:px-2 [&_[cmdk-group-heading]]:pb-1 [&_[cmdk-group-heading]]:pt-2 [&_[cmdk-group-heading]]:text-overline [&_[cmdk-group-heading]]:uppercase [&_[cmdk-group-heading]]:text-subtle"
              >
                <Command.Item
                  value="action-new-meeting"
                  onSelect={() => run(() => router.push("/meetings?new=1"))}
                  className="flex cursor-pointer items-center gap-2.5 rounded-control px-2 py-2 text-body text-foreground data-[selected=true]:bg-surface-raised"
                >
                  <Plus className="size-4 shrink-0 text-subtle" aria-hidden />
                  Schedule a new meeting
                </Command.Item>

                <Command.Item
                  value="action-toggle-theme"
                  onSelect={() =>
                    run(() => {
                      const next = resolvedTheme === "dark" ? "light" : "dark";
                      update({ theme: next });
                      toast({
                        tone: "info",
                        title: `Switched to ${next} theme`,
                      });
                    })
                  }
                  className="flex cursor-pointer items-center gap-2.5 rounded-control px-2 py-2 text-body text-foreground data-[selected=true]:bg-surface-raised"
                >
                  {resolvedTheme === "dark" ? (
                    <Sun className="size-4 shrink-0 text-subtle" aria-hidden />
                  ) : (
                    <Moon className="size-4 shrink-0 text-subtle" aria-hidden />
                  )}
                  Switch to {resolvedTheme === "dark" ? "light" : "dark"} theme
                </Command.Item>

                <Command.Item
                  value="action-toggle-sidebar"
                  onSelect={() =>
                    run(() =>
                      update({
                        sidebarCollapsed: !preferences.sidebarCollapsed,
                      }),
                    )
                  }
                  className="flex cursor-pointer items-center gap-2.5 rounded-control px-2 py-2 text-body text-foreground data-[selected=true]:bg-surface-raised"
                >
                  <PanelLeft
                    className="size-4 shrink-0 text-subtle"
                    aria-hidden
                  />
                  {preferences.sidebarCollapsed ? "Expand" : "Collapse"} sidebar
                </Command.Item>
              </Command.Group>
            </Command.List>
          </Command>
        </DialogPrimitive.Content>
      </DialogPrimitive.Portal>
    </DialogPrimitive.Root>
  );
}

/** Binds ⌘K / Ctrl+K and returns the palette's open state. */
export function useCommandPalette() {
  const [open, setOpen] = useState(false);

  useEffect(() => {
    const handler = (event: KeyboardEvent) => {
      if (event.key === "k" && (event.metaKey || event.ctrlKey)) {
        event.preventDefault();
        setOpen((current) => !current);
      }
    };

    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, []);

  return { open, setOpen };
}
