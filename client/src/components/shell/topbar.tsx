"use client";

import {
  LogOut,
  Menu,
  Monitor,
  Moon,
  Search,
  Settings,
  Sun,
  UserRound,
} from "lucide-react";
import Link from "next/link";
import { useState } from "react";
import { useAuth } from "@/components/providers/auth-provider";
import { usePreferences } from "@/components/providers/preferences-provider";
import { Avatar } from "@/components/ui/avatar";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { Breadcrumbs, type Crumb } from "@/components/ui/navigation";
import { Tooltip } from "@/components/ui/tooltip";
import type { ThemeMode } from "@/types/domain";
import { NotificationCenter } from "./notification-center";

const THEME_OPTIONS: { value: ThemeMode; label: string; icon: typeof Sun }[] = [
  { value: "light", label: "Light", icon: Sun },
  { value: "dark", label: "Dark", icon: Moon },
  { value: "system", label: "System", icon: Monitor },
];

export function Topbar({
  crumbs,
  onOpenSearch,
  onOpenMobileNav,
}: {
  crumbs: Crumb[];
  onOpenSearch: () => void;
  onOpenMobileNav: () => void;
}) {
  const { session, signOut } = useAuth();
  const { preferences, update } = usePreferences();
  const [signingOut, setSigningOut] = useState(false);

  async function handleSignOut() {
    setSigningOut(true);
    try {
      await signOut();
    } finally {
      setSigningOut(false);
    }
  }

  return (
    <header className="sticky top-0 z-30 flex h-14 shrink-0 items-center gap-3 border-b border-border bg-surface/95 px-4 backdrop-blur">
      <Button
        variant="ghost"
        size="icon"
        className="md:hidden"
        aria-label="Open navigation"
        onClick={onOpenMobileNav}
      >
        <Menu />
      </Button>

      <Breadcrumbs items={crumbs} className="min-w-0 flex-1" />

      {/* Search opens the palette rather than being a second search surface. */}
      <button
        type="button"
        onClick={onOpenSearch}
        className="hidden h-9 w-64 items-center gap-2 rounded-control border border-border bg-surface-sunken px-2.5 text-caption text-subtle transition-colors hover:border-border-strong hover:text-muted lg:flex"
      >
        <Search className="size-4 shrink-0" aria-hidden />
        <span className="flex-1 text-left">Search…</span>
        <kbd className="rounded border border-border px-1 py-0.5 text-overline">
          ⌘K
        </kbd>
      </button>

      <Tooltip label="Search">
        <Button
          variant="ghost"
          size="icon"
          className="lg:hidden"
          aria-label="Search"
          onClick={onOpenSearch}
        >
          <Search />
        </Button>
      </Tooltip>

      <NotificationCenter />

      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <button
            type="button"
            className="flex items-center gap-2 rounded-control p-0.5 transition-colors hover:bg-surface-raised"
            aria-label="Account menu"
          >
            <Avatar
              name={session?.name ?? "User"}
              src={session?.avatarUrl}
              size="md"
            />
          </button>
        </DropdownMenuTrigger>

        <DropdownMenuContent className="w-60">
          <div className="flex items-center gap-2.5 px-2 py-2">
            <Avatar
              name={session?.name ?? "User"}
              src={session?.avatarUrl}
              size="lg"
            />
            <div className="min-w-0">
              <p className="truncate text-body font-medium text-foreground">
                {session?.name}
              </p>
              <p className="truncate text-caption text-muted">
                {session?.email}
              </p>
            </div>
          </div>

          <DropdownMenuSeparator />

          <DropdownMenuItem asChild>
            <Link href="/settings">
              <UserRound />
              Profile
            </Link>
          </DropdownMenuItem>
          <DropdownMenuItem asChild>
            <Link href="/settings">
              <Settings />
              Settings
            </Link>
          </DropdownMenuItem>

          <DropdownMenuSeparator />

          <DropdownMenuLabel>Appearance</DropdownMenuLabel>
          <DropdownMenuRadioGroup
            value={preferences.theme}
            onValueChange={(value) => update({ theme: value as ThemeMode })}
          >
            {THEME_OPTIONS.map((option) => (
              <DropdownMenuRadioItem key={option.value} value={option.value}>
                <option.icon />
                {option.label}
              </DropdownMenuRadioItem>
            ))}
          </DropdownMenuRadioGroup>

          <DropdownMenuSeparator />

          <DropdownMenuItem
            destructive
            disabled={signingOut}
            onSelect={(event) => {
              // Keep the menu mounted while the async sign-out resolves.
              event.preventDefault();
              handleSignOut();
            }}
          >
            <LogOut />
            {signingOut ? "Signing out…" : "Sign out"}
          </DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>
    </header>
  );
}
