# ServiceOps

Atlas Facility Services' internal application. **Phase 1 only:** seeded sign-in, current account, sign-out, and server-side Operations/Manager policies. There are no work orders, dashboard, future navigation items, or account-management screens.

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

The explicit migration job runs before API startup. Demo seeding is a separate command, allowed only in Development. Seed reruns preserve user IDs and password hashes and ensure each role membership exists. Changing a seed password variable does not reset an existing account password. No password-reset feature is implemented.

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
npm run build
```

Tests cover both seeded users, anonymous rejection, Operations/Manager policy enforcement, invalid credentials, missing CSRF, sign-out, seed idempotency, and health. Role-protected probe controllers are injected by the test host only; no artificial manager-only product endpoint ships. Browser smoke steps: sign in with each account, verify the role, refresh, sign out, then try an incorrect password.

To regenerate frontend contract types while the development API is running on port 5080:

```powershell
cd frontend
npm run api:generate
```

The generated `src/lib/api/schema.d.ts` is checked in. CI regenerates it and checks for drift. The GitHub Actions workflow also performs backend build/integration tests, frontend type/build checks, and Compose validation. It has been authored but not run on GitHub in this workspace.

For schema changes in later approved work, `dotnet tool restore` installs the repository-local EF CLI manifest. There is no automatic migration during normal API startup.

## Structure

```text
backend/
  ServiceOps.sln
  src/ServiceOps.Api/
    Features/Auth/       # Four auth endpoints and their DTOs
    Identity/            # ApplicationUser
    Persistence/         # DbContext, Identity migration, demo seed
    Program.cs           # Configuration, middleware, commands, health
  src/ServiceOps.Domain/ # Intentionally empty until business behavior exists
  tests/ServiceOps.Api.IntegrationTests/
frontend/
  src/app/               # Router, theme, brand, signed-in account view
  src/features/auth/     # Login form
  src/lib/api/           # Small fetch client and generated contract types
.github/workflows/ci.yml
compose.yaml
```

No generic repositories, MediatR, unit-of-work wrapper, event bus, Redis, or future domain structures. The Domain project is present because the approved phase requests it, but the API does not reference an empty assembly. Identity stays in the API and uses direct EF Core.

## Phase 1 choices and dependencies

| Dependency | Current purpose |
| --- | --- |
| ASP.NET Core Identity EF Core 10.0.12 | Password hashing, users, roles, lockout, and cookie sign-in using EF stores. |
| Npgsql EF Core provider 10.0.3 | PostgreSQL persistence through EF Core. |
| ASP.NET Core OpenAPI 10.0.12 | Development API contract. |
| EF Core Design / dotnet-ef 10.0.12 | Generate the checked-in Identity migration. Design tooling is private to the project. |
| MVC Testing, Microsoft.NET.Test.Sdk, xUnit and runner | Real application integration tests against PostgreSQL. |
| React 19.3 / React DOM | Interactive login and signed-in view. |
| Material UI 9.4 with Emotion | Form, account card, feedback, and theme styling. No grid/chart package yet. |
| React Router 8.4 | Login, authenticated home, and not-found routing. |
| TanStack Query 5.104 | Session loading/refresh and sign-in/out mutation state. |
| Vite 8.3 / React plugin / TypeScript 5.9 | Development server, build, and static checking. TS 5.9 satisfies the generator's peer constraint. |
| openapi-typescript 7.13 | Generate the auth DTO types consumed by the frontend. |

Exact versions and transitive dependencies are locked in NuGet/npm lockfiles. Passwords/cookies/request bodies are not logged. JSON console logs contain request method/path/status/duration and trace ID. Login is limited to 10 attempts/minute per remote address, with Identity lockout after five failures per user. The local Vite proxy means browser clients share a backend remote address; this is adequate for the local single-user demonstration.

Simple built-in form validation is sufficient for two fields; React Hook Form/Zod are deferred until a feature actually needs them. Both roles currently use the same account page, with role badges. There are no role-specific navigation destinations because Phase 1 has no such feature.

See [ARCHITECTURE.md](ARCHITECTURE.md), [IMPLEMENTATION.md](IMPLEMENTATION.md), and [Phase 1 review](PHASE1_REVIEW.md) for scope, acceptance criteria, actual verification, and limitations.
