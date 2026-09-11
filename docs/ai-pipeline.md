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

### Handler seam — one queue, a handler per job kind

`ProcessingJob` carries a `Kind` (`JobKind`, enum-as-string) and an optional JSON
`Payload`. `PipelineWorker` owns only the plumbing — claim (`FOR UPDATE SKIP LOCKED`),
attempts, `Skipped`/`Failed`, SSE — and dispatches to `PipelineHandlerSelector.For(kind)`
(the same shape as `SourceExtractorSelector`). Each `IPipelineHandler` builds its note and
adds it (+ links, tags) to the `DbContext`; the worker's final `SaveChanges` commits note
and job status together.

- `BuildSourceNote` → `SourceNoteHandler` (the file → note flow; `AssetId` set, no payload).
- `BuildSynthesis` → `SynthesisHandler` (merge notes; `AssetId` null, payload =
  `SynthesisJobPayload { TargetKind, GroupLabel, InputNoteIds }`).
- `GroupTags` → `TagGroupingHandler` (batch tag-merge suggestions; no `AssetId`, no
  payload — see *Tag grouping pass* below).
- A new kind = a new handler class + a `JobKind` value. Nothing in the worker changes.
- Shared post-processing (the tag filter, link filter, dangling + inherited links) is
  `NoteWriter.CommitAsync`, used by both note-writing handlers.
- **`HandleAsync` returns `Note?`**, not `Note` — `GroupTags` changes `Tag` rows directly
  and writes no note. `PipelineWorker` publishes `notes/created` when a note came back,
  `notes/updated` (no id) otherwise, so any UI subscribed to `notes` (e.g. `TagsSection`)
  still refreshes.

### Analyzer contract — a thin transport

- `IContentAnalyzer` (+ `OllamaAnalyzer` on a **typed `HttpClient`**) in `Core/Ai/`.
  `BaseAddress` and `Timeout` are set at registration, not in the analyzer.
- `RunAsync<T>(AiTask { SystemPrompt, UserPrompt, Schema, Image? })` — the analyzer knows
  nothing about prompts. Each pipeline owns its prompt and its result shape: `SourceNotes/
  SourceNotePrompt`, `Synthesis/SynthesisPrompt`, both producing `NoteDraft { Title,
  Tags[], MarkdownBody, Links[] }` against `NoteDraft.Schema()`.
- Ollama `POST /api/chat`, `stream:false`, **structured output** via `format` = the JSON
  schema — the model is *required* to return that shape. More reliable than "ask for JSON
  in the prompt and parse whatever comes". `message.content` arrives as a string that is
  itself JSON — hence the double parse.
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

- Dangling-link resolution: `NoteWriter.CommitAsync` loads `NoteLink` rows with
  `TargetNoteId IS NULL AND TargetTitle == note.Title` as tracked entities and sets
  `TargetNoteId` in the same `SaveChanges`. With the filter above no danglers come from
  the analyzer — the code stays for future inline `[[...]]` parsing from body text.
- **Same stance for tags** (`NoteWriter.AttachProposedTags`, source notes only): the
  model over-tags and coins near-duplicates, so its `Tags` list is not trusted. Keep
  order, drop blanks/dupes, cap at 5, reuse an existing `Tag` on an **exact** name match
  (case-insensitive — no fuzzy, a wrong snap loses a relevant tag; near-duplicates are
  the review UI's job), and allow **at most one** freshly invented tag per run
  (`Confirmed = false`). No tag survives → the note is untagged, a normal state.
  `Ordinal` follows the surviving order.
- The prompt lists confirmed tags and other tags in use separately; the model also returns
  `closestKnownTag` — for an invented tag, the closest confirmed one **by meaning**. It
  becomes `Tag.SuggestedMergeIntoId` only if it names a confirmed tag exactly. See *Tag
  review* in [`database.md`](database.md).
- **No vague placeholder tags.** `SourceNotePrompt` tells the model not to invent a
  non-descriptive tag ("Unknown", "Unidentified", "Miscellaneous"…) when it cannot pin
  something down — skip that tag slot instead. Confusing to see sitting in the confirmed
  list next to real topic tags; a code-side filter cannot fix this one since the model
  is asked for meaning, not matched against a list.
- A synthesis / index is not asked for tags (`NoteDraft.Schema(withTags: false)`): an L2
  gets its group tag, the index none.

### Synthesis pipeline (L2 / L3)

Three layers over one file: `Source` (per file) → `Synthesis` (per tag, merges the Source
notes carrying it) → `Index` (one note, merges every `Synthesis`). L2 and L3 are the
**same `SynthesisHandler`** — the endpoint gathers the input note ids and the target kind,
the handler merges whatever list it is given.

- Trigger is explicit: `POST /api/synthesis/tag { tag }` and `POST /api/synthesis/index`.
  Both `202`; the worker publishes `notes/created` on completion (no failure signal to the
  UI yet — Open).
- **Identity is `(Note.Kind, Note.SynthesisGroup)`**, unique among the living. `GroupLabel`
  is the tag name for L2, `"index"` for L3. A re-run bins the old note (separate
  `SaveChanges`, as with a source re-run) and the new one inherits its inbound links.
- **Threshold 2**: a tag with one Source note, or fewer than two `Synthesis` notes, is a
  `409` at the endpoint. Fewer than two inputs still live when the job runs → `Skipped`.
- `SynthesisSource { SynthesisNoteId, InputNoteId }` records provenance — kept for a
  future staleness check. Soft delete does not cascade it; a purge does.
- Size guard `Pipeline:MaxSynthesisChars` (24000) over the combined input bodies →
  `ContentTooLargeException` → `Skipped`. Map-reduce per group is the real fix (Open).
- The synthesis links **outward only** — its input notes' titles are removed from the
  linkable list, since it absorbs their content rather than pointing back at it.
- `SynthesisJobPayload` dedup: the endpoint refuses a second job for a `(kind, group)`
  already `Pending`/`Running` (payloads are tiny, checked in memory).

### Tag grouping pass

`TagGroupingHandler` re-runs the closest-confirmed-tag idea (*Model does not obey the
prompt* above, `closestKnownTag`) as a standalone batch pass over the whole vocabulary,
triggered by `POST /api/tags/suggest-merges` (see *Tag review* in
[`database.md`](database.md)) rather than at note-write time.

- One model call per run: every confirmed tag name and every unconfirmed one, in one
  prompt (`TagGroupingPrompt`) — tag names are short, so even a few hundred fit
  comfortably, unlike a synthesis body. The reply is one `{tag, closestConfirmedTag}` pair
  per unconfirmed tag (`""` when nothing fits), written straight onto
  `Tag.SuggestedMergeIntoId` by exact name match.
- Same reason it exists as a *separate* job from `BuildSourceNote`'s per-file suggestion:
  that one only ever compares a freshly invented tag against the confirmed list at that
  moment. Two tags invented on different uploads, before either is confirmed, are never
  compared to each other until something confirms one of them and a re-run of this job
  catches the other.
- Always overwrites `SuggestedMergeIntoId` on every unconfirmed tag it can decide for -
  it is a deliberate "recompute", not a fill-only pass.
- Guarded like `BuildSynthesis`: the endpoint 409s instead of queuing when there is
  nothing to group (no confirmed tag, or no unconfirmed one) or a run is already
  pending/running.

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
  the **base** (fixed — it caught only `ContentTooLargeException` before, so a scanned PDF
  or empty file went to `Failed` after three retries). Scanned PDF, empty file and
  over-large text now all say "retry won't help" with one `catch`. A separate type, not a
  `bool retryable` field on the exception.
- `GET /api/assets` returns job state (`processingStatus`, `processingError`) and
  `noteId` (LEFT JOIN); `GET /api/features` returns `maxSourceChars`.

## Open

- [ ] **`GroupTags` unverified against a real vocabulary.** Written but never run against
      Ollama: whether one prompt with a large tag list still gets good matches (same
      "model pads/ignores instructions" risk as the item below), and what the right
      re-run cadence is (manual button only, for now).
- [ ] **Analyzer over-tags with irrelevant known tags.** With "prefer tags from the known
      list", `qwen2.5vl:7b` pads the list — a hypercar note came back tagged
      `Windows Activation` and `Person Portrait` alongside the right ones. The guard stops
      *proliferation* (no new junk tags) but not a wrong *existing* tag being attached.
      Options: tighten the prompt ("only tags that genuinely describe the content; fewer is
      better"), drop `maxItems` to 3, or a relevance re-check. Prompt-tuning loop, needs a
      few sample files.
- [ ] **No failure signal for a synthesis.** A `BuildSynthesis` job that ends `Failed`/
      `Skipped` publishes nothing — the Tags section shows "Queued" until a reload. Needs a
      job-state read (like `GET /api/assets` has) or an SSE event carrying the outcome.
- [ ] **Map-reduce a large synthesis group.** A tag with many notes blows
      `MaxSynthesisChars` → `Skipped`. Split the inputs, draft per batch, reduce to one
      note — same shape as the document-chunking item below.
- [ ] **Synthesis staleness + regeneration.** `SynthesisSource` records what a synthesis
      was built from; nothing yet flags "an input changed since" or offers a one-click
      rebuild. Also: L3 grouping beyond "all into one" (topic clusters), and a
      "synthesise every eligible tag" button (N jobs from one click).
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
