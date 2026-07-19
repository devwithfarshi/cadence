"use client";

import { PanelLeftClose, PanelLeftOpen, Plus } from "lucide-react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { Logo, LogoMark } from "@/components/brand/logo";
import { usePreferences } from "@/components/providers/preferences-provider";
import { Button } from "@/components/ui/button";
import { Tooltip } from "@/components/ui/tooltip";
import {
  isActiveRoute,
  NAV_SECTIONS,
  type NavItem,
  SETTINGS_ITEM,
} from "@/lib/navigation";
import { cn } from "@/lib/utils/cn";

function NavLink({
  item,
  collapsed,
  active,
  label,
}: {
  item: NavItem;
  collapsed: boolean;
  active: boolean;
  /** Already translated by the caller, which owns the preferences context. */
  label: string;
}) {
  const link = (
    <Link
      href={item.href}
      aria-current={active ? "page" : undefined}
      className={cn(
        "group relative flex items-center gap-2.5 rounded-control px-2 py-1.5 text-body transition-colors",
        active
          ? "bg-accent-subtle text-accent-subtle-foreground font-medium"
          : "text-muted hover:bg-surface-raised hover:text-foreground",
        collapsed && "justify-center px-0",
      )}
    >
      <item.icon
        className={cn(
          "size-4 shrink-0",
          active ? "text-accent" : "text-subtle group-hover:text-foreground",
        )}
        aria-hidden
      />
      {!collapsed ? (
        <>
          <span className="truncate">{label}</span>
          {item.upcoming ? (
            <span
              className="ml-auto text-overline uppercase text-subtle"
              title="Not built yet"
            >
              Soon
            </span>
          ) : null}
        </>
      ) : null}
    </Link>
  );

  // When collapsed the label is gone, so the tooltip carries the name instead.
  return collapsed ? (
    <Tooltip label={label} side="right">
      {link}
    </Tooltip>
  ) : (
    link
  );
}

export function Sidebar() {
  const pathname = usePathname();
  const { preferences, update, t } = usePreferences();
  const collapsed = preferences.sidebarCollapsed;

  return (
    <aside
      className={cn(
        "hidden shrink-0 flex-col border-r border-border bg-surface transition-[width] duration-200 md:flex",
        collapsed ? "w-14" : "w-60",
      )}
    >
      {/* Brand */}
      <div
        className={cn(
          "flex h-14 shrink-0 items-center border-b border-border",
          collapsed ? "justify-center px-0" : "px-3",
        )}
      >
        <Link href="/dashboard" className="rounded-control">
          {collapsed ? <LogoMark className="size-6 text-accent" /> : <Logo />}
        </Link>
      </div>

      {/* Primary action */}
      <div className={cn("shrink-0 py-3", collapsed ? "px-2" : "px-3")}>
        {collapsed ? (
          <Tooltip label="New meeting" side="right">
            <Button
              variant="primary"
              size="icon"
              className="w-full"
              aria-label="New meeting"
              asChild
            >
              <Link href="/meetings?new=1">
                <Plus />
              </Link>
            </Button>
          </Tooltip>
        ) : (
          <Button variant="primary" size="md" className="w-full" asChild>
            <Link href="/meetings?new=1">
              <Plus />
              {t("action.newMeeting")}
            </Link>
          </Button>
        )}
      </div>

      {/* Sections */}
      <nav
        aria-label="Main"
        className={cn(
          "flex-1 space-y-5 overflow-y-auto scrollbar-thin pb-4",
          collapsed ? "px-2" : "px-3",
        )}
      >
        {NAV_SECTIONS.map((section) => (
          <div key={section.label} className="space-y-1">
            {!collapsed ? (
              <p className="px-2 pb-1 text-overline uppercase text-subtle">
                {t(section.labelKey)}
              </p>
            ) : (
              <div className="mx-auto my-2 h-px w-6 bg-border" aria-hidden />
            )}

            {section.items.map((item) => (
              <NavLink
                key={item.href}
                item={item}
                label={t(item.labelKey)}
                collapsed={collapsed}
                active={isActiveRoute(pathname, item.href)}
              />
            ))}
          </div>
        ))}
      </nav>

      {/* Footer */}
      <div
        className={cn(
          "shrink-0 space-y-1 border-t border-border py-3",
          collapsed ? "px-2" : "px-3",
        )}
      >
        <NavLink
          item={SETTINGS_ITEM}
          label={t(SETTINGS_ITEM.labelKey)}
          collapsed={collapsed}
          active={isActiveRoute(pathname, SETTINGS_ITEM.href)}
        />

        <button
          type="button"
          onClick={() => update({ sidebarCollapsed: !collapsed })}
          aria-label={collapsed ? "Expand sidebar" : "Collapse sidebar"}
          aria-pressed={collapsed}
          className={cn(
            "flex w-full items-center gap-2.5 rounded-control px-2 py-1.5 text-body text-muted transition-colors hover:bg-surface-raised hover:text-foreground",
            collapsed && "justify-center px-0",
          )}
        >
          {collapsed ? (
            <PanelLeftOpen className="size-4 shrink-0" aria-hidden />
          ) : (
            <>
              <PanelLeftClose className="size-4 shrink-0" aria-hidden />
              <span>{t("action.collapse")}</span>
            </>
          )}
        </button>
      </div>
    </aside>
  );
}
