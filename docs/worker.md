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
  `IHost`). The worker takes `AddDatabase` but does not migrate. A worker on an old schema
  fails on the first hit to a new table, so it **waits on startup**
  (`WaitForMigrationsAsync`, every 30 s) until no migration it knows is pending. This makes
  the API-first order independent of how either side is deployed (decision 2026-09-18,
  chosen over sequencing it in the pipelines).
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

### Face analysis is a separate local pipeline job

`CompareFaceDetectors` is an independent experiment job linked by `FaceComparisonRun.JobId`
(the job's `AssetId` is null so history can keep several runs of one photo). It uses no ArcFace
embeddings or Ollama. `FaceComparison:ModelDirectory` defaults to the local application-data model
cache; Docker sets `/models/face-comparison` on the persistent volume. SCRFD/YuNet thresholds default
to 0.5/0.9 and NMS to 0.4; YOLO uses the current `FaceAnalysis` settings. Models download on first use
and are verified by SHA-256 before loading. Completed model outputs survive a job retry.

`CompareFaceRecognizers` is a separate archive-wide experiment. It runs ArcFace R50, ArcFace
MobileFaceNet and FaceONNX ResNet27 over every current detected face that is missing any model
embedding, then retains the embeddings so later runs process only new detections. Confirmed,
non-partial person references are optional ground truth: when enough exist, the run also scores
same-person and different-person pairs automatically. It changes no person suggestion. The run
retains metrics plus at most 24 model disagreements or near-threshold pairs for explanation.
MobileFaceNet is lazily downloaded to
`FaceRecognitionComparison:ModelDirectory` and checksum-verified.

For inference without a host, queue or database:
`dotnet run --project backend/KnowledgeBase.Worker --no-launch-profile -- compare-faces <image>`.
This command uses default comparison settings and prints one JSON result per model.

For a local folder of test images, `dotnet run --project backend/KnowledgeBase.Worker --no-launch-profile -- compare-face-pack <folder> [output-folder]`
runs nine named configurations: current and stricter YOLO, SCRFD at 0.7/0.5/0.4, and YuNet at
0.9/0.8/0.7/0.5. It creates `results.json`, `detections.csv`, `summary.csv` and a README in a
timestamped `results/` subfolder by default. The command starts no host and does not access the
database, Garage, the queue or the photo archive.

After changing only the local review layout, `dotnet run --project backend/KnowledgeBase.Worker --no-launch-profile -- compare-face-report <results.json>` regenerates `report.html` from the existing output in seconds; it never runs model inference.

`AnalyzeFaces` is queued for every image through `FingerprintAsset`. The API controls whether it also
queues `BuildSourceNote` for an image, and marks already-pending image-to-note jobs `Skipped` when that
feature is disabled; the worker has no setting or branch for it. Face analysis can be retried
independently and runs even while Ollama is unavailable. `FaceAnalysisHandler` uses the
local FaceONNX detector and ArcFace embedder, stores face occurrences and identities (no person
candidates), queues `ClusterFaces`, and sends a `photo-analysis` change hint instead of a note
change. A confirmed Person review creates the reference vector used by later groupings; the worker
never directly identifies a person as fact.

`ClusterFaces` groups every open face of the archive into review rows - joins confirmed people and
ignored groups, clusters the rest (thresholds in the `FaceClustering` config section, validated on
start) - and rewrites the open person candidates. At most one waits in the queue; it finishes
without work while a `FingerprintAsset` or `AnalyzeFaces` job is active, since the last detection
job queues another. A worker start also queues one when faces exist but no grouping has ever run.
`RescoreFaces` is no longer queued; leftover rows run the same grouping. Details in
[`photo-archive.md`](photo-archive.md).

`FingerprintAsset` runs before a new image's face job and records a SHA-256 of its stored bytes.
Only the oldest image with that hash queues `AnalyzeFaces`; copies are kept but skipped. The batch
archive action queues missing fingerprints for older images and never queues source-note jobs.

### Scene analysis is local CLIP evidence, not a location decision

`AnalyzeScenes` uses a local ONNX CLIP vision model (downloaded once into the worker's local model
cache on its first scene job) to write one 512-value `VisualEmbedding` for the canonical image.
It ranks the best confirmed appearance of each location by cosine similarity and creates at most
five `Location` candidates. The worker never assigns a location directly. A human decision turns
the image vector into a `LocationObservation`, which later scene jobs can use as a reference.

### Scene observations are cautious VLM output, not archive facts

`AnalyzeSceneObservations` runs only when a canonical image already has a reviewed person or
location. It supplies that limited context to Ollama and requests up to ten structured `Action`,
`Interaction`, `Object`, `Text`, or `Mood` observations. The model may use only the supplied
person names; every result needs a visible-evidence field and a cautious ranking. The handler
writes model/configuration provenance to `PhotoAnalysisRuns`, then appends observations. A fresh
run supersedes only unreviewed observations for that asset; a separate human `Confirmed` or
`Rejected` decision remains immutable.

### Event clustering creates evidence, never events

`AnalyzeEventCandidates` is a manual aggregate job over canonical images. It scores photo pairs
from capture-time proximity, shared reviewed people/locations, compatible CLIP vectors, and
confirmed scene-observation kinds. Only connected pairs above the configured threshold become
temporary `EventClusters`; the raw per-pair signals are stored as JSON. The handler then creates
one reviewable candidate per cluster and supersedes only earlier unreviewed candidates. It never
creates or changes an `ArchiveEvent`.

### Job and dependency diagnostics

Each attempt restores its saved trace/upload context and records outcome, duration, dependencies and its SSE event identity. See [observability.md](observability.md).

## Open

- [ ] Verify the observability integration in the running application; runtime checks and iteration 2 request reduction are tracked in [observability.md](observability.md).

- [ ] Run the persisted detector comparison end to end after API migration and worker restart;
      standalone inference is verified, queue/resume/SSE against the application are pending.

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
