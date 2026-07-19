"use client";

import { AlertTriangle, CheckCircle2, Info, X, XCircle } from "lucide-react";
import {
  createContext,
  type ReactNode,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
} from "react";
import { cn } from "@/lib/utils/cn";

export type ToastTone = "success" | "error" | "warning" | "info";

export interface Toast {
  id: string;
  title: string;
  description?: string;
  tone: ToastTone;
  /** Optional inline action, e.g. "Undo". */
  action?: { label: string; onClick: () => void };
}

interface ToastContextValue {
  toast: (input: Omit<Toast, "id">) => string;
  dismiss: (id: string) => void;
}

const ToastContext = createContext<ToastContextValue | null>(null);

const DEFAULT_DURATION = 4500;

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([]);
  // Timers are held in a ref so dismissing early can cancel them cleanly.
  const timers = useRef(new Map<string, ReturnType<typeof setTimeout>>());

  const dismiss = useCallback((id: string) => {
    setToasts((current) => current.filter((t) => t.id !== id));

    const timer = timers.current.get(id);
    if (timer) {
      clearTimeout(timer);
      timers.current.delete(id);
    }
  }, []);

  const toast = useCallback(
    (input: Omit<Toast, "id">) => {
      const id = `toast_${Math.random().toString(36).slice(2, 9)}`;
      setToasts((current) => [...current, { ...input, id }]);

      timers.current.set(
        id,
        setTimeout(() => dismiss(id), DEFAULT_DURATION),
      );
      return id;
    },
    [dismiss],
  );

  // Clear any outstanding timers if the provider unmounts.
  useEffect(() => {
    const pending = timers.current;
    return () => {
      for (const timer of pending.values()) clearTimeout(timer);
      pending.clear();
    };
  }, []);

  const value = useMemo(() => ({ toast, dismiss }), [toast, dismiss]);

  return (
    <ToastContext.Provider value={value}>
      {children}
      <ToastViewport toasts={toasts} onDismiss={dismiss} />
    </ToastContext.Provider>
  );
}

export function useToast(): ToastContextValue {
  const context = useContext(ToastContext);
  if (!context) {
    throw new Error("useToast must be used inside a <ToastProvider>");
  }
  return context;
}

const TONE_CONFIG = {
  success: { Icon: CheckCircle2, className: "text-success" },
  error: { Icon: XCircle, className: "text-danger" },
  warning: { Icon: AlertTriangle, className: "text-warning" },
  info: { Icon: Info, className: "text-info" },
} as const;

function ToastViewport({
  toasts,
  onDismiss,
}: {
  toasts: Toast[];
  onDismiss: (id: string) => void;
}) {
  return (
    <div
      // Polite so toasts are announced without interrupting the current task.
      aria-live="polite"
      aria-relevant="additions"
      className="pointer-events-none fixed bottom-4 right-4 z-[100] flex w-full max-w-sm flex-col gap-2"
    >
      {toasts.map((item) => {
        const { Icon, className } = TONE_CONFIG[item.tone];

        return (
          // No role here: the viewport above is already a live region, and a
          // nested status role would announce each toast twice.
          <div
            key={item.id}
            className="pointer-events-auto flex items-start gap-3 rounded-surface border border-border bg-surface p-3 shadow-lg"
          >
            <Icon
              className={cn("mt-0.5 size-4 shrink-0", className)}
              aria-hidden
            />

            <div className="min-w-0 flex-1 space-y-0.5">
              <p className="text-body font-medium text-foreground">
                {item.title}
              </p>
              {item.description ? (
                <p className="text-caption text-muted">{item.description}</p>
              ) : null}
              {item.action ? (
                <button
                  type="button"
                  onClick={() => {
                    item.action?.onClick();
                    onDismiss(item.id);
                  }}
                  className="pt-1 text-caption font-medium text-accent underline-offset-4 hover:underline"
                >
                  {item.action.label}
                </button>
              ) : null}
            </div>

            <button
              type="button"
              onClick={() => onDismiss(item.id)}
              aria-label="Dismiss notification"
              className="-mr-1 -mt-1 flex size-6 shrink-0 items-center justify-center rounded-control text-subtle hover:bg-surface-raised hover:text-foreground"
            >
              <X className="size-3.5" />
            </button>
          </div>
        );
      })}
    </div>
  );
}
