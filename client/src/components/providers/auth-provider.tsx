"use client";

import { useRouter } from "next/navigation";
import {
  createContext,
  type ReactNode,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
} from "react";
import * as authApi from "@/lib/api/auth";
import type { AuthSession } from "@/types/domain";

type AuthStatus = "loading" | "authenticated" | "unauthenticated";

interface AuthContextValue {
  status: AuthStatus;
  session: AuthSession | null;
  signIn: () => Promise<void>;
  signOut: () => Promise<void>;
  /** Refreshes cached display fields after a profile edit. */
  refresh: () => void;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  // Starts as "loading": localStorage is unreadable during SSR and the first
  // client render, so committing to a status before mount would flash the
  // sign-in screen at an already-authenticated user.
  const [status, setStatus] = useState<AuthStatus>("loading");
  const [session, setSession] = useState<AuthSession | null>(null);
  const router = useRouter();

  const restore = useCallback(() => {
    const stored = authApi.getStoredSession();
    setSession(stored);
    setStatus(stored ? "authenticated" : "unauthenticated");
  }, []);

  useEffect(() => {
    restore();
  }, [restore]);

  const signIn = useCallback(async () => {
    const next = await authApi.signInWithGoogle();
    setSession(next);
    setStatus("authenticated");
    router.push("/dashboard");
  }, [router]);

  const signOut = useCallback(async () => {
    await authApi.signOut();
    setSession(null);
    setStatus("unauthenticated");
    router.push("/signin");
  }, [router]);

  const value = useMemo(
    () => ({ status, session, signIn, signOut, refresh: restore }),
    [status, session, signIn, signOut, restore],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context)
    throw new Error("useAuth must be used inside an <AuthProvider>");
  return context;
}

/**
 * The signed-in session, asserted non-null.
 *
 * Only valid beneath the authenticated layout, which does not render children
 * until a session exists.
 */
export function useSession(): AuthSession {
  const { session } = useAuth();
  if (!session) {
    throw new Error("useSession requires an authenticated layout");
  }
  return session;
}
