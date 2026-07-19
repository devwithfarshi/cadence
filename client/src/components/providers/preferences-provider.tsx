"use client";

import {
  createContext,
  type ReactNode,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
} from "react";
import { getStoredPreferences, savePreferences } from "@/lib/api/workspace";
import { DEFAULT_PREFERENCES } from "@/lib/db/seed";
import { createTranslator, type TranslationKey } from "@/lib/i18n";
import type { Preferences, ThemeMode } from "@/types/domain";

interface PreferencesContextValue {
  preferences: Preferences;
  update: (patch: Partial<Preferences>) => void;
  /** True once localStorage has been read, so callers can avoid a flash. */
  ready: boolean;
  /** Theme actually applied, with "system" already resolved. */
  resolvedTheme: "light" | "dark";
  /** Translates an interface string into the selected language. */
  t: (key: TranslationKey) => string;
}

const PreferencesContext = createContext<PreferencesContextValue | null>(null);

function systemPrefersDark(): boolean {
  return (
    typeof window !== "undefined" &&
    window.matchMedia("(prefers-color-scheme: dark)").matches
  );
}

function resolveTheme(theme: ThemeMode): "light" | "dark" {
  if (theme === "system") return systemPrefersDark() ? "dark" : "light";
  return theme;
}

function applyThemeClass(resolved: "light" | "dark"): void {
  document.documentElement.classList.toggle("dark", resolved === "dark");
}

export function PreferencesProvider({ children }: { children: ReactNode }) {
  const [preferences, setPreferences] =
    useState<Preferences>(DEFAULT_PREFERENCES);
  const [ready, setReady] = useState(false);
  const [resolvedTheme, setResolvedTheme] = useState<"light" | "dark">("light");

  // Read persisted preferences after mount — the inline script in the document
  // head has already applied the correct theme class by this point, so this
  // only syncs React state with what is already on screen.
  useEffect(() => {
    const stored = getStoredPreferences();
    setPreferences(stored);

    const resolved = resolveTheme(stored.theme);
    setResolvedTheme(resolved);
    applyThemeClass(resolved);
    setReady(true);
  }, []);

  // Follow the OS only while the user's preference is "system".
  useEffect(() => {
    if (preferences.theme !== "system") return;

    const media = window.matchMedia("(prefers-color-scheme: dark)");
    const handleChange = () => {
      const resolved = media.matches ? "dark" : "light";
      setResolvedTheme(resolved);
      applyThemeClass(resolved);
    };

    media.addEventListener("change", handleChange);
    return () => media.removeEventListener("change", handleChange);
  }, [preferences.theme]);

  const update = useCallback((patch: Partial<Preferences>) => {
    const next = savePreferences(patch);
    setPreferences(next);

    if (patch.theme !== undefined) {
      const resolved = resolveTheme(patch.theme);
      setResolvedTheme(resolved);
      applyThemeClass(resolved);
    }
  }, []);

  // Rebuilt only when the language actually changes.
  const t = useMemo(
    () => createTranslator(preferences.language),
    [preferences.language],
  );

  const value = useMemo(
    () => ({ preferences, update, ready, resolvedTheme, t }),
    [preferences, update, ready, resolvedTheme, t],
  );

  return (
    <PreferencesContext.Provider value={value}>
      {children}
    </PreferencesContext.Provider>
  );
}

export function usePreferences(): PreferencesContextValue {
  const context = useContext(PreferencesContext);
  if (!context) {
    throw new Error(
      "usePreferences must be used inside a <PreferencesProvider>",
    );
  }
  return context;
}
