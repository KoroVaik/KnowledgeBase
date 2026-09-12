---
name: fix-bug
description: End-to-end flow for fixing something already broken in this project (a bug, a layout defect, wrong behavior) - orient, diagnose the root cause, explain the fix and wait for approval, implement narrowly, build/lint, verify with the right browser-testing subagent, then log it. Use when the user reports something is broken or wrong, as opposed to asking for new functionality (that's /new-feature). Invoked as /fix-bug.
---

# Fix a bug

Same working rules as [`new-feature`](../new-feature/SKILL.md)
([CLAUDE.md](../../../CLAUDE.md)), sequenced for a fix instead of new work. The
difference from `/new-feature`: you start from a symptom, not a spec, so diagnosis and
picking the right check come before anything else.

## 1. Orient

Read [`docs/architecture.md`](../../../docs/architecture.md), then the area doc(s) the
symptom touches (see [`docs/README.md`](../../../docs/README.md)). Check the area's
**Decisions** section — a "bug" is sometimes a deliberate constraint; don't "fix" one of
those without flagging it to the user first.

## 2. Pin down what kind of broken this is

Before reading code, decide what would prove the fix worked — this also tells you which
subagent will verify it later:

| The report sounds like... | It's a... | Verify later with |
|---|---|---|
| wrong data, a crash, a click that does nothing, a failed save/upload | functional bug | `ui-tester` |
| something overlaps/clips/scrolls sideways/misaligns at some screen size | layout bug | `layout-checker`, desktop profile by default |
| both, or unclear from the description | either/both | ask |

Frontend fixing here is normally a desktop pass — mobile layout gets its own separate
fixing pass later. So default `layout-checker` to the desktop profile unless the report
explicitly names phone/tablet, or you're doing that separate mobile pass.

If you can't tell whether it's functional or layout — **ask the user a clarifying
question** (e.g. via `AskUserQuestion`) instead of guessing. Getting this wrong means
verifying with the wrong tool later and missing the actual regression.

When the report is about UI and the description alone doesn't pin down where or what's
wrong, ask the user for a screenshot of the problem spot instead of guessing or hunting
for it blind — a screenshot settles it faster than reading code speculatively.

If diagnosing means you need to look at the live page yourself (not just read the code) —
seeing a layout bug rendered, reproducing a functional one — that's still browser testing
and the same rule as step 6 applies: one quick look is fine directly, anything more goes
to the matching subagent (`layout-checker` or `ui-tester`) instead of driving the browser
step-by-step in this session.

## 3. Diagnose

Find the actual root cause in code before proposing anything — read the relevant
component/handler, don't pattern-match from the symptom alone. Explain, in Ukrainian and
in practical terms, *why* it's currently doing that (see CLAUDE.md's Explanation level for
what needs explaining vs what doesn't) and what you intend to change. Wait for an explicit
go-ahead before touching any file — same hard stop as `/new-feature`.

## 4. Implement

Fix the root cause, not the symptom, but stay narrow — a bug fix doesn't need surrounding
cleanup or refactoring (CLAUDE.md's "one iteration, one scope" applies even more here than
for new work). If the real fix needs a wider change than expected, say so and confirm
before widening it, rather than layering a workaround to stay small.

Comment only where the fix itself is non-obvious (e.g. why this was broken, if a future
reader would otherwise "fix" it back) — see CLAUDE.md's Comment policy.

## 5. Build and lint (silent)

```bash
cd frontend && npm run build && npm run lint
```

```bash
dotnet build backend/KnowledgeBase.sln
```

Fix what they surface before moving on.

## 6. Verify the fix

Ask "перевіряти?" first — this step drives the browser, unlike build/lint.

Once approved, use [`browser-check`](../browser-check/SKILL.md) with the check(s) decided
in step 2: `ui-tester` given the exact broken-then-fixed scenario, or `layout-checker`
with the right profile, or both if the symptom spanned both. For a backend-only fix with
no UI surface, a direct call to the endpoint is enough instead.

Report the real result. If the subagent still reproduces the problem, that's the answer —
don't move to step 7 until it doesn't.

## 7. Log it

- If this bug was already a tracked Open item, remove it; otherwise there's nothing to
  tick.
- Add one line to [`docs/archive.md`](../../../docs/archive.md): what was broken, what
  fixed it, and how it was verified (per CLAUDE.md: "verified: X → Y, all green" is
  enough, not a transcript).
- If the fix surfaced a related problem out of scope, add it as a new Open item in the
  area file rather than chasing it now.
- If the "bug" turned out to be a deliberate choice you were asked to change anyway,
  update the area's **Decisions** section to reflect the new decision.

## Boundaries

Don't commit, push, or create a branch as part of this flow unless the user asks
separately — this skill ends at "fixed, verified, logged."
