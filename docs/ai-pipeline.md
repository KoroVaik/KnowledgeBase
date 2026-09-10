# AI pipeline

How an uploaded file becomes a note. The code is split across `Core/Ai/`,
`Core/Pipeline/`, `Core/Pipeline/Extraction/` and `Worker/Extraction/`, and it runs in
the [worker process](worker.md).

## Flow

Uploaded file → `AssetRecord` row → `ProcessingJobs` row (queued in the same
`SaveChanges` as the asset row, one transaction). The worker polls the queue, pulls the
bytes from the bucket **directly** (it is server-side and has the key — no presigned URL),
picks the extractor for the content kind, feeds the text/image to the model, writes
`Note` + `NoteLink` back to Postgres in one `SaveChanges`, then pings the API for SSE.

The note body is a `text` column with an appended `## Related` section listing the
`[[Links]]` — not an `.md` file.

## Decisions

### Analyzer contract

- `IContentAnalyzer` (+ `OllamaAnalyzer` on a **typed `HttpClient`**) in `Core/Ai/`.
  `BaseAddress` and `Timeout` are set at registration, not in the analyzer.
- Input `AnalysisRequest { Text?, Image? { Bytes, ContentType }, ExistingTitles,
  KnownTags }` — the worker fills `Text` or `Image` by content kind. Output
  `AnalysisResult { Title, Tags[], MarkdownBody, Links[] }`; `Tags` is 1–5, most relevant
  first (the worker still filters — see below).
- Ollama `POST /api/chat`, `stream:false`, **structured output** via `format` = a JSON
  schema (`OllamaAnalyzer.ResultSchema()`) — the model is *required* to return that
  shape. More reliable than "ask for JSON in the prompt and parse whatever comes".
  `message.content` arrives as a string that is itself JSON — hence the double parse.
- **Multimodal:** a vision model sees an image only if the `/api/chat` message carries an
  `images` array of base64. Same endpoint, same schema — only the message content
  changes. A text model will not take an image at all.
  `DefaultIgnoreCondition = WhenWritingNull` drops the `images` field for text requests —
  not cosmetic.
- Model: `qwen2.5vl:7b`, one multimodal model for both text and photos.
- Flexible config `Ai:Ollama` — `BaseUrl`, `Model`, `KeepAlive`, `Timeout`,
  `Options { Temperature, NumCtx, NumGpu, NumThread }` (all nullable, only the set ones go
  into the request; `NumGpu=0` = pure CPU).

### Startup vs run-time checks

- `ValidateOnStart` checks `BaseUrl`/`Model` are non-empty — at host start.
- `EnsureModelAvailableAsync` hits `GET /api/tags` and fails with `ollama pull …` (model
  missing) or "Ollama is not reachable" (service down) — **not** at start, but when the
  worker takes a job. A host with no Ollama must still start while there is no worker yet.

### Extractor strategy per file type

- `ISourceExtractor` (`Kind` + `ExtractAsync(SourceAsset)`) in
  `Core/Pipeline/Extraction/`; `SourceExtractorSelector` indexes them by `ContentKind`
  (DI hands it `IEnumerable<ISourceExtractor>`, it builds a dictionary — not keyed
  services). New file type = new class, not a `case`.
- Text and image extractors are in **Core**; the PDF extractor (`PdfPigTextExtractor`,
  package `PdfPig` 0.1.16 — pure managed, works on Alpine) is in **Worker**, so the prod
  API image does not carry it.
- `ProcessableContent` is a `type → ContentKind` table (`Text`, `Image`, `Pdf` —
  `application/pdf` / `.pdf`). The API queues jobs with the same `Classify`, no
  `AssetsController` change.
- `IAssetContentReader.ReadTextAsync` was removed — the worker always reads bytes, the
  extractor decodes. `S3AssetContentReader` (via `IAmazonS3.GetObjectAsync`) is separate
  from the browser-facing `IAssetStorage`.
- PDF is text-layer only for now. A scanned PDF with no text → `SkippableContentException`
  → job `Skipped`.

### Model does not obey the prompt — filter the output

The 14B model ignores "only link from these titles" — it invents titles and links a note
to itself. The guard is **not a better prompt**: `result.Links` is intersected with the
actual list of existing titles in code (exact match, case-insensitive; fuzzy later)
before any `NoteLink` is created. "Ask again, more firmly" is not a production fix.

- Dangling-link resolution: `ProcessAsync` loads `NoteLink` rows with
  `TargetNoteId IS NULL AND TargetTitle == note.Title` as tracked entities and sets
  `TargetNoteId` in the same `SaveChanges`. With the filter above no danglers come from
  the analyzer — the code stays for future inline `[[...]]` parsing from body text.
- **Same stance for tags** (`PipelineWorker.AttachTags`): the model over-tags and coins
  near-duplicates, so its `Tags` list is not trusted. Keep order, drop blanks/dupes, cap
  at 5, reuse an existing `Tag` on an **exact** name match (case-insensitive — no fuzzy,
  a wrong snap loses a relevant tag; near-duplicates are the reconciliation item's job),
  and allow **at most one** freshly invented tag per run (`Confirmed = false`). No tag
  survives → the note is untagged, a normal state. `Ordinal` follows the surviving order.

### Large text broke the worker — size limits + a Skipped state

A 2 MB `.txt` failed all 3 attempts with `JsonException`. Cause: `NumCtx=8192`, Ollama
silently truncates the prompt, leaves no room for the answer, and the model cuts the JSON
mid-string (`done_reason: "length"`). Fixed:

- `Pipeline:MaxSourceChars` (default 12000): the worker measures `text.Length` after
  reading and throws `ContentTooLargeException` **before** the model call. Applies to any
  extracted text, PDF included.
- **`ProcessingStatus.Skipped`** — terminal, does not spend attempts, is not re-queued
  (unlike `Failed`); reason in `ProcessingJob.Error`. No migration (enum-as-string).
- `OllamaAnalyzer` reads `done_reason`; `"length"` → `ContentTooLargeException` instead
  of a cryptic `JsonException`.
- **Exception hierarchy as flow control:** `SkippableContentException` (base) → job
  `Skipped`; `ContentTooLargeException : SkippableContentException`. The worker catches
  the base, so a scanned PDF, an empty file and over-large text all say "retry won't
  help" with one `catch`. A separate type, not a `bool retryable` field on the exception.
- `GET /api/assets` returns job state (`processingStatus`, `processingError`) and
  `noteId` (LEFT JOIN); `GET /api/features` returns `maxSourceChars`.

## Open

- [ ] **Analyzer over-tags with irrelevant known tags.** With "prefer tags from the known
      list", `qwen2.5vl:7b` pads the list — a hypercar note came back tagged
      `Windows Activation` and `Person Portrait` alongside the right ones. The guard stops
      *proliferation* (no new junk tags) but not a wrong *existing* tag being attached.
      Options: tighten the prompt ("only tags that genuinely describe the content; fewer is
      better"), drop `maxItems` to 3, or a relevance re-check. Prompt-tuning loop, needs a
      few sample files.
- [ ] **Pipeline routing by `NoteKind`.** Today the worker only builds `Source` notes
      from an uploaded file. `Synthesis` (aggregate many notes into one, grouped by topic)
      and later kinds (user notes, general notes) each need their own trigger, prompt and
      maybe model — a selector keyed by note kind, the way `SourceExtractorSelector` picks
      by file type. Design the seam before the third kind. See [`database.md`](database.md).
- [ ] The `Failed` branch (`Attempts >= MaxAttempts`) exists but is unverified live —
      needs an "Ollama answers, but with garbage" scenario. The outage case (does not
      spend attempts) is verified.
- [ ] Idempotency on reprocessing: one job per asset (unique index), but no protection
      against "job deleted by hand → file re-uploaded".
- [ ] **Chunking large documents (map-reduce)** — analyse the whole file, not the first
      `MaxSourceChars`. Split, draft per chunk, reduce to one note. N model calls per
      job; replaces the `Skipped` workaround for text and opens the way to long PDFs.
- [ ] Render scanned-PDF pages to images (no text layer) → vision model. Needs a render
      library with native binaries — separate, together with chunking.
- [ ] Model error handling: bad output must not corrupt existing notes.
- [ ] Model prioritisation: when there are more file types than one model fits in VRAM,
      a queue/lease per model so Ollama does not swap weights on every job.
- [ ] Model API key (when cloud agents arrive) via user-secrets / env vars.
- [ ] Remove the `analyze` CLI once the worker has an integration test (see
      [`worker.md`](worker.md)).
