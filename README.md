# Cadence — AI Meeting Assistant

Cadence is an enterprise-grade meeting intelligence platform — a Fireflies/Otter/Granola-style assistant that records meetings, transcribes them with speaker labels, summarises what was decided, and turns spoken commitments into tracked action items.

---

## Features

### Dashboard
- Time-aware greeting and a live workspace overview
- Statistic cards computed from real data: total meetings, hours recorded, AI summaries, open/completed tasks, documents, knowledge items
- Live "recording now" banner when a meeting is in progress
- Today / Upcoming / Recent meeting rails
- Pending action items with inline complete + undo
- Recent activity feed and quick actions

### Meetings
- List and grid views with a persisted per-user preference
- Search across titles, descriptions, tags and participants
- Filters: status, platform, AI-summary state, tags, favourites, archived
- Sortable columns, pagination, row selection and bulk actions (archive, delete)
- Favourite, archive/restore, duplicate, delete
- **Meeting history** — a month-grouped chronological archive with restore
- Schedule a meeting with participant multi-select and a date-time picker

### Meeting details
- **AI summary** — executive summary, key discussion points, and grouped decisions / risks / open questions, each linked back to the transcript line it came from
- **Transcript** — timestamped, speaker-labelled, searchable with match highlighting, filterable to detected commitments, copy and export
- **Action items** — inline create, assign, prioritise, re-status and delete
- **Attachments** and **threaded comments** with @mentions and replies
- Participant panel with talk-time distribution, bookmarks, and meeting metadata

### Live meeting
- Recording indicator, elapsed timer, and pause/resume that genuinely freezes the clock
- Live transcript with speaker labels, current-speaker highlighting and per-participant audio meters
- AI notes panel with **detected action items that require human review** before they are saved
- Quick notes, bookmarks, participant list and chat
- Ending a meeting persists the meeting, transcript, summary and accepted action items in one operation

### Tasks
- Scopes: assigned to me, created by me, completed, all — with live counts
- **Three views**: list (full data table), Kanban board, and calendar by due date
- Drag-and-drop between board columns, plus a keyboard-accessible move menu
- Bulk mark-done, reassign, re-prioritise and delete
- Detail drawer showing provenance: the meeting and the exact transcript line a task was extracted from

### Calendar
- Month, week and day views with a persisted preference
- Events positioned by real start/end time
- Meeting details drawer with a join path into a live meeting
- Search, status and platform filters, plus an upcoming rail

### AI chat
- Conversation list with persistent history
- Prompt suggestions and follow-ups derived from what was actually cited
- **Answers are grounded in workspace records only** — every citation links to a real meeting, document or knowledge entry, and the assistant says so plainly when it finds nothing
- Report generation and summary rewriting (brief / bullets / detailed)

### Knowledge base & documents
- Grid and list views, categories, tags, favourites and a recently-opened rail
- Documents: real file upload with an indexing lifecycle, rename, download, share link, bulk delete
- Filters by type, processing status and tags

### Team & organisation
- Member directory with roles, status and last-active
- Role management, suspend/reactivate, remove — with the last owner protected
- Organizations with switching, creation and plan display
- Invitations: send, resend, revoke, with duplicate and validity checks
- Permission matrix across owner / admin / member / guest
- Activity timeline

### Analytics
- Date-range presets scoping every chart and statistic below them
- Meeting volume, recording hours and AI usage as small multiples
- Task throughput (created vs. completed), meeting frequency by weekday
- Team productivity and speaker distribution weighted by meeting length
- **Every chart has a table view**, so no value is reachable only by hovering

### Integrations
- Category-grouped catalogue: Zoom, Google Meet, Teams, Google Calendar, Outlook, Drive, Dropbox, OneDrive, Notion, Slack, Jira, Trello, Linear
- Connect, disconnect and reconnect with account labels and error states

### Notifications & settings
- Notification inbox with day grouping, type filters, read/archive/delete and bulk actions
- Settings: profile, workspace, notification preferences, appearance, AI preferences, security, API keys
- API keys with reveal-once secrets and revocation

### Design & experience
- Clean, minimalist enterprise design system — neutral palette, single accent, hierarchy from type and 1px borders rather than shadows
- Full light and dark mode with system preference, applied before hydration so there is no flash
- Global command palette (⌘K) with cross-entity search, recent searches and recently opened meetings
- Collapsible sidebar, breadcrumbs, responsive down to tablet
- Interface language switching (English, German, French, Spanish) — UI chrome only; meeting content is never machine-translated
- Reusable component library: buttons, inputs, selects, multi-select, date picker, checkboxes, tables, cards, modals, drawers, tabs, accordions, timeline, tooltips, badges, toasts, progress, skeletons, pagination, empty states
- Machine-validated chart colours: the categorical palette passes lightness-band, chroma-floor, colour-blind separation and contrast checks in **both** themes

---

## Tech stack

- **Frontend** — Next.js 16 (App Router) · React 19 · Tailwind CSS v4 · TypeScript · Biome
- **Backend** — ASP.NET Core (.NET 10) · PostgreSQL 17 · EF Core 10 · Redis 7 · Hangfire · SignalR
- **Media / infra** — Cloudinary · Docker · OpenAPI/Swagger

### Frontend libraries

| Library | Used for |
|---|---|
| `next` · `react` · `react-dom` | framework and runtime |
| `tailwindcss` v4 | styling, configured entirely in CSS via `@theme inline` |
| `radix-ui` | accessible primitives (dialog, popover, select, tabs, accordion, tooltip, …) |
| `cmdk` | the ⌘K command palette |
| `recharts` | analytics charts |
| `lucide-react` | outline icon set |
| `date-fns` | date maths and formatting |
| `class-variance-authority` · `clsx` · `tailwind-merge` | component variants and class merging |
| `@biomejs/biome` | lint, format and import organisation |

### Backend packages

Grouped by concern, with rationale, in [`server/docs/backend-architecture.md` §21](server/docs/backend-architecture.md#21-recommended-nuget-packages). Headline choices: `Npgsql.EntityFrameworkCore.PostgreSQL`, `Mediator.SourceGenerator`, `FluentValidation`, `Mapster`, `Google.Apis.Auth`, `StackExchange.Redis`, `CloudinaryDotNet`, `Hangfire.PostgreSql`, `Serilog.AspNetCore`, `Swashbuckle.AspNetCore`, `Testcontainers`.

---

## Backend architecture overview

Clean Architecture with four projects and inward-pointing dependencies, plus vertical-slice module folders inside `Application`.

```mermaid
graph TD
  API["<b>Api</b><br/>endpoints · middleware · DI · SignalR hubs"]
  APP["<b>Application</b><br/>use cases · DTOs · validators · behaviors · ports"]
  INF["<b>Infrastructure</b><br/>EF Core · Redis · Cloudinary · Google · Hangfire · AI"]
  DOM["<b>Domain</b><br/>entities · value objects · events · invariants"]

  API --> APP
  API -.->|"composition root only"| INF
  INF --> APP
  APP --> DOM
  INF --> DOM
```

Highlights:

- **CQRS-lite** — a source-generated mediator with validation, logging, caching, transaction and performance behaviors, so cross-cutting concerns are applied once rather than in every handler.
- **Multi-tenant** at the organization level, enforced by a global EF query filter, with a dedicated cross-tenant isolation test.
- **Google OAuth only** — the client obtains a Google ID token, the API validates it against cached JWKS and issues its own short-lived JWT plus a rotating refresh token with reuse detection.
- **Meeting pipeline** — Hangfire jobs chain transcription → summarisation → action-item extraction, with bounded retries and terminal failure states that notify rather than fail silently.
- **UUIDv7 keys**, `timestamptz` everywhere, soft delete with partial indexes, and audit fields written by an EF interceptor.

Full detail, including the ERD, the REST contract and every decision's trade-offs, is in **[`server/docs/backend-architecture.md`](server/docs/backend-architecture.md)**.

---

## Repository structure

```
ai-meeting-assistant/
├─ client/                        # Next.js frontend (the product UI)
│  ├─ src/app/                    # App Router: (app) route group + signin
│  ├─ src/components/             # ui primitives, feature and shell components
│  ├─ src/lib/api/                # service layer — the seam that becomes real HTTP
│  ├─ src/lib/db/                 # localStorage engine + demo seed
│  └─ src/types/domain.ts         # shared domain model
└─ server/                        # ASP.NET Core backend
   ├─ src/Cadence.Domain/         # entities, value objects, events — no dependencies
   ├─ src/Cadence.Application/    # use cases, DTOs, validators, ports
   ├─ src/Cadence.Infrastructure/ # EF Core, Redis, Cloudinary, Google, Hangfire
   ├─ src/Cadence.Api/            # endpoints, middleware, SignalR, OpenAPI
   ├─ tests/                      # unit · integration (Testcontainers) · architecture
   ├─ docker/                     # Dockerfile + docker-compose
   └─ docs/                       # backend-architecture.md · PROGRESS.md
```

---

## Getting started

### Prerequisites

| Tool | Version | Needed for |
|---|---|---|
| **Node.js** | 20+ (24 tested) | frontend |
| **pnpm** | 10+ | frontend (`corepack enable pnpm`) |
| **.NET SDK** | 10.0 | backend |
| **Docker** + Compose | any recent | Postgres, Redis, integration tests |

PostgreSQL and Redis run in Docker; you never install them by hand.

### Frontend

The client is feature-complete and runs standalone against its built-in mock backend — no server required.

```bash
cd client
pnpm install
pnpm dev                 # → http://localhost:3000
```

Open <http://localhost:3000> and click **Continue with Google**. Sign-in is simulated locally, and a realistic demo workspace is seeded on first load.

### Backend

> The backend is currently a design blueprint; the commands below are the intended workflow as modules land. Track progress in [`server/docs/PROGRESS.md`](server/docs/PROGRESS.md).

```bash
cd server

# 1. Postgres + Redis
docker compose -f docker/docker-compose.yml up -d postgres redis

# 2. configuration — copy the template and fill in real values
cp .env.example .env

# 3. .NET does NOT read .env on its own; export it into the environment first
set -a && source .env && set +a      # values containing ';' must stay quoted

dotnet run --project src/Cadence.Api  # → http://localhost:5290
```

Then point the frontend at it:

```bash
cd client
cp .env.example .env.local            # set NEXT_PUBLIC_API_BASE_URL=http://localhost:5290/api/v1
pnpm dev
```

---

## Environment variables

Every value is documented with a placeholder in the committed templates — copy them, never commit the filled-in versions.

| File | Copy to | Key settings |
|---|---|---|
| `server/.env.example` | `server/.env` | see below |
| `client/.env.example` | `client/.env.local` | `NEXT_PUBLIC_API_BASE_URL`, `NEXT_PUBLIC_GOOGLE_CLIENT_ID` |

**Backend:**

| Variable | Purpose |
|---|---|
| `ConnectionStrings__Postgres` | PostgreSQL connection string |
| `Redis__ConnectionString` | Redis connection (optional — falls back to in-memory cache, single instance only) |
| `Jwt__SigningKey` | HS256 signing key, **≥ 32 bytes** |
| `Jwt__Issuer` · `Jwt__Audience` | token issuer and audience |
| `Jwt__AccessTokenMinutes` · `Jwt__RefreshTokenDays` | token lifetimes (defaults 15 / 30) |
| `Google__ClientId` | Google OAuth client id — must match the client's |
| `Cloudinary__Url` | `cloudinary://key:secret@cloud` — file endpoints fail loudly until set |
| `Ai__Provider` · `Ai__ApiKey` · `Ai__Model` | summary and chat provider |
| `Transcription__Provider` · `Transcription__ApiKey` | speech-to-text provider |
| `Cors__AllowedOrigins__0` | frontend origin, e.g. `http://localhost:3000` |
| `Database__MigrateOnStartup` | `true` for local/dev only — **never** in production |

`NEXT_PUBLIC_*` values are inlined at **build** time, so set them before `pnpm build` for each environment.

---

## Docker setup

```bash
# Postgres + Redis only (for local backend development)
docker compose -f server/docker/docker-compose.yml up -d postgres redis

# full backend stack, built from source
docker compose -f server/docker/docker-compose.yml up --build -d
curl -s localhost:8080/health          # -> Healthy
```

The API image is a multi-stage build running as a **non-root** user, with a `HEALTHCHECK` on `/health/live`. Postgres and Redis carry health checks and the API waits for `service_healthy` rather than racing a database that is still starting.

Tear down with `docker compose -f server/docker/docker-compose.yml down -v` (`-v` also drops the database volume).

---

## Commands

**Frontend** (from `client/`):

```bash
pnpm dev            # dev server
pnpm build          # production build
pnpm start          # serve the production build
pnpm lint           # Biome check
pnpm format         # Biome format --write
npx tsc --noEmit    # typecheck
```

**Backend** (from `server/`):

```bash
dotnet build                                  # whole solution (warnings are errors)
dotnet run --project src/Cadence.Api
dotnet test                                   # unit + architecture + integration
dotnet ef migrations add <Name> -p src/Cadence.Infrastructure -s src/Cadence.Api
dotnet ef database update  -p src/Cadence.Infrastructure -s src/Cadence.Api
```

Integration tests spin up throwaway Postgres/Redis containers via Testcontainers, so **Docker must be running** for `dotnet test`.

---

## Where things are

| URL | What |
|---|---|
| http://localhost:3000 | the app |
| http://localhost:5290/swagger | Swagger UI — **Development environment only** |
| http://localhost:5290/health | health check (`/health/live`, `/health/ready` too) |
| `/hubs/meetings` | SignalR hub streaming the live meeting transcript |

## Documentation

- **[`server/docs/backend-architecture.md`](server/docs/backend-architecture.md)** — the full design blueprint: architecture, ERD, REST contract, security, performance, deployment, and the decision record behind every choice.
- **[`server/docs/PROGRESS.md`](server/docs/PROGRESS.md)** — the backend build checklist and current status.
- **`client/AGENTS.md`** — a standing note that this Next.js version has breaking changes relative to older documentation; read the bundled guides under `client/node_modules/next/dist/docs/` before writing framework code.
