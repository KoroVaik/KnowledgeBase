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
| `Assets` (`AssetRecord`) | File metadata. **Source of truth** — `GET /api/assets` reads this, not the store. `AssetRecord.For` is a factory taking separate values, not `IFormFile` (the pipeline creates rows too). Empty name → stored name; empty MIME → `application/octet-stream`. `UploadedAtUtc` is `timestamp with time zone`. |
| `ProcessingJobs` (`ProcessingJob`) | `Id`, `AssetId` FK, `Status` (enum-as-string), `Attempts`, times, `Error`. Unique index on `AssetId` (no duplicate job). Index `(Status, CreatedAtUtc)` for polling. |
| `Notes` (`Note`) | `Id`, `Title`, `Category`, `Body` (`text`), `Kind`, `SourceAssetId` FK, `SourceFileName`, times, `DeletedAtUtc`. |
| `NoteLinks` (`NoteLink`) | `Id`, `SourceNoteId` FK, `TargetTitle` (raw), `TargetNoteId?` (nullable for a dangling `[[...]]`). Unique `(SourceNoteId, TargetTitle)`. |
| `DataProtectionKeys` | Auth-cookie encryption keys. Migration `AddDataProtectionKeys`. See [`backend.md`](backend.md). |

FK behaviour: `ProcessingJobs → Assets` CASCADE; `NoteLinks → Notes` CASCADE (source) +
SET NULL (target); `Notes → Assets` (`SourceAssetId`) SET NULL.

### `NoteKind` — one table, two lifecycles

`NoteKind` (`Source` | `Synthesis`) is a column, not a separate table: links and search
are shared, and the only difference is who owns the lifecycle. A **source** note is a
structured description of one file and goes to the bin with that file. A **synthesis**
note is collected from many source notes by a (future) second pipeline and lives its own
life.

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
- [ ] **Second pipeline: synthesis (aggregation) notes** from many source notes.
      `NoteKind.Synthesis` already exists as a column; what is missing is the pipeline that
      writes one — grouping source notes by topic/category, not by file. Needs a mark of
      which source notes are already accounted for, or every run re-reads everything.
      Deleting a source note does not change the synthesis text — it absorbed the content,
      it does not point at it.
- [ ] **More note kinds, each processed its own way.** After `Source` and `Synthesis`:
      user-written notes (no AI, or AI only on request), general/standalone notes not tied
      to any file. `NoteKind` grows; every new kind needs its own trigger, prompt and
      possibly model in the worker — settle the per-kind routing (a selector by
      `NoteKind`, the way `SourceExtractorSelector` picks by file type) before the third
      kind lands. Pipeline side of this is in [`ai-pipeline.md`](ai-pipeline.md).
- [ ] Orphan sweep — reconcile `Assets` against the store (also in [`backend.md`](backend.md)).
