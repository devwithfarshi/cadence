import { type LucideIcon, TrendingDown, TrendingUp } from "lucide-react";
import Link from "next/link";
import { Skeleton } from "@/components/ui/feedback";
import { cn } from "@/lib/utils/cn";
import { formatTrend } from "@/lib/utils/format";

export function StatCard({
  label,
  value,
  icon: Icon,
  trend,
  hint,
  href,
  loading,
}: {
  label: string;
  value: string | number;
  icon: LucideIcon;
  /**
   * Percentage change vs the previous period. Pass `null` (or omit) when the
   * baseline is too small to be meaningful — the card falls back to `hint`.
   */
  trend?: number | null;
  hint?: string;
  href?: string;
  loading?: boolean;
}) {
  const body = (
    <>
      <div className="flex items-start justify-between gap-2">
        <p className="text-label text-muted">{label}</p>
        <Icon className="size-4 shrink-0 text-subtle" aria-hidden />
      </div>

      {loading ? (
        <Skeleton className="mt-2 h-7 w-16" />
      ) : (
        // Proportional figures, deliberately. `tabular-nums` gives every digit
        // the width of a zero, which makes a value like 121 look loose at
        // display size — it belongs in aligned columns, not on a headline.
        <p className="mt-2 text-display text-foreground">{value}</p>
      )}

      {trend !== undefined && trend !== null && !loading ? (
        <p
          className={cn(
            "mt-1 flex items-center gap-1 text-caption tabular",
            trend > 0
              ? "text-success-foreground"
              : trend < 0
                ? "text-danger-foreground"
                : "text-muted",
          )}
        >
          {trend > 0 ? (
            <TrendingUp className="size-3.5" aria-hidden />
          ) : trend < 0 ? (
            <TrendingDown className="size-3.5" aria-hidden />
          ) : null}
          {formatTrend(trend)}
          <span className="text-subtle">vs last 30 days</span>
        </p>
      ) : hint && !loading ? (
        <p className="mt-1 text-caption text-subtle">{hint}</p>
      ) : null}
    </>
  );

  const className = cn(
    "rounded-surface border border-border bg-surface p-4 transition-colors",
    href && "hover:border-border-strong hover:bg-surface-raised/50",
  );

  return href ? (
    <Link href={href} className={className}>
      {body}
    </Link>
  ) : (
    <div className={className}>{body}</div>
  );
}
