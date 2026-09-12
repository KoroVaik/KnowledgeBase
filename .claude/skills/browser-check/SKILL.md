---
name: browser-check
description: Verify something in the running KnowledgeBase frontend by delegating to the right browser-testing subagent - ui-tester for functional scenarios (does the feature work), layout-checker for layout/responsive checks (mobile/tablet or desktop profile). Use after implementing or changing UI-facing behavior, or whenever asked to check/verify something in the browser.
---

# Browser check

Every browser-driven check in this project goes through a subagent, never the main
session — keeps screenshots and DOM dumps out of the primary context, and keeps the main
session from accidentally clicking a destructive control mid-task. This skill is the one
front door for both browser-testing agents, so the pre-flight rules below live in one
place instead of being duplicated per agent.

## Which agent

| Question being asked | Agent |
|---|---|
| Does this scenario/feature actually work end-to-end? | `ui-tester` |
| Does the layout hold up at phone/tablet widths, in both themes? | `layout-checker` (profile: mobile) |
| Does the layout hold up on a wide/desktop screen, in both themes? | `layout-checker` (profile: desktop) |

A change that's both new behavior and layout-sensitive (new component, changed CSS) may
need more than one call — they check different things and none substitutes for another.

**Default `layout-checker` to the desktop profile.** Frontend fixes here are normally
done for desktop; mobile layout gets its own separate fixing pass. Only ask for the
mobile profile when the task explicitly says phone/tablet, or when the desktop fix is
done and a mobile pass is being run as its own step.

## Just need to see how something looks (not a pass/fail check)

Sometimes the need isn't "does this scenario work" or "does the layout hold up" — it's
just seeing the current UI state, e.g. before proposing a CSS/styling fix, or judging how
a component renders right now. That doesn't need `ui-tester` or `layout-checker` — those
answer a scenario, not "what does this look like." Pick one:

1. **Ask the user for a screenshot** of the spot in question — fastest when they've
   already spotted the thing and can point the camera at it.
2. **Look at the site directly**, in the current session, for a quick one-off
   navigate + screenshot (or `read_page` for structure/text) — fine as a single quick
   look, same as elsewhere in this project. Don't turn it into a multi-step exploration;
   if it grows into that, it's really a `layout-checker`/`ui-tester` job and belongs there
   instead.

## Before running anything

1. Confirm both dev processes are already up (frontend `5173`, backend `5244`). Neither
   agent starts, stops, or restarts them — if either is down, say so and stop.
2. **Ask before testing.** Building and linting happen silently; actually driving the
   browser does not — confirm with the user first, unless they explicitly asked for this
   check in the same message.

## Environment / auth

- Frontend: `http://localhost:5173`. Login is password-only (no username).
- Local dev password: `baba` (from `AuthOptions.cs` — already a plaintext value committed
  in the backend for local debugging, not a secret introduced here).

## Running a check

Call the agent (via the `Agent` tool) with a self-contained task.

For `ui-tester`:
- the exact scenario to verify, as concrete steps and an expected outcome — not "test the
  upload feature" but "upload a `.txt` file via the drop zone, expect it to appear in the
  asset list with status X within N seconds";
- the starting URL if not the root;
- the password, only if it differs from the default above;
- anything specific to watch for (a console error class, a particular API call).

For `layout-checker`:
- the profile: `mobile` or `desktop`;
- the URL/screen(s) to walk;
- the sign-in password, if the screen needs auth;
- what to pay particular attention to this time, if anything.

Keep each call scoped to one concern. For several unrelated scenarios, prefer several
focused calls over one sprawling task — the report is easier to act on.

## After the report

Relay the agent's verdict plainly: pass/fail, what was checked, and on failure the
evidence it gave. Don't re-verify by driving the browser yourself in the main session —
if the report is unclear, send the subagent a follow-up instead.
