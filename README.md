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
| Bot Protection | Cloudflare Turnstile |
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

Forms reads the permanent denylist of blocked accounts that core writes (core's `docs/account-access-gate.md`). In `enforce` mode every request carrying a validated token is checked before the endpoint runs; anonymous requests are never checked. The gate opens its own connection to the dedicated access Redis, never to the form cache.

`ACCOUNT_ACCESS_GATE_MODE` accepts `off` or `enforce`; `enforce` requires:

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

The mTLS files are required when TLS is on; mount them read-only and never put key contents in an environment variable.

A blocked subject gets `401`. A missing contract, malformed marker, timeout, or Redis failure gets `503` with `Retry-After`, without reaching the endpoint. `GET /health/live` never touches Redis; `GET /health/ready` reports `503` until the gate contract reads back.

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
- Reusable component groups
- Anonymous response support; a form whose fields carry `props.identity` (`firstName`, `lastName`, `email`) makes a guest give a name and email, stored with the response
- Guest file uploads and Cloudflare Turnstile verification for signed-out respondents, see [Guest uploads and Turnstile](#guest-uploads-and-turnstile)
- Single or multiple response control
- Redis-backed form and response drafts
- Response and component-group sharing tokens
- XLSX response export
- Mail notifications and pending-response reminders
- Response notifications to core: each new response and each status change is queued in `ResponseNotifications` in the same transaction and posted to core (`POST /v1/forms/{formId}/responses`), which writes the tickets of Event forms; Forms itself knows nothing about Events. Undelivered notifications are retried with backoff for seven days

**Database Models:**

| Table | Description |
|-------|-------------|
| `Forms` | Form definitions, JSONB schema, status, and response settings |
| `Responses` | User responses, the name and email a guest typed into the identity fields, review information, archive state, and timing |
| `Collaborators` | Collaborator roles with a composite user/form key |
| `ComponentGroup` | Reusable form component templates |
| `Workflows` | Workflow header: name, owner, repeat-run setting, and intake |
| `WorkflowVersions` | One frozen graph per version, in draft, published, or archived state |
| `WorkflowNodes` | The forms a version chains, which one starts the flow, and whether each step needs review |
| `WorkflowTransitions` | Routes between nodes, with trigger, JSONB condition, and priority |
| `WorkflowInstances` | One user's run of a workflow, bound to the version it started on |
| `WorkflowSteps` | Each position in a run, with its response and the route chosen out of it |
| `ResponseNotifications` | Response notifications waiting for core; a row is deleted once core accepts it |

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

Publishing freezes a version. Editing a published workflow opens a **new draft** instead of mutating what is live, and an application keeps running on the version it started on, even after a newer one is published and even when the newer one no longer contains the form the applicant is on. A saved graph that **matches the live version** (steps, review settings, positions, routes, priorities and conditions) opens no draft and removes an open one, so a workflow reports unpublished changes only when publishing would change something.

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

### Application journey

The public payloads describe the whole application so the client can show where the applicant stands. `GET /api/forms/{id}` and `POST /api/forms/responses` carry a `workflow` block in `data` whenever the form belongs to a published workflow:

```json
"workflow": {
  "title": "Ekip Başvurusu 2026",
  "maxSteps": 3,
  "route": [
    { "stage": 1, "formTitle": "Ön Başvuru", "status": "submitted", "requiresManualReview": false, "certain": true, "submittedAt": "2026-09-12T14:32:00Z" },
    { "stage": 2, "formTitle": "Teknik Görüşme Formu", "status": "current", "requiresManualReview": true, "certain": true },
    { "stage": 3, "formTitle": "Üyelik Bilgileri", "status": "upcoming", "requiresManualReview": false, "certain": true }
  ]
}
```

`route` has three parts: the steps the application already took, read from its own steps with `submittedAt`, `reviewedAt` and `reviewNote`; the step it is waiting on; and the steps it is expected to take next. Expected steps follow the approval route of a reviewed step and the submission route of any other, taking the default route where several exist. **None of them is promised**: `certain` turns `false` as soon as a route depends on an answer, and an upcoming step whose form depends on the answer has `formTitle: null`. `maxSteps` is the longest route still possible from the current step across every trigger, so the client can draw the remaining steps without inventing a total.

| `status` | Meaning |
|----------|---------|
| `submitted` | Answered on a step without review |
| `approved` / `declined` | Reviewed; `reviewedAt` and `reviewNote` are set |
| `inReview` | Answered and waiting for review |
| `current` | The form the applicant fills in next |
| `upcoming` | Expected later; see `certain` |

A finished application has no upcoming steps, and a refused submission carries no `workflow` block.

A workflow that lets people apply again keeps the **last result** in view. When the applicant's previous application ended (completed or declined) and intake is open, the start form's display payload adds `lastRun`, so the client can show how it ended before opening the new form:

```json
"lastRun": {
  "state": 4,
  "reviewNote": "Bu dönem yazılım ekibinde yer kalmadı.",
  "reviewedAt": "2026-09-24T16:20:00Z",
  "workflow": {
    "title": "Ekip Başvurusu 2026",
    "maxSteps": 2,
    "route": [
      { "stage": 1, "formTitle": "Ön Başvuru", "status": "submitted", "requiresManualReview": false, "certain": true, "submittedAt": "2026-09-12T14:32:00Z" },
      { "stage": 2, "formTitle": "Teknik Görüşme Formu", "status": "declined", "requiresManualReview": true, "certain": true, "submittedAt": "2026-09-20T10:05:00Z", "reviewedAt": "2026-09-24T16:20:00Z", "reviewNote": "Bu dönem yazılım ekibinde yer kalmadı." }
    ]
  }
}
```

`state` is `3` for a completed application and `4` for a declined one, and `workflow` is that application's own journey. A faulted application adds nothing, so it never stands between the applicant and a new start.

Forms used on their own get the smaller part of the same information. The display payload's `form.requiresManualReview` tells the client whether an answer goes to review; answered states (`201`, `600`, `601`, `602`) add `formTitle` and `submittedAt`; `401` and `410` add `formTitle`, so every status screen can name the form. A signed-in user who already answered a form that takes one response gets `201` or the review status, never the form again.

### Intake

`FormWorkflow.Intake` decides who may still move through a workflow. It lives on the workflow rather than on a version, so a change takes effect at once without publishing and also reaches applications bound to older versions.

| Value | New applications | Running applications |
|-------|------------------|----------------------|
| `Open` (0) | Start as usual | Continue |
| `NewRunsClosed` (1) | Refused with `newRunsClosed` | Continue |
| `Closed` (2) | Refused with `workflowClosed` | Stopped with `workflowClosed` at the next form to fill in |

Displaying or submitting a refused form returns `410` with `reason`, `stage`, and `startFormId` in `data`. `stage` is `0` for someone who has not started and the current step's sequence for a stopped application, which is how the client tells the two apart. A refused submission saves nothing.

The start form checks in a fixed order: steps the user may not open yet, then how a finished application ended, then intake, and only then shows the form. A user who may run the workflow once therefore still sees their outcome after it closes; one who may run it again gets the form with `lastRun` while intake is open (see [Application journey](#application-journey)).

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

## Signed-in answer files

A signed-in respondent's browser uploads a file answer to core itself, as `answer_file`: private, encrypted, malware-scanned and the respondent's own. Core purges an `answer_file` that no record links within 24 hours, so Forms links each one in core (`POST /v1/media/{id}/attachments`, role `answer`, on the respondent's behalf) for as long as something in Forms holds it:

- **Draft.** Saving a response draft links its file answers to the draft (`owner.type` `draft`, `owner.id` `<formId>:<userId>`). Taking the file out of the draft, deleting the draft, submitting, or a form change that clears drafts removes that link. A draft that expires in Redis says nothing, so a daily check finds it.
- **Response.** Saving a signed-in response links its file answers to the response, written in the same transaction as the response: on a form of its own, on a workflow step, and in the provisional response a timed form builds from the draft when time runs out. Removing a provisional response (more time given, or a later submission) removes its links.

Forms keeps each link in `AnswerFileLinks` with core's attachment id, which removing the link needs. `AnswerFileLinkWorker` applies them every 10 seconds and retries a failure with a growing delay of up to an hour, so a core outage delays a link but loses none; a file that was linked once stays in core for 30 days after its last link goes. A file core no longer has, one uploaded without a purpose (`legacy`), someone else's, or one the scan rejected is not linked.

On submit Forms reads each file answer from core and refuses one that is not the respondent's `answer_file` (`fileExpired`), that is still scanning (`fileScanning`, `409`) or that the scan rejected (`fileRejected`). A `legacy` file, uploaded before answer files were private, is accepted as before and not linked.

## Guest uploads and Turnstile

Signed-out respondents (guests) can attach files on forms that take answers without sign-in (`allowAnonymousResponses`). A guest's browser never sends the file to core. It sends it to Forms, which checks the guest's upload session, uploads the file to core with its own service account (purpose `answer_file_guest`: private, encrypted and malware-scanned) and links it to the response in core when the answer is saved. Signed-in respondents upload to core themselves; see [Signed-in answer files](#signed-in-answer-files).

Cloudflare Turnstile guards the two guest entry points: opening an upload session (action `guest-upload`) and submitting an answer (action `guest-submit`).

### When it is active

Guest uploads are **active** only when `FORMS_GUEST_UPLOADS=on` **and** `TURNSTILE_SECRET_KEY` is set. With the mode on and no secret, Forms logs a warning at startup and keeps guest uploads off, because a session without verification would let anyone push files into core through the Forms service account. Forms logs the effective state (Turnstile on or off, guest uploads on or off) once at startup.

- `GET /api/forms/guest-uploads` returns `{ "maxBytes": 52428800, "types": ["application/pdf", "image/jpeg", "image/png"] }` while guest uploads are active and `data: null` otherwise. The form editor reads it to decide whether an anonymous form may have file questions.
- `GET /api/forms/{id}` adds the same object as `guestUploads` for a form that takes signed-out answers.
- While guest uploads are active, anonymous forms may have file questions. While they are off, saving such a form is refused as before.

### Flow

1. **Session.** `POST /api/forms/{id}/guest-uploads/sessions` with `{ "turnstileToken": "..." }` returns `{ sessionId, expiresAt }`. A session belongs to one form and lives `FORMS_GUEST_UPLOAD_SESSION_MINUTES`.
2. **Upload.** `POST /api/forms/{id}/guest-uploads` as `multipart/form-data` with `questionId` and `file`, the session in the `X-Guest-Upload-Session` header. Forms checks the question, the size and type, and the counters, uploads the file to core and answers with the media `id` and `status: "scanning"`.
3. **Scan.** `GET /api/forms/{id}/guest-uploads/{mediaId}` with the same header answers `status` `scanning`, `ready`, `rejected` (with `scanResult`) or `missing` (core no longer has the file).
4. **Submit.** The answer to a file question is the media id. `POST /api/forms/responses` carries `turnstileToken` and, when it has file answers, `guestUploadSession`. Forms accepts a file only when the session holds it for that question and core reports it clean and unattached. It links the files to the new response id in core **before** saving; if the save fails, it removes those links again, and after a successful save the session forgets the files.

### Limits

| Limit | Value |
|-------|-------|
| File size | 50 MiB, or the question's `maxSize` when that is smaller |
| File types | PDF, JPEG and PNG, narrowed by the question's `acceptedFiles`. Core checks the content again |
| Request body | 52 MiB, larger bodies get `413` |
| Files per session | `FORMS_GUEST_UPLOAD_SESSION_MAX_FILES` (10) |
| Sessions per client address and minute | `FORMS_GUEST_UPLOAD_IP_SESSIONS_PER_MINUTE` (30) |
| Files per client address and minute | `FORMS_GUEST_UPLOAD_IP_FILES_PER_MINUTE` (60) |
| Guest files per form and minute | `FORMS_GUEST_UPLOAD_FORM_FILES_PER_MINUTE` (200) |

The counters run on clock minutes, so `retryAfterSeconds` is at most 60. Short windows let a burst through (an event where everyone uploads at once from one network) and keep anyone over a limit waiting seconds, not an hour. Sessions are counted **before** Turnstile is asked, so a flood of session requests never reaches Cloudflare. A client address counts as itself for IPv4 and as its `/64` for IPv6, and is kept only as a hash. Every guest upload is also charged to the Forms service account's budget in core (100 uploads per 10 minutes and 2 GiB a day by default, shared by all guests; at 50 MiB a file that is about 40 files a day); a refusal there reaches the guest as `tooManyUploads` with core's `retryAfterSeconds`.

### Guest answers

Every guest answer is counted per client address and per form, with or without files, **before** Turnstile is asked: an answer refused here never spends its token, and a flood never reaches Cloudflare.

| Limit | Verified answer | Answer accepted without verification |
|-------|-----------------|--------------------------------------|
| Per client address and minute | `FORMS_GUEST_SUBMIT_IP_PER_MINUTE` (300) | `FORMS_GUEST_UNVERIFIED_IP_PER_MINUTE` (20) |
| Per form and minute | `FORMS_GUEST_SUBMIT_FORM_PER_MINUTE` (1000) | `FORMS_GUEST_UNVERIFIED_FORM_PER_MINUTE` (60) |
| All forms per minute | none | `FORMS_GUEST_UNVERIFIED_PER_MINUTE` (300) |

The verified limits are wide because an event's check-in form can get a few hundred answers within a minute from one network. An answer accepted without verification (see [Failure behavior](#failure-behavior)) is counted again against the tight limits, because during an outage nothing else stops a script. Over a limit, the answer is refused with `429` `tooManySubmissions` and `retryAfterSeconds`; the frontend waits and sends it again by itself.

When Redis cannot be reached, verified answers skip the counters, so a Redis outage does not lose answers, and unverified answers are refused with `503` `submitUnavailable`, because nothing would bound them.

An answer accepted without verification is saved as **`Flagged`** (`5`), whatever the form's review setting, and the guest is told it is under review (`PendingApproval`). Nobody checked that a person sent it or that the typed address belongs to them, so until a reviewer decides:

- it gets **no response copy mail**;
- it is reported to core as `pending`, so an Event form writes no ticket for it;
- it counts as waiting in the response counts and the pending reminders, and has its own status in the list filter.

Approving it sends the usual approval mail and reports it to core as `accepted`. Declining it, or archiving it (which declines it), sends **no mail**, because the address may be someone else's. A reviewer cannot move an answer into `Flagged`.

### Reason codes

Refusals use the usual envelope with the code in `data.reason`. Submit refusals also name the `questionId` and, where it applies, `scanResult` and `retryAfterSeconds`.

| `reason` | Status | When |
|----------|--------|------|
| `verificationFailed` | 400 | Turnstile refused the token, or none was sent |
| `guestUploadsDisabled` | 403 | Guest uploads are off, or the form takes no signed-out answers or has no file question. On submit: a file answer while guest uploads are off |
| `guestUploadsUnavailable` | 503 | Redis or core cannot be reached, or Cloudflare while a session opens. `retryAfterSeconds` is 5 unless core sent its own |
| `sessionExpired` | 410 | The session is missing, expired or belongs to another form |
| `tooManySessions` | 429 | The client address opened too many sessions this minute |
| `tooManyUploads` | 429 | The client address or the form reached its file count for this minute, or core's budget refused |
| `tooManySubmissions` | 429 | The client address or the form reached its answer count for this minute, see [Guest answers](#guest-answers) |
| `submitUnavailable` | 503 | An answer that would be accepted without verification while Redis cannot be reached. `retryAfterSeconds` is 5 |
| `sessionFileLimit` | 429 | The session used up its file count |
| `questionNotFound` | 400 | `questionId` is not a top-level file question of the form |
| `fileEmpty` | 400 | No file, or an empty one |
| `fileTooLarge` | 400 | Over the limit; on upload, `maxBytes` gives the limit that applied |
| `fileTypeNotAllowed` | 400 | Neither the extension nor the declared type is allowed, or core detected another type |
| `fileNameInvalid` | 400 | Core refused the file name |
| `fileScanning` | 409 | On submit: the scan has not finished |
| `fileRejected` | 400 | On submit: the scan rejected the file |
| `fileExpired` | 410 | On submit: the session does not hold the file, core purged it, or it is already linked. The status check answers `404` with this code for a file the session does not hold |

A closed form answers `410` with `reason: "closed"`, as the display payload does.

### Failure behavior

Uploads **fail closed**. Sessions and counters live in Redis, and every Redis call has a one-second budget; when Redis or core cannot be reached, the upload endpoints answer `503` instead of letting a file through unchecked. Opening a session also needs Cloudflare to answer.

Submit verification **fails open**. When Cloudflare does not answer after one retry (about six seconds in total), answers with a `5xx` or reports `internal-error`, Forms logs a warning and accepts the answer without verification, so a Cloudflare outage does not lose guest answers. A token Cloudflare refuses is always rejected, and so is every answer while siteverify returns a `4xx` (a block or rate-limit page): Cloudflare answered, so that is a refusal, not an outage. Answers accepted without verification face the tight limits in [Guest answers](#guest-answers). An answer with files still needs Redis and core, so its files fail closed.

A guest answer **without a token** is accepted only while Forms itself cannot reach Cloudflare. During a full outage the browser cannot load the widget, so it sends the answer without a token; Forms then checks siteverify itself and accepts the answer only when that check gets no reply. When Cloudflare answers, the missing token is the browser's own problem (an ad blocker, a firewall) and the answer is refused with `verificationFailed`, so leaving the token out never skips verification. The result of that check is kept for 60 seconds while Cloudflare is reachable and for 15 seconds while it is not; answers that arrive while it runs wait for that one check instead of starting their own. `GET /api/forms/turnstile` returns `{ enabled, reachable }` from the same check, so the page can tell an outage from a blocked script.

Core purges a guest file that is never linked after 24 hours; Forms runs no cleanup job of its own.

### Reviewing files

| Method | Endpoint | Answers |
|--------|----------|---------|
| `GET` | `/api/admin/forms/responses/{responseId}/files/{mediaId}` | `status` (`ready`, `scanning`, `rejected` or `deleted`), `isPrivate`, name, type, size, and `url` only for a public file |
| `POST` | `/api/admin/forms/responses/{responseId}/files/{mediaId}/link` | A five-minute download link core issues for the reviewer, sent with `Cache-Control: no-store` |

Both follow the rule for viewing the response: a collaborator, `skyforms:*`, or a share token covering the response through `?token=`. The media id must also be the answer of a file question in that response. A link is refused with `scanning` (`409`, with `retryAfterSeconds`), `rejected` (`410`, with `scanResult`), `deleted` (`404`), `subjectInactive` (`403`, core cannot issue a link for the reviewer's account) or `unavailable` (`503`).

### Client address

`UseForwardedHeaders` runs first in the pipeline and takes `X-Forwarded-For` only from `TRUSTED_PROXY_RANGES`. Production sits behind Traefik on a private overlay network that rewrites the header to a single entry. The address feeds only the counters and Turnstile's `remoteip`; nothing is authorized by it.

### Rollout

1. **Core.** `answer_file_guest` needs private media and the malware scanner on that side, and the `forms` service account needs the `media:attach` role on the core client with `aud` `core` in Keycloak. Until then core refuses guest uploads and Forms answers `503`.
2. **Frontend.** Deploy the frontend with `NEXT_PUBLIC_TURNSTILE_SITE_KEY`, so every guest submission carries a token.
3. **Turnstile secret.** Set `TURNSTILE_SECRET_KEY` (and `TURNSTILE_ALLOWED_HOSTNAMES`) on Forms. From then on every guest submission needs a valid token; setting the secret before the frontend sends tokens rejects every guest answer with `verificationFailed`.
4. **Guest uploads.** Set `FORMS_GUEST_UPLOADS=on`.

### Test keys

Cloudflare's test keys work on any host name, `localhost` included. Site keys go to the frontend (`NEXT_PUBLIC_TURNSTILE_SITE_KEY`):

| Site key | Widget |
|----------|--------|
| `1x00000000000000000000AA` | Visible, always passes |
| `2x00000000000000000000AB` | Visible, always fails |
| `1x00000000000000000000BB` | Invisible, always passes |
| `2x00000000000000000000BB` | Invisible, always fails |
| `3x00000000000000000000FF` | Visible, forces an interactive challenge |

Secret keys go to Forms (`TURNSTILE_SECRET_KEY`):

| Secret key | Verification |
|------------|--------------|
| `1x0000000000000000000000000000000AA` | Always passes |
| `2x0000000000000000000000000000000AA` | Always fails |
| `3x0000000000000000000000000000000AA` | Fails as an already spent token |

Test tokens come back with host name `localhost` and action `test`, so with a test secret Forms skips the host name and action checks and logs a warning at startup. **A test secret must never reach production**: `1x0000000000000000000000000000000AA` accepts any dummy token. Outside the `Development` environment Forms therefore refuses to start with a test secret unless `TURNSTILE_ALLOW_TEST_SECRET=true` is set.

> **Invisible mode:** Cloudflare enables the invisible widget only on the condition that the site's privacy policy references the [Turnstile Privacy Addendum](https://www.cloudflare.com/turnstile-privacy-policy/). Add that reference before the frontend switches to an invisible site key.

## API Endpoints

### Forms - Public

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/forms/{id}` | Get a form for display |
| `GET` | `/api/forms/{id}/meta` | Get public form metadata |
| `POST` | `/api/forms/responses` | Submit a response; a guest also sends `turnstileToken` and, with file answers, `guestUploadSession` |
| `GET` | `/api/forms/guest-uploads` | Guest upload capability: `{ maxBytes, types }` while guest uploads are active, `data: null` otherwise |
| `GET` | `/api/forms/turnstile` | Whether Turnstile is on and Forms can reach Cloudflare: `{ enabled, reachable }` |
| `POST` | `/api/forms/{id}/guest-uploads/sessions` | Open a guest upload session with a Turnstile token |
| `POST` | `/api/forms/{id}/guest-uploads` | Upload a guest file (`multipart/form-data` with `questionId` and `file`, header `X-Guest-Upload-Session`) |
| `GET` | `/api/forms/{id}/guest-uploads/{mediaId}` | Scan status of a file in the guest's upload session |
| `POST` | `/api/forms/responses/draft` | Save an authenticated user's response draft and keep its file answers linked in core (see [Signed-in answer files](#signed-in-answer-files)); a draft whose answers are all blank (empty text, list or object, `false`) deletes the stored one instead |
| `GET` | `/api/forms/responses/draft/{formId}` | Get an authenticated user's response draft with its `savedAt`; a stored draft with only blank answers is deleted and answers 404 |
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
| `GET` | `/api/admin/forms/{id}/draft` | Get a form editing draft; a draft that matches the saved form is deleted and answers 404, comparing schemas regardless of property order |
| `POST` | `/api/admin/forms/{id}/draft` | Save a form editing draft |
| `DELETE` | `/api/admin/forms/{id}/draft` | Delete a form editing draft |
| `GET` | `/api/admin/forms/{id}/responses` | List form responses |
| `GET` | `/api/admin/forms/{id}/responses/export` | Export responses to Excel |
| `GET` | `/api/admin/forms/{id}/metrics` | Get form metrics |
| `GET` | `/api/admin/forms/{id}/analytics` | Get answer analytics |
| `GET` | `/api/admin/forms/metrics` | Get service-wide metrics |
| `GET` | `/api/admin/forms/responses/{id}` | Get one response |
| `GET` | `/api/admin/forms/responses/{responseId}/files/{mediaId}` | Metadata of a file answer, see [Reviewing files](#reviewing-files) |
| `POST` | `/api/admin/forms/responses/{responseId}/files/{mediaId}/link` | Five-minute download link for a private file answer |
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
| `PUT` | `/api/admin/workflows/{id}/definition` | Replace the draft graph as a whole; each node may carry an optional canvas `position` `{ x, y }` that is stored and echoed back untouched, and an optional `requiresManualReview` that falls back to the form's own setting; a graph identical to the published version removes the draft instead of storing a copy |
| `GET` | `/api/admin/workflows/{id}/available-forms` | Forms usable as steps, with their review setting and a reason when they are not eligible |
| `POST` | `/api/admin/workflows/{id}/validate` | Report what would block publishing |
| `POST` | `/api/admin/workflows/{id}/publish` | Publish the draft and archive the previous version |
| `GET` | `/api/admin/workflows/{id}/versions` | List every version with its status |
| `DELETE` | `/api/admin/workflows/{id}` | Archive the workflow and close it for good, which stops running applications |

### Component Groups - Admin

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/admin/forms/component-groups` | List the current user's component groups |
| `GET` | `/api/admin/forms/component-groups/{id}` | Get component-group details |
| `POST` | `/api/admin/forms/component-groups` | Create a component group |
| `PUT` | `/api/admin/forms/component-groups/{id}` | Update a component group |
| `DELETE` | `/api/admin/forms/component-groups/{id}` | Delete a component group; the row stays archived and its share token is revoked |
| `POST` | `/api/admin/forms/component-groups/{id}/share` | Create or refresh a share token |
| `POST` | `/api/admin/forms/component-groups/{id}/clone` | Clone a shared component group |

## Authentication & Authorization

- **Incoming bearer validation:** JwtBearer validates tokens against the Keycloak issuer and the `forms` audience. A request without an `Authorization` header is anonymous; one whose header does not validate gets `401`, even on anonymous endpoints, instead of being treated as anonymous.
- **Current user:** The user ID and realm/client roles come from the validated token. Client roles are read from `forms` and the legacy `dotnet` client.
- **External user data:** User details are fetched from core (`Services:Users:BaseUrl`, Compose DNS `http://core:8080`) through an Application abstraction and Infrastructure HTTP adapter.
- **Service authentication:** SkyMail and core calls use a Keycloak client-credentials token. On core's `core` client that service account needs `ticket:forms` for response notifications and `media:attach` for guest uploads, their links to responses and reviewers' read links.
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
| `Authentication__Issuer` (Compose: `AUTH_ISSUER`) | Incoming-token issuer; sandbox uses `https://e.yildizskylab.com/realms/e-skylab-sandbox` | No, defaults to `https://e.yildizskylab.com/realms/e-skylab` |
| `Authentication__Audience` (Compose: `AUTH_AUDIENCE`) | Incoming-token audience | No, defaults to `forms` |
| `Services__Users__BaseUrl` / `CORE_URL` | Core API Compose DNS URL (`GET /v1/users/:id`, Bearer `aud=core` + `users:read`; guest files under `/v1/media` need `media:attach`) | No, defaults to `http://core:8080` |
| `Services__SkyMail__BaseUrl` / `SKYMAIL_URL` | SkyMail Compose DNS URL | No, defaults to `http://skymail:3000/v1/` |
| `ALLOWED_ORIGIN` | CORS allowed origin | No, defaults to `http://localhost:3000` |
| `KEYCLOAK_TOKEN_URL` | Keycloak token endpoint used by Compose | For SkyMail and core calls |
| `KEYCLOAK_CLIENT_ID` | Keycloak service client ID | For SkyMail and core calls |
| `KEYCLOAK_CLIENT_SECRET` | Keycloak service client secret | For SkyMail and core calls |
| `FORMMAIL_FORM_COPY_TEMPLATE_ID` | Submitted-form copy template. A guest's unverified address gets one copy per form and at most `FormMail:GuestCopyDailyLimit` (default 3) a day, counting `+tag` and Gmail dot variants as one inbox | Optional |
| `FORMMAIL_STATUS_CHANGED_TEMPLATE_ID` | Review status template | Optional |
| `FORMMAIL_PENDING_REMINDER_TEMPLATE_ID` | Pending response reminder template | Optional |
| `FORMMAIL_ATTEMPT_UPDATE_TEMPLATE_ID` | Timed task updates: reminder, extension, expiry, accept, close. The team's remind action is refused while it is empty | Optional |
| `FORMS_GUEST_UPLOADS` | Guest file uploads, exactly `off` or `on` (anything else stops startup). Active only together with `TURNSTILE_SECRET_KEY`, see [Guest uploads and Turnstile](#guest-uploads-and-turnstile) | No, defaults to `off` |
| `TURNSTILE_SECRET_KEY` | Cloudflare Turnstile secret. Empty turns Turnstile off: guest submissions are not verified and guest uploads stay off | No |
| `TURNSTILE_ALLOWED_HOSTNAMES` | Comma-separated host names a Turnstile token must come from; empty skips the check. An entry with a scheme or port stops startup | No |
| `FORMS_GUEST_UPLOAD_SESSION_MINUTES` | Guest upload session lifetime in minutes, from 10 to 1440 | No, defaults to `360` |
| `FORMS_GUEST_UPLOAD_SESSION_MAX_FILES` | Files one guest upload session may upload, from 1 to 100 | No, defaults to `10` |
| `FORMS_GUEST_UPLOAD_IP_SESSIONS_PER_MINUTE` | Guest upload sessions one client address may open per minute, from 1 to 10000 | No, defaults to `30` |
| `FORMS_GUEST_UPLOAD_IP_FILES_PER_MINUTE` | Guest files one client address may upload per minute, from 1 to 10000 | No, defaults to `60` |
| `FORMS_GUEST_UPLOAD_FORM_FILES_PER_MINUTE` | Guest files one form may receive per minute, from 1 to 100000 | No, defaults to `200` |
| `FORMS_GUEST_SUBMIT_IP_PER_MINUTE` | Guest answers one client address may send per minute, all forms together, from 1 to 100000 | No, defaults to `300` |
| `FORMS_GUEST_SUBMIT_FORM_PER_MINUTE` | Guest answers one form may receive per minute, from 1 to 100000 | No, defaults to `1000` |
| `FORMS_GUEST_UNVERIFIED_IP_PER_MINUTE` | Guest answers accepted without verification from one client address per minute, from 1 to 100000 | No, defaults to `20` |
| `FORMS_GUEST_UNVERIFIED_FORM_PER_MINUTE` | Guest answers accepted without verification for one form per minute, from 1 to 100000 | No, defaults to `60` |
| `FORMS_GUEST_UNVERIFIED_PER_MINUTE` | Guest answers accepted without verification across all forms per minute, from 1 to 1000000 | No, defaults to `300` |
| `TURNSTILE_ALLOW_TEST_SECRET` | `true` lets a Cloudflare test secret run outside `Development` | No |
| `TRUSTED_PROXY_RANGES` | Comma-separated CIDRs or addresses whose `X-Forwarded-For` is trusted; an invalid entry stops startup | No, defaults to `10.0.0.0/8,172.16.0.0/12,192.168.0.0/16,127.0.0.0/8,::1/128,fc00::/7` |

The guest upload, Turnstile and proxy settings read the environment variable first and then the configuration key: `GuestUploads:Mode`, `GuestUploads:SessionMinutes`, `GuestUploads:SessionMaxFiles`, `GuestUploads:IpSessionsPerHour`, `GuestUploads:IpFilesPerHour`, `GuestUploads:FormFilesPerHour`, `Turnstile:SecretKey`, `Turnstile:AllowedHostnames` and `ForwardedHeaders:TrustedProxyRanges`. A number outside its range stops startup even while guest uploads are off.

Database access uses an automatic retry strategy with five retries and a maximum ten-second delay.

Token validation fails closed when the issuer's discovery document or JWKS cannot be loaded, so the container must reach `${Authentication__Issuer}/.well-known/openid-configuration`.

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
