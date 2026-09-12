# Working rules for this project

## Language

- **Talk to the owner in Ukrainian** — every chat reply, and visible thinking.
- **Everything in the repo is English**: code, identifiers, comments, commit messages,
  and all docs — except `docs/learning.md`, the owner's personal study notes, which stay
  Ukrainian on purpose.

## The project

Personal Knowledge Base — a web app for collecting notes and documents (an Obsidian-alike).
Monorepo: `frontend/` (React + TS + Vite), `backend/` (ASP.NET Core 8, three projects).

The full picture and the **deliberate constraints** (things not to "fix") are in
[`docs/architecture.md`](docs/architecture.md). Read it first.

## Docs — read only what the task touches

[`docs/README.md`](docs/README.md) is the index. Per area:
[`backend.md`](docs/backend.md), [`frontend.md`](docs/frontend.md),
[`worker.md`](docs/worker.md), [`ai-pipeline.md`](docs/ai-pipeline.md),
[`database.md`](docs/database.md), [`infra.md`](docs/infra.md).

Each area file has a **Decisions** section (the *why* — do not undo a deliberate choice)
and an **Open** section (actionable `[ ]` items).

- **At the start of a task**: read the area file(s) it touches, plus `architecture.md`.
- **At the end**: update the **Open** section — tick what is done, add what surfaced.
- Tick `[x]` **only for what was actually run and checked** — not "code written" but
  "ran and works". Then compress the line to one sentence and move it to
  [`docs/archive.md`](docs/archive.md).
- **Propose new items yourself.** If something out of the current scope surfaces, do not
  do it silently — offer it as an Open item.
- Verification detail does not belong in a living doc: "verified: login → upload →
  delete, all green" is enough.

## Explanation level

The owner is a **test-automation engineer, not a professional developer**, learning
development on this project — mostly from *reading* other people's code at work.

**Do not explain** (they know this): C# syntax and the language (generics, LINQ,
`async`/`await`, records, nullable types); xUnit and general testing principles.

**Explain plainly, with analogies to C# and test automation:**

- **ASP.NET Core as a framework** — they know it only shallowly: the request pipeline and
  middleware (what each `Use*` does and why the order), the DI container and service
  lifetimes, hosting and Kestrel, configuration (`appsettings`, launch profiles,
  user-secrets), minimal API vs MVC, parameter/form binding, `Results.*`.
- Everything frontend: React (hooks, state, re-renders), TypeScript specifics, npm/Vite,
  SPA structure.
- General web mechanics: CORS, origin, HTTP headers, browser cache, DevTools.
- Node ecosystem tools — what they are, why, the .NET equivalent.

**Explain before you change code**: first *why this matters in practice*, then wait for a
"yes", then edit files.

**Proposing a CSS/styling fix**: describe what will look or behave differently, not the
CSS itself. "Card перестане вилазити за правий край на вузьких екранах" is right;
"додам `overflow: hidden` і `flex-wrap: wrap`" is not — CSS property names and selectors
read as noise, not information, to someone who doesn't know what they render.

## Comment policy

**Comments are the exception, not the norm.** The owner does not like reading them — the
code should speak for itself. Do not describe in a comment what the code already shows,
and do not write teaching inserts. A comment earns its place **only** where there is a
**non-obvious decision**: why this way and not another; counter-intuitive framework or
browser behaviour; a workaround for a known trap. Keep it to one or two lines. Concept
explanations go in the chat reply, not the code.

Exception: `///` XML-doc comments on API controllers and actions feed Swagger — keep
those.

## How to work

- **One iteration, one scope.** Do not do "while I'm here" work nobody asked for.
  Something noticed in passing → an Open item, not the current commit.
- **Verify, do not assume.** Before saying "works", actually build, run, call the
  endpoint. Show the command and the real output.
- **Do not invent repo state.** Before claiming anything about configs, CI or
  dependencies, look at the files. (The project description has drifted from reality
  before: the linter turned out to be oxlint, not typescript-eslint; the CI workflow did
  not exist at all.)
- **Dev servers: check before starting, stop what you started.** Before starting Vite,
  the API, or the worker for a check, verify whether it's already running instead of
  assuming — Vite in particular has hot reload, so an already-running instance already
  reflects your latest changes; starting a second one wastes time and can collide on the
  port. If you did start one yourself to run a check, you own stopping it again before you
  finish — this has been skipped before, so don't rely on it happening on its own.
- **Clean up after yourself**: test files in `data/assets`.
- **Do not commit or push without being asked.**
- **Do not create git branches without being asked.** Work on the current branch; if that
  seems wrong, say so and wait.
- **Secrets** (AI model keys) — only via user-secrets or env vars. Never in
  `appsettings.json`, never in code.

## Running locally

Separate processes, each in its own terminal, running at once.

```bash
cd infra/postgres && docker compose up -d
```

```bash
dotnet run --project backend/KnowledgeBase.Api --launch-profile http
```

```bash
cd frontend && npm run dev
```

Frontend: http://localhost:5173 · API: http://localhost:5244 · Swagger:
http://localhost:5244/swagger

`--launch-profile http` is required: the default `https` profile listens on 7087 and the
frontend proxy will miss it.

**AI worker** — separately, when uploads need processing (needs Ollama on
`localhost:11434`):

```bash
dotnet run --project backend/KnowledgeBase.Worker
```

Profile `worker` sets `DOTNET_ENVIRONMENT=Development` → local Postgres + local Garage
(`appsettings.Local.json`) + local Ollama. Analyzer smoke test without the queue:
`dotnet run --project backend/KnowledgeBase.Worker -- analyze <file>`.

Pre-commit checks:

```bash
cd frontend && npm run build && npm run lint
```

```bash
dotnet build backend/KnowledgeBase.sln
```
