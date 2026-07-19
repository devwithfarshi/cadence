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

import type { TranslationKey } from "./i18n";

export interface NavItem {
  /** English label; also the fallback when a translation is missing. */
  label: string;
  /** Key used to translate the label. */
  labelKey: TranslationKey;
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
  labelKey: TranslationKey;
  items: NavItem[];
}

export const NAV_SECTIONS: NavSection[] = [
  {
    label: "Workspace",
    labelKey: "nav.section.workspace",
    items: [
      {
        label: "Dashboard",
        labelKey: "nav.dashboard",
        href: "/dashboard",
        icon: LayoutDashboard,
      },
      {
        label: "Meetings",
        labelKey: "nav.meetings",
        href: "/meetings",
        icon: Video,
      },
      { label: "Live", labelKey: "nav.live", href: "/live", icon: Radio },
      {
        label: "Calendar",
        labelKey: "nav.calendar",
        href: "/calendar",
        icon: Calendar,
      },
    ],
  },
  {
    label: "Intelligence",
    labelKey: "nav.section.intelligence",
    items: [
      {
        label: "AI Chat",
        labelKey: "nav.chat",
        href: "/chat",
        icon: MessagesSquare,
      },
      {
        label: "Knowledge Base",
        labelKey: "nav.knowledge",
        href: "/knowledge",
        icon: Library,
      },
      {
        label: "Documents",
        labelKey: "nav.documents",
        href: "/documents",
        icon: FileText,
      },
      {
        label: "Tasks",
        labelKey: "nav.tasks",
        href: "/tasks",
        icon: CheckSquare,
      },
    ],
  },
  {
    label: "Organisation",
    labelKey: "nav.section.organisation",
    items: [
      { label: "Team", labelKey: "nav.team", href: "/team", icon: Users },
      {
        label: "Analytics",
        labelKey: "nav.analytics",
        href: "/analytics",
        icon: BarChart3,
      },
      {
        label: "Integrations",
        labelKey: "nav.integrations",
        href: "/integrations",
        icon: Plug,
      },
      {
        label: "Notifications",
        labelKey: "nav.notifications",
        href: "/notifications",
        icon: Bell,
      },
    ],
  },
];

export const SETTINGS_ITEM: NavItem = {
  label: "Settings",
  labelKey: "nav.settings",
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
