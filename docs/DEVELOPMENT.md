# Development and local operation

ServiceOps is a local portfolio application. Compose runs a Vite development server; it is not a public production deployment. Never use the fictional demo accounts or seed configuration for production.

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
docker compose --profile tools run --rm seed --seed-demo
docker compose ps --all
```

Open **http://localhost:5173**. The web service may take a moment to install its locked dependencies on first start; check `docker compose logs web` if it is not ready. API health is at http://localhost:5080/health/ready and the development OpenAPI document at http://localhost:5080/openapi/v1.json.

| Account | Role | Password |
| --- | --- | --- |
| elena.brooks@atlas.example | Operations | Your SEED_OPERATIONS_PASSWORD value |
| marcus.chen@atlas.example | Manager | Your SEED_MANAGER_PASSWORD value |

The explicit migration job runs before API startup. Demo seeding is a separate command, allowed only in Development. Seed reruns preserve user IDs and password hashes and ensure each role membership exists. Changing a seed password variable does not reset an existing account password. No password-reset feature is implemented. Seeding also ensures 20 fictional customers, 50 locations, and 15 technicians exist, preserving stable IDs and existing records. The `--seed-demo` command also installs the portfolio dataset described below. Use `--seed` only when you intentionally want users/reference data without demonstration orders.

```powershell
docker compose logs api web
docker compose --profile tools run --rm seed --seed-demo  # safe rerun
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
dotnet run --project backend/src/ServiceOps.Api --no-build -- --migrate --seed-demo
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

Tests cover both seeded users, anonymous rejection, Operations/Manager policy enforcement, invalid credentials, missing CSRF, sign-out, seed idempotency, and health. The real dashboard endpoint requires Manager. Test-only policy probes remain confined to the test host. Browser smoke steps: sign in with each account, verify the role, refresh, sign out, then try an incorrect password.

To regenerate frontend contract types while the development API is running on port 5080:

```powershell
cd frontend
npm run api:generate
```

The generated `src/lib/api/schema.d.ts` is checked in. CI regenerates it and checks for drift. The GitHub Actions workflow also performs backend build/integration tests, frontend test/type/build checks, and Compose validation. Execution of GitHub Actions has not been verified in this workspace.

For schema changes in later approved work, `dotnet tool restore` installs the repository-local EF CLI manifest. There is no automatic migration during normal API startup.


## Existing databases

Final portfolio polish introduces no migration or seed change. Preserve the installed database and private `.env`; rebuilding the application does not require rerunning the seed. Normal startup never resets data.

## Demo dataset and time

On a clean database, the startup commands above migrate, seed both users and reference data, and install **450 work orders** across roughly six months. No separate fixture command is needed. Existing databases containing work orders without the portfolio marker are refused without changing those orders; use a separate fresh development database for the demo. Test fixtures remain separate.

The fixed default reference instant is **2026-10-01T18:00:00Z**, with history beginning in April 2026. Business content, timestamps, distributions and chronological work-order numbers repeat on fresh databases using the same anchor and original reference seed; generated UUIDs/password hashes need not match. The seed records its version and anchor in `DemoSeedStates`. Reruns preserve all orders, activities and subsequent edits. Changing an explicitly supplied anchor requires a fresh database; the command never resets or rebases existing data.

SLA state is always evaluated against real server time. Open Good and AtRisk examples are intended for a review near the selected anchor and will naturally become Breached later. For a later portfolio review, choose an explicit, fixed UTC instant near the planned review and retain it for reproducibility. On a fresh host database, set this **before** the migration/demo command:

```powershell
$env:DemoSeed__AnchorUtc = '2026-10-02T12:30:00Z' # example review reference; select deliberately
dotnet run --project backend/src/ServiceOps.Api --no-build -- --migrate --seed-demo
```

The equivalent Compose override is:

```powershell
docker compose --profile tools run --rm -e DemoSeed__AnchorUtc=2026-10-02T12:30:00Z seed --seed-demo
```

Omitting the override on reruns accepts the already-installed anchor. A supplied anchor must use `yyyy-MM-ddTHH:mm:ssZ`. These are Development-only commands. Historical terminal outcomes remain stable as the live backlog ages; no runtime clock override or background worker is involved.

The authored issue catalog supplies service/priority-specific titles, context and matching completion summaries. Specialized assets are limited to appropriate customer types. A fixed random seed weights weekday history, service demand, priorities and technician workload. Existing domain methods create every assignment and workflow activity. All orders and the installation marker commit together; a failure leaves no partial work-order dataset. PostgreSQL sequences can consume numbers during a failed attempt, so exact number repeatability assumes a fresh database.

The former `QueueReviewFixture` and `--seed-queue` population path have been removed. `PHASE3_REVIEW.md` is historical verification, not a current seed guide. See [Phase 5 review](../PHASE5_REVIEW.md) for measured distributions, dates, tests and queue screenshots.


## Dashboard dates

The dashboard reports calendar dates in America/New_York. Its two visible dates are inclusive; the API converts the start and the day after the end into UTC boundaries. CompletedAt selects the cohort, not creation time. Queue/detail/activity timestamps use the named browser timezone; advanced creation-time filters explicitly use UTC.
