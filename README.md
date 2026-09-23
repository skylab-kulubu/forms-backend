<div align="center">
  <img src=".github/skylab.svg" alt="SKY LAB Logo" width="80" />
  <h1>SKY LAB Forms API</h1>
  <p>
    The dedicated forms backend powering<br/>
    <strong>Yıldız Technical University - SKY LAB</strong>
  </p>
  <p>
    <img src="https://img.shields.io/badge/.NET-9.0-512BD4?style=for-the-badge&logo=dotnet" alt=".NET" />
    <img src="https://img.shields.io/badge/PostgreSQL-16-4169E1?style=for-the-badge&logo=postgresql&logoColor=white" alt="PostgreSQL" />
    <img src="https://img.shields.io/badge/Redis-7-DC382D?style=for-the-badge&logo=redis&logoColor=white" alt="Redis" />
    <img src="https://img.shields.io/badge/Docker-Ready-2496ED?style=for-the-badge&logo=docker&logoColor=white" alt="Docker" />
  </p>
</div>

<br/>

> A Forms-only backend organized as a single bounded context with strict Clean Architecture dependency rules.

---

## Tech Stack

| Layer | Technology |
|-------|------------|
| Runtime | .NET 9.0 / C# |
| Database | PostgreSQL (EF Core 9 + Npgsql) |
| Cache & Drafts | Redis |
| API | ASP.NET Core Minimal APIs |
| Service Authentication | Keycloak client credentials |
| Excel Export | ClosedXML |
| Documentation | Swagger / OpenAPI |
| Container | Docker (multi-stage build) |

## Architecture

The repository contains one service and one bounded context: **Forms**. The former Feedbacks module, generic Exports module, and Shared projects have been removed. Capabilities that belong to Forms now live in the appropriate Forms layer.

```text
src/
├── Forms.Domain/                          # Entities, enums, domain models and rules
├── Forms.Application/                     # Use cases, contracts, validators and ports
├── Forms.Infrastructure/                  # EF Core, Redis, HTTP clients, mail, Excel, workers
├── Forms.Api/                             # Minimal API endpoints and composition root
└── Forms.sln
```

### Dependency Direction

```text
Forms.Api ───────────────┐
                        v
Forms.Infrastructure -> Forms.Application -> Forms.Domain
```

### Layer Responsibilities

- **Domain** - Forms entities, enums, domain models, and domain behavior. It has no project or framework dependency.
- **Application** - Use-case services, repository/external-service abstractions, request and response contracts, validators, result types, and orchestration. It depends only on Domain.
- **Infrastructure** - EF Core persistence, PostgreSQL migrations, Redis, identity clients, SkyMail integration, Excel generation, and background workers. It implements Application ports.
- **API** - Minimal API endpoints, middleware, Swagger, CORS, and dependency composition.

### Key Patterns

- **Clean Architecture** with inward-only project dependencies
- **DDD-oriented Forms bounded context**
- **Repository and Unit of Work abstractions**
- **Result Pattern** through `ServiceResult<T>`
- **Minimal APIs** with endpoint groups
- **JSONB storage** for flexible form and response schemas
- **Port and Adapter approach** for Redis, identity, mail, and Excel

## Account access gate

Forms can enforce the shared, permanent account-blocking denylist after JwtBearer has
validated a credential and before any endpoint runs. Anonymous requests do not query
the gate, so public form display, metadata/share views, and anonymous response
submission keep working. Supplying a valid credential makes even those optional-auth
routes subject to the gate.

The gate uses its own named StackExchange.Redis connection
(`account-access-gate`). It must point at the dedicated persistent/no-eviction access
Redis deployment; it must never point at `Redis__ConnectionString`, the form/draft
cache, or merely another logical database on that cache process.

`ACCOUNT_ACCESS_GATE_MODE` is required and accepts only `off` or `enforce`. `enforce`
also requires all of the following values:

| Variable | Meaning |
|---|---|
| `ACCOUNT_ACCESS_REDIS_ENDPOINT` | One `host:port`, not a Redis connection string |
| `ACCOUNT_ACCESS_REDIS_USERNAME` | Read-only gate ACL user |
| `ACCOUNT_ACCESS_REDIS_PASSWORD` | Read-only gate ACL password |
| `ACCOUNT_ACCESS_REDIS_DATABASE` | Dedicated gate database, identical across services |
| `ACCOUNT_ACCESS_REDIS_TLS` | Explicit `true` in production; `false` is for local integration tests |
| `ACCOUNT_ACCESS_REDIS_CA_CERT_FILE` | Absolute path to the mounted private CA certificate |
| `ACCOUNT_ACCESS_REDIS_TLS_CERT_FILE` | Absolute path to the mounted Forms client certificate |
| `ACCOUNT_ACCESS_REDIS_TLS_KEY_FILE` | Absolute path to the mounted Forms client private key |
| `ACCOUNT_ACCESS_REDIS_OPERATION_TIMEOUT_MS` | Bounded operation deadline, default `200` (range 50–2000) |
| `ACCOUNT_ACCESS_GATE_RETRY_AFTER_SECONDS` | Bounded unavailable retry hint, default `1` (range 1–30) |

The three mTLS files are mandatory whenever TLS is enabled. Mount them read-only
(for example under `/run/secrets/account-access`) and never place private-key PEM
contents in an environment variable.

Authenticated blocked subjects receive a generic `401`. A missing/wrong contract,
malformed marker, timeout, or Redis failure returns `503` with `Cache-Control:
no-store` and `Retry-After`, without invoking the endpoint. Logs contain only the
decision and request correlation id, never a subject or digest.

- `GET /health/live` is process-only and never queries Redis.
- `GET /health/ready` validates the exact access-gate contract through the gate
  connection and reports non-ready on mismatch or outage.

## Forms Capabilities

Dynamic form creation and response management service.

**Core Features:**

- Form CRUD operations and soft deletion
- JSONB-based flexible form schema support
- Response collection and management
- Multi-step form workflows with conditional branching, see [Form Workflows](#form-workflows)
- Collaborator management with Owner, Editor, and Viewer roles
- Manual review workflow (`Pending -> Approved / Declined`)
- Response archiving
- Form metrics and answer analytics
- Reusable component groups with archive and restore
- Anonymous response support
- Single or multiple response control
- Redis-backed form and response drafts
- Response and component-group sharing tokens
- XLSX response export
- Mail notifications and pending-response reminders

**Database Models:**

| Table | Description |
|-------|-------------|
| `Forms` | Form definitions, JSONB schema, status, and response settings |
| `Responses` | User responses, review information, archive state, and timing |
| `Collaborators` | Collaborator roles with a composite user/form key |
| `ComponentGroup` | Reusable form component templates |
| `Workflows` | Workflow header: name, owner, repeat-run setting, and intake |
| `WorkflowVersions` | One frozen graph per version, in draft, published, or archived state |
| `WorkflowNodes` | The forms a version chains, which one starts the flow, and whether each step needs review |
| `WorkflowTransitions` | Routes between nodes, with trigger, JSONB condition, and priority |
| `WorkflowInstances` | One user's run of a workflow, bound to the version it started on |
| `WorkflowSteps` | Each position in a run, with its response and the route chosen out of it |

## Form Workflows

A workflow chains several forms into one application. The definition is a **directed acyclic graph**: it may branch, but a single application follows **exactly one route** through it and never revisits a form. A route may span at most **three forms**, while the workflow as a whole may hold more.

Routing is decided the moment an answer arrives and is then written to the application. Nothing is recomputed on later reads, so editing a definition can never change the path an application already took.

```text
                      ┌─ answer = A ─> Form 2A ─ approved ─┐
Form 1 ───────────────┤                                     ├─> Form 3
                      └─ otherwise ──> Form 2B ─ approved ──┘
```

### Definition and versions

| Entity | Holds |
|--------|-------|
| `FormWorkflow` | Name, owner, whether one user may run the flow more than once, and whom it lets in |
| `FormWorkflowVersion` | One frozen copy of the graph, in `Draft`, `Published`, or `Archived` |
| `FormWorkflowNode` | One step: the form it shows, its `NodeKey`, whether it starts the flow |
| `FormWorkflowTransition` | One route out of a node: trigger, optional condition, priority, target |

Publishing freezes a version. Editing a published workflow opens a **new draft** instead of mutating what is live, and an application keeps running on the version it started on, even after a newer one is published and even when the newer one no longer contains the form the applicant is on.

Steps are addressed by **`NodeKey`**, not by id. Node ids are regenerated for every version, so conditions that point at an earlier step survive a new draft.

### Triggers and routes

| Trigger | Fires when |
|---------|------------|
| `ResponseSubmitted` (0) | The answer is saved and the form needs no review |
| `ResponseApproved` (1) | A reviewer approves the answer |
| `ResponseDeclined` (2) | A reviewer declines the answer |

A node's own review setting decides which triggers are legal: a step that requires review may only route on approval or decline, and a step that does not may only route on submission. Allowing both would pick a route twice for the same step and leave the application on two branches at once.

Within one trigger, transitions are evaluated by ascending `Priority` and the first matching condition wins. **A transition with no condition is the default route** and is always evaluated last, whatever its priority. A trigger with no transitions at all ends the flow, so a terminal step needs no configuration.

> **A conditional group needs a default route.** Publishing fails without one, because an answer that matches nothing would otherwise leave the application with nowhere to go.

### Conditions

A condition reads the answer snapshot, never the live form. Rules address a question by id, optionally in an earlier step through `nodeKey`:

```json
{
  "operator": 0,
  "rules": [
    { "questionId": "department", "comparison": 0, "value": "engineering" },
    { "nodeKey": "stage1", "questionId": "experience", "comparison": 6, "value": "2" }
  ]
}
```

`operator` is `0` for **all** and `1` for **any**.

| Value | Comparison | Reads |
|-------|------------|-------|
| 0, 1 | `Equals`, `NotEquals` | The whole answer, trimmed and case-insensitive |
| 2, 3 | `In`, `NotIn` | `values`, against the whole answer and against the answer split into its selections |
| 4 | `Contains` | `value` as a substring of the answer |
| 5-8 | `GreaterThan`, `GreaterThanOrEqual`, `LessThan`, `LessThanOrEqual` | Both sides parsed as numbers |
| 9, 10 | `IsEmpty`, `IsNotEmpty` | Only whether an answer exists |

A multiple-choice answer is split from a JSON array (`["react","vue"]`) when it looks like one, and from a comma-separated list otherwise. Because answers are stored as the option's visible text, a condition compares labels, not option ids: a label a condition reads cannot be renamed without breaking the route, which is why the form contract reports those labels as locked.

Answers are stored as text, so numeric comparisons parse both sides with a decimal point. A single comma with no dot is read as a decimal separator, so `3,5` is 3.5, while anything ambiguous such as `1.234,5` fails to parse. **A rule that cannot read its answer evaluates to false instead of throwing**, so one malformed answer can never break routing for everyone else.

> **Enums travel as numbers but are stored as names.** Requests and responses use the numeric value, matching the rest of this API. The `Condition` column is jsonb and keeps the names (`"comparison": "greaterThanOrEqual"`) so a stored definition stays readable.

### Applications

| Entity | Holds |
|--------|-------|
| `FormWorkflowInstance` | One user's run: the version it is bound to, its status and outcome |
| `FormWorkflowStep` | One position in that run: node, response, previous step, chosen transition, sequence |

A step with no `CompletedAt` is the step the application is waiting on. When that step already carries a response, the application is **waiting for review**. There is no separate status for that, so the two can never disagree.

Opening the workflow's **start form** resumes an application at whatever step it reached. Opening another node's form directly is refused, so a shared link cannot skip a step or enter a branch that was never chosen.

### Intake

`FormWorkflow.Intake` decides who may still move through a workflow. It lives on the workflow rather than on a version, so a change takes effect at once without publishing and also reaches applications bound to older versions.

| Value | New applications | Running applications |
|-------|------------------|----------------------|
| `Open` (0) | Start as usual | Continue |
| `NewRunsClosed` (1) | Refused with `newRunsClosed` | Continue |
| `Closed` (2) | Refused with `workflowClosed` | Stopped with `workflowClosed` at the next form to fill in |

Displaying or submitting a refused form returns `410` with `reason`, `stage`, and `startFormId` in `data`. `stage` is `0` for someone who has not started and the current step's sequence for a stopped application, which is how the client tells the two apart. A refused submission saves nothing.

The start form checks in a fixed order: steps the user may not open yet, then how a finished application ended, then intake, and only then shows the form. A user who may run the workflow once therefore still sees their outcome after it closes.

Closing writes nothing to the applications and does not stop review, so answers already waiting can still be approved or declined. An applicant whose step is under review keeps seeing it as under review and is stopped only when the next form would open. Reopening the workflow lets every application continue from the step it reached. Archiving closes a workflow for good: its intake can no longer change.

### What the database enforces

These are constraints rather than checks in code, so a race cannot get past them.

| Constraint | Prevents |
|------------|----------|
| One step per application with `CompletedAt IS NULL` | Two branches running at once |
| `WorkflowSteps.ResponseId` unique | A repeated submission opening a second step |
| One `Active` application per workflow and user | Two concurrent first submissions both starting a run |
| One `Published` and one `Draft` version per workflow | Ambiguity about which definition is live |
| Unique `(version, source node, trigger, priority)` | Two routes tied at the same priority |
| One node per form and one start node per version | An answer that belongs to no single step |
| `xmin` on the application | A lost update when two requests advance the same run |

> **Order matters when saving.** Closing a step and opening the next one are two saves inside one transaction, because a single save would let the provider write the insert before the update and trip the one-open-step index. The same applies to archiving a published version before promoting a draft.

### Publishing

`POST /api/admin/workflows/{id}/publish` validates before it promotes anything. A draft that does not validate leaves the live version untouched and returns every finding at once, and a draft may be saved in any state so the editor can work on an unfinished graph.

Publishing refuses a definition that has no start node or more than one, contains a cycle, has a route longer than three forms, leaves a node unreachable, points a route at a node that does not exist, mixes a trigger with the wrong review setting, leaves a conditional group without a default route, ties two routes at one priority, or reads a question the form does not have.

A condition may only read a step that lies on **every** route to the step being left. Reading a step that some routes skip would silently evaluate against an answer that was never given.

The forms themselves must also hold up: each must exist, be open, reject anonymous answers because a run belongs to a signed-in user, and name the workflow owner as an Owner. A form may belong to at most one published workflow, or an answer could not say which run it belongs to.

### Forms used by a published workflow

Publishing locks the parts of a form that routing depends on. While the workflow is live you may still add questions, fix wording, and reorder the schema, but you cannot remove a question a condition reads, open the form to anonymous answers, close it, or delete it. A workflow is stopped through its intake, not by closing its forms one by one.

The review setting is not locked because a step carries its own. The form's value applies only when the form is used on its own, and it is the default for a step added without one. Turning review on in a new version leaves running applications alone, since each stays on the version it started on.

One lock the backend cannot enforce: **the option labels a condition compares against**. Answers are stored as the option's visible text and the backend never reads the option list out of a question's `props`, so renaming an option silently stops the condition from matching. The form contract reports those labels so the editor can protect them.

> **Legacy:** `Forms.LinkedFormId` and the `step` and `linkedFormId` fields in the public payloads survive from the older two-form chaining. Nothing reads the column any more, and the payload fields are filled only for two-step workflows so the previous client keeps working. Both go away once the frontend reads `state` and `stage`.

## API Endpoints

### Forms - Public

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/forms/{id}` | Get a form for display |
| `GET` | `/api/forms/{id}/meta` | Get public form metadata |
| `POST` | `/api/forms/responses` | Submit a response |
| `POST` | `/api/forms/responses/draft` | Save an authenticated user's response draft |
| `GET` | `/api/forms/responses/draft/{formId}` | Get an authenticated user's response draft |
| `DELETE` | `/api/forms/responses/draft/{formId}` | Delete an authenticated user's response draft |
| `GET` | `/api/forms/component-groups/{id}/meta` | Get shared component-group metadata |
| `GET` | `/api/forms/responses/{id}/meta` | Get shared response metadata |

### Forms - Admin

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/admin/forms/` | List the current user's forms; `SortBy` accepts `updatedAt`, `status`, `responseCount`, `userRole`, `workflow`. `AllowMultiple` and `RequiresManualReview` match a form in a published workflow on the workflow's and the step's settings |
| `GET` | `/api/admin/forms/all` | List all forms for service administrators, with the same `workflow` reference and filters as the user's list |
| `POST` | `/api/admin/forms/` | Create a form |
| `GET` | `/api/admin/forms/{id}` | Get form details; `workflow` carries the workflow's `allowMultipleRuns` and the step's `requiresManualReview` |
| `PUT` | `/api/admin/forms/{id}` | Update a form |
| `DELETE` | `/api/admin/forms/{id}` | Soft-delete a form |
| `GET` | `/api/admin/forms/{id}/info` | Get form summary information |
| `GET` | `/api/admin/forms/{id}/draft` | Get a form editing draft |
| `POST` | `/api/admin/forms/{id}/draft` | Save a form editing draft |
| `DELETE` | `/api/admin/forms/{id}/draft` | Delete a form editing draft |
| `GET` | `/api/admin/forms/{id}/responses` | List form responses |
| `GET` | `/api/admin/forms/{id}/responses/export` | Export responses to Excel |
| `GET` | `/api/admin/forms/{id}/metrics` | Get form metrics |
| `GET` | `/api/admin/forms/{id}/analytics` | Get answer analytics |
| `GET` | `/api/admin/forms/metrics` | Get service-wide metrics |
| `GET` | `/api/admin/forms/responses/{id}` | Get one response |
| `POST` | `/api/admin/forms/responses/{id}/share` | Create or refresh a response share token |
| `POST` | `/api/admin/forms/responses/{id}/revoke-token` | Revoke a response share token |
| `PATCH` | `/api/admin/forms/responses/{id}/status` | Update response review status |
| `POST` | `/api/admin/forms/responses/{id}/archive` | Archive a response |

### Workflows - Admin

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/admin/workflows` | List the current user's workflows, paged; accepts `Page`, `PageSize`, `Search`, `SortDirection`, and `ShowArchived`, which must be `true` for archived workflows to appear |
| `POST` | `/api/admin/workflows` | Create a workflow with an empty draft |
| `GET` | `/api/admin/workflows/{id}` | Get the workflow with its draft and published versions and the number of running applications (`activeRunCount`) |
| `PUT` | `/api/admin/workflows/{id}` | Update name, description, and repeat-run setting |
| `PUT` | `/api/admin/workflows/{id}/intake` | Set the intake (`0` open, `1` closed to new applications, `2` closed) from a body such as `{ "intake": 1 }` |
| `PUT` | `/api/admin/workflows/{id}/definition` | Replace the draft graph as a whole; each node may carry an optional canvas `position` `{ x, y }` that is stored and echoed back untouched, and an optional `requiresManualReview` that falls back to the form's own setting |
| `GET` | `/api/admin/workflows/{id}/available-forms` | Forms usable as steps, with their review setting and a reason when they are not eligible |
| `POST` | `/api/admin/workflows/{id}/validate` | Report what would block publishing |
| `POST` | `/api/admin/workflows/{id}/publish` | Publish the draft and archive the previous version |
| `GET` | `/api/admin/workflows/{id}/versions` | List every version with its status |
| `DELETE` | `/api/admin/workflows/{id}` | Archive the workflow and close it for good, which stops running applications |

### Component Groups - Admin

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/admin/forms/component-groups?lifecycle=current|inactive|all` | List the current user's component groups; defaults to `current` |
| `GET` | `/api/admin/forms/component-groups/{id}` | Get component-group details |
| `POST` | `/api/admin/forms/component-groups` | Create a component group |
| `PUT` | `/api/admin/forms/component-groups/{id}` | Update a component group |
| `DELETE` | `/api/admin/forms/component-groups/{id}` | Idempotently archive a component group (`204`) |
| `POST` | `/api/admin/forms/component-groups/{id}/restore` | Idempotently restore an owned component group |
| `POST` | `/api/admin/forms/component-groups/{id}/share` | Create or refresh a share token |
| `POST` | `/api/admin/forms/component-groups/{id}/clone` | Clone a shared component group |

Archived component groups are hidden from ordinary detail, share, clone, and metadata reads. Only the owner can include them through the explicit `inactive` or `all` management filter. Archive metadata is returned as `archivedAt` and `archivedBy`. Archiving revokes the Redis-backed share token; restoring the durable group does not restore that ephemeral token, so the owner must create a new share link.

Component groups currently have neither a parent lifecycle dependency nor a unique business key: titles are intentionally reusable. Restore therefore revalidates ownership but does not invent a title-conflict rule. A future invariant that can genuinely block restoration must return an explicit conflict instead of partially restoring the record.

## Authentication & Authorization

- **Incoming bearer validation:** If an `Authorization` header is present, it must contain exactly one canonical `Bearer <token>` credential. Unsupported schemes, blank/odd formatting, combined or duplicate credentials, unsigned/malformed tokens, and tokens with the wrong issuer, audience, lifetime, or signing key return `401`, including on Swagger and otherwise anonymous endpoints. ASP.NET Core JwtBearer validates accepted credentials through Keycloak discovery/JWKS before any identity claim is used.
- **Current user:** The API resolves the current user ID and realm/client roles only from the authenticated `HttpContext.User`. Existing `forms`/legacy `dotnet` client-role mapping remains supported after validation.
- **Anonymous access:** Public form reads, Swagger, anonymous response submission, and credential-free CORS preflight still work when the request has no `Authorization` header. Invalid credentials are never downgraded to anonymous access. Credential validation runs before CORS and docs; a rejected request applies the configured CORS policy before returning its challenge so the allowed frontend can still read the `401`.
- **External user data:** User details are fetched from core (`Services:Users:BaseUrl`, Compose DNS `http://core:8080`) through an Application abstraction and Infrastructure HTTP adapter.
- **Service authentication:** SkyMail calls use a Keycloak client-credentials token.
- **Authorization:** Role-based rules are enforced in the Application services:
  - **Owner** - Full control and collaborator management
  - **Editor** - Edit forms and manage responses
  - **Viewer** - Read-only access

## Getting Started

### Prerequisites

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [PostgreSQL](https://www.postgresql.org/download/)
- [Redis](https://redis.io/)

### Environment Setup

Create the local Compose environment file from the tracked template:

```powershell
# PowerShell
Copy-Item .env.example .env
```

```bash
# Bash
cp .env.example .env
```

Edit `.env` before starting Docker. The local `.env` file is ignored by Git.

### Running Locally

Provide `CONNECTION_STRING`, `Redis__ConnectionString`, and any required integration configuration through environment variables or an ignored `src/Forms.Api/appsettings.Development.json`.

```bash
# Restore dependencies
dotnet restore src/Forms.sln

# Build
dotnet build src/Forms.sln

# Run the application
dotnet run --project src/Forms.Api
```

Database migrations run automatically on startup. Swagger UI is available at the application's `/swagger` path.

### Docker Compose

```bash
docker compose up --build
```

The Compose file runs only the Forms API. Start shared Postgres and Redis first (`core-backend` `make data-up`, network `skylab`). Internal URLs are Compose DNS (`postgres:5432`, `redis:6379`, `http://core:8080`, `http://skymail:3000`).

### Docker Image

```bash
docker build -f src/Dockerfile -t skylab-forms-api src
```

## Configuration

| Variable | Description | Required |
|----------|-------------|----------|
| `CONNECTION_STRING` | PostgreSQL connection string for local/non-Compose execution | Yes |
| `Redis__ConnectionString` | Redis connection string (logical DB 1 on the shared instance) | No, defaults to `localhost:6379,defaultDatabase=1` |
| `Authentication__Issuer` (Compose: `AUTH_ISSUER`) | Incoming-token issuer. Production uses `https://e.yildizskylab.com/realms/e-skylab`; sandbox must explicitly use `https://e.yildizskylab.com/realms/e-skylab-sandbox`. Every other value is rejected at startup. | No, defaults to production issuer |
| `Authentication__Audience` (Compose: `AUTH_AUDIENCE`) | Incoming-token audience; startup rejects every value except `forms` | No, fixed default |
| `Authentication__ClockSkewSeconds` (Compose: `AUTH_CLOCK_SKEW_SECONDS`) | Allowed JWT lifetime skew, from 0 to 120 seconds | No, defaults to `30` |
| `Authentication__MetadataTimeoutSeconds` (Compose: `AUTH_METADATA_TIMEOUT_SECONDS`) | Timeout for Keycloak discovery/JWKS HTTP operations, from 1 to 30 seconds | No, defaults to `5` |
| `Services__Users__BaseUrl` / `CORE_URL` | Core API Compose DNS URL (`GET /v1/users/:id`, Bearer `aud=core` + `users:read`) | No, defaults to `http://core:8080` |
| `Services__SkyMail__BaseUrl` / `SKYMAIL_URL` | SkyMail Compose DNS URL | No, defaults to `http://skymail:3000/v1/` |
| `ALLOWED_ORIGIN` | CORS allowed origin | No, defaults to `http://localhost:3000` |
| `KEYCLOAK_TOKEN_URL` | Keycloak token endpoint used by Compose | For SkyMail and core user lookup |
| `KEYCLOAK_CLIENT_ID` | Keycloak service client ID | For SkyMail and core user lookup |
| `KEYCLOAK_CLIENT_SECRET` | Keycloak service client secret | For SkyMail and core user lookup |
| `FORMMAIL_FORM_COPY_TEMPLATE_ID` | Submitted-form copy template | Optional |
| `FORMMAIL_STATUS_CHANGED_TEMPLATE_ID` | Review status template | Optional |
| `FORMMAIL_PENDING_REMINDER_TEMPLATE_ID` | Pending response reminder template | Optional |

Database access uses an automatic retry strategy with five retries and a maximum ten-second delay.

Incoming JWT validation fails closed when the configured Keycloak discovery document or JWKS cannot be loaded. Before deployment, verify that the Forms container can reach `${Authentication__Issuer}/.well-known/openid-configuration` and the `jwks_uri` it publishes. Production and sandbox each accept only the issuer explicitly selected for that deployment.

## Database Migrations

```bash
dotnet ef migrations add MigrationName \
  --project src/Forms.Infrastructure \
  --startup-project src/Forms.Api
```

## Validation

```bash
dotnet build src/Forms.sln -c Release
dotnet list src/Forms.sln package --vulnerable --include-transitive
```

See [FRONTEND.md](FRONTEND.md) for the client-side contracts and [CONTRIBUTING.md](CONTRIBUTING.md) for architectural boundaries and contribution rules.
