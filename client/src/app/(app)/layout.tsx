"use client";

import { Loader2 } from "lucide-react";
import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { useAuth } from "@/components/providers/auth-provider";
import {
  BreadcrumbProvider,
  useBreadcrumbs,
} from "@/components/shell/breadcrumb-context";
import {
  CommandPalette,
  useCommandPalette,
} from "@/components/shell/command-palette";
import { Sidebar } from "@/components/shell/sidebar";
import { Topbar } from "@/components/shell/topbar";
import { Sheet, SheetContent } from "@/components/ui/sheet";
import { isActiveRoute, NAV_SECTIONS, SETTINGS_ITEM } from "@/lib/navigation";
import { cn } from "@/lib/utils/cn";

/** Full-screen loader shown while the session is being restored. */
function ShellLoading() {
  return (
    <div className="flex min-h-dvh items-center justify-center">
      <Loader2 className="size-5 animate-spin text-subtle" aria-hidden />
      <span className="sr-only">Loading your workspace</span>
    </div>
  );
}

function MobileNav({
  open,
  onOpenChange,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const pathname = usePathname();

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="left" title="Navigation" className="max-w-72">
        <div className="space-y-6">
          {[...NAV_SECTIONS, { label: "", items: [SETTINGS_ITEM] }].map(
            (section) => (
              <div key={section.label || "settings"} className="space-y-1">
                {section.label ? (
                  <p className="px-2 pb-1 text-overline uppercase text-subtle">
                    {section.label}
                  </p>
                ) : null}

                {section.items.map((item) => {
                  const active = isActiveRoute(pathname, item.href);
                  return (
                    <Link
                      key={item.href}
                      href={item.href}
                      onClick={() => onOpenChange(false)}
                      aria-current={active ? "page" : undefined}
                      className={cn(
                        "flex items-center gap-2.5 rounded-control px-2 py-2 text-body transition-colors",
                        active
                          ? "bg-accent-subtle font-medium text-accent-subtle-foreground"
                          : "text-muted hover:bg-surface-raised hover:text-foreground",
                      )}
                    >
                      <item.icon className="size-4 shrink-0" aria-hidden />
                      <span className="flex-1 truncate">{item.label}</span>
                      {item.upcoming ? (
                        <span className="text-overline uppercase text-subtle">
                          Soon
                        </span>
                      ) : null}
                    </Link>
                  );
                })}
              </div>
            ),
          )}
        </div>
      </SheetContent>
    </Sheet>
  );
}

/** Inner shell — separate so it can consume the breadcrumb context. */
function Shell({ children }: { children: React.ReactNode }) {
  const { crumbs } = useBreadcrumbs();
  const { open: paletteOpen, setOpen: setPaletteOpen } = useCommandPalette();
  const [mobileNavOpen, setMobileNavOpen] = useState(false);

  return (
    <div className="flex min-h-dvh">
      <Sidebar />

      <div className="flex min-w-0 flex-1 flex-col">
        <Topbar
          crumbs={crumbs}
          onOpenSearch={() => setPaletteOpen(true)}
          onOpenMobileNav={() => setMobileNavOpen(true)}
        />
        <main className="flex-1 bg-background">{children}</main>
      </div>

      <MobileNav open={mobileNavOpen} onOpenChange={setMobileNavOpen} />
      <CommandPalette open={paletteOpen} onOpenChange={setPaletteOpen} />
    </div>
  );
}

/**
 * Guards every route beneath it. Children are never rendered without a session,
 * so pages can treat the signed-in user as a given.
 */
export default function AppLayout({ children }: { children: React.ReactNode }) {
  const { status } = useAuth();
  const router = useRouter();

  useEffect(() => {
    if (status === "unauthenticated") router.replace("/signin");
  }, [status, router]);

  if (status !== "authenticated") return <ShellLoading />;

  return (
    <BreadcrumbProvider>
      <Shell>{children}</Shell>
    </BreadcrumbProvider>
  );
}
