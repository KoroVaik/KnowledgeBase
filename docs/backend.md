# Backend (`KnowledgeBase.Api`)

The HTTP API and SPA host. The only project that ships to prod. See
[`architecture.md`](architecture.md) for the process split and constraints,
[`database.md`](database.md) for the EF model, [`infra.md`](infra.md) for deployment.

## Decisions

### Structure — vertical slices

`Controllers/<feature>/` holds the controller at the folder root, with subfolders for
its helpers (`Contracts/`, `Services/`, `Configuration/`, later `Validators/`). Anything
shared by no single controller lives in `Infrastructure/` (`Hosting/`, `Features/`).
To change one feature you open one folder, DI registration included. `Program.cs` is
~30 lines — a list of features.

- A mid-way layout that split by layer at the top level (`Endpoints/` + `Contracts/` +
  `Services/`) was rejected: across 30 endpoints each feature would be smeared over five
  folders.
- Named `Controllers/` (not `Features/`) on purpose — matches the owner's work project.
  ASP.NET does not care where controllers sit; the folders are for humans.

### MVC controllers, not minimal API

Decision 2026-09-06. `[ApiController]`, `[Route("api/[controller]")]`, `[Authorize]` on
the class, `[AllowAnonymous]` on `login`. Reason: same style as the owner's job — habits
read both ways.

- `LowercaseUrls = true`: the `[controller]` token takes the class name verbatim, so the
  schema was emitting `/api/Auth/login`. Routing is case-insensitive so calls worked, but
  a generated client would carry that casing.
- **Minimal-API trap, kept as a note if we ever go back:** a handler whose only parameter
  is `HttpContext` matches the `RequestDelegate` overload of `MapPost`, and the returned
  `IResult` is silently dropped — `logout` would return 200 with an empty body instead of
  204. Caught by analyzer `ASP0016`, fixed with a `(Delegate)` cast. Controllers do not
  have this trap.

### Swagger docs

`GenerateDocumentationFile` + `IncludeXmlComments`, `///` on controllers and actions,
`[ProducesResponseType]` with types. `NoWarn 1591` so the compiler does not demand `///`
on every public type — only endpoints need it. **The `///` comments feed Swagger; keep
them.** (The comment-trimming policy in `CLAUDE.md` is about `//` noise, not these.)

### Real-time updates — SSE

Server-Sent Events, not WebSocket: the channel is one-way, and SSE is a plain GET the
server just never finishes, with `EventSource` reconnecting on its own.

- `Infrastructure/RealTime/` (contract in `Core/RealTime/`): `IChangeNotifier`
  (singleton) fans `ChangeEvent(Resource, Action, Id)` out to subscriptions, each with
  its own `Channel.CreateBounded(32, DropOldest)` so a client that stopped reading cannot
  wedge someone else's upload.
- `GET /api/events`, `[Authorize]`, `: ping` every 20 s — keeps proxies from timing out
  **and** detects a dead socket (the write fails and the loop exits on the token).
- **An event is a signal, not data.** There is no event log, so a missed event cannot be
  replayed; the client just re-reads the collection after every (re)connect. A lost event
  is therefore harmless.

### Auth

- **Cookie session, password from config.** The identity source is decoupled from the
  session, so Google OAuth only replaces the login endpoint.
- **Login rate limit:** 5 *failed* attempts per minute, per source address. Done by hand,
  not middleware — middleware would spend an attempt on every request, successful logins
  included. The check runs *before* the password comparison, so an exhausted limit gives
  a guesser no signal. 429 carries the reason in the body. Needs `ForwardedHeaders` too:
  behind a proxy every request arrives from one address otherwise.
- `OnRedirectToLogin` → 401 instead of 302: the cookie scheme default redirects to a
  login page, and `fetch()` would read that HTML as success.
- **The session carries the user id** (claim `kb:user-id`, `User.GetUserId()`), not only a
  display name — decision 2026-09-18. Password sign-in → the owner row; Google →
  `IUserDirectory` looks the email up in `Users` and falls back to the owner (the allow-list
  predates accounts and lists the owner's own addresses). Not `NameIdentifier`: Google fills
  that with its own account id. `OnValidatePrincipal` rejects a cookie without the claim, so
  the pre-accounts cookies cost one sign-in after that deploy.
- **Per-user preferences** — `GET /api/preferences`, `PUT /api/preferences/{key}`. See
  [`storage-and-caching.md`](storage-and-caching.md).
- Password hash: PBKDF2 via `PasswordHasher<T>` (in-framework, no NuGet). Generate with
  `dotnet run -- hash-password <pw>`, store in user-secrets.
- **Google OAuth** — a second identity source, no separate on/off flag: it is live
  whenever `Auth:Google` credentials are present. The handler has `SignInScheme =
  cookie`, so it issues the same `kb.auth` and `/api/auth/me`, logout, `[Authorize]` are
  unchanged. Package `Microsoft.AspNetCore.Authentication.Google`.
  - The scheme registers **only** when `ClientId`/`ClientSecret` are present — an empty
    `ClientId` fails the OAuth options validator and would take down the whole API. That
    registration is also the whole feature toggle: `google/start` 404s without it, and
    `/api/features` reports it so the SPA hides the button.
  - `CallbackPath = /api/auth/google/callback` (not the default `/signin-google`): Vite
    only proxies `/api`.
  - Correlation cookie: `SameSite = Lax` **and** `SecurePolicy = SameAsRequest`. The
    defaults (`None` + `Secure`) require https and break the flow on plain http (a phone
    on the LAN).
  - Allow-list `Auth:Google:AllowedEmails` — **empty means nobody**: Google auth itself
    succeeds for any account in the world. Checked in `OnTicketReceived` (not
    `OnCreatingTicket` — `Fail()` is ignored there), rejection via `HandleResponse()` +
    redirect to `/?authError=…`.
  - `/api/features` returns `Google.IsConfigured` — the SPA shows the button only when
    the flow will actually work.
  - Vite proxy needs `changeOrigin: false` — the backend builds `redirect_uri` from the
    `Host` header. See [`frontend-features.md`](frontend-features.md).

### Data Protection keys → database

They encrypt the auth cookie and by default sit on disk; Render's disk is ephemeral, so
that meant a logout every deploy. Package
`Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`,
`KnowledgeBaseDbContext : IDataProtectionKeyContext`, `PersistKeysToDbContext` registered
in `AddAuthFeature`. Table `DataProtectionKeys` via migration `AddDataProtectionKeys`.

### SPA hosting

`UseStaticFiles` + fallback to `index.html` — an auth prerequisite, not cosmetics: one
address means the cookie is configured in its final form, and prod needs no CORS at all.
The fallback is deliberately double: `MapFallback("/api/{**path}")` returns 404, because
a blanket catch-all would answer a typo in an API path with `index.html` at status 200
and `fetch()` would read the markup as success.

### Antiforgery

Moot — there is no form binding in the API. `upload-link` and `confirm` take JSON, bytes
go to the bucket past ASP.NET.

### Worker status — `GET /api/jobs`

Lists `ProcessingJob` rows still `Pending` or `Running`, oldest first, joined to `Assets` for a
file name when the kind carries one (`BuildSourceNote`). A plain read over the same table the
worker already polls — no new job-kind logic, no write path. Scoped to active jobs on purpose:
`Done`/`Skipped` jobs are history, already visible per-file via `AssetSummaryResponse`; `Failed`
is terminal too, `MaxAttempts` exhausted. `Error` is still returned for a `Pending` row — the
worker resets a failed-but-not-final attempt back to `Pending` and leaves `Error` set (see
`PipelineWorker.TickAsync`), so a queued job can already carry its last failure. UI:
[`frontend-features.md`](frontend-features.md) *Jobs section*.

`GET /api/jobs/failed` lists the `Failed` rows; `POST /api/jobs/{id}/retry` and
`POST /api/jobs/failed/retry` reset them to `Pending` with `Attempts = 0` and `Error` cleared,
so the worker gives each one a full `MaxAttempts` again. Only `Failed` is retriable — a
`Skipped` job would be skipped again on the same input.

`KindDescription` — one sentence on what the kind does, for the UI's info tooltip. Lives in
code (`JobKindDescriptions`, a switch over `JobKind` with no default arm), **not** a lookup
table: `JobKind` is stored as a string so a new kind needs no migration, and a table of
descriptions would bring that migration back (decision 2026-09-13).

`GET /api/jobs/last-completed?kind=…` — latest `CompletedAtUtc` over `Done`/`Skipped` jobs of the
given kinds (`Failed` excluded), null if none. No new column: aggregation jobs insert a fresh row
per run, so the table already is the run history. Feeds the "last run" hint on *Suggest for review*.

### Photo archive catalogue and review — `GET/POST /api/photo-analysis`

`PhotoAnalysisController` lists and creates user-curated people, locations and events; an event
can link uploaded image assets, people and one location. Its list response also contains only
unreviewed model candidates. `POST /candidates/{id}/decisions` validates the candidate kind and
the selected canonical target, then appends one human decision without editing model evidence.
Runs and candidates are written by future worker handlers directly to the shared database.
`POST /analyze-faces` starts the initial archive backfill: it queues fingerprints first, then the
worker queues a face job only for each exact-duplicate group's canonical asset. `POST
/analyze-scenes` refreshes location suggestions for canonical images that do not yet have a
confirmed location observation; it never changes reviewed decisions. `POST /analyze-observations`
queues VLM observation jobs only for canonical images with reviewed person or location context.
The list includes only active, unreviewed observations; `POST /observations/{id}/decisions`
appends a `Confirmed` or `Rejected` outcome without changing the model output. A refresh
supersedes only the older unreviewed observations for that image.
`POST /analyze-events` queues one aggregate event-clustering job after any new images are
fingerprinted. The response exposes active event candidates with their member photos and immutable
edge evidence. `POST /event-candidates/{id}/decisions` either creates a user-named event with the
selected candidate photos, attaches those selected photos to an existing event, or records a
rejection; the original cluster is never altered.

### Password in plaintext — deferred on purpose

During local dev the password sits as a plain default (`Password`) in
`Options/AuthOptions.cs`, so it is in git history. Deploy currently ships with it. The
repo is public, so this is the remaining blocker for treating the public URL as safe.
Fix when it matters: restore `PasswordHasher<T>`, hash in config (`Auth:PasswordHash`),
the `hash-password` CLI command, constant-time comparison. (Deferred knowingly — do not
re-raise every task.)

### Structured request and browser diagnostics

Serilog request records, bounded authenticated browser ingestion, and queue context persistence are described in [observability.md](observability.md). No application payload bodies are logged.

### Bounded asset batches and combined job status

The frontend uses `POST /api/assets/download-links`, `/upload-links` and `/confirm-batch`.
Each accepts `{ items: [{ id, request, uploadBatchId?, traceParent? }] }`, with 1�50 items,
unique item ids and a 128 KiB body limit. Responses contain `{ id, status, value, error }`
per item; an HTTP 200 envelope can contain individual failures. Structurally invalid envelopes
are rejected as a whole. Download signing loads metadata in one query and limits bucket HEADs
to four concurrently; missing bucket objects still return an item-level 404.

Confirmation is idempotent by stored file name, including concurrent API instances. The unique
asset key and the atomic file/job SaveChanges prevent duplicate rows and jobs. A retry returns
200 with the existing row; a new confirmation returns 201. Batches commit items independently,
so successfully confirmed files remain available if another item fails. Per-item upload ids and
trace parents continue into queued jobs and SSE. The older single-item routes remain available.

`GET /api/jobs/summary` returns `{ jobs, failed }` from one database read, retaining the existing
ordering and fields. The frontend polls it five seconds after the preceding read completes.

## Open

- [ ] Verify batch endpoint limits, partial failures, concurrent confirmation retries and jobs
      summary against the running API. Implemented; tests/runtime checks skipped by request on 2026-09-23.

- [ ] Verify the observability integration in the running application; runtime checks and iteration 2 request reduction are tracked in [observability.md](observability.md).

- [ ] **Detector comparison API integration.** Verify authenticated
      `GET/POST /api/photo-analysis/face-comparisons`, `GET /{id}`,
      `PUT /detections/{id}/review` and `PUT /results/{id}/missed-faces` after migration.
      History pages hold 20 runs; repeated requests reuse an existing pending/running comparison
      when found. Review labels are experimental and never change person suggestions.

- [ ] Input-model validation (FluentValidation in `Controllers/<feature>/Validators/`).
      Nothing to validate yet — `LoginRequest` has one field. Relevant once note creation
      exists.
- [ ] Orphan sweep: reconcile the metadata table against the store. Bytes with no row
      (upload died mid-way) are invisible to the API. `IAssetStorage.ListAsync` is kept
      for exactly this; the controller no longer calls it.
- [ ] A 401 during upload does not drop the frontend to "logged out" — the session went
      stale and the user sees an upload error. Needs a shared 401 handler. (Also breaks
      `GET /api/events` → the connection banner says "no server" though the server is
      alive and just does not recognise us.) Partly a
      [`frontend-features.md`](frontend-features.md) item.
- [ ] Remove the dead `Features__*` env vars from Render — `Features__DirectAssetAccessEnabled`
      and `Features__GoogleSignInEnabled`. The `Features` section is now used only by
      `Features__ImageSourceNotes__Enabled`; ASP.NET ignores unknown keys, so this is cleanup,
      not a blocker; Google stays on because `Auth__Google__*` are still set.
- [ ] xUnit + `WebApplicationFactory` integration tests — see [`../CLAUDE.md`] and the
      Tests section of [`archive.md`](archive.md). `IAssetStorage` is now swappable for a
      fake via `WithWebHostBuilder`.
- [ ] Show the Google account picker every time —
      `GoogleChallengeProperties { Prompt = "select_account" }`. Deliberately not done:
      silent re-login is nicer for a single-user app. Revisit only if a second Google
      account starts causing confusion.
- [ ] **Multi-user, step 2:** registration / inviting a user, and an owner column on notes,
      assets, tags and archive records with every query filtered by it. Today only
      preferences are per user.
- [ ] GitHub OAuth as a second identity source — after Google, likely redundant. Kept as
      an option, not a plan.
