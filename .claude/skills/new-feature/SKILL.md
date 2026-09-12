---
name: new-feature
description: End-to-end flow for implementing a new feature or change in this project - read the relevant docs, explain the approach and wait for approval, implement, build/lint, verify UI-facing changes via the ui-tester subagent, then update the area doc's Open section. Use when the user asks to add, build, or implement a feature (invoked as /new-feature).
---

# New feature

Orchestrates a feature/change from the project's own working rules
([CLAUDE.md](../../../CLAUDE.md)) end to end. Each step below is a rule already in force
project-wide — this skill just sequences them for one piece of work.

## 1. Orient

Read [`docs/architecture.md`](../../../docs/architecture.md), then only the area doc(s)
the task touches (see [`docs/README.md`](../../../docs/README.md) for the map). Check each
area's **Decisions** section — do not "fix" something listed there as deliberate.

## 2. Explain, then wait

Before touching any file: explain, in Ukrainian, what you're about to do and why —
practical terms, not textbook. Wait for an explicit go-ahead. Don't batch this with other
questions; it's a hard stop, not a heads-up.

If the change is frontend/React or general web mechanics the owner doesn't work with
daily, explain those parts plainly (see CLAUDE.md's Explanation level) — skip explaining
C#/xUnit basics, they already know those.

## 3. Implement

One iteration, one scope — no drive-by cleanup. If something unrelated surfaces, note it
as a candidate Open item for step 5 instead of doing it now. No stubs or half-finished
paths: if the scope needs to grow to avoid a hack, say so and confirm before widening it.

Comments only where a decision is non-obvious (one or two lines) — see CLAUDE.md's
Comment policy. `///` XML-doc on API controllers/actions is the one exception, keep those.

## 4. Build and lint (silent)

Run without asking first — this is the one verification step that doesn't need
permission:

```bash
cd frontend && npm run build && npm run lint
```

```bash
dotnet build backend/KnowledgeBase.sln
```

Fix what they surface before moving on.

## 5. Verify behavior

Everything past build/lint needs the user's go-ahead first (ask "перевіряти?").

- **If the change is UI-facing** (touches `frontend/`, or backend behavior a screen
  depends on): once approved, use the
  [`browser-check`](../browser-check/SKILL.md) skill, which delegates to the right
  browser-testing subagent (`ui-tester` for the scenario itself, `layout-checker` too if
  the change is layout-sensitive) — don't drive the browser from the main session
  yourself.
- **If it's backend-only with no UI surface**: a `curl`/Swagger call against the real
  endpoint is enough; state the command and the real response.

Report the real result — don't say "should work" when it wasn't run.

## 6. Update docs

In the area file(s) touched:

- Tick `[x]` only for what step 5 actually ran and confirmed — "code written" doesn't
  count.
- Compress the ticked line to one sentence and move it to
  [`docs/archive.md`](../../../docs/archive.md).
- Add any new Open item that surfaced along the way (step 3's parking lot, anything the
  ui-tester report flagged that's out of this scope).
- If a real design decision got made or changed during this work, record it in the area
  file's **Decisions** section now — not left to be re-litigated next session.

## Boundaries

Don't commit, push, or create a branch as part of this flow unless the user asks
separately — this skill ends at "implemented, verified, docs updated."
