# Database

EF Core over Postgres — local in Docker (`infra/postgres`), prod on Neon. Same engine
both places on purpose (see [`architecture.md`](architecture.md)). EF Core rather than
Dapper for the migrations. The EF model and migrations live in `KnowledgeBase.Core`
(`Persistence/`).

## Decisions

### Schema ownership

**The API owns the schema.** It runs migrations on startup (`MigrateDatabase`, an
extension on `IHost`). The worker takes `AddDatabase` but does not migrate.

```bash
dotnet ef migrations add <Name> \
  --project backend/KnowledgeBase.Core \
  --startup-project backend/KnowledgeBase.Api
```

Deploy order: **API first** (applies the migration to Neon), then restart the worker.

### Tables

| Table | Notes |
|---|---|
| `Assets` (`AssetRecord`) | File metadata. **Source of truth** — `GET /api/assets` reads this, not the store. `AssetRecord.For` is a factory taking separate values, not `IFormFile` (the pipeline creates rows too). Empty name → stored name; empty MIME → `application/octet-stream`. `UploadedAtUtc` is `timestamp with time zone`. `CapturedAtUtc`/`Latitude`/`Longitude` (all nullable) hold EXIF read from image bytes — see *Photo capture metadata* in [`ai-pipeline.md`](ai-pipeline.md). |
| `ProcessingJobs` (`ProcessingJob`) | `Id`, `Kind` (`JobKind` enum-as-string), `AssetId?` FK, `Payload?` (JSON, synthesis only), `Status` (enum-as-string), `Attempts`, times, `Error`. Unique index on `AssetId` **among non-null** (aggregation jobs carry none). Index `(Status, CreatedAtUtc)` for polling. |
| `Notes` (`Note`) | `Id`, `Title`, `Body` (`text`), `Kind`, `SourceAssetId?` FK, `SourceFileName`, `SynthesisGroup?`, times, `DeletedAtUtc`. Classification is via `NoteTag`, not a column. Unique `(Kind, SynthesisGroup)` among the living — one aggregate note per group. |
| `NoteLinks` (`NoteLink`) | `Id`, `SourceNoteId` FK, `TargetTitle` (raw), `TargetNoteId?` (nullable for a dangling `[[...]]`). Unique `(SourceNoteId, TargetTitle)`. |
| `SynthesisSources` (`SynthesisSource`) | `SynthesisNoteId` + `InputNoteId` composite PK, both FK to `Notes` CASCADE. Provenance: which notes a synthesis absorbed. Index on `InputNoteId` for the staleness query. |
| `Tags` (`Tag`) | `Id`, `Name` (unique), `Confirmed`, `SuggestedMergeIntoId?` (self-FK). A tag the user has vouched for is `Confirmed`; a pipeline-invented one is not (shown anyway, flagged for review) and carries the confirmed tag the model judged closest. |
| `NoteTags` (`NoteTag`) | `NoteId` + `TagId` composite PK (the pair is unique on its own), `Ordinal`. `Ordinal 0` = primary tag **by convention**, no `IsPrimary` flag. Index on `TagId` for facet queries. No query filter — a binned note keeps its tags on screen. |
| `DataProtectionKeys` | Auth-cookie encryption keys. Migration `AddDataProtectionKeys`. See [`backend.md`](backend.md). |

FK behaviour: `ProcessingJobs → Assets` CASCADE; `NoteLinks → Notes` CASCADE (source) +
SET NULL (target); `Notes → Assets` (`SourceAssetId`) SET NULL; `NoteTags → Notes` and
`NoteTags → Tags` both CASCADE; `Tags → Tags` (`SuggestedMergeIntoId`) SET NULL.

### Tag review

- **Similarity is the model's call, never string distance.** When the pipeline invents a
  tag it also names the closest *confirmed* tag by meaning (`closestKnownTag`), stored as
  `SuggestedMergeIntoId`. Trigram / Levenshtein was rejected: "car" and "automobile" are
  far apart as strings.
- `POST /api/tags/{id}/confirm`, `POST /api/tags/{id}/merge { intoId }`,
  `DELETE /api/tags/{id}`. Routes by id — a name may contain `/`.
- `GET /api/tags?query=&excludeId=` doubles as the merge-target search: with `query` it
  keeps only tags matching it (exact → starts-with → contains, then note count, then
  name) instead of the busiest-first default. Plain text ranking, not the model's call —
  it ranks a human's typing for a pick list, it does not decide a "correct" merge (that
  stays `SuggestedMergeIntoId`, below). `excludeId` drops one tag (the one being merged)
  from the results. See `TagPicker` in [`frontend.md`](frontend.md).
- `POST /api/tags/suggest-merges` queues a `GroupTags` job (see *Synthesis pipeline* in
  [`ai-pipeline.md`](ai-pipeline.md)) that re-runs the closest-matching-tag call over
  **every** unconfirmed tag in one pass, not only ones invented in the same run as a
  source note, and against the **whole vocabulary** - confirmed tags and other
  unconfirmed ones alike, not confirmed-only. Fixes the case that motivated it: tag A is
  invented and points nowhere (nothing confirmed is close yet, and the old batch pass only
  checked against confirmed tags too); tag B is invented later and would have been A's
  match, but the per-file pipeline never compares tags to each other, only to the
  confirmed list at that moment. 409 when there is nothing to group (no unconfirmed tag,
  or fewer than two tags total) or a run is already queued.
- **Merge A → B**: every `NoteTag` of A moves to B (a note carrying both keeps the lower
  `Ordinal`), tags suggesting A now suggest B, B becomes `Confirmed` (merging into it
  vouches for it), A is deleted.
- **Merge or delete bins A's synthesis note.** It is tied to the tag by name only
  (`SynthesisGroup`), so it would describe a group that no longer exists. A queued
  synthesis for a vanished tag is `Skipped`.
- **A synthesis carries exactly its group tag; the index carries none.** The model is not
  asked for tags there — it used to invent some.

### Tag hierarchy

- `TagParent` (`ChildId → ParentId`) is a **DAG, not a tree** — a tag may have several
  parents (e.g. "Porsche" under both "Cars" and "German brands"). `POST /api/tags/{id}/parents`
  rejects a link that would create a cycle (`CreatesCycleAsync` walks the new parent's own
  ancestors looking for the child).
- **AI placement suggestions run in one direction only**: for a confirmed tag with no
  parent yet, a pipeline handler asks the model which existing confirmed tag(s) it could
  go under, and stores each guess as a row — `TagParentSuggestion (ChildId, ParentId)`,
  separate from the real `TagParent` table until a human accepts one. There is no separate
  model call that searches for a tag's *children*: a tag's "suggested children" are just
  every `TagParentSuggestion` where it is the `ParentId` — the flip side of another tag's
  own placement search. One search direction, one table, two ways to filter it.
- Only tags with **zero parents and zero pending (non-dismissed) suggestions** are worth
  re-running — a tag already placed, or already reviewed and rejected, does not need
  asking again. Mirrors the *Tag review* re-run guard above.
- Accepting a suggestion is the existing `AddParent` path (same cycle check); rejecting
  sets a `Dismissed` flag rather than deleting the row, so the same guess is not proposed
  again next run.
- UI: [`frontend.md`](frontend.md) *Tag hierarchy tree* and *Placement suggestions*.

### `NoteKind` — one table, three lifecycles

`NoteKind` (`Source` | `Synthesis` | `Index`) is a column, not a separate table: links and
search are shared, only the lifecycle owner differs. A **source** note describes one file
and goes to the bin with it. A **synthesis** note merges the source notes of one tag; the
**index** note merges every synthesis. Both aggregate kinds absorb their inputs' text
(they do not point at them) so they outlive every file, and carry `SynthesisGroup` (the
tag name, or `"index"`) as their identity for replace-on-rerun. Pipeline side:
[`ai-pipeline.md`](ai-pipeline.md).

### Soft delete

`Note.DeletedAtUtc` + a global query filter. Not for a recycle bin as such — to tell
**"deleted"** from **"never existed"**: without it both look identical in the DB
(`NoteLink.TargetNoteId = null`). Under soft delete the FK stays, the link still points
at the row, and the renderer can show the reason. A final purge nulls the FK — the link
then honestly becomes "not yet".

- Binned notes stay readable by id — the bin has to show what it holds, and a link
  pointing at one still wants its title.
- **Partial unique index on `Title` among the living** (`WHERE "DeletedAtUtc" IS NULL`).
  Links resolve by title, and two "Ollama" notes make resolution non-deterministic.
  Deleting frees the title. Migration `AddNoteKindAndSoftDelete` had to remove existing
  duplicates first (the older of each pair → bin) or the index would not build; the
  `Kind` default `'Source'` was dropped after the backfill.

### `Process again` / version replacement

- `ProcessingJobs` has a unique index on `AssetId`, so a re-run **resets the row** to
  `Pending` (`ProcessingQueue.EnsurePendingAsync`), it does not add a second.
- The old version's soft delete is a **separate `SaveChanges`** before the new note is
  inserted — the title is usually the same and the partial unique index does not know
  EF's INSERT/UPDATE order.
- On replacement, **inbound links move** to the new note — just the id. `TargetTitle` is
  literal text in someone else's body, not ours to edit.

### Link resolution

`GET /api/notes/{id}` returns `links`, one entry per `[[...]]` in the body. The `NoteLink`
rows are asked **first** — they say where the link actually goes, which survives the
target being renamed by a re-run. A title lookup is the fallback for body text that never
became a row.

### `Note.SourceFileName`

The worker writes `asset.OriginalFileName` here. It survives the file's deletion so the
bin entry can still name it. `SourceAssetId` is an FK with `OnDelete(SetNull)`.

### SQLite trap (why the workaround existed)

Before Postgres the store was SQLite. It does not sort `DateTimeOffset` in `ORDER BY`,
and returns `DateTime` with `Kind.Unspecified` — no `Z` in the JSON, so the browser read
UTC as local time. Fixed with a value converter in the model; **not needed in Postgres**
(`timestamp with time zone`). Remove the converter if you see it and it is dead.

## Open

- [ ] **Tag review (confirm / merge / delete).** Done in code (design in *Tag review*
      above, migration `AddTagMergeSuggestion`); build + lint pass. **Run pending**:
      migration on local DB, the three endpoints, a fresh upload producing a suggestion.
- [ ] **Tag search + batch grouping.** `GET /api/tags?query=` ranking, `TagPicker`, and
      the `GroupTags` job behind `POST /api/tags/suggest-merges` are done in code (Core
      builds clean; Api/Worker not rebuilt this session — a dev instance of each was
      running and locking the output). **Run pending**: the migration-free `GroupTags`
      job end to end (needs ≥1 confirmed + ≥1 unconfirmed tag), the search endpoint's
      ranking with a real 100+-tag vocabulary, `TagPicker` in the browser.
- [ ] **Stale synthesis / index badge.** An L2 goes stale when its tag's note set changes
      (new upload, merge into it, delete); L3 when any L2 changes. `SynthesisSources`
      already records the inputs — compare against the tag's current notes and show
      "outdated — re-synthesise".
- [ ] **Point re-tag endpoint.** Model gets the note body + current tag list, returns tags
      only — body and links untouched. Cheap fix for an untagged note or a bad set, without
      `process-again`.
- [ ] **Note version selection.** `Process again` stacks versions in the bin and soft
      delete keeps them there — what is left is to show the version list of one note with
      dates and let the active one be switched. Then the bin stops being a bin and becomes
      version history. Current rule: newer wins; `restore` of an older version while a
      newer one is live → 409.
      - Backend: an endpoint to list an asset's note versions (live + binned, newest
        first), and one to activate a chosen version — soft-delete the live one, un-delete
        the pick, move inbound links. The swap logic is the `process-again` replacement in
        reverse (separate `SaveChanges` around the partial unique index on `Title`).
      - UI: a version dropdown in `FilePanel` — see [`frontend.md`](frontend.md).
- [ ] **More note kinds, each processed its own way.** After `Source` / `Synthesis` /
      `Index`: user-written notes (no AI, or AI only on request), general/standalone notes
      not tied to any file. The `JobKind` → `IPipelineHandler` seam is in place — each new
      kind is a handler class. Pipeline side: [`ai-pipeline.md`](ai-pipeline.md).
- [ ] Orphan sweep — reconcile `Assets` against the store (also in [`backend.md`](backend.md)).
