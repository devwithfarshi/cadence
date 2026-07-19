"use client";

import { usePathname } from "next/navigation";
import {
  createContext,
  type ReactNode,
  useContext,
  useEffect,
  useMemo,
  useState,
} from "react";
import type { Crumb } from "@/components/ui/navigation";
import { ALL_NAV_ITEMS } from "@/lib/navigation";

interface BreadcrumbContextValue {
  crumbs: Crumb[];
  /** Pages call this to append detail-level crumbs, e.g. a meeting title. */
  setTrail: (trail: Crumb[] | null) => void;
}

const BreadcrumbContext = createContext<BreadcrumbContextValue | null>(null);

/**
 * Derives a trail from the URL: `/meetings/abc` → Meetings › abc.
 * Segments that match a known nav destination use its label.
 */
function deriveFromPath(pathname: string): Crumb[] {
  const segments = pathname.split("/").filter(Boolean);
  if (segments.length === 0) return [{ label: "Dashboard" }];

  const crumbs: Crumb[] = [];
  let href = "";

  for (const segment of segments) {
    href += `/${segment}`;
    const known = ALL_NAV_ITEMS.find((item) => item.href === href);
    crumbs.push({
      label: known?.label ?? segment,
      href,
    });
  }

  return crumbs;
}

export function BreadcrumbProvider({ children }: { children: ReactNode }) {
  const pathname = usePathname();
  const [trail, setTrail] = useState<Crumb[] | null>(null);

  // A page-supplied trail belongs to that page only, so navigating away drops it.
  // biome-ignore lint/correctness/useExhaustiveDependencies: pathname is the trigger, not a value read
  useEffect(() => {
    setTrail(null);
  }, [pathname]);

  const value = useMemo(
    () => ({ crumbs: trail ?? deriveFromPath(pathname), setTrail }),
    [trail, pathname],
  );

  return (
    <BreadcrumbContext.Provider value={value}>
      {children}
    </BreadcrumbContext.Provider>
  );
}

export function useBreadcrumbs(): BreadcrumbContextValue {
  const context = useContext(BreadcrumbContext);
  if (!context) {
    throw new Error(
      "useBreadcrumbs must be used inside a <BreadcrumbProvider>",
    );
  }
  return context;
}

/**
 * Declarative helper for pages with a dynamic trail.
 * Pass `null` while the data that names the page is still loading.
 */
export function useSetBreadcrumbs(trail: Crumb[] | null): void {
  const { setTrail } = useBreadcrumbs();

  // Serialised so an inline array literal doesn't re-fire the effect endlessly.
  const key = trail ? JSON.stringify(trail) : null;

  useEffect(() => {
    if (key) setTrail(JSON.parse(key) as Crumb[]);
  }, [key, setTrail]);
}
