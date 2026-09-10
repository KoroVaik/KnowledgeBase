# Worker (`KnowledgeBase.Worker`)

The AI pipeline as its own process. Polls the `ProcessingJobs` table in Neon, pulls
bytes from the R2 bucket, hands them to the local Ollama, writes `Note` + `NoteLink`
back to Postgres. See [`ai-pipeline.md`](ai-pipeline.md) for the model contract and
extractors, [`database.md`](database.md) for the schema, [`infra.md`](infra.md) for
running it in prod.

## Decisions

### Separate process

Split from the single `Backend.csproj` on 2026-09-09. Lives on the home PC next to Ollama
(`localhost:11434`), reaches Neon and R2 over the internet. Only `KnowledgeBase.Api`
ships to prod. **Nobody connects to the worker** — it only makes outbound calls, no
inbound.

- **No `Pipeline:Enabled` flag.** After the split the API does not host the worker, and
  the worker should always run its loop — there is nothing to switch off.
- Namespaces went `Backend.*` → `KnowledgeBase.{Core,Api,Worker}.*`; the `Infrastructure/`
  wrapper folder in Core was dropped. Migrations moved with the rest of `Persistence/`
  into Core; `dotnet ef` still works with
  `--project KnowledgeBase.Core --startup-project KnowledgeBase.Api`.
- **Only the API runs migrations**, on startup (`MigrateDatabase`, an extension on
  `IHost`). The worker takes `AddDatabase` but does not migrate. **Deploy order: API
  first** (it applies the migration to Neon), then restart the worker — a worker brought
  up against an old schema fails on the first hit to a new table.
- `UseLocalOverrides` is a generic extension on `IHostApplicationBuilder` (works for both
  `WebApplicationBuilder` and the worker's `HostApplicationBuilder`).
- Worker `Program.cs`: `AddDatabase` + `AddAssetStorage` + `AddContentAnalyzer` +
  `AddContentPipeline` + `NullChangeNotifier`. Profile `worker` sets
  `DOTNET_ENVIRONMENT=Development` (not `ASPNETCORE_` — it is a generic host).
- Config files mirror the API's: `appsettings.json` (non-secret defaults,
  `Storage:S3:Region=auto` for R2), `appsettings.Development.json` (local Postgres),
  `appsettings.Local.json` (Garage, gitignored).

### The queue is a Postgres table

Not an external broker — the "no separate service" rule. Selection is
`SELECT ... FOR UPDATE SKIP LOCKED`.

- **`FOR UPDATE SKIP LOCKED` as a queue:** `FOR UPDATE` locks the row, `SKIP LOCKED`
  tells a sibling worker to take the next one instead of waiting. The lock is held to
  commit, so the claim and the flip to `Running` are in **one transaction**. Raw SQL —
  EF cannot express it. The result is materialised via `ToListAsync`.
- **`EnableRetryOnFailure` forbids a bare `BeginTransaction`** — the whole transactional
  block must go through `Database.CreateExecutionStrategy().ExecuteAsync(...)` so a retry
  replays it as a whole. A lone `SaveChanges` needs no wrapping — it is already
  retriable.
- `BackgroundService` is a singleton, `DbContext` is scoped. Every loop iteration:
  `scopeFactory.CreateScope()` + `GetRequiredService` inside it. You cannot inject one
  `DbContext` for the worker's lifetime.
- Config `Pipeline`: `PollInterval` (5 s), `OutageDelay` (30 s), `MaxAttempts` (3).

### Worker → API bridge for live note updates — HTTP ping

After committing a note the worker `POST`s `/api/events/ingest` with a `ChangeEvent` in
the body and a shared secret in `X-Ingest-Token`; the API drops the event into its
`ChangeNotifier` and the existing SSE fans it out to open tabs. `NullChangeNotifier` →
`HttpChangeNotifier`.

- **Fire-and-forget**: the note is already in the DB, a failed ping is only a missed live
  update and the frontend picks it up on reconnect. 5 s timeout so a Render cold start
  does not hold the poll loop.
- Endpoint is `[AllowAnonymous]` + a constant-time token compare
  (`CryptographicOperations.FixedTimeEquals`). An empty `Events:IngestToken` → `503`, the
  bridge is off.
- The ping adds no load at rest — no new notes, no requests. `LISTEN/NOTIFY` was rejected:
  it would hold a constant connection to Neon and burn CU-hours; API-side table polling
  the same.
- **Reversible**: moving to "the API polls the DB" later is a local swap
  (`HttpChangeNotifier` → `NullChangeNotifier` + a `BackgroundService` in the API); the
  `ChangeEvent` contract and the frontend are untouched.
- Deleting a file that has a note happens in `AssetsController.Delete` inside the API
  process, so it sends `notes/deleted` directly via `_notifier` — no bridge needed.

### CLI `analyze` command — temporary

`dotnet run --project backend/KnowledgeBase.Worker -- analyze <file>` runs the analyzer
on one file with no queue. `AnalyzeCommand` lives in the Worker project. It accepts text,
images and PDFs. Remove it once the worker has its first integration test.

## Open

- [ ] Shared Rider run configs in `backend/.run/` (in git, not `.idea`):
      `Frontend (vite)` + two compounds (`Local: API + Worker`, `Local: full stack`).
      They must be `LaunchSettings` type referencing the profiles Rider generates from
      `launchSettings.json` — a hand-written `DotNetProject` config ignores the profile
      and starts as Production. `Frontend (vite)` works; the compounds after re-targeting
      do not yet.
- [ ] Worker → API bridge end-to-end with a live worker (API + worker + Ollama, a new
      note appears without F5), and in prod (generate a secret, `Events__IngestToken` on
      Render, fill `infra/worker/worker.env`).
- [ ] Bring the worker up on the home PC: R2 key `knowledgebase-worker`, fill
      `infra/worker/worker.env` (Neon + R2), run `run-worker.ps1`. Until then `confirm`
      jobs pile up `Pending` in Neon.
- [ ] Remove the `analyze` CLI and `AnalyzeCommand` — keep until the first worker
      integration test.
- [ ] Replace the **temporary** title dedup in `PipelineWorker.UniqueTitle` (suffix
      `(2)`, `(3)`… when the model reuses a live title) with real handling: either the
      model contract guarantees a fresh title, or a collision is a first-class outcome
      (`ProcessingStatus` + a UI hint), not a silent rename. Two near-identical source
      files currently both land as notes with one auto-suffixed.
