"use client";

import { useCallback, useEffect, useRef, useState } from "react";

export interface AsyncState<T> {
  data: T | undefined;
  loading: boolean;
  error: Error | undefined;
  /** Re-runs the loader, e.g. after a mutation or from an error state. */
  refetch: () => void;
  /**
   * Applies a local update without a round trip, for optimistic UI.
   * The updater may return `undefined` to leave an unloaded state untouched.
   */
  setData: (
    updater: T | undefined | ((current: T | undefined) => T | undefined),
  ) => void;
}

/**
 * Runs an async loader and tracks loading/error state.
 *
 * `deps` controls re-running, exactly like `useEffect`. Results from a stale
 * invocation are discarded, so a fast filter change can't be overwritten by a
 * slower earlier request landing late.
 */
export function useAsync<T>(
  loader: () => Promise<T>,
  deps: unknown[],
): AsyncState<T> {
  const [data, setDataState] = useState<T | undefined>(undefined);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<Error | undefined>(undefined);

  // Bumped on every run; only the newest run is allowed to commit its result.
  const runIdRef = useRef(0);
  const mountedRef = useRef(true);

  // Held in a ref so `deps` alone decides when to re-run — an inline loader
  // would otherwise change identity every render and loop forever.
  const loaderRef = useRef(loader);
  loaderRef.current = loader;

  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
    };
  }, []);

  const run = useCallback(() => {
    const runId = runIdRef.current + 1;
    runIdRef.current = runId;

    setLoading(true);
    setError(undefined);

    loaderRef
      .current()
      .then((result) => {
        if (!mountedRef.current || runIdRef.current !== runId) return;
        setDataState(result);
        setLoading(false);
      })
      .catch((cause: unknown) => {
        if (!mountedRef.current || runIdRef.current !== runId) return;
        setError(cause instanceof Error ? cause : new Error(String(cause)));
        setLoading(false);
      });
  }, []);

  // biome-ignore lint/correctness/useExhaustiveDependencies: deps are the caller's contract
  useEffect(run, deps);

  const setData = useCallback(
    (updater: T | undefined | ((current: T | undefined) => T | undefined)) => {
      setDataState((current) =>
        typeof updater === "function"
          ? (updater as (c: T | undefined) => T | undefined)(current)
          : updater,
      );
    },
    [],
  );

  return { data, loading, error, refetch: run, setData };
}

/**
 * Delays a rapidly-changing value — search inputs, mainly — so a query only
 * fires once the user pauses.
 */
export function useDebounced<T>(value: T, delayMs = 250): T {
  const [debounced, setDebounced] = useState(value);

  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delayMs);
    return () => clearTimeout(timer);
  }, [value, delayMs]);

  return debounced;
}
