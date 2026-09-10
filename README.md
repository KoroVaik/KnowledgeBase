# Knowledge Base

A personal web app for collecting notes and documents (an Obsidian-alike). End goal: a
multimodal AI model reads a PDF or photo, produces a Markdown note, picks a category, and
wires it into the existing notes with two-way `[[wiki-links]]`.

Monorepo: `frontend/` (React + TypeScript + Vite) and `backend/` (ASP.NET Core 8 —
`KnowledgeBase.Api`, `KnowledgeBase.Worker`, shared `KnowledgeBase.Core`).

## Docs

- [`CLAUDE.md`](CLAUDE.md) — working rules, running locally.
- [`docs/`](docs/README.md) — architecture and per-area design notes
  (backend, frontend, worker, AI pipeline, database, infra).

## Run

See [`CLAUDE.md`](CLAUDE.md) → "Running locally".
