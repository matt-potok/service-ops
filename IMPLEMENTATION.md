# ServiceOps — Implementation Plan

Status: Phases 1–6 accepted, committed and pushed. Final portfolio polish implemented for review on October 6, 2026; no commit or push in this phase. The application is feature-complete for its portfolio purpose.

Prepared: September 29, 2026. Companion specification: [ARCHITECTURE.md](ARCHITECTURE.md).

## Delivery approach

Build small vertical slices that demonstrate usable behavior from browser through API to PostgreSQL. Each phase ends with a working review checkpoint and a suggested commit. The first phase establishes the minimum runnable foundation; subsequent phases extend it without requiring unfinished future features. Commit messages below are suggestions, not commits created by this task.

Preserve React/TypeScript/Vite, ASP.NET Core, direct EF Core, a PostgreSQL database, feature-oriented use cases, and explicit domain rules. Do not introduce microservices, generic repositories, MediatR, generic unit-of-work abstractions, Redis, message brokers, or event sourcing. AWS deployment remains deferred.

For every phase, update OpenAPI/generated frontend types when contracts change, include migrations when schema changes, and keep the README's run/review instructions accurate. Run relevant tests plus compilation/type checks for affected projects. Do not add unrelated abstractions or exhaustive coverage. Loading, empty, validation, and error behavior belongs with each feature rather than being postponed entirely to the polish phase.

The phases below record the delivered sequence. Phases 1–6 have been accepted; final polish is awaiting review. If a phase needs multiple commits, split by the completed behavior named in its acceptance criteria, keeping each commit buildable. No sub-agent delegation or separate work streams are required.

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

**Backend work:** Implement server-side search, customer/location/technician/service/status/SLA/date filters, sorting, pagination, and total counts. Include open-only filtering. Completed-date and attention predicates were deferred and remain outside the revised dashboard scope. Allowlist sorting and bound inputs. Evaluate SLA using one captured server instant.

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

## Revised Phase 6 — Focused Manager Dashboard

The October 2 implementation request supersedes the earlier broader plan: no customer/service filters, charts, trends, mean-resolution metric, attention table or new drill-through infrastructure in this phase.

**Objective:** Give Managers a polished view of current work and completed SLA performance with clearly separate date semantics.

**Backend work:** Add the Manager-policy endpoint with three direct EF Core aggregates, one TimeProvider instant and repeatable-read consistency. Return open total/status/SLA counts, technician workload with unassigned separate, and period completed/met/missed/compliance. Select by CompletedAt, exclude cancellations, and return null compliance for empty periods. Validate calendar date ranges and convert New York midnight boundaries to UTC.

**Frontend work:** Manager navigation and guarded route; Current Operations and Period Performance sections; inclusive dates and a last-30-calendar-days default; evaluation time and simple Material UI summaries. Include loading/error/empty states and links to existing queue filters. No charting dependency. Review desktop/tablet layouts.

**Database work:** No counters, worker, pipeline or seed changes. Inspect queries against the existing dataset; add indexes only if measurements justify them.

**Tests:** Real PostgreSQL checks for Manager access and anonymous/Operations denial, open/terminal separation, SLA boundaries, old backlog independent of period, completion-date boundaries, met-at-deadline, empty periods, date validation and DST. Reconcile queue links at fixed time. Test frontend role navigation, date conversion and loading/error/empty states.

**Acceptance criteria:** Status/SLA/workload totals reconcile with backlog; completed = met + missed; empty compliance is not misleading. Changing the period leaves current scope unchanged. API authorization is authoritative. No work-order dataset is aggregated in the browser. Update docs and PHASE6_REVIEW.md with performed checks/screenshots/limitations. Do not commit or push; stop for review.

**Suggested Git commit boundary:** `feat: add manager operations and SLA performance dashboard` — endpoint, UI, generated contract, tests and review documentation.

## Final phase — Portfolio and delivery polish

**Objective:** Make the completed application and repository understandable to a prospective client in 2–5 minutes, without adding product functionality.

**Backend/database:** No business changes, schema changes or seed changes. Verify the existing build, domain rules, PostgreSQL integration suite, generated contract and practical security/configuration defaults. Use a copy of the installed dataset for manual write workflows.

**Frontend:** Standard React.lazy route loading for Dashboard, queue, creation and detail, with a loading state inside the persistent shell. Balance the six queue filters into two rows of three at desktop width. Keep technician assignment visible beside status on desktop. Match Activity timestamps to detail formatting and name the timezone; separate hold/terminal notices from the detail fields. Review existing screens at desktop/tablet widths; fix only meaningful presentation defects.

**Documentation/delivery:** Project-first README, final screenshots, a simple Mermaid architecture diagram, a separate development runbook, truthful implemented architecture and this completed delivery record. Keep useful historical reviews. Record final checks, actual bundle measurements and remaining limitations in docs/FINAL_REVIEW.md.

**Tests and acceptance:** Existing backend/frontend suites, build/type checks, OpenAPI drift, Compose configuration and dataset sanity. Manual Manager and Operations journeys, route refresh and desktop/tablet review. Do not claim Docker runtime, hosted delivery, browser automation or GitHub execution without evidence. No production static-file profile is required by the final request; the earlier Phase 7 hosting proposal is removed from portfolio scope.

**Suggested Git commit boundary:** `chore: polish ServiceOps portfolio presentation and delivery documentation` — UI polish, route splitting, screenshots and final documentation. No commit or push until user review.

## Delivery status and intentional exclusions

| Phase | Delivered scope | Status |
| --- | --- | --- |
| 1 | Foundation, local stack and seeded Identity sign-in | Accepted |
| 2 | Work-order creation, detail and SLA calculations | Accepted |
| 3 | Searchable operations queue | Accepted |
| 4 | Assignment, workflow, corrections, concurrency and activity | Accepted |
| Revised 5 | Deterministic 450-order fictional portfolio dataset | Accepted |
| Revised 6 | Manager Dashboard: Current Operations and Period Performance | Accepted |
| Final polish | Presentation, route loading, screenshots, documentation and verification | Awaiting final review |

The original priority-change phase and Notes/SLA-worker phase were deliberately removed from the portfolio scope. They are not unfinished requirements. Scheduling, billing, exports, administration, notifications, additional analytics, AWS and production hosting are also excluded. No later implementation phase is planned.

Historical PHASE1_REVIEW.md through PHASE6_REVIEW.md preserve evidence from their original review dates; their old counts, screenshots and “awaiting review” statements are historical. README.md and docs/DEVELOPMENT.md are the current entry points. Stop after final review delivery; do not commit or push.
