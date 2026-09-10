# Archive — done & verified

One line per completed piece of work, so an agent can see what has been *proven to work*
without the verification transcripts (those are in git history). Not loaded by default.
Grouped by area, roughly oldest first. "Verified" means it was actually run and checked,
not just compiled.

## Backend — API

- Scaffold `backend/` (ASP.NET Core 8, minimal API first, then MVC controllers).
- `KnowledgeBase.sln` split into Core + Api + Worker (was one `Backend.csproj`).
- Vertical-slice structure: `Controllers/<feature>/` + `Infrastructure/`. `Program.cs`
  down from 213 lines to ~30. State moved from closures into DI.
- MVC controllers replace minimal API (`AuthController`, `NotesController`,
  `AssetsController`, `EventsController`, `FeaturesController`). `LowercaseUrls = true`.
- Swagger: `GenerateDocumentationFile` + `IncludeXmlComments`, `[ProducesResponseType]`,
  `NoWarn 1591`. Verified against generated `swagger.json`.
- `/api/assets` CRUD: `upload-link` → `PUT` to bucket → `confirm`, `GET` list, `GET
  {name}/link`, `DELETE`. Path-traversal names rejected in the store itself.
- Feature flags: `Features` config → `FeatureOptions`, `GET /api/features` (anonymous).
  `UploadEnabled`, `DownloadEnabled` — enforced in the controller (403/JSON), not just UI.
  `IOptionsSnapshot` so a flag flips without a restart.
- SSE: `IChangeNotifier` singleton, `GET /api/events`, `: ping` every 20 s, bounded
  drop-oldest channels. Verified through the Vite proxy with concurrent streams.
- Health check `GET /health`, anonymous. Weatherforecast removed.
- `/weatherforecast` and hardcoded `ContentRootPath` removed; storage path from config.
- Backend no longer touches bytes: `multipart` `POST /api/assets`, `GET
  /api/assets/{name}` (byte stream), `IAssetStorage.SaveAsync`/`OpenReadAsync`,
  `LocalFileAssetStorage`, the whole `Provider=Local` path, and the
  `DirectAssetAccessEnabled` flag all deleted.

## Backend — auth

- Cookie session, password from config, identity source decoupled from session.
- Login rate limit: 5 failed / minute / source address, checked before the password
  compare. 429 with a reason. Verified with `X-Forwarded-For` spoofing.
- `POST /api/auth/login` / `logout`, `GET /api/auth/me`. `OnRedirectToLogin` → 401.
- PBKDF2 via `PasswordHasher<T>`, `dotnet run -- hash-password`. **(Now reverted to a
  plaintext default for local dev — see [`backend.md`](backend.md); restore before
  treating the public URL as safe.)**
- Google OAuth behind `Features:GoogleSignInEnabled`: scheme registers only with
  credentials present, `CallbackPath = /api/auth/google/callback`, correlation cookie
  `Lax` + `SameAsRequest`, allow-list `Auth:Google:AllowedEmails` (empty = nobody),
  check in `OnTicketReceived`. `/api/features` returns the effective state.
  Vite proxy `changeOrigin: false`. **Enabled in prod**, real sign-in passed in the
  browser locally and on prod.
- `ForwardedHeaders` before `UseHttpsRedirection`. Data Protection keys → DB
  (`DataProtectionKeys`, migration `AddDataProtectionKeys`), verified the key comes from
  the DB.
- SPA served from ASP.NET (`UseStaticFiles` + `index.html` fallback,
  `MapFallback("/api/{**path}")` → 404).

## Frontend

- Scaffold `frontend/` (React 19 + TS + Vite), `strict: true`. Lint is oxlint.
- Vite proxy `/api` → `:5244` instead of CORS. `VITE_API_BASE_URL` removed.
- Login form, Sign-out, session check on load. `unreachable` state with "Try again" for a
  cold start against a dead API.
- Asset list: `ul` + CSS Grid (was a table — broke mobile layout). Download via hidden
  `<iframe>`. Delete with `confirm` naming the file.
- SSE client `api/realtime.ts`: module-singleton, connection lives only while
  subscribed / tab visible / recently active, backoff 2→60 s, `paused` vs `offline`.
  Connection banner in `App`. Verified in the browser, all four backend-down scenarios.
- Bulk upload drop zone: native `<dialog>`, `upload/classify.ts` (ready / warning /
  blocked), clipboard paste (button + `.upload-paste` field), folder detection.
  **Code + build + lint done; full browser run still pending — see
  [`frontend.md`](frontend.md).**
- Upload progress bar: `putToBucket` on `XMLHttpRequest`.
- Markdown render via `marked` + `dangerouslySetInnerHTML` (no DOMPurify, on purpose).
- Wiki-link rendering: `notes/renderNoteBody.ts` as a `marked` extension, server-computed
  link state, three states (resolved / deleted / missing), `--text-muted` token. Verified
  in the browser incl. `[[...]]` inside a code block staying text.
- Notes list with a `Bin (n)` section, `Process again` (polls every 4 s), delete dialog
  naming what disappears. Verified in the browser by subagent (10 steps, `ZZ`-prefixed
  test data).
- Removed the raw-JSON panel after upload. Raw JSON debug panel gone.
- `mobile-layout-checker` subagent added. Mobile fixes: file-list layout, `h1`
  line-height, Delete button height.
- "Backend down is visible on the page" — `realtime.ts` connection state, `unreachable`
  cold-start state, `apiFetch` → `ApiUnreachableError`. Verified, four scenarios.

## Worker & AI pipeline

- Worker split into its own process (`KnowledgeBase.Worker`), namespaces
  `KnowledgeBase.{Core,Api,Worker}.*`. End-to-end run passed locally (Postgres + Garage +
  Ollama `qwen2.5vl:7b`).
- Worker in prod: Docker container, `restart: unless-stopped`, `infra/worker/`.
  Image built, run against local Postgres + Garage. **Not yet up on the home PC.**
- `IContentAnalyzer` + `OllamaAnalyzer` (typed `HttpClient`, `POST /api/chat`, structured
  output via JSON schema). `EnsureModelAvailableAsync` on `GET /api/tags`. Flexible
  `Ai:Ollama` config.
- `PipelineWorker : BackgroundService`: claim via `FOR UPDATE SKIP LOCKED` in a
  transaction inside `CreateExecutionStrategy().ExecuteAsync`, read bytes from S3,
  analyse, write `Note` + `NoteLink` in one `SaveChanges`, `Publish("notes","created")`.
  Verified end-to-end against live Ollama.
- LLM link filter: `result.Links` intersected with actual existing titles before creating
  `NoteLink` (model invents titles / self-links).
- Dangling-link resolution in `ProcessAsync`.
- Image analysis: `ContentKind { Text, Image }`, base64 in the `/api/chat` `images` array,
  `qwen2.5vl:7b`. Verified end-to-end (a grocery-list PNG → a correct note).
- Large-text guard: `Pipeline:MaxSourceChars` (12000), `ProcessingStatus.Skipped`
  (terminal, no attempts spent), `done_reason: "length"` → `ContentTooLargeException`.
- PDF text-layer analysis: extractor strategy (`ISourceExtractor` +
  `SourceExtractorSelector`), `PdfPig` in the Worker project, `SkippableContentException`
  base type. Verified end-to-end with a text-layer PDF; scanned/broken PDF → `Skipped`.
- Worker → API bridge (`HttpChangeNotifier` → `POST /api/events/ingest`, `X-Ingest-Token`
  constant-time compare, `503` when unset). Verified with `curl` + a parallel SSE client.
  **Not yet verified with a live worker or in prod.**

## Database

- `AddPipelineAndNotes` migration: `ProcessingJob`, `Note`, `NoteLink` with indexes and
  FK behaviour. Applied locally, checked with `psql \d`.
- Postgres + EF Core for asset metadata (was SQLite — dropped, no disk in the prod
  container). Table is the source of truth for `GET /api/assets`. `AssetRecord.For`
  factory.
- Original file name + MIME stored; `Content-Disposition` with the original name,
  `Content-Type` from the row.
- `AddNoteKindAndSoftDelete`: `NoteKind` column, `Note.DeletedAtUtc` + global filter,
  partial unique index on `Title` among the living (removed existing duplicates first).
- Note lifecycle: `DELETE ?deleteSource=`, `GET {id}/backlinks`, `POST {id}/restore`,
  `DELETE {id}/purge`, `POST {id}/process-again`, `POST /api/assets/{name}/process`.
  Inbound links move to the new version on replacement. Verified with `curl` end-to-end.
- `Note.SourceFileName` (survives the file's deletion) — kept for the bin entry after the
  "note belongs to its file" decision.

## Infra & deploy

- Multi-stage `Dockerfile` at the repo root (only API; worker in `.dockerignore`).
  Kestrel on `PORT`.
- Web Service on Render, Docker runtime, branch `main`. Live at
  `knowledgebase-9z29.onrender.com`. Verified on the live URL.
- Stateless prod: metadata in Neon, Data Protection keys in Neon, bytes in R2. Verified
  on the live URL (upload → row in Neon, object byte-identical in R2, delete removes
  both).
- Neon project linked (`.neon`, `neon.ts`), connection string translated to ADO.NET
  format, account API key revoked and replaced with a project-scoped one.
- Garage: single-node in Docker, bucket + RW+owner key, data in a volume (survives
  `compose down`), CORS rule (`*`) via `PutBucketCors`. Round-trip verified.
- Tailscale Funnel: `https://rospc.tail11818d.ts.net` → `127.0.0.1:3900`. Verified from
  outside incl. a presigned GET (Funnel does not rewrite `Host`).
- Prod store moved to R2 (`UseChunkEncoding = false`). Separate prod key
  `knowledgebase-render`.
- Direct bucket access: `upload-link` / `confirm` / `{name}/link`, `IAssetLinkSigner`
  separate from `IAssetStorage`, `HeadObject` before signing, size check in `confirm`,
  `409` on repeat `confirm`. Verified with `curl` and by click in the browser locally.
- CI: `frontend-ci.yml` and `backend-ci.yml`, both green.
