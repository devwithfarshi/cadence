"use client";

import { MoreHorizontal, Star, Users } from "lucide-react";
import Link from "next/link";
import { AvatarGroup } from "@/components/ui/avatar";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { cn } from "@/lib/utils/cn";
import { formatDateTime, formatDuration } from "@/lib/utils/format";
import type { Meeting } from "@/types/domain";
import { MeetingStatusBadge, PlatformLabel, SummaryBadge } from "./status";

export function MeetingGridCard({
  meeting,
  selected,
  onSelectChange,
  onToggleFavorite,
  onOpenMenu,
}: {
  meeting: Meeting;
  selected: boolean;
  onSelectChange: (selected: boolean) => void;
  onToggleFavorite: () => void;
  onOpenMenu: () => void;
}) {
  return (
    <div
      className={cn(
        "group relative flex flex-col rounded-surface border bg-surface transition-colors",
        selected
          ? "border-accent/50 bg-accent-subtle/30"
          : "border-border hover:border-border-strong",
      )}
    >
      {/* Selection and row actions */}
      <div className="flex items-start gap-2 px-4 pt-3.5">
        <Checkbox
          checked={selected}
          onCheckedChange={(value) => onSelectChange(value === true)}
          aria-label={`Select ${meeting.title}`}
          className={cn(
            "mt-0.5 transition-opacity",
            !selected &&
              "opacity-0 focus-visible:opacity-100 group-hover:opacity-100",
          )}
        />

        <Link href={`/meetings/${meeting.id}`} className="min-w-0 flex-1">
          <p className="line-clamp-2 text-body font-medium text-foreground">
            {meeting.title}
          </p>
          <p className="mt-1 text-caption text-muted">
            {formatDateTime(meeting.startsAt)}
            {meeting.durationSeconds > 0 ? (
              <>
                <span aria-hidden> · </span>
                <span className="tabular">
                  {formatDuration(meeting.durationSeconds)}
                </span>
              </>
            ) : null}
          </p>
        </Link>

        <div className="flex shrink-0 items-center">
          <Button
            variant="ghost"
            size="icon-sm"
            onClick={onToggleFavorite}
            aria-label={
              meeting.isFavorite
                ? "Remove from favourites"
                : "Add to favourites"
            }
            aria-pressed={meeting.isFavorite}
          >
            <Star
              className={cn(
                meeting.isFavorite
                  ? "fill-warning text-warning"
                  : "text-subtle",
              )}
            />
          </Button>
          <Button
            variant="ghost"
            size="icon-sm"
            onClick={onOpenMenu}
            aria-label={`Actions for ${meeting.title}`}
          >
            <MoreHorizontal />
          </Button>
        </div>
      </div>

      <div className="flex-1 px-4 py-3">
        {meeting.description ? (
          <p className="line-clamp-2 text-caption text-muted">
            {meeting.description}
          </p>
        ) : null}

        {meeting.tags.length > 0 ? (
          <div className="mt-2.5 flex flex-wrap gap-1">
            {meeting.tags.slice(0, 3).map((tag) => (
              <Badge key={tag} tone="outline" size="sm">
                {tag}
              </Badge>
            ))}
            {meeting.tags.length > 3 ? (
              <Badge tone="outline" size="sm">
                +{meeting.tags.length - 3}
              </Badge>
            ) : null}
          </div>
        ) : null}
      </div>

      <div className="flex items-center justify-between gap-2 border-t border-border px-4 py-2.5">
        <div className="flex items-center gap-2">
          <AvatarGroup people={meeting.participants} max={3} size="xs" />
          <span className="flex items-center gap-1 text-label text-subtle tabular">
            <Users className="size-3" aria-hidden />
            {meeting.participants.length}
          </span>
        </div>

        <div className="flex items-center gap-1.5">
          <PlatformLabel platform={meeting.platform} />
          {meeting.status === "completed" ? (
            <SummaryBadge status={meeting.summaryStatus} />
          ) : (
            <MeetingStatusBadge status={meeting.status} />
          )}
        </div>
      </div>
    </div>
  );
}
