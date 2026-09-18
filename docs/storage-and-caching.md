# Storage and caching

Where every piece of state lives, how long it survives, and what is cached on the way.
Read it before adding any new kind of state: the first question is always "which row of
the table below does this belong to?". See [`architecture.md`](architecture.md) for the
constraint behind the split (media in S3, everything else in Postgres).

## Where state lives

| What | Where | Survives a deploy | Shared across devices | Owner |
|---|---|---|---|---|
| Notes, tags, links, archive records, job queue | Postgres (Neon / local Docker) | yes | yes | API writes, worker writes pipeline output |
| File metadata (name, MIME, size, EXIF) | Postgres `Assets` — **source of truth** for the listing | yes | yes | API |
| File bytes | S3 bucket — R2 in prod, Garage locally | yes | yes | browser `PUT`/`GET` via signed links |
| Users | Postgres `Users` | yes | yes | migration seeds the owner |
| UI settings (collapsed sections, …) | Postgres `UserPreferences`, per user | yes | yes | SPA via `/api/preferences` |
| Data Protection keys (encrypt the auth cookie) | Postgres `DataProtectionKeys` | yes — that is why they are there | — | API |
| Session | `kb.auth` cookie in the browser, 30 days sliding | yes (keys are in the DB) | no, per browser | API |
| Settings cache + unsent settings | `localStorage` `kb.preferences.<userId>` | yes | no, per browser | SPA |
| SSE subscribers, login-attempt limiter | API process memory | **no**, and nothing needs it to | — | API |

Anything that must survive a deploy goes to Postgres or the bucket. The Render container's disk
and memory are thrown away on every deploy.

## User settings

`GET /api/preferences` returns every setting of the signed-in user as one object;
`PUT /api/preferences/{key}` upserts one (body = raw JSON value, ≤ 4 KB; key
`[a-z0-9:._-]`, ≤ 100 chars). The user id always comes from the session, never from the
request.

- **Key naming:** `<feature>:<name>`, e.g. `section-collapsed:tags:review`. The value is JSON,
  so a new setting of any shape needs no migration.
- **Frontend:** `preferences/preferences.ts` is a module singleton (same pattern as
  `api/realtime.ts`), `usePreference(key, fallback, isValid)` reads it into render.
  `useCollapsibleSection` is built on it.
- **Cache first, server wins.** On sign-in the per-user `localStorage` cache is read
  **synchronously**, before the page renders, so sections do not flip once the server answers.
  Then the server copy replaces the cache.
- **Offline writes are not lost.** A change is applied locally at once and kept as *pending*
  until the `PUT` succeeds; pending values are re-sent on the next start and override the server
  copy then.
- **One-off migration:** the old per-browser keys `kb.section-collapsed.*` are read, sent up
  as pending writes and deleted on the first sign-in after the move.
- A value that fails its `isValid` check (wrong type, old shape) falls back to the default:
  a changed setting shape never breaks a section.

## Caching

| Layer | What | Behaviour |
|---|---|---|
| Browser HTTP cache | Vite build output (`assets/index-<hash>.js/css`) | Safe to cache forever: a new build means new file names. |
| Browser HTTP cache | `index.html` | Served by `UseStaticFiles` with no `Cache-Control`, so the browser may use a stale copy after a deploy (see Open). |
| Browser HTTP cache | Images from the bucket | Effectively **not** cached: every mount asks for a new signed URL, and a different query string is a different cache entry. |
| Signed bucket links | `GET` / `PUT` URLs | Live `Storage:S3:LinkLifetime` (default 5 min). Requested on click / on mount, never stored. |
| SSE stream | `/api/events` | `Cache-Control: no-cache`. An event is only a hint: the client re-reads the collection. |
| Server | — | No server-side cache. Every API read goes to Postgres. |
| Client data | lists, notes | No client-side data cache (no TanStack Query yet — frontend Open). Each section fetches on mount and on SSE hints. |

## Decisions

- **UI settings on the server, not only in `localStorage`** (2026-09-18). `localStorage` is one
  browser only: another device, a private window, cleared site data, and Safari's 7-day
  eviction of script-written storage all brought the defaults back. It stays as a cache so the
  first paint is right.
- **Settings are per user from day one** (2026-09-18), keyed by `Users.Id`, not the display
  name, so password and Google sign-in resolve to the same account. See *Users* in
  [`database.md`](database.md).
- **Upsert in one SQL statement** (`INSERT … ON CONFLICT`): two quick toggles of one key would
  race a read-then-write.

## Open

- [ ] `index.html` has no `Cache-Control: no-cache`: after a deploy a browser can keep running
      the previous build until a hard reload. Set it for `index.html` only (hashed assets can
      stay cacheable).
- [ ] Photo thumbnails re-download on every mount: a fresh signed URL each time defeats the
      HTTP cache. Options: cache the URL client-side until shortly before `expiresAtUtc`, or
      longer-lived links for thumbnails.
- [ ] More settings to move onto `usePreference`: the Notes bin toggle, Hierarchy Tree/Graph
      view, collapsed nodes in the tag tree.
