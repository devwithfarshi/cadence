/**
 * Interface translation.
 *
 * Deliberately narrow: this covers navigation and shared chrome only. Meeting
 * titles, transcripts and AI output are never translated — they are the user's
 * own content, and machine-translating it would misrepresent what was said.
 *
 * Missing keys fall back to English rather than rendering the key itself, so a
 * partial translation degrades to readable rather than broken.
 */

export const LANGUAGES = [
  { code: "en", label: "English" },
  { code: "de", label: "Deutsch" },
  { code: "fr", label: "Français" },
  { code: "es", label: "Español" },
] as const;

export type LanguageCode = (typeof LANGUAGES)[number]["code"];

const EN = {
  "nav.section.workspace": "Workspace",
  "nav.section.intelligence": "Intelligence",
  "nav.section.organisation": "Organisation",
  "nav.dashboard": "Dashboard",
  "nav.meetings": "Meetings",
  "nav.live": "Live",
  "nav.calendar": "Calendar",
  "nav.chat": "AI Chat",
  "nav.knowledge": "Knowledge Base",
  "nav.documents": "Documents",
  "nav.tasks": "Tasks",
  "nav.team": "Team",
  "nav.analytics": "Analytics",
  "nav.integrations": "Integrations",
  "nav.notifications": "Notifications",
  "nav.settings": "Settings",
  "action.newMeeting": "New meeting",
  "action.search": "Search…",
  "action.collapse": "Collapse",
  "action.signOut": "Sign out",
  "action.profile": "Profile",
  "label.appearance": "Appearance",
  "label.notifications": "Notifications",
  "label.markAllRead": "Mark all read",
  "label.viewAll": "View all notifications",
  "label.unread": "unread",
  "theme.light": "Light",
  "theme.dark": "Dark",
  "theme.system": "System",
} as const;

export type TranslationKey = keyof typeof EN;

type Dictionary = Partial<Record<TranslationKey, string>>;

const DE: Dictionary = {
  "nav.section.workspace": "Arbeitsbereich",
  "nav.section.intelligence": "Intelligenz",
  "nav.section.organisation": "Organisation",
  "nav.dashboard": "Übersicht",
  "nav.meetings": "Besprechungen",
  "nav.live": "Live",
  "nav.calendar": "Kalender",
  "nav.chat": "KI-Chat",
  "nav.knowledge": "Wissensdatenbank",
  "nav.documents": "Dokumente",
  "nav.tasks": "Aufgaben",
  "nav.team": "Team",
  "nav.analytics": "Analysen",
  "nav.integrations": "Integrationen",
  "nav.notifications": "Benachrichtigungen",
  "nav.settings": "Einstellungen",
  "action.newMeeting": "Neue Besprechung",
  "action.search": "Suchen…",
  "action.collapse": "Einklappen",
  "action.signOut": "Abmelden",
  "action.profile": "Profil",
  "label.appearance": "Darstellung",
  "label.notifications": "Benachrichtigungen",
  "label.markAllRead": "Alle als gelesen markieren",
  "label.viewAll": "Alle Benachrichtigungen",
  "label.unread": "ungelesen",
  "theme.light": "Hell",
  "theme.dark": "Dunkel",
  "theme.system": "System",
};

const FR: Dictionary = {
  "nav.section.workspace": "Espace de travail",
  "nav.section.intelligence": "Intelligence",
  "nav.section.organisation": "Organisation",
  "nav.dashboard": "Tableau de bord",
  "nav.meetings": "Réunions",
  "nav.live": "En direct",
  "nav.calendar": "Calendrier",
  "nav.chat": "Chat IA",
  "nav.knowledge": "Base de connaissances",
  "nav.documents": "Documents",
  "nav.tasks": "Tâches",
  "nav.team": "Équipe",
  "nav.analytics": "Analyses",
  "nav.integrations": "Intégrations",
  "nav.notifications": "Notifications",
  "nav.settings": "Paramètres",
  "action.newMeeting": "Nouvelle réunion",
  "action.search": "Rechercher…",
  "action.collapse": "Réduire",
  "action.signOut": "Se déconnecter",
  "action.profile": "Profil",
  "label.appearance": "Apparence",
  "label.notifications": "Notifications",
  "label.markAllRead": "Tout marquer comme lu",
  "label.viewAll": "Voir toutes les notifications",
  "label.unread": "non lues",
  "theme.light": "Clair",
  "theme.dark": "Sombre",
  "theme.system": "Système",
};

const ES: Dictionary = {
  "nav.section.workspace": "Espacio de trabajo",
  "nav.section.intelligence": "Inteligencia",
  "nav.section.organisation": "Organización",
  "nav.dashboard": "Panel",
  "nav.meetings": "Reuniones",
  "nav.live": "En vivo",
  "nav.calendar": "Calendario",
  "nav.chat": "Chat de IA",
  "nav.knowledge": "Base de conocimiento",
  "nav.documents": "Documentos",
  "nav.tasks": "Tareas",
  "nav.team": "Equipo",
  "nav.analytics": "Analíticas",
  "nav.integrations": "Integraciones",
  "nav.notifications": "Notificaciones",
  "nav.settings": "Ajustes",
  "action.newMeeting": "Nueva reunión",
  "action.search": "Buscar…",
  "action.collapse": "Contraer",
  "action.signOut": "Cerrar sesión",
  "action.profile": "Perfil",
  "label.appearance": "Apariencia",
  "label.notifications": "Notificaciones",
  "label.markAllRead": "Marcar todo como leído",
  "label.viewAll": "Ver todas las notificaciones",
  "label.unread": "sin leer",
  "theme.light": "Claro",
  "theme.dark": "Oscuro",
  "theme.system": "Sistema",
};

const DICTIONARIES: Record<LanguageCode, Dictionary> = {
  en: EN,
  de: DE,
  fr: FR,
  es: ES,
};

/** Builds a lookup for one language, falling back to English per key. */
export function createTranslator(language: string) {
  const dictionary = DICTIONARIES[language as LanguageCode] ?? DICTIONARIES.en;

  return (key: TranslationKey): string => dictionary[key] ?? EN[key];
}
