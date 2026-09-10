# Architecture

Personal Knowledge Base — a web app for collecting notes and documents (an Obsidian-alike).
End goal: a multimodal AI model reads a PDF/photo, produces an `.md` note, picks a
category, and adds two-way `[[wiki-links]]` into the notes that already exist.

Monorepo: `frontend/` (React + TypeScript + Vite) and `backend/` (ASP.NET Core 8).

## The three processes

`backend/KnowledgeBase.sln` has three projects:

| Project | SDK | Role |
|---|---|---|
| `KnowledgeBase.Core` | `Microsoft.NET.Sdk` | Shared: EF model + migrations, S3 storage, Ollama client, `PipelineWorker`, the SSE event contract |
| `KnowledgeBase.Api` | `Microsoft.NET.Sdk.Web` | HTTP API + serves the SPA. **The only thing that ships to prod** (Render, Docker). Runs migrations on startup — it owns the schema. |
| `KnowledgeBase.Worker` | `Microsoft.NET.Sdk.Worker` | The AI pipeline, as its own process on the home PC next to Ollama. Reaches Neon and R2 over the internet. Does **not** run migrations. |

`Core` is referenced by both apps. Api and Worker are independent entry points.

```
Browser ──static + API──► Render        (KnowledgeBase.Api in Docker)
        ──bytes──────────► Cloudflare R2  (bucket: signed PUT/GET, direct from browser)
                            Neon          (Postgres: metadata, notes, queue, Data Protection keys)

Home PC ──► KnowledgeBase.Worker ──► Ollama (localhost:11434)
                                ├──► Neon + R2 over the internet
                                └──► Render /api/events/ingest  ("new note" hint for SSE)
```

Data flow for an upload: browser gets a signed URL → `PUT`s bytes straight to the bucket
→ `POST /api/assets/{key}/confirm` writes the metadata row and queues a `ProcessingJob`
→ the worker polls the queue, pulls the bytes, runs the model, writes `Note` + `NoteLink`
back to Postgres → it pings the API so the open SPA refreshes over SSE.

## Deliberate constraints

These are decisions, not gaps. Do not offer to "fix" them.

- **Backend is C# / ASP.NET Core only.** No Python. Model calls go straight through
  `HttpClient` (or an official SDK), **no separate service**.
- **Media in S3, everything else in Postgres.** A note body is a `text` column, not an
  `.md` file (decision 2026-09-09, supersedes an earlier "notes stay files"): the
  `[[wiki-link]]` graph and full-text search live in the DB, and a note is kilobytes of
  text, not media. Real `.md` files, if ever needed for Obsidian, are a derived export.
  Media bytes live **only** in an S3-compatible store behind `IAssetStorage` — R2 in
  prod, Garage on the home PC (which doubles as the second copy). No local file storage:
  the backend never touches bytes; the browser `PUT`/`GET`s the bucket directly via
  signed links (`IAssetLinkSigner`). File metadata (original name, MIME, size, time) is
  in **Postgres** via EF Core, and **that table is the source of truth** — the listing
  reads from it, the store only serves bytes. Local Postgres in Docker
  (`infra/postgres`), prod Postgres is managed (Neon).
- **One database engine in both environments, on purpose.** Different EF providers would
  mean two sets of migrations and a class of "works locally, not in the cloud" bugs.
  SQLite was dropped because the ephemeral prod container has no disk the DB file would
  survive a deploy on.
- **Full-text search and backlinks in the DB have not moved yet.** When they do, it is
  the same database, not a new one.
- **Frontend**: React + TypeScript, `strict: true`, avoid `any`.
- **Auth is single-user, no registration** (decision 2026-09-06, supersedes "not
  planned"). It is a deploy prerequisite: a public URL without it means open access to
  the notes. Cookie session, password from config; Google OAuth is a second identity
  source behind a flag. See [`backend.md`](backend.md).

## Working rules

The comment policy, the "explain before coding" rule, and how much to explain to the
owner (a test-automation engineer learning development, not a pro) are in
[`../CLAUDE.md`](../CLAUDE.md).
