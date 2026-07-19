import { cn } from "@/lib/utils/cn";

/**
 * Wordmark glyph — an abstracted waveform, referencing recorded audio without
 * resorting to a microphone cliché. Uses `currentColor` so it inherits tone
 * from context.
 */
export function LogoMark({ className }: { className?: string }) {
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      aria-hidden="true"
      className={cn("size-6", className)}
    >
      <rect
        x="0.75"
        y="0.75"
        width="22.5"
        height="22.5"
        rx="5.25"
        fill="currentColor"
      />
      <g
        stroke="var(--accent-foreground)"
        strokeWidth="1.75"
        strokeLinecap="round"
      >
        <path d="M6.5 9.5v5" />
        <path d="M10 6.75v10.5" />
        <path d="M13.5 8.75v6.5" />
        <path d="M17 10.75v2.5" />
      </g>
    </svg>
  );
}

export function Logo({
  className,
  showWordmark = true,
}: {
  className?: string;
  showWordmark?: boolean;
}) {
  return (
    <span className={cn("flex items-center gap-2", className)}>
      <LogoMark className="size-6 shrink-0 text-accent" />
      {showWordmark ? (
        <span className="text-subheading tracking-tight text-foreground">
          Cadence
        </span>
      ) : null}
    </span>
  );
}

/** Google's four-colour mark, inlined so the page has no external requests. */
export function GoogleMark({ className }: { className?: string }) {
  return (
    <svg
      viewBox="0 0 18 18"
      aria-hidden="true"
      className={cn("size-4", className)}
    >
      <path
        fill="#4285F4"
        d="M17.64 9.2c0-.64-.06-1.25-.16-1.84H9v3.48h4.84a4.14 4.14 0 0 1-1.8 2.72v2.26h2.92c1.7-1.57 2.68-3.88 2.68-6.62Z"
      />
      <path
        fill="#34A853"
        d="M9 18c2.43 0 4.47-.8 5.96-2.18l-2.92-2.26c-.8.54-1.84.86-3.04.86-2.34 0-4.32-1.58-5.03-3.7H.96v2.33A9 9 0 0 0 9 18Z"
      />
      <path
        fill="#FBBC05"
        d="M3.97 10.72a5.4 5.4 0 0 1 0-3.44V4.95H.96a9 9 0 0 0 0 8.1l3.01-2.33Z"
      />
      <path
        fill="#EA4335"
        d="M9 3.58c1.32 0 2.5.45 3.44 1.35l2.58-2.59C13.46.9 11.43 0 9 0A9 9 0 0 0 .96 4.95l3.01 2.33C4.68 5.16 6.66 3.58 9 3.58Z"
      />
    </svg>
  );
}
