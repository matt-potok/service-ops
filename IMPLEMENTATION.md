# ServiceOps — Implementation Plan

Status: Approved September 29, 2026. Phases 1–4 are accepted. Remaining phases revised October 2, 2026 for portfolio value. Revised Phase 5 is the demo dataset; revised Phase 6 is the Manager Dashboard. Priority changes, notes and the SLA worker are deferred indefinitely unless explicitly requested.

Prepared: September 29, 2026. Companion specification: [ARCHITECTURE.md](ARCHITECTURE.md).

## Delivery approach

Build small vertical slices that demonstrate usable behavior from browser through API to PostgreSQL. Each phase ends with a working review checkpoint and a suggested commit. The first phase establishes the minimum runnable foundation; subsequent phases extend it without requiring unfinished future features. Commit messages below are suggestions, not commits created by this task.

Preserve React/TypeScript/Vite, ASP.NET Core, direct EF Core, a PostgreSQL database, feature-oriented use cases, and explicit domain rules. Do not introduce microservices, generic repositories, MediatR, generic unit-of-work abstractions, Redis, message brokers, or event sourcing. AWS deployment remains deferred.

For every phase, update OpenAPI/generated frontend types when contracts change, include migrations when schema changes, and keep the README's run/review instructions accurate. Run relevant tests plus compilation/type checks for affected projects. Do not add unrelated abstractions or exhaustive coverage. Loading, empty, validation, and error behavior belongs with each feature rather than being postponed entirely to the polish phase.

Phases are sequential. Each can be independently reviewed and committed on top of the preceding accepted phase. If a phase needs multiple commits, split by the completed behavior named in its acceptance criteria, keeping each commit buildable. No sub-agent delegation or separate work streams are required.

## Phase 1 — Runnable application and seeded sign-in

**Objective:** A developer can start the stack and sign in as either role to a branded application shell.

**Backend work:** Create the Domain and API projects with feature folders. Configure EF Core/Npgsql, Identity, Operations/Manager policies, login/logout/current-user and antiforgery endpoints, Problem Details, structured logging, health endpoints, and OpenAPI. Keep account features limited to sign-in/out. Pin compatible stable dependencies after checking their requirements.

**Frontend work:** Create React/TypeScript/Vite with Material UI, routing, query provider, API client, login form, authenticated shell, and role-aware navigation. Display a simple signed-in landing page; do not show nonfunctional feature controls. Configure same-origin API proxying and CSRF handling.

**Database work:** Initial Identity migration and explicit idempotent demo-user seed for both roles; passwords supplied via local environment. Add Compose database, migration, seed, API, and web services and an example configuration without secrets.

**Tests:** A focused PostgreSQL-backed authentication check for valid/invalid credentials and protected-route access; build/type checks and a browser smoke check for both roles. Establish reusable test setup without a custom testing framework.

**Acceptance criteria:** Clean documented startup works; both seeded accounts sign in/out; an unauthenticated request is rejected; role policies are enforced server-side; migration/seed reruns do not duplicate users; login failures are understandable. Registration, password-reset UI, SSO, and administration are absent.

**Suggested Git commit boundary:** `feat: establish local stack and role-based sign-in` — include runnable foundation, lockfiles, initial migration, seed command, minimal CI build checks, and setup documentation.

## Phase 2 — Create and inspect a work order

**Objective:** An operations user can create a real work order and inspect its backend-calculated deadline.

**Backend work:** Add Customer, Location, Technician, WorkOrder, and WorkOrderActivity entities and mappings. Implement creation/domain validation, priority-to-duration policy, derived SLA state with TimeProvider, reference lookups, create and detail endpoints. Capture creator and Created activity atomically. Return revision in detail bodies.

**Frontend work:** Add the full-page creation form and detail Overview route. Provide customer-dependent location selection, service/priority inputs, server validation feedback, save progress, success navigation, and readable deadline/SLA badges. Show immutable classification as display data on detail.

**Database work:** Migrate initial business tables, relationships, work-order number sequence, constraints, indexes, Revision and SlaRevision fields. Seed the 20 customers, 50 locations, and 15 technicians with realistic fictional names; defer the full historical order dataset to revised Phase 5.

**Tests:** Parameterized SLA durations and exact 75%/deadline boundaries; creation integration checks for valid persistence, mismatched customer/location, and required fields; one frontend test for clearing location when customer changes.

**Acceptance criteria:** Creation returns a unique number and correct original-time-based risk/deadline; detail survives refresh; invalid references are rejected; creation and activity persist together; backend timestamps and actor cannot be overridden by the client; SLA display works with no worker running.

**Suggested Git commit boundary:** `feat: create work orders with calculated SLA deadlines` — include the complete create-to-detail slice, schema, reference seed, and focused tests.

## Phase 3 — Searchable operations queue

**Objective:** Users can find and inspect orders in a professional data-heavy list.

**Backend work:** Implement server-side search, customer/location/technician/service/status/SLA/date filters, sorting, pagination, and total counts. Include completed-date, open-only, and attention predicates for later dashboard drill-throughs. Allowlist sorting and bound inputs. Evaluate SLA using one captured server instant.

**Frontend work:** Build the Community data grid, business-filter toolbar, active filter chips, debounced search, pagination, sorting, and detail links. Persist state in URL parameters and reset the page on filter changes. Add distinct initial-empty, no-results, loading, and retry states.

**Database work:** Add or adjust only indexes justified by list queries. Use a small deterministic development/test fixture while the full dataset is pending; no cache or search service.

**Tests:** Focused integration cases for combined filters, SLA boundary filtering, date endpoints, stable sort/page behavior, and invalid parameters. Test filter URL round-tripping in the frontend.

**Acceptance criteria:** Every required filter is usable; browser back/forward restores the list; selecting a row reaches detail and returning preserves context; total counts match filters; expired deadlines appear correctly with the worker disabled; desktop/tablet layouts remain usable.

**Suggested Git commit boundary:** `feat: add searchable and filterable work-order queue` — include list API, UI, query indexes, and filter tests.

## Phase 4 — Assignment, workflow, and editable details

**Objective:** Operations users can move an order through a credible lifecycle and see what changed.

**Backend work:** Implement assignment/unassignment, title/description correction, and allowed status transitions as small use cases invoking domain rules. Require hold/cancellation reasons and completion summary where specified. Add chronological activity retrieval. Persist each business change and activity together. Accept optional expectedRevision, increment Revision on changes, and translate stale-client/EF conflicts into 409 `stale_revision` responses.

**Frontend work:** Add assignment and workflow controls to detail, correction form, required-reason dialogs, and the Activity tab. Send the displayed revision with business mutations. On conflict, retain user input, explain the conflict, and offer reload/review without silently resubmitting. Refresh affected queries on success.

**Database work:** Add any remaining workflow/audit fields and activity pagination index. Keep terminal timestamps and relational constraints consistent with domain rules; no HTTP concurrency metadata storage.

**Tests:** Domain transition matrix with representative legal/illegal paths; API checks for assignment-to-completion, terminal edit rejection, transactional activity, and stale expectedRevision. Test omission of expectedRevision against the documented current-state behavior.

**Acceptance criteria:** Create -> assign -> start -> hold -> resume -> complete works; cancellation requires a reason; terminal orders reject disallowed changes; activity identifies actor and changes; stale requests receive a clear 409 conflict; no ETag/If-Match or 412/428 protocol is introduced.

**Suggested Git commit boundary:** `feat: manage assignment and work-order lifecycle` — include workflow, revision conflicts, activity UI, and tests.

## Revised Phase 5 — Portfolio-quality demo dataset

**Objective:** Make the existing queue/detail experience convincing with 400–500 realistic orders covering roughly six months, while supplying coherent history for the future dashboard.

**Backend work:** One straightforward deterministic seeder using a fixed default anchor (or explicit fixed override), authored facilities issues, plausible customer/service/priority/technician distributions, and existing domain workflow methods. Include predominantly completed work, a small cancellation cohort and varied current backlog. Use no new dependencies, generator framework, notes, priority mutations or SLA worker. Replace the Phase 3 queue-review fixture with the single `--seed-demo` command, including existing user/reference seeding.

**Frontend work:** No feature additions. Inspect the populated queue, existing filters, terminal/open details and activity at desktop/tablet widths. Capture representative screenshots.

**Database work:** Preserve 20 customers, 50 locations and 15 technicians. Add only a seed version/reference-time marker. First install requires an empty work-order database; one transaction commits orders/history and marker. Reruns preserve edits and never shift timestamps. Existing unmarked work is preserved and requires a fresh database for this dataset.

**Tests:** Focused PostgreSQL checks for approximate counts, all statuses/services/priorities, realistic distribution, open SLA examples at the anchor, both completed outcomes, coherent activity, deterministic business-visible output on two fresh databases, safe reruns and failure rollback. Retain Phases 1–4 regressions and run backend build/tests plus frontend tests/typecheck/build.

**Acceptance criteria:** One documented demo-data workflow produces a useful 400–500-order dataset. Titles and context are credible; dates and histories respect existing rules; filters show meaningful results; current SLA remains based on server time and naturally ages. No dashboard or deferred features are implemented. Supply counts, effective date range, verification and queue screenshots; stop for review without committing or pushing.

**Suggested Git commit boundary:** `feat: seed realistic portfolio operations history` — seeder, minimal marker migration, tests, updated plan/runbook and review evidence.

The original priority-change Phase 5 and notes/worker Phase 6 are indefinitely deferred. They are not prerequisites for this dataset, the dashboard or portfolio polish. The former 750-order Phase 7 dataset is superseded by this 400–500-order scope.

## Revised Phase 6 — Manager dashboard and drill-throughs

**Objective:** Managers can assess the current queue and period performance without confusing their date semantics.

**Backend work:** Implement the Manager-protected dashboard query using the architecture's metric definitions, one evaluatedAt, and consistent read handling. Include open/risk/breached counts, period completions and mean resolution time, zero-filled created/completed trends, created-cohort breakdowns, and attention preview. Reuse filtering predicates where useful without creating a generic query framework.

**Frontend work:** Add Current Operations and Period Performance sections, customer/service/date filters, KPI cards, Recharts charts, attention table, sample counts, and evaluation time. State explicitly that date range applies to Period Performance. Provide chart table equivalents and keyboard-accessible drill-through links carrying the correct list filters.

**Database work:** Add only required reporting indexes after inspecting queries against the seeded dataset. No materialized reporting pipeline, Redis, warehouse, or background-generated KPI values.

**Tests:** A small fixed dataset verifies metric definitions, cancellation exclusion, no-completion averages, customer/service scope, old breached backlog outside the selected period, and created-versus-completed timestamp cohorts. Verify drill-through predicates match counts at fixed time and that Operations users cannot access dashboard data.

**Acceptance criteria:** All five KPIs, three charts, and attention table are present. Old open risk remains visible when the period changes; customer/service filters affect both sections. Empty resolution averages display an em dash. KPI/chart navigation selects the intended cohort, and later live-data changes are not presented as snapshot guarantees.

**Suggested Git commit boundary:** `feat: deliver manager operations and performance dashboard` — include reporting API, dashboard UI, drill-throughs, and metric tests.

## Revised Phase 7 — Portfolio review and delivery polish

**Objective:** Deliver a coherent, documented demonstration with the critical user journeys verified.

**Backend work:** Review safe errors/logs, validation, role checks, cancellation, and observed query performance. Fix concrete issues found during review. Finish the production-like local asset-serving profile; do not design AWS infrastructure or add speculative hardening systems.

**Frontend work:** Refine spacing, typography, table density, navigation, keyboard/focus behavior, tablet layouts, contrast, and loading/empty/error/conflict states. Ensure chart alternatives and action feedback are usable. Keep branding consistent across login, list, creation, detail, and dashboard.

**Database work:** Verify migrations on a clean database and seed reruns on an existing development database. Add indexes only for measured issues; do not rewrite accepted schema for hypothetical scale.

**Tests:** Two concise end-to-end journeys: operations login/create/assign/hold/resume/complete and manager login/filter/drill-through. Run the focused domain/API/frontend suites, affected builds, lint/type checks, and contract drift check. Perform manual desktop/tablet and keyboard reviews, with a focused automated accessibility scan. Fix regressions rather than expanding into an exhaustive enterprise matrix.

**Acceptance criteria:** Clean setup follows the README; both roles can complete their supported journeys; required states are polished; builds and relevant tests pass; known demo-data aging and application assumptions are documented. Supply a short demo walkthrough and representative screenshots. Final review confirms all requested capabilities and exclusions, with no unintended account, billing, dispatch, or deployment scope.

**Suggested Git commit boundary:** `chore: polish and document the ServiceOps demo` — include review fixes, final critical-path tests/CI, screenshots, runbook, and concise accepted ADRs reflecting the delivered design. If fixes are substantive, commit each independently before this documentation boundary.

## Review gate

Phases 1–4 are accepted, committed and pushed. Revised Phase 5 is the only authorized implementation in this task. The dashboard (revised Phase 6) requires a separate instruction. No priority-change, notes or SLA-worker work is planned unless explicitly requested later. Commit messages remain suggestions; do not commit or push the Phase 5 changes before review.
