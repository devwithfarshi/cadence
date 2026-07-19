"use client";

import { AlertTriangle, Check, Plug, Search } from "lucide-react";
import { useMemo, useState } from "react";
import { PageContainer, PageHeader } from "@/components/shell/page-header";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { ConfirmDialog } from "@/components/ui/dialog";
import { EmptyState, ErrorState, Skeleton } from "@/components/ui/feedback";
import { SearchInput } from "@/components/ui/input";
import { useToast } from "@/components/ui/toast";
import {
  connectIntegration,
  disconnectIntegration,
  listIntegrations,
} from "@/lib/api/team";
import { useAsync } from "@/lib/hooks/use-async";
import { cn } from "@/lib/utils/cn";
import { formatRelative } from "@/lib/utils/format";
import type { Integration, IntegrationCategory } from "@/types/domain";

const CATEGORY_ORDER: {
  key: IntegrationCategory;
  label: string;
  description: string;
}[] = [
  {
    key: "meetings",
    label: "Meetings",
    description: "Join and record calls automatically",
  },
  {
    key: "calendar",
    label: "Calendar",
    description: "Sync upcoming meetings and detect conferencing links",
  },
  {
    key: "storage",
    label: "Storage",
    description: "Index documents from where they already live",
  },
  {
    key: "productivity",
    label: "Productivity",
    description: "Push summaries and action items into your other tools",
  },
];

/**
 * Brand marks, inlined.
 *
 * Each is a simplified geometric stand-in rather than a real logo — shipping a
 * third party's actual trademark into a demo is a licensing problem, and a
 * recognisable shape does the same navigational job.
 */
function IntegrationMark({ integration }: { integration: Integration }) {
  const initials = integration.name
    .split(/\s+/)
    .map((word) => word[0])
    .join("")
    .slice(0, 2)
    .toUpperCase();

  return (
    <span
      aria-hidden
      className="flex size-9 shrink-0 items-center justify-center rounded-control border border-border bg-surface-raised text-label font-semibold text-muted"
    >
      {initials}
    </span>
  );
}

export default function IntegrationsPage() {
  const { toast } = useToast();
  const [search, setSearch] = useState("");
  const [busyId, setBusyId] = useState<string | null>(null);
  const [pendingDisconnect, setPendingDisconnect] =
    useState<Integration | null>(null);

  const integrations = useAsync(() => listIntegrations(), []);

  const filtered = useMemo(() => {
    const needle = search.trim().toLowerCase();
    return (integrations.data ?? []).filter(
      (item) =>
        !needle ||
        item.name.toLowerCase().includes(needle) ||
        item.description.toLowerCase().includes(needle),
    );
  }, [integrations.data, search]);

  const connectedCount = (integrations.data ?? []).filter(
    (item) => item.status === "connected",
  ).length;

  async function handleConnect(integration: Integration) {
    setBusyId(integration.id);
    try {
      await connectIntegration(integration.id);
      integrations.refetch();
      toast({
        tone: "success",
        title: `${integration.name} connected`,
        description: "Cadence can now sync with this workspace.",
      });
    } catch {
      toast({ tone: "error", title: `Could not connect ${integration.name}` });
    } finally {
      setBusyId(null);
    }
  }

  async function handleDisconnect() {
    if (!pendingDisconnect) return;
    setBusyId(pendingDisconnect.id);
    try {
      await disconnectIntegration(pendingDisconnect.id);
      integrations.refetch();
      toast({
        tone: "info",
        title: `${pendingDisconnect.name} disconnected`,
      });
      setPendingDisconnect(null);
    } catch {
      toast({ tone: "error", title: "Could not disconnect" });
    } finally {
      setBusyId(null);
    }
  }

  return (
    <PageContainer>
      <PageHeader
        title="Integrations"
        description="Connect the tools your meetings already live in."
      />

      <div className="mb-5 flex flex-wrap items-center gap-3">
        <SearchInput
          value={search}
          onValueChange={setSearch}
          placeholder="Search integrations…"
          className="w-full sm:w-72"
        />
        {integrations.data ? (
          <p className="text-caption text-muted tabular">
            {connectedCount} of {integrations.data.length} connected
          </p>
        ) : null}
      </div>

      {integrations.error ? (
        <ErrorState
          description={integrations.error.message}
          onRetry={integrations.refetch}
        />
      ) : integrations.loading && !integrations.data ? (
        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
          {[0, 1, 2, 3, 4, 5].map((index) => (
            <Skeleton key={index} className="h-32 w-full" />
          ))}
        </div>
      ) : filtered.length === 0 ? (
        <EmptyState
          icon={Search}
          title="No integrations match"
          description={`Nothing matches “${search}”.`}
          action={
            <Button variant="secondary" size="sm" onClick={() => setSearch("")}>
              Clear search
            </Button>
          }
          className="rounded-surface border border-border bg-surface"
        />
      ) : (
        <div className="space-y-8">
          {CATEGORY_ORDER.map((category) => {
            const inCategory = filtered.filter(
              (item) => item.category === category.key,
            );
            if (inCategory.length === 0) return null;

            return (
              <section key={category.key}>
                <div className="mb-3">
                  <h2 className="text-subheading text-foreground">
                    {category.label}
                  </h2>
                  <p className="text-caption text-muted">
                    {category.description}
                  </p>
                </div>

                <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
                  {inCategory.map((integration) => {
                    const busy = busyId === integration.id;
                    const connected = integration.status === "connected";
                    const errored = integration.status === "error";

                    return (
                      <div
                        key={integration.id}
                        className={cn(
                          "flex flex-col rounded-surface border bg-surface p-4 transition-colors",
                          errored
                            ? "border-danger/40"
                            : "border-border hover:border-border-strong",
                        )}
                      >
                        <div className="flex items-start gap-3">
                          <IntegrationMark integration={integration} />

                          <div className="min-w-0 flex-1">
                            <p className="truncate text-body font-medium text-foreground">
                              {integration.name}
                            </p>
                            {connected ? (
                              <Badge tone="success" size="sm" className="mt-1">
                                <Check className="size-2.5" />
                                Connected
                              </Badge>
                            ) : errored ? (
                              <Badge tone="danger" size="sm" className="mt-1">
                                <AlertTriangle className="size-2.5" />
                                Needs attention
                              </Badge>
                            ) : (
                              <Badge tone="neutral" size="sm" className="mt-1">
                                Not connected
                              </Badge>
                            )}
                          </div>
                        </div>

                        <p className="mt-3 flex-1 text-caption text-muted">
                          {integration.description}
                        </p>

                        {integration.accountLabel ? (
                          <p className="mt-2 truncate text-label text-subtle">
                            {errored ? "Problem: " : "Account: "}
                            {integration.accountLabel}
                          </p>
                        ) : null}

                        <div className="mt-3 flex items-center gap-2 border-t border-border pt-3">
                          {connected ? (
                            <>
                              <Button
                                variant="secondary"
                                size="sm"
                                loading={busy}
                                onClick={() =>
                                  setPendingDisconnect(integration)
                                }
                              >
                                Disconnect
                              </Button>
                              {integration.connectedAt ? (
                                <span className="text-label text-subtle">
                                  Since{" "}
                                  {formatRelative(integration.connectedAt)}
                                </span>
                              ) : null}
                            </>
                          ) : (
                            <Button
                              variant={errored ? "danger" : "primary"}
                              size="sm"
                              loading={busy}
                              onClick={() => handleConnect(integration)}
                            >
                              <Plug />
                              {errored ? "Reconnect" : "Connect"}
                            </Button>
                          )}
                        </div>
                      </div>
                    );
                  })}
                </div>
              </section>
            );
          })}
        </div>
      )}

      <ConfirmDialog
        open={pendingDisconnect !== null}
        onOpenChange={(open) => {
          if (!open) setPendingDisconnect(null);
        }}
        title={`Disconnect ${pendingDisconnect?.name ?? ""}?`}
        description="Cadence will stop syncing with this tool. Meetings and documents already imported are kept."
        confirmLabel="Disconnect"
        destructive
        loading={busyId !== null}
        onConfirm={handleDisconnect}
      />
    </PageContainer>
  );
}
