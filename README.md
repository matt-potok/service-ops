# ServiceOps

Atlas Facility Services' internal application. **Phases 1–3:** seeded sign-in, work-order creation/detail with server-calculated SLA deadlines, and a searchable operations queue. Phase 4 has not started. See [Phase 3 review](PHASE3_REVIEW.md) for implementation details and verification.

## Run with Docker Compose

Prerequisite: Docker Desktop running Linux containers, with Docker Compose. No host .NET or Node installation is required for this workflow.

From this repository directory in PowerShell:

```powershell
Copy-Item .env.example .env
notepad .env
```

Fill all three password values. Each demo-user password must be at least 12 characters and include uppercase, lowercase, a digit, and punctuation. Avoid semicolons in the database password because Compose interpolates it into the connection string. Keep `.env` private; it is ignored by Git. Do not overwrite an existing `.env` when returning to the project.

```powershell
docker compose config --quiet
docker compose up --build -d
docker compose --profile tools run --rm seed
docker compose ps --all
```

Open **http://localhost:5173**. The web service may take a moment to install its locked dependencies on first start; check `docker compose logs web` if it is not ready. API health is at http://localhost:5080/health/ready and the development OpenAPI document at http://localhost:5080/openapi/v1.json.

| Account | Role | Password |
| --- | --- | --- |
| elena.brooks@atlas.example | Operations | Your SEED_OPERATIONS_PASSWORD value |
| marcus.chen@atlas.example | Manager | Your SEED_MANAGER_PASSWORD value |

The explicit migration job runs before API startup. Demo seeding is a separate command, allowed only in Development. Seed reruns preserve user IDs and password hashes and ensure each role membership exists. Changing a seed password variable does not reset an existing account password. No password-reset feature is implemented. Seeding also ensures 20 fictional customers, 50 locations, and 15 technicians exist, preserving stable IDs and existing records. It creates no work orders.

```powershell
docker compose logs api web
docker compose --profile tools run --rm seed  # safe rerun
docker compose down                         # preserves database
```

To deliberately erase this project's local database and start fresh, `docker compose down --volumes` removes the project volumes. This is destructive; it is not part of normal startup. Run startup and seed again afterward.

## Run API and frontend on the host

Prerequisites: .NET SDK **10.0.401** (or a compatible patch), Node **24.18.0**, and PostgreSQL 18. The database can run in Compose while the API/frontend run in terminals. Start with the `.env` setup above and:

```powershell
docker compose up -d db
```

Terminal 1, from the repository root:

```powershell
# Load this project's simple KEY=value file into this terminal's environment.
Get-Content .env | Where-Object { $_ -match '^[A-Z_]+=' } | ForEach-Object {
    $key, $value = $_ -split '=', 2
    Set-Item -Path "Env:$key" -Value $value
}
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ConnectionStrings__ServiceOps = "Host=localhost;Port=5432;Database=serviceops;Username=serviceops;Password=$env:POSTGRES_PASSWORD"
$env:Seed__OperationsPassword = $env:SEED_OPERATIONS_PASSWORD
$env:Seed__ManagerPassword = $env:SEED_MANAGER_PASSWORD
dotnet restore backend/ServiceOps.sln --locked-mode
dotnet build backend/ServiceOps.sln --no-restore
dotnet run --project backend/src/ServiceOps.Api --no-build -- --migrate
dotnet run --project backend/src/ServiceOps.Api --no-build -- --seed
dotnet run --project backend/src/ServiceOps.Api --no-build --urls http://127.0.0.1:5080
```

Terminal 2, from the repository root:

```powershell
cd frontend
npm ci
npm run dev -- --host 127.0.0.1
```

Open http://localhost:5173. Vite proxies `/api` to port 5080 so the browser uses a single origin for cookies and CSRF. With an existing PostgreSQL server instead, create a `serviceops` database, substitute its connection string, and omit the Compose database command. Use only one API/web workflow at a time to avoid port conflicts.

The `.env` file is consumed automatically by Compose, **not** by `dotnet run`; the explicit environment loading above is intentional. Cookies are HttpOnly, nonpersistent browser-session cookies, and Secure outside Development. API restarts may end sessions in containers because Phase 1 does not persist data-protection keys. Sign in again if needed.

## Checks

The integration suite requires real PostgreSQL. It creates a randomly named `serviceops_test_*` database per test and drops only that generated database afterward. The supplied account needs CREATEDB permission; the local Compose account has it. Never point this at a production server.

From the root in the host terminal configured above:

```powershell
$env:TEST_DATABASE_CONNECTION = "Host=localhost;Port=5432;Database=postgres;Username=serviceops;Password=$env:POSTGRES_PASSWORD"
dotnet build backend/ServiceOps.sln --no-restore
dotnet test backend/ServiceOps.sln --no-build
cd frontend
npm ci
npm run typecheck
npm test
npm run build
```

Tests cover both seeded users, anonymous rejection, Operations/Manager policy enforcement, invalid credentials, missing CSRF, sign-out, seed idempotency, and health. Role-protected probe controllers are injected by the test host only; no artificial manager-only product endpoint ships. Browser smoke steps: sign in with each account, verify the role, refresh, sign out, then try an incorrect password.

To regenerate frontend contract types while the development API is running on port 5080:

```powershell
cd frontend
npm run api:generate
```

The generated `src/lib/api/schema.d.ts` is checked in. CI regenerates it and checks for drift. The GitHub Actions workflow also performs backend build/integration tests, frontend test/type/build checks, and Compose validation. It has been authored but not run on GitHub in this workspace.

For schema changes in later approved work, `dotnet tool restore` installs the repository-local EF CLI manifest. There is no automatic migration during normal API startup.

## Structure

```text
backend/
  ServiceOps.sln
  src/ServiceOps.Api/
    Features/Auth/       # Four auth endpoints and their DTOs
    Identity/            # ApplicationUser
    Features/WorkOrders/ # Create/detail endpoints and DTOs
    Features/ReferenceData/ # Customer/location and creation-option lookups
    Persistence/         # DbContext, mappings, migrations, user/reference seed
    Program.cs           # Configuration, middleware, commands, health
  src/ServiceOps.Domain/ # Reference entities, WorkOrder creation and SLA rules
  tests/ServiceOps.Domain.Tests/
  tests/ServiceOps.Api.IntegrationTests/
frontend/
  src/app/               # Router, theme, brand, signed-in account view
  src/features/auth/     # Login form
  src/features/work-orders/ # Queue/URL state, create/detail, API calls and tests
  src/lib/api/           # Small fetch client and generated contract types
.github/workflows/ci.yml
compose.yaml
```

No generic repositories, MediatR, unit-of-work wrapper, event bus, Redis, or future domain structures. The API references the dependency-free Domain project. Identity and EF mappings stay in the API; controllers use EF Core directly.

## Choices and dependencies

| Dependency | Current purpose |
| --- | --- |
| ASP.NET Core Identity EF Core 10.0.12 | Password hashing, users, roles, lockout, and cookie sign-in using EF stores. |
| Npgsql EF Core provider 10.0.3 | PostgreSQL persistence through EF Core. |
| ASP.NET Core OpenAPI 10.0.12 | Development API contract. |
| EF Core Design / dotnet-ef 10.0.12 | Generate the checked-in migrations. Design tooling is private to the project. |
| MVC Testing, Microsoft.NET.Test.Sdk, xUnit and runner | Real application integration tests against PostgreSQL. |
| React 19.3 / React DOM | Login, account, creation, and detail screens. |
| Material UI 9.4 with Emotion | Form, account card, feedback, and theme styling. No grid/chart package yet. |
| React Router 8.4 | Authenticated home/create/detail routes and not-found routing. |
| TanStack Query 5.104 | Session/reference/detail loading and sign-in/out/create mutation state. |
| Vite 8.3 / React plugin / TypeScript 5.9 | Development server, build, and static checking. TS 5.9 satisfies the generator's peer constraint. |
| openapi-typescript 7.13 | Generate API DTO types consumed by the frontend. |

Exact versions and transitive dependencies are locked in NuGet/npm lockfiles. Passwords/cookies/request bodies are not logged. JSON console logs contain request method/path/status/duration and trace ID. Login is limited to 10 attempts/minute per remote address, with Identity lockout after five failures per user. The local Vite proxy means browser clients share a backend remote address; this is adequate for the local single-user demonstration.

Local form state and explicit validation cover the current fields; no form framework is needed. Both roles can create and inspect work orders. Vitest 5.0.2, Testing Library React 16.3.3/DOM 10.4.2, and jsdom 30.1.1 are development-only dependencies for the focused customer/location component test. No new runtime package was needed.

See [ARCHITECTURE.md](ARCHITECTURE.md), [IMPLEMENTATION.md](IMPLEMENTATION.md), and [Phase 1 review](PHASE1_REVIEW.md) for scope, acceptance criteria, actual verification, and limitations.

## Phase 2 review journey

Sign in as Operations, choose **Create work order**, then select a customer and one of its locations. Choose a service type and priority, enter a title and description, and create the order. The app navigates to its detail URL; use the Work orders queue to find it again. Refresh to verify persistence.

High priority has a four-hour deadline and a three-hour risk threshold from the original creation time. Critical is two hours, Normal eight, and Low 24; all risk thresholds are 75% of duration. The server derives SLA state on every detail response. The page refreshes this response every minute while active, and displays the evaluation time and browser time zone. No SLA worker is involved.

The backend tests also cover all priority durations and exact SLA boundaries, creation validation, customer/location mismatch, rejection of client-authoritative fields, unique sequence numbers, reference-seed reruns, detail reads, and transaction rollback when the activity insert fails. `npm test` checks that changing customer clears the selected location and replaces its options. The API accepts only the six documented creation fields; extra properties are rejected.

When upgrading an existing Phase 1 checkout, preserve `.env` and its database volume. Run `docker compose up --build -d` followed by `docker compose --profile tools run --rm seed`, or rerun the host migration and seed commands above. No historical work-order dataset is seeded in this phase.

## Phase 3 operations queue

Open **Work orders** in the workspace navigation. Search title/number, combine customer/location/service/status/SLA filters, sort the grid, and choose 25/50/100 rows per page. More filters exposes inclusive/exclusive created-time inputs in UTC and Open only. Other displayed timestamps use the named browser time zone. Filters, sort and page live in the URL; refresh and browser Back/Forward preserve them. Use the work-order number link to open detail, then **Back to work orders** to restore the queue URL.

New is the only current workflow status. Technician/assignment, completed-date and attention filters are intentionally unavailable until their domain behavior exists. The API rejects these parameters instead of silently ignoring them. There are no workflow mutations or assignment controls in this phase.

The only new runtime dependency is MIT-licensed MUI X Data Grid Community 9.14.0. Pagination, single-column sorting and filtering are server-side. No Pro package or paid feature is used. Existing test tooling is unchanged.

### Optional small queue review fixture

Normal `--seed` still creates only users/reference data. To review multiple pages, point the host connection string at an **empty work-order database**, run the normal migration and user/reference seed, then run:

```powershell
# Choose and retain a fixed anchor for reproducible content/timestamps.
$env:QueueFixture__AnchorUtc = '2026-09-30T01:00:00Z'
dotnet run --project backend/src/ServiceOps.Api --no-build -- --seed-queue
```

This separate Development-only command creates 60 New orders through the existing domain factory, with creation activities, varied services/priorities and 30-minute creation spacing. It requires the seeded reference data and Operations account. It refuses a nonempty work-order database, so a rerun never duplicates or rebases existing orders. IDs and numbers are generated normally; content and timestamps are deterministic for the chosen anchor. SLA states naturally age as server time advances. This is not the Phase 7 historical demonstration dataset.

For Compose against an empty seeded database, the equivalent command is:

```powershell
docker compose --profile tools run --rm -e QueueFixture__AnchorUtc=2026-09-30T01:00:00Z seed --seed-queue
```

No new schema migration is required for Phase 3. See `PHASE3_REVIEW.md` for exact API semantics and query/index review.
