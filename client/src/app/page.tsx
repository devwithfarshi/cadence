"use client";

import { Loader2 } from "lucide-react";
import { useRouter } from "next/navigation";
import { useEffect } from "react";
import { useAuth } from "@/components/providers/auth-provider";

/**
 * Entry point. The session lives in localStorage, which is unreadable on the
 * server, so the routing decision has to happen on the client after mount.
 */
export default function RootPage() {
  const { status } = useAuth();
  const router = useRouter();

  useEffect(() => {
    if (status === "loading") return;
    router.replace(status === "authenticated" ? "/dashboard" : "/signin");
  }, [status, router]);

  return (
    <div className="flex min-h-dvh items-center justify-center">
      <Loader2 className="size-5 animate-spin text-subtle" aria-hidden />
      <span className="sr-only">Loading</span>
    </div>
  );
}
