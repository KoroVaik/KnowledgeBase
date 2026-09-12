# Frontend (`frontend/`)

React + TypeScript + Vite SPA. `strict: true`, avoid `any`. Lint is **oxlint**, not
typescript-eslint. See [`architecture.md`](architecture.md) for constraints,
[`backend.md`](backend.md) for the API side.

Layout: `api/` (one module per resource + `http.ts` + `realtime.ts`), `components/`,
`hooks/`, `upload/` (queue, classifier, drop-zone state), `notes/` (body renderer),
`format.ts`, `assetKind.ts` (is-image / is-processable, mirrors the backend), `download.ts`.

### Component vs hook split, co-located per component

Every component that mixed state/fetch/handlers with markup was split into a component file
(JSX only) plus a `use<Name>.ts` hook (state, effects, every handler) - then each such pair
(and any sub-component private to it) was moved into its own folder under `components/`,
e.g. `components/AssetList/{AssetList.tsx,useAssetList.ts}`. Group-by-feature, not
group-by-type: easier to find and delete everything one component owns, at the cost of
`hooks/` no longer listing every hook in the app.

- `NotesList/` - `NotesList.tsx`, `useNotesList.ts`, `NoteRow.tsx` (the row shell shared by
  the active and bin lists, private to this component).
- `TagsSection/` - `TagsSection.tsx`, `useTagsSection.ts`, `ConfirmedTags.tsx`,
  `TagParentsControl.tsx` (both private to this component, confirmed by grep before moving).
- `AssetList/`, `DeleteNoteDialog/`, `FilePanel/`, `LoginForm/`, `TagPicker/`,
  `UploadQueueDialog/` - component + its one hook, no sub-components.
- **Stayed flat, not co-located:**
  - `hooks/useResourceChanges.ts`, `hooks/useConnectionStatus.ts` - used by several
    components (`NotesList`, `AssetList`, `TagsSection`, `App`), so they have no single
    component to live inside. Left in `hooks/` for now rather than invented a `shared/` home.
  - `components/TagChips.tsx` - no hook, used by both `NotesList` and `FilePanel`.
  - `components/UploadDropZone.tsx` + `upload/useUploadDropZone.ts` - the hook already lives
    in `upload/` next to the related `useUploadQueue.ts` and `classify.ts`, an existing
    domain module rather than a private one-component hook.
  - `App.tsx` + `hooks/useAppAuth.ts` - the root composition `main.tsx` mounts, not "a
    component among components".
  - `TagPicker/` is not nested inside `TagsSection/` even though that is its only two
    callers today (`TagsSection`'s row and `TagParentsControl`) - it is built generic on
    purpose (see its own doc comment) for any future picker over the tag vocabulary.

No behaviour changed doing any of this - e.g. `NoteRow`'s one existing asymmetry (the active
list shows a loading/error state for the expanded body, the bin only ever shows the ready
state) was kept as-is, not unified.

## Decisions

### One origin, dev and prod

Vite proxies `/api` → `localhost:5244` instead of CORS, so the browser sees a single
address in both dev and prod. Paths are relative; `VITE_API_BASE_URL` is gone.

- The Vite proxy needs `changeOrigin: false`: Vite 8 rewrites `Host` to the target by
  default, and the backend builds the OAuth `redirect_uri` from `Host` — Google would
  send the browser back to `:5244`, past the proxy.

### SSE client — `api/realtime.ts`

Module-singleton with its own subscriber list; the hooks (`useConnectionStatus`,
`useResourceChanges`) only carry it into render. Not `useState` in a component, not
context: the connection is one per tab regardless of who is mounted.

- The connection lives only while there is a subscriber, the tab is visible
  (`visibilitychange`, with a 30 s grace so Alt-Tab does not tear the stream), and there
  was activity in the last 15 min. Not `window.blur` — a window on a second monitor is
  still visible.
- Reconnect backoff 2→60 s — the browser recovers a dropped network on its own, but after
  an HTTP error (502 on API restart, a stale session) it gives up for good.
- `paused` (a deliberate close) vs `offline` (any `onerror`, **including the browser's
  own retry** — for the page that is the same absence of connection). The banner reacts
  only to `offline`.
- Cold start against a dead API does **not** drop to `anonymous` (that showed a login
  form pointing nowhere) — a separate `unreachable` state with "Try again". The tell is
  "`fetchCurrentUser` threw", not "network error": it returns `null` only on 401.
- `apiFetch` in `api/http.ts` turns a `fetch` rejection into `ApiUnreachableError`.
  `fetch` rejects only when there was no response at all, and its message differs per
  engine — "Failed to fetch" must not reach the user.
- `reloadToken` after the user's own upload sits alongside the stream: it refreshes the
  list without waiting for the round-trip and covers a down stream. Cost: two GETs after
  your own upload. Remove if the stream proves reliable.

### Markdown + wiki-links

- Body render: `marked` (~12 KB gzip), `marked.parse(md, { async: false })` →
  `dangerouslySetInnerHTML`. **No `DOMPurify` on purpose** — single-user app, the body is
  produced by the local Ollama from the user's own files, same trust level as every other
  DB row. Revisit if notes ever take content from outside.
- Wiki-links: `notes/renderNoteBody.ts` is a **`marked` extension, not a raw-text
  replace** — the tokenizer knows what is code, so `[[...]]` inside a code block stays
  text. Link state is computed by the **server** (`GET /api/notes/{id}` returns `links`),
  never encoded in the `.md` (which would break Obsidian export).
- Three link states at render: resolved; **deleted** (grey, strikethrough, no click);
  **missing** (grey, dashed). Token `--text-muted` (both themes); strikethrough sits on
  the inner `.wiki-link-text` so it does not cross the "· deleted" tag.
- A click on an active link expands the target note — one handler on the container, the
  body is already HTML.

### Upload — drop zone, queue, classifier

- The popup is a native `<dialog>` + `showModal()`, not hand-rolled: top layer (no
  `z-index`/`overflow` on an ancestor clips it), `::backdrop`, `inert` on the rest of the
  page, focus trap — all free. The `open` attribute does **not** give modality, only the
  method call — so this is the rare case where React must poke the DOM via `ref`.
- The queue closes itself when no row is left waiting on the user (`every` on an empty
  list is true). A `blocked` row is removed by an explicit `Dismiss`, not by the popup
  vanishing — otherwise the reason is missed.
- Upload **all at once**, no pool: single-user, a batch is tens of files, not thousands.
- Uploads start straight from `add`/`uploadNow`, **not from an effect**: StrictMode
  double-runs effects in dev and the second run would upload every file twice.
- ESC is blocked (event `cancel` + `preventDefault`) while any `PUT` is in flight. Click
  on the backdrop needs no blocking — `<dialog>` ignores it.
- A row `id` is a counter, not `crypto.randomUUID()` — the latter exists only in a secure
  context, and the app opens from a phone over plain http on a LAN address.
- `<input type="file">` is a **sibling** of the button, not a child: `input.click()`
  dispatches a click that bubbles, and from inside the button that is an infinite loop.
- `dragleave` also fires when the cursor crosses onto a child — checked with
  `currentTarget.contains(relatedTarget)`.
- `dragover`/`drop` on `window` are always swallowed: a file dropped *past* the zone
  otherwise makes the browser navigate to it, taking the SPA and any upload in flight.
- A dropped **folder** arrives in `dataTransfer.files` as a size-0 entry — indistinguishable
  from an empty file. Filtered via `webkitGetAsEntry().isDirectory` (call must be
  synchronous — the item list empties as soon as the handler returns).
- `upload/classify.ts` is a pure sync function, three categories: `ready` (upload now),
  `warning` (text file over `maxSourceChars` — uploads, but no note), `blocked` (empty or
  over `MaxUploadBytes` — server would refuse, no upload button). Both limits come from
  `/api/features`.
- Clipboard: the button is **not** disabled by clipboard content — reading is what raises
  the permission prompt, so probing to grey it out would raise that prompt anyway. Empty
  clipboard is reported after the click. Names are synthesised (`clipboard-<time>.<ext>`)
  — a screenshot has none. A `paste` listener on `window` covers Ctrl+V and Win+V; the
  `.upload-paste` field is the only thing a phone's clipboard history can paste into.

### Progress bar

`api/assets.ts` `putToBucket` is on `XMLHttpRequest`, not `fetch`: `fetch` gives no
upload-progress events, `xhr.upload` does (`lengthComputable`, `loaded`, `total`).
Progress is shown only on the `PUT` to the bucket — the two API steps (`upload-link`,
`confirm`) are tiny JSON. Exactly **one** header (`Content-Type`) or the bucket's CORS
preflight breaks.

### Download

A hidden `<iframe>`, not `window.location.assign`: an attachment downloads the same, but
any other response (a stale link, a missing object, a bucket XML error) is just dropped
in the frame instead of replacing the whole SPA (navigation is top-level).

### Delete dialog

`DeleteNoteDialog.tsx` — native `<dialog>` like the queue. It names what disappears: how
many notes link here and how their links will read afterwards, plus a checkbox "Also
delete the file …" that is **checked by default** (unchecking explains the consequence).
Mounted with `key={id}` — a fresh mount is the checkbox reset, no set-state-in-effect
(that is what oxlint `react(set-state-in-effect)` flags).

### One "Files" section — a source note lives under its file

A `Source` note is 1:1 with its file and has no identity of its own, so the two lists were
one. `AssetList` is now the only list; a row expands (one open at a time) to a `FilePanel`:

- Actions on top — `Process again` / `Delete` / `Download`. `Process again` calls the
  note's `process-again` when there is a note, else the asset's `process`.
- A `Note | File` toggle. **Note**: the note body via `renderNoteBody(…, { linkable: false })`
  — `[[links]]` render highlighted but inert, because the accordion that a click needs is
  gone (note-to-note navigation is an Open item). **File**: an inline `<img>` for images
  (signed URL fetched on panel open — expiry does not matter for a preview open now);
  every other type says "use Download".
- `Delete`: the full `DeleteNoteDialog` (backlink warning + "also delete the file"
  checkbox) once the note has loaded; a plain `confirm` + `deleteAsset` before that or when
  there is no note.
- `FilePanel` is keyed `storedFileName:noteId` in `AssetList` — a re-run makes a new note
  id, and the key change remounts the panel so its `useState`-seeded loading state is fresh
  rather than showing the old body (same trick as `DeleteNoteDialog`'s `key`). This is why
  the fetch effects never call `setState` for "loading" — that would trip oxlint
  `react(set-state-in-effect)`.

### Capture date + geolocation in the file row

`AssetList`'s meta line (`formatSize · formatDateTime(uploadedAtUtc)`) appends `Taken …`
and `lat, lon` when `AssetSummary.capturedAtUtc`/`latitude`/`longitude` are present — null
before the pipeline has run, and null after if the photo carried no EXIF (a screenshot, a
re-encoded one). See *Photo capture metadata* in [`ai-pipeline.md`](ai-pipeline.md) for
where these come from. The `Done` badge text is `Processed`, not `Note ready`.

### Notes list — synthesis only

`NotesList` now fetches `GET /api/notes?kind=Synthesis` (Source notes are under their file)
and the whole section returns `null` while there are no synthesis notes **and** an empty
bin. Row split into a disclosure button and `.note-actions` (`Process again` only when a
file exists — never for synthesis — and `Delete`). A `Bin (n)` section with `Restore` /
`Delete forever`. After `Process again` the list **polls the server every 4 s** until the
old version's id disappears — the worker is a separate process and its SSE does not reach
the browser (same reason in `AssetList`).

### Bulk select in the file list

`AssetList` keeps a `Set<storedFileName>` of ticked rows. A file ticked then removed by an
SSE reload stays in the set but is filtered out on render and cleared on the next delete —
pruning it in an effect would trip oxlint `react(set-state-in-effect)` for no real gain
(single-user, tens of files). "Delete selected" fires the existing single-file
`DELETE /api/assets/{name}` per row via `Promise.allSettled`: no bulk endpoint, and a
partial failure leaves the still-selected rows on screen with a count in the error line.
The action bar is built to take more verbs later; for now it is only Delete.

### Mobile layout

The file list is a `ul` + CSS Grid, not a table (a table forced a 400 px min width and
the Delete button off-screen; it was never tabular data). `h1` needs an explicit
`line-height: 110%` — an inherited percentage becomes a fixed pixel value. Tap targets
≥ 44 px.

### Sections are cards, depth carries the hierarchy

`.assets` and `.notes` are cards (`--surface`, border, 12 px radius) whose first `h2` is
the header strip (`--surface-2`, uppercase label + a `.section-count` pill). The strip and
every row use `margin-inline: -18px` to bleed back out to the card edge while the card's
own padding sets the text inset — so a row's hover fill and its separator span the full
width. **Change the card padding and the bleed margins together** (the mobile block at the
bottom of `App.css` does exactly that), or rows stop lining up with the strip.

Four depth levels, and nothing relies on colour alone: page `--bg` → card `--surface` →
open row `--surface-hover` + a 2 px `inset` accent rail → `FilePanel` recessed back onto
`--bg`. The panel had its own accent `border-left` before the rail existed; two accent
edges on one row read as a mistake, so the row keeps it and the panel does not.

Before this the sections were a bare `h2` over a hairline list on the page background —
the whole page read as one continuous wall of rows, and restyling inside the rows changed
nothing you could see from a step back.

`.subsection-panel` (`TagsSection.css`) mirrors the same card-with-header-strip shape one
depth step below the card itself: bordered box, `.tags-subhead` as the bled header strip
(uppercase, `.section-count` pill), body below. Both its tones step up from the card's own
(`--surface-2` body, `--surface-hover` strip) rather than reusing `--surface`/`--surface-2`
— reusing those exactly would make the subsection blend into the card instead of reading as
nested inside it. Generic on purpose so any subsection (not just Tags') can opt in. Used
today by every subsection in `TagsSection`: "To review", "To place", "Confirmed" and
"Hierarchy".

### Colour tokens — surface vs accent

`--surface` / `--surface-hover` are the raised-panel and hover fills; `--accent-*` is a
line/text accent only, never a large fill. The file panel is `--surface` + a 2 px
`border-left` in `--accent-border` — an earlier `background: var(--accent-bg)` wash went
muddy on the dark ground. Status colours are tokens (`--ok` / `--warn` / `--danger`,
`--danger-bg` / `--danger-border`) with a **separate dark-theme set** — the light-theme
`#15803d` / `#b45309` / `#dc2626` were near-illegible on `#16171d`.

### Tag merge search — `TagPicker`

The "merge into" picker in `TagsSection` used to be a plain `<select>` of every tag,
ordered by note count — unusable once the vocabulary passes a hundred entries, and it
could not surface a match that was not yet confirmed (see *Tag review* in
[`database.md`](database.md)). `TagPicker.tsx` replaces it: a text input, debounced
(200 ms), calling `GET /api/tags?query=&excludeId=` — ranking happens on the backend, the
component just renders what comes back (top 8). Built with no dependency on the merge
flow specifically (`onPick(tag)`, `excludeId`), so a future manual "add a tag to this
note" picker can reuse it rather than growing its own search.

For an **unconfirmed** tag that already carries an AI suggestion (`suggestedMergeIntoId`),
`TagsSection`'s row skips the picker entirely and renders `TagMergeOptions` instead — one
button per candidate (the suggestion, then confirmed tags close in spelling), fetched once,
no search box, no click-to-open. A tag with no suggestion falls back to `TagPicker` — there
is nothing to make explicit yet. Confirmed tags always keep `TagPicker`; a search box still
earns its place there since there is no AI guess to shortcut.

### Jobs section — worker status

`JobsSection` lists jobs the pipeline has queued or is running, via `GET /api/jobs`
([backend.md](backend.md)). Kept simple on purpose:

- **Continuous polling (5 s), not SSE.** The worker flips `Pending` → `Running` → `Done`
  entirely inside its own process with no ping to the API in between (only a finished note
  triggers one, see *Worker → API bridge* in [`worker.md`](worker.md)), so an event-based
  refresh would miss the states this section exists to show. Same reasoning as `NotesList`'s
  post-`Process again` poll, just running all the time instead of only after one action —
  this section's whole point is being a live worker heartbeat.
- **Does not hide when empty** — shows "No active jobs.", unlike `NotesList` returning `null`
  when there is nothing to show. An empty list here is itself the useful signal ("worker is
  caught up"), not a section with nothing to say.
- One list, status as a badge (`Queued`/`Running`), not two separate blocks — see chat decision
  2026-09-12, kept out of a Decisions-worthy debate: job count is small (single-user), so a
  badge reads fine without the extra grouping markup.

### Note body is untrusted-shape Markdown

`.note-body` scopes its own `h1`–`h4` (20 / 17 / 15 / 14 px) and `p` / `ul` / `ol`
spacing. Without the heading scope the model's `# Title` inherits the page's 56 px
marketing `h1` and fills the row.

## Open

### Bugs / polish

- [ ] Following a wiki-link closes the source note (the list is an accordion) with no way
      back. Allow several expanded, or add a "back".
- [ ] After a backend restart the page waits on the backoff (~30 s after the API already
      answered): 502 through the proxy puts the stream in `CLOSED` and the delay doubles
      2→60 s. Reset the delay on a user action or a tab return.
- [ ] `GET /api/auth/me` returns 401 before login — a red console error every cold start.
      Behaviour is right, the noise is not.
- [ ] Enter in the password field does not submit the login form — click only.
- [ ] Native `<input type="file">` is 21 px tall, half the 44 px finger target. Needs a
      hidden input + a label button.
- [ ] `.note-body` has no styling for `table` / `blockquote` / `hr` / nested lists — the
      model can emit any of them. Only headings, `p`, `ul`/`ol`, `pre`, `code`, `a` are
      covered.
- [ ] dev: one dialog show fires **six** `GET /{id}/backlinks`, and each mutation a
      doubled `notes` + `trash`. StrictMode explains a doubling, not six — worth a look.
- [ ] SSE events after a delete (`notes` + `assets`) — the `Publish` calls are in place
      but the stream was not listened to separately.
- [ ] N files → N `onUploaded()` → N `GET /api/assets` + N SSE events. `AssetList`
      survives it (a counter drops stale responses) but the requests are wasteful.
      Debounce the reload in `AssetList`.
- [ ] Forbid the layout agent from injecting its own markup into the page — it measured a
      pasted copy of the login form instead of the live screen.
- [ ] Remove unused template files: `src/assets/{react.svg,vite.svg,hero.png}`,
      `public/icons.svg` — committed, no reference anywhere.

### Pending browser verification

- [ ] **Jobs section** (`GET /api/jobs`, `JobsSection`): build + lint pass, **browser run
      pending** — a queued job appearing (upload, `suggest-merges`, `suggest-hierarchy`), the
      `Running` badge while the worker has it, the row disappearing once `Done`/`Failed`, the
      retry case (`Error` shown on a re-queued `Pending` row), phone width.
- [ ] Bulk upload: drop several files, per-row progress popup, a `warning` file (text
      over 12 000 bytes) with "Upload anyway", a `blocked` file (empty and over 25 MB)
      with `Dismiss`, "Upload all anyway", "Dismiss all", ESC during upload (ignored) and
      after (closes), auto-close after the last success, rows appearing in `AssetList`.
- [ ] "Upload from clipboard" button (desktop + phone).
- [ ] Clipboard paste via Ctrl+V / Win+V / Gboard clipboard tab.
- [ ] `Escape` in a dialog — CDP key injection does not raise the native `cancel` event;
      needs one real keypress.
- [ ] Notes list: click a note on a live app and render its Markdown (needs one processed
      note).
- [ ] Tag chips (`FilePanel` note tab + `NotesList` `.note-meta`) — confirmed vs
      unconfirmed styling, wrapping with a long tag set, both themes.
- [ ] `.subsection-panel` background + larger `.tags-subhead` font on "To review",
      "Confirmed", "Hierarchy" — build + lint pass, **browser run pending** (both themes;
      how the tint reads behind `TagHierarchyTree`'s own `--surface` nodes).
- [ ] Bulk select in `AssetList`: still unverified in the browser — the partial-failure
      path (some deletes fail → rows stay ticked + "Could not delete X of N") and
      checkboxes/buttons disabled while a bulk delete runs. (Selection, select-all,
      indeterminate, confirm text, happy-path delete and phone layout were checked.)
- [ ] The `connectingTimer` 1.5 s branch (server accepts the connection then goes silent
      — Render cold start). Locally a dead backend gives an instant `onerror` by another
      path.
- [ ] SSE client in the browser: stream closing on a hidden tab, the 15-min idle close,
      the re-read after returning, reconnect after an API restart.

### File panel — next steps

- [ ] **Version dropdown in `FilePanel`.** Pick an earlier version of the note and make it
      active. Needs the two backend endpoints in [`database.md`](database.md) (list
      versions for an asset, activate one); this is the UI on the "Note version selection"
      item there.
- [ ] **Follow a `[[link]]` from the panel.** `renderNoteBody(..., { linkable: false })` for
      now. To wire it up: `AssetList` has `noteId` per row, so build a `noteId → file` map,
      expand that row and switch its panel to Note. A link to a synthesis note goes to the
      Notes section instead.
- [ ] `Process again` from the panel does not poll for the fresh note the way `NotesList`
      does — it leans on `AssetList`'s job poll + the `FilePanel` key change. Fine so far;
      revisit if the panel ever feels stale after a re-run.

### Tagging (design in [`database.md`](database.md))

- [ ] Faceted tag filter on the notes / files lists, plus an "Untagged" filter.
- [ ] Tag chips (`components/TagChips.tsx`): `note.tags` (`{ name, confirmed }[]`, primary
      first), unconfirmed = grey dashed chip, same cue as an unresolved wiki-link. **Done
      in code** in `NotesList` `.note-meta` (was `{category}`) and above the note body in
      `FilePanel`; build + lint pass, **browser run pending**. Faceted / "Untagged" filter
      and the review UI are the items below.
- [ ] Tag review UI in `TagsSection`: every tag listed, "To review" block for unconfirmed
      ones — explicit "→ «candidate»" merge buttons (`TagMergeOptions`) when there is an AI
      suggestion, a searchable "Merge into…" (`TagPicker`) as fallback when there is none —
      both ask before merging, Confirm, Synthesise (≥2 notes), Delete (asks first for a
      confirmed tag). "Suggest merges" head button queues the `GroupTags` job (see
      *Synthesis pipeline* in [`ai-pipeline.md`](ai-pipeline.md)). Build + lint pass,
      **browser run pending** (incl. phone width — the row wraps with several merge
      buttons, and the picker's dropdown position).
- [ ] Tag hierarchy tree and placement suggestions panel — done in code and verified in the
      browser (see [`archive.md`](archive.md)); **phone width still unchecked** for the
      tree's drag-and-drop and the suggestion cards' pill wrapping (the tree's own row
      wrapping at phone width was checked and fixed — see archive.md).
- [ ] `TagHierarchyTree` drag-and-drop, real-mouse check: the `onDragLeave` flicker fix
      (`useTagHierarchyTree.ts`) is build+lint clean but unverified by an actual drag —
      browser automation's synthetic mouse drag does not fire native HTML5 `dragstart` at
      all (confirmed: no request, no tree change after a scripted drag), so this needs the
      owner's own mouse, not a subagent.

### Larger features

- [ ] Routing (react-router).
- [ ] Server-state library (consider TanStack Query).
- [ ] Markdown editor.
- [ ] Note search.
- [ ] Link graph.
- [ ] Folder drag with a recursive walk (`FileSystemDirectoryReader`) — currently a
      folder is recognised and honestly skipped.
