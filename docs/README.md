# Project docs

Start here. These files exist so an agent can get oriented **without reading the whole
history**. Load only what the task touches.

| File | Read it when | Holds |
|---|---|---|
| [`architecture.md`](architecture.md) | always, first | the system shape, the process split, the deliberate constraints |
| [`backend.md`](backend.md) | touching `backend/KnowledgeBase.Api` | HTTP API structure, auth, decisions + open items |
| [`frontend.md`](frontend.md) | touching `frontend/` | SPA structure, SSE client, decisions + open items |
| [`worker.md`](worker.md) | touching `backend/KnowledgeBase.Worker` or the queue | the AI process, queue, worker→API bridge |
| [`ai-pipeline.md`](ai-pipeline.md) | touching analysis / Ollama / extraction | model contract, extractors, model quirks |
| [`database.md`](database.md) | touching EF model or migrations | schema, migration ownership, soft-delete, indexes |
| [`infra.md`](infra.md) | deploying, or touching `infra/` | Render, Neon, R2, Garage, CI, Docker, env vars |
| [`archive.md`](archive.md) | only if you need the history of a done thing | one-line log of completed + verified work |
| [`learning.md`](learning.md) | never (it is the owner's study queue, in Ukrainian) | — |

## How these docs are kept

- **`architecture.md`** — stable. Changes only when a deliberate constraint changes.
- **Each area file** has two sections:
  - **Decisions** — the *why* behind the current design. The thing an agent needs so it
    does not "fix" something that was chosen on purpose. Newer wins; a superseded
    decision says so in one line.
  - **Open** — actionable `[ ]` items. `[x]` only after it was actually run and checked,
    then compressed to one line and moved to `archive.md`.
- **Verification transcripts do not live here.** "Verified: login → upload → delete, all
  green" is enough; the blow-by-blow belongs in git history, not a living doc.
- **`archive.md`** is not loaded by default. It is the ledger of what has been proven to
  work, one line each.

## Language

- Talk to the **owner in Ukrainian**.
- **Everything in the repo is English**: code, identifiers, comments, commit messages,
  and every doc here except `learning.md` (the owner's personal study notes).
