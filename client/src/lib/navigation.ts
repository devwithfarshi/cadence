import {
  BarChart3,
  Bell,
  Calendar,
  CheckSquare,
  FileText,
  LayoutDashboard,
  Library,
  type LucideIcon,
  MessagesSquare,
  Plug,
  Radio,
  Settings,
  Users,
  Video,
} from "lucide-react";

export interface NavItem {
  label: string;
  href: string;
  icon: LucideIcon;
  /**
   * Marks sections that are not implemented yet. These still appear in the
   * sidebar because they are part of the real information architecture, but
   * they route to an honest placeholder rather than a fake screen.
   */
  upcoming?: boolean;
}

export interface NavSection {
  label: string;
  items: NavItem[];
}

export const NAV_SECTIONS: NavSection[] = [
  {
    label: "Workspace",
    items: [
      { label: "Dashboard", href: "/dashboard", icon: LayoutDashboard },
      { label: "Meetings", href: "/meetings", icon: Video },
      { label: "Live", href: "/live", icon: Radio },
      { label: "Calendar", href: "/calendar", icon: Calendar },
    ],
  },
  {
    label: "Intelligence",
    items: [
      { label: "AI Chat", href: "/chat", icon: MessagesSquare },
      {
        label: "Knowledge Base",
        href: "/knowledge",
        icon: Library,
      },
      {
        label: "Documents",
        href: "/documents",
        icon: FileText,
      },
      { label: "Tasks", href: "/tasks", icon: CheckSquare },
    ],
  },
  {
    label: "Organisation",
    items: [
      { label: "Team", href: "/team", icon: Users },
      { label: "Analytics", href: "/analytics", icon: BarChart3 },
      {
        label: "Integrations",
        href: "/integrations",
        icon: Plug,
      },
      {
        label: "Notifications",
        href: "/notifications",
        icon: Bell,
      },
    ],
  },
];

export const SETTINGS_ITEM: NavItem = {
  label: "Settings",
  href: "/settings",
  icon: Settings,
};

/** Flat list of every destination, used by the command palette. */
export const ALL_NAV_ITEMS: NavItem[] = [
  ...NAV_SECTIONS.flatMap((section) => section.items),
  SETTINGS_ITEM,
];

/**
 * True when `href` is the active route.
 *
 * Nested routes count as active for their parent — `/meetings/abc` keeps
 * "Meetings" highlighted — but `/` only ever matches itself.
 */
export function isActiveRoute(pathname: string, href: string): boolean {
  if (href === "/") return pathname === "/";
  return pathname === href || pathname.startsWith(`${href}/`);
}
