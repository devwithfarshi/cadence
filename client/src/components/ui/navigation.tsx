"use client";

import {
  ChevronLeft,
  ChevronRight,
  ChevronsLeft,
  ChevronsRight,
} from "lucide-react";
import Link from "next/link";
import { Fragment } from "react";
import { cn } from "@/lib/utils/cn";
import { Button } from "./button";

/* -------------------------------------------------------------------------- */
/* Breadcrumbs                                                                */
/* -------------------------------------------------------------------------- */

export interface Crumb {
  label: string;
  href?: string;
}

export function Breadcrumbs({
  items,
  className,
}: {
  items: Crumb[];
  className?: string;
}) {
  return (
    <nav aria-label="Breadcrumb" className={className}>
      <ol className="flex items-center gap-1 text-caption">
        {items.map((item, index) => {
          const isLast = index === items.length - 1;

          return (
            <Fragment key={`${item.label}-${index}`}>
              <li className="flex items-center">
                {item.href && !isLast ? (
                  <Link
                    href={item.href}
                    className="rounded-control text-muted transition-colors hover:text-foreground"
                  >
                    {item.label}
                  </Link>
                ) : (
                  <span
                    aria-current={isLast ? "page" : undefined}
                    className={cn(
                      isLast ? "text-foreground font-medium" : "text-muted",
                      // Long meeting titles shouldn't push the bar off-screen.
                      "max-w-64 truncate",
                    )}
                  >
                    {item.label}
                  </span>
                )}
              </li>
              {!isLast ? (
                <li aria-hidden className="text-subtle">
                  <ChevronRight className="size-3.5" />
                </li>
              ) : null}
            </Fragment>
          );
        })}
      </ol>
    </nav>
  );
}

/* -------------------------------------------------------------------------- */
/* Pagination                                                                 */
/* -------------------------------------------------------------------------- */

/**
 * Builds a compact page list with ellipses, e.g. 1 … 4 5 6 … 20.
 * Always shows the first and last page so the range is legible.
 */
function pageWindow(current: number, total: number): (number | "gap")[] {
  if (total <= 7) {
    return Array.from({ length: total }, (_, i) => i + 1);
  }

  const pages: (number | "gap")[] = [1];
  const start = Math.max(2, current - 1);
  const end = Math.min(total - 1, current + 1);

  if (start > 2) pages.push("gap");
  for (let page = start; page <= end; page += 1) pages.push(page);
  if (end < total - 1) pages.push("gap");

  pages.push(total);
  return pages;
}

export function Pagination({
  page,
  totalPages,
  total,
  pageSize,
  onPageChange,
  className,
}: {
  page: number;
  totalPages: number;
  total: number;
  pageSize: number;
  onPageChange: (page: number) => void;
  className?: string;
}) {
  if (total === 0) return null;

  const from = (page - 1) * pageSize + 1;
  const to = Math.min(page * pageSize, total);

  return (
    <div
      className={cn(
        "flex flex-wrap items-center justify-between gap-3 px-1",
        className,
      )}
    >
      <p className="text-caption text-muted tabular">
        Showing <span className="text-foreground">{from}</span>–
        <span className="text-foreground">{to}</span> of{" "}
        <span className="text-foreground">{total}</span>
      </p>

      <nav aria-label="Pagination" className="flex items-center gap-1">
        <Button
          variant="ghost"
          size="icon-sm"
          aria-label="First page"
          disabled={page === 1}
          onClick={() => onPageChange(1)}
        >
          <ChevronsLeft />
        </Button>
        <Button
          variant="ghost"
          size="icon-sm"
          aria-label="Previous page"
          disabled={page === 1}
          onClick={() => onPageChange(page - 1)}
        >
          <ChevronLeft />
        </Button>

        {pageWindow(page, totalPages).map((entry, index) =>
          entry === "gap" ? (
            <span
              // biome-ignore lint/suspicious/noArrayIndexKey: gaps are positional
              key={`gap-${index}`}
              aria-hidden
              className="px-1 text-caption text-subtle"
            >
              …
            </span>
          ) : (
            <Button
              key={entry}
              variant={entry === page ? "primary" : "ghost"}
              size="icon-sm"
              aria-label={`Page ${entry}`}
              aria-current={entry === page ? "page" : undefined}
              onClick={() => onPageChange(entry)}
              className="tabular"
            >
              {entry}
            </Button>
          ),
        )}

        <Button
          variant="ghost"
          size="icon-sm"
          aria-label="Next page"
          disabled={page === totalPages}
          onClick={() => onPageChange(page + 1)}
        >
          <ChevronRight />
        </Button>
        <Button
          variant="ghost"
          size="icon-sm"
          aria-label="Last page"
          disabled={page === totalPages}
          onClick={() => onPageChange(totalPages)}
        >
          <ChevronsRight />
        </Button>
      </nav>
    </div>
  );
}
