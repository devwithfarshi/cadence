"use client";

import { CheckCircle2, Loader2 } from "lucide-react";
import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { GoogleMark, Logo } from "@/components/brand/logo";
import { useAuth } from "@/components/providers/auth-provider";
import { useToast } from "@/components/ui/toast";

const VALUE_PROPS = [
  "Automatic transcription with speaker labels",
  "Executive summaries, decisions and risks",
  "Action items extracted and assigned",
];

export default function SignInPage() {
  const { status, signIn } = useAuth();
  const router = useRouter();
  const { toast } = useToast();
  const [signingIn, setSigningIn] = useState(false);

  // Someone arriving here with a live session shouldn't have to sign in again.
  useEffect(() => {
    if (status === "authenticated") router.replace("/dashboard");
  }, [status, router]);

  async function handleSignIn() {
    setSigningIn(true);
    try {
      await signIn();
    } catch (error) {
      setSigningIn(false);
      toast({
        tone: "error",
        title: "Sign-in failed",
        description:
          error instanceof Error
            ? error.message
            : "Something went wrong. Please try again.",
      });
    }
  }

  return (
    <main className="grid min-h-dvh lg:grid-cols-2">
      {/* Sign-in column */}
      <div className="flex flex-col justify-center px-6 py-12 sm:px-12 lg:px-16">
        <div className="mx-auto w-full max-w-sm">
          <Logo />

          <div className="mt-10 space-y-2">
            <h1 className="text-display text-foreground">Welcome back</h1>
            <p className="text-body text-muted">
              Sign in to access your meetings, summaries and action items.
            </p>
          </div>

          <button
            type="button"
            onClick={handleSignIn}
            disabled={signingIn || status === "loading"}
            className="mt-8 flex h-11 w-full items-center justify-center gap-2.5 rounded-control border border-border bg-surface px-4 text-body font-medium text-foreground transition-colors hover:border-border-strong hover:bg-surface-raised disabled:cursor-not-allowed disabled:opacity-60"
          >
            {signingIn ? (
              <>
                <Loader2 className="size-4 animate-spin" aria-hidden />
                Connecting to Google…
              </>
            ) : (
              <>
                <GoogleMark />
                Continue with Google
              </>
            )}
          </button>

          <p className="mt-4 text-caption text-subtle">
            Single sign-on with Google is the only way to access Cadence. Your
            workspace administrator manages access.
          </p>

          <p className="mt-8 text-caption text-muted">
            By continuing you agree to our{" "}
            <a
              href="/terms"
              className="text-foreground underline underline-offset-4 hover:text-accent"
            >
              Terms of Service
            </a>{" "}
            and{" "}
            <a
              href="/privacy"
              className="text-foreground underline underline-offset-4 hover:text-accent"
            >
              Privacy Policy
            </a>
            .
          </p>
        </div>
      </div>

      {/* Context column — hidden on small screens where it would just add scroll */}
      <aside className="hidden border-l border-border bg-surface-sunken lg:flex lg:flex-col lg:justify-center lg:px-16">
        <div className="max-w-md">
          <p className="text-overline uppercase text-subtle">
            Meeting intelligence
          </p>
          <p className="mt-4 text-heading text-foreground">
            Every meeting becomes a searchable record of what was decided, and
            what happens next.
          </p>

          <ul className="mt-8 space-y-3">
            {VALUE_PROPS.map((item) => (
              <li key={item} className="flex items-start gap-2.5">
                <CheckCircle2
                  className="mt-0.5 size-4 shrink-0 text-accent"
                  aria-hidden
                />
                <span className="text-body text-muted">{item}</span>
              </li>
            ))}
          </ul>

          <div className="mt-10 border-t border-border pt-6">
            <p className="text-caption text-muted">
              Trusted by product, engineering and revenue teams to keep
              decisions from getting lost in the recording.
            </p>
          </div>
        </div>
      </aside>
    </main>
  );
}
