"use client";

import { Search, X } from "lucide-react";
import type { ComponentProps, ReactNode } from "react";
import { useId } from "react";
import { cn } from "@/lib/utils/cn";

const fieldBase = [
  "w-full rounded-control border border-border bg-surface text-foreground",
  "placeholder:text-subtle transition-colors",
  "hover:border-border-strong",
  "focus:outline-none focus-visible:border-accent focus-visible:ring-2 focus-visible:ring-accent/25",
  "disabled:cursor-not-allowed disabled:bg-surface-sunken disabled:text-subtle",
].join(" ");

export interface InputProps extends ComponentProps<"input"> {
  invalid?: boolean;
}

export function Input({ className, invalid, ...props }: InputProps) {
  return (
    <input
      className={cn(
        fieldBase,
        "h-9 px-3 text-body",
        invalid &&
          "border-danger focus-visible:border-danger focus-visible:ring-danger/25",
        className,
      )}
      aria-invalid={invalid || undefined}
      {...props}
    />
  );
}

export function Textarea({
  className,
  invalid,
  ...props
}: ComponentProps<"textarea"> & { invalid?: boolean }) {
  return (
    <textarea
      className={cn(
        fieldBase,
        "min-h-20 px-3 py-2 text-body resize-y",
        invalid &&
          "border-danger focus-visible:border-danger focus-visible:ring-danger/25",
        className,
      )}
      aria-invalid={invalid || undefined}
      {...props}
    />
  );
}

/**
 * Label + control + helper/error text.
 *
 * Wiring `id`, `aria-describedby` and `aria-invalid` by hand at every call site
 * is where accessibility quietly rots, so this does it once. Pass a render
 * function to receive the generated ids.
 */
export function Field({
  label,
  hint,
  error,
  required,
  children,
  className,
}: {
  label: string;
  hint?: string;
  error?: string;
  required?: boolean;
  children: (props: {
    id: string;
    "aria-describedby": string | undefined;
    invalid: boolean;
  }) => ReactNode;
  className?: string;
}) {
  const id = useId();
  const hintId = `${id}-hint`;
  const errorId = `${id}-error`;

  const describedBy =
    [error ? errorId : null, hint ? hintId : null].filter(Boolean).join(" ") ||
    undefined;

  return (
    <div className={cn("flex flex-col gap-1.5", className)}>
      <label htmlFor={id} className="text-label text-foreground">
        {label}
        {required ? (
          <span className="ml-0.5 text-danger" aria-hidden>
            *
          </span>
        ) : null}
      </label>

      {children({
        id,
        "aria-describedby": describedBy,
        invalid: Boolean(error),
      })}

      {error ? (
        <p id={errorId} role="alert" className="text-caption text-danger">
          {error}
        </p>
      ) : hint ? (
        <p id={hintId} className="text-caption text-muted">
          {hint}
        </p>
      ) : null}
    </div>
  );
}

/** Search input with a leading icon and a clear button once it has a value. */
export function SearchInput({
  className,
  value,
  onValueChange,
  placeholder = "Search…",
  ...props
}: Omit<ComponentProps<"input">, "onChange" | "value"> & {
  value: string;
  onValueChange: (value: string) => void;
}) {
  return (
    <div className={cn("relative", className)}>
      <Search
        aria-hidden
        className="pointer-events-none absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-subtle"
      />
      <input
        type="search"
        value={value}
        onChange={(event) => onValueChange(event.target.value)}
        placeholder={placeholder}
        className={cn(
          fieldBase,
          "h-9 pl-8.5 pr-8 text-body",
          // Suppress the native clear affordance; we render our own.
          "[&::-webkit-search-cancel-button]:appearance-none",
        )}
        {...props}
      />
      {value ? (
        <button
          type="button"
          onClick={() => onValueChange("")}
          aria-label="Clear search"
          className="absolute right-1.5 top-1/2 flex size-6 -translate-y-1/2 items-center justify-center rounded-control text-subtle hover:bg-surface-raised hover:text-foreground"
        >
          <X className="size-3.5" />
        </button>
      ) : null}
    </div>
  );
}
