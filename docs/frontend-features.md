# Frontend features

Per-feature decisions for the SPA — why each screen and client-side piece is the way it
is — plus every frontend Open item. The evergreen part (SPA structure, conventions,
shared components inventory, styling rules) lives in [`frontend.md`](frontend.md).

## Decisions

### Buffered section updates

Sections load when expanded and visible (180 ms dwell), and stop background reads/polling
when collapsed, offscreen or in a hidden browser tab. SSE still marks hidden resources stale.
An unchanged section can reuse its snapshot for a quick return; after 15 seconds it revalidates.
Drafts and selections protect a returning section, and interaction during an entrance request
prevents that response from replacing the working snapshot.

Visible lists keep displayed and incoming snapshots separately. `Show N new items` counts
unique new records matching the list filter, inserts them around their sorted neighbours,
and expands the visible limit so existing rows stay visible. It never applies edits, removals,
reordering, or new faces inside an existing group. Those changes use `Reload`. A successful
local action applies its affected IDs without accepting unrelated pending changes. Failed
refreshes retain the current rows and offer retry. Buffered snapshots are memory-only.

Files and Jobs keep status badges live without changing list membership. A job absent from
the active response is labelled "No longer active" (or "Failed" when known), not assumed
successful. File panels retain the selected Note/File tab when accepting a new note version.
Persons, Locations and Events have independent visibility and working snapshots; they still
share the existing archive endpoint and its request coalescing. Comparison results require
Reload while being viewed. Upload progress remains independent of section visibility.

### Row removal transitions

Files, Notes/Bin, tag review/confirmed lists, people review/unsorted/ignored groups and
photo review candidates opt into `GenericList`'s shared `AnimatedList`. A removed row stays
mounted and inert for a 380 ms fade and height collapse; lower rows move into its space,
and the next page item fades in after the departure. Concurrent refreshes retain exits
and row identity. Server reordering waits for departures to finish, then animates surviving
rows from their previous positions without remounting their inputs or disclosure state.
Filters and explicit page-limit changes reset transitions immediately. Reduced motion uses
a 120 ms fade with no animated movement or height change. Lists that become empty remain
mounted so the final row can finish departing. Actions retain their busy state through
the refresh, and a failed mutation leaves the row available for retry.

### Shared row limits

Row lists use `GenericList` with five items initially, "Show more" in batches of up to five,
and "Show less" returning to five. Each logical list has its own saved user preference;
changing its filter resets the limit. This applies to Files, Notes/Bin, tag review/confirmed
lists and tree siblings, active/failed jobs, archive records and review lists, detector groups
and recognizer evidence. Graph views, selection menus, model summaries and horizontal photo
strips keep their existing format. Files selects/deletes only visible rows; wiki-link navigation
reveals the target note before scrolling to it.

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
- `paused` (a deliberate close) vs `offline` (an error or a reconnect still pending after
  1.5 s). The banner reacts only to `offline`. The grace timer only shows the banner;
  it never aborts a pending connection. Actual errors schedule retries; the manual button
  replaces the stream immediately.
- Cold start against a dead API does **not** drop to `anonymous` (that showed a login
  form pointing nowhere) — a separate `unreachable` state with "Try again". The tell is
  "`fetchCurrentUser` threw", not "network error": it returns `null` only on 401.
- `apiFetch` in `api/http.ts` turns a `fetch` rejection into `ApiUnreachableError`.
  `fetch` rejects only when there was no response at all, and its message differs per
  engine — "Failed to fetch" must not reach the user.
- Upload notifications and SSE both refresh the listing, including when the stream is down.
  Asset reads share a 200 ms window and one request in flight; a change during that request
  schedules one trailing refresh. Pointer activity no longer cancels offline SSE backoff.

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
- At most four bucket PUTs run at once, across additions and retries. Signing and confirmation
  use batches of up to 50 items, collected for 20 ms. Each file keeps its progress and result;
  a failed confirmation retries the same uploaded key without sending the bytes again.
  Supersedes the previous all-at-once decision (2026-09-23).
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
- `FilePanel` is keyed by `storedFileName`. The loaded note carries its own ID, so accepting
  a new version reloads the body without remounting the panel or losing the Note/File tab.
  An explicit section Reload also reloads the expanded note body.

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

### One review queue, not two

"To review" and "To place" used to be separate subsections, but both were the same action
(approve an AI-guessed tag relationship) wearing two different looks - a chat with the owner
2026-09-12 confirmed the duplication. Merged into one `.tags-list`: `TagsSection.tsx` feeds
unconfirmed tags followed by confirmed-tag placements into one `GenericList`, rendering
`TagReviewRow` or `TagPlacementRow` inside the same `<ul>`. Both use the same
`tag-action-chip`/pill classes as `TagParentOptions`, with an accept/reject pair per
placement candidate instead of a single pick.

`toPlace` now filters to **confirmed** tags only
(`confirmed.filter(tag => tag.hasPendingPlacementSuggestion)`) - an unconfirmed tag's own
pending parent suggestion already shows inline in its review row via `TagParentOptions`
(confirm + place in one click); including it in `toPlace` too was showing the identical
suggestion twice, once per UI.

### To review — List/Graph toggle

`TagSuggestionGraph` gives "To review" the same List/Graph split the Hierarchy subsection
already has (`TagHierarchyGraph`): a mini node-link diagram per row instead of the flat chip
pills — the tag under review centred, parent candidates above, child candidates below,
connected by curved edges. No dagre/pan/zoom like the big graph - a row only ever has a
couple of candidates per side, so a fixed three-tier arithmetic layout
(`computeLayout` in `TagSuggestionGraph.tsx`) is enough.

Candidate/accept/reject controls are real `<button>`s inside `<foreignObject>`, not plain SVG
shapes - keeps normal focus/`disabled`/`aria-label` behaviour instead of hand-rolled hit
testing. Node width is estimated from character count (name + confidence word), same
technique `useTagHierarchyGraph`'s dagre sizing already uses - **a first pass only counted the
name and clipped both the text and the accept/reject hit area** for confirmed-tag placement
rows; fixed by budgeting the confidence word's width too, then browser-verified with
`elementFromPoint` that ✓/× now resolve to the button, not the parent `<svg>`.

"Merge into…" stays the existing button/picker rendered beside the graph, untouched by the
toggle - it replaces a tag, it does not add a parent/child edge, so it does not belong inside
the hierarchy diagram.

### Tag merge search — `TagSearchPicker`

The "merge into" picker in `TagsSection` used to be a plain `<select>` of every tag,
ordered by note count — unusable once the vocabulary passes a hundred entries, and it
could not surface a match that was not yet confirmed (see *Tag review* in
[`database.md`](database.md)). `TagSearchPicker` (on the shared `SearchPicker`) replaces it: a text input, debounced
(200 ms), calling `GET /api/tags?query=&excludeId=` — ranking happens on the backend, the
component just renders what comes back (top 8). Built with no dependency on the merge
flow specifically (`onPick(tag)`, `excludeId`), so a future manual "add a tag to this
note" picker can reuse it rather than growing its own search. Listed in the shared
components inventory in [`frontend.md`](frontend.md).

For an **unconfirmed** tag that already carries an AI suggestion (`suggestedMergeIntoId`),
`TagsSection`'s row skips the picker entirely and renders `TagMergeOptions` instead — one
button per candidate (the suggestion, then confirmed tags close in spelling), fetched once,
no search box, no click-to-open. A tag with no suggestion falls back to `TagSearchPicker` — there
is nothing to make explicit yet. Confirmed tags always keep `TagSearchPicker`; a search box still
earns its place there since there is no AI guess to shortcut.

### Jobs section — worker status

`JobsSection` lists jobs the pipeline has queued or is running, via `GET /api/jobs/summary`
([backend.md](backend.md)). Kept simple on purpose:

- **Polling while expanded and visible, five seconds after the preceding request completes.** The worker flips `Pending` → `Running` → `Done`
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
- **Kind description from the backend**, shown as a custom tooltip behind an "i" icon next to
  the label (hover, keyboard focus, or tap — mobile Safari does not focus a tapped button, so
  tap toggles explicit state). Not the native `title`: it waits ~1 s and never shows on touch.
  Labels themselves still live in `KIND_LABELS`.
- **Failed jobs get their own block** under the active list (the same summary response), each with
  **Retry**, plus **Retry all**. Retry is always a human decision, never automatic: until the
  cause is fixed a job just fails three times again. Skipped jobs are not offered — the same
  input cannot succeed. Jobs of a deleted file vanish with it (cascade), so none show here.

### Photo analysis catalogue

`PhotoAnalysisSection` is a card with two experiment subsections and three archive subsections:
face detector comparison, face recognizer comparison, Persons, Locations and Events. The archive
subsections are live records, not placeholder UI: a person or location can be created directly,
and an event can be created with an optional date/location plus selected people and existing image
assets. Unreviewed AI candidates appear in these same contextual subsections rather than a
separate generic review page. Each location/event candidate shows its source photo, candidate rank
and raw score; the reviewer can expand the immutable model evidence and accept, reject, or correct it
to a canonical record. The section is empty until a worker writes candidates.

Faces are not reviewed one by one. Between the Persons "Add" form and *Known Persons* sits
`PeopleReviewSection` (own folder, `usePeopleReview` hook, reads `GET /api/photo-analysis/people-review`)
with one row per person: confirmed people with new faces, then anonymous groups (largest first), then
*Unsorted faces*, then Ignored groups (collapsed by default, saved per user). **To review**,
**Unsorted faces** and **Ignored** use the shared subsection cards with header strips and counts;
each whole header toggles its content, with collapsed state saved per user. To review and
Unsorted start expanded; Ignored starts collapsed. To review counts person and anonymous groups,
Unsorted counts faces, and Ignored counts groups. A row is a wrapping
strip of 64 px crops; a confirmed person's row has two labelled strips, one above the other: *Approved
faces* (up to three most typical confirmed faces, chosen by the API as the highest average similarity to
the person's other confirmed faces, with a small green check in the corner) and *New suggested faces*.
New faces have a checkbox under them, checked by default. Nothing is removed on the spot - **Submit person** / **Ignore** send the
checked faces and the unchecked ones together, and the unchecked ones move to Unsorted in the same save
(no half-applied row if the call fails). Unsorted faces have no checkbox - one face, nothing to uncheck. Crops touching the photo edge show a yellow warning indicator; detections unconfirmed by secondary SCRFD verification show an orange one instead. The shared `ExpandableBadge` starts as a 12 px circle inside the crop's lower-left corner, with a 60% opaque background and an opaque warning icon. Hover or keyboard focus expands it rightward into **Photo edge** or **Needs review**, without a tooltip. A separate positioning wrapper keeps the badge independent of image clipping and shared component styles. Edge contact alone never disables naming or grouping; the validation reasons still show it when both flags apply. Clicking a crop opens the full-photo popup with
the face box. Rows without a person have a **Select person name** picker (`PersonNamePicker`, an adapter over the
shared `SearchPicker` - same panel as the tag picker): focusing it lists up to 10 existing people (by name), typing narrows them to names containing the text, plus a
last "Add new name "…"" option (hidden on an exact match, so no duplicate). Picking either files the
checked faces immediately - there is no separate Submit on these rows; confirmed-person rows keep
**Submit person**. Anonymous and unsorted rows also have **Ignore**. A grey "Looks like: Name" chip fills
the picker and focuses it, so the suggestion is one more click. A 409 reloads the list; "Grouping faces…" shows while grouping
or detection jobs are active; SSE `photo-analysis` refreshes it. `FullPhotoPreview`, `FaceCropPreview`
and `PhotoPopupDialog` moved to the shared `components/FacePreview/` so Known * and people review use the
same code. Semantics and thresholds: [`photo-archive.md`](photo-archive.md).

Unsorted faces also show validation status and reasons, expandable CPU/model evidence, **Allow automatic grouping**,
**Exclude from people**, and **Retry validation** when an unfinished check has no active job. The first
two actions are explicit, reversible validity decisions without assigning a person.
Choosing a person also manually approves selected faces that are not eligible, including excluded ones,
in the same save as assignment. The picker stays available regardless of validation status; Ignored and
Unsorted explain that assigning a person also confirms the face. Ignored groups retain their faces
and expose the same validity controls. A separate waiting banner reflects active validation jobs;
an error never silently approves a face. An older API response without validation fields retains its
previous rendering during a rolling restart.
Ignored groups use one subsection per face, with a 96 px crop, a neutral selection checkbox and a short
validation badge. Each face always shows its own validation controls beside its crop, or below it
on narrow containers; clicking a crop still opens the original photo.
Person assignment stays per group, with a selected count and an explanation of combined
confirmation and assignment. Ignored means set aside; Excluded from people is a separate face-validity decision.
Each validation status uses the shared `InfoHint` component to explain automatic waiting, grouping
eligibility, review requirements and manual overrides; validation never assigns a person's name.

Each subsection ends with a collapsible **Known …** subsection instead of a bare list of every
record: *Known Persons* shows each person's confirmed reference faces (click for the full photo,
Revoke undoes the review decision's side effect), *Known Locations* shows each location's
confirmed observation photos, *Known Events* shows each event's title, date, location and photo
thumbnails. Records without confirmed evidence appear nowhere outside the "Correct to…" pickers
and the event form — that bare list was removed as noise (chat decision 2026-09-17). The
location reference photos come from the same `LocationResponse` (`referencePhotos`, asset id +
name + confirmed date), so a rolling API restart defaults them to an empty list like the
statuses.

Locations add a **Refresh location suggestions** action. It requests a new scene-analysis pass for
canonical photos that still have no confirmed location; the API client supplies zero-valued analysis
statuses when an older or transitional backend response lacks a newly added status field, so a
rolling API restart does not crash the card before the next refresh.

Events contains **Refresh scene observations**, not a fourth top-level archive section. It shows
each active, unreviewed VLM observation with photo name, kind, optional context names, cautious
confidence and expandable visible evidence. Confirm and Reject are review decisions; they never
rewrite the observation itself. The API client also defaults a missing observation list or status
to an empty list / zero count during a rolling backend restart.

Events also contains **Refresh event candidates**. A candidate card shows the member photos, score
and expandable clustering evidence. The reviewer can uncheck photos, enter the canonical event
details to create it, attach the chosen photos to an existing event, or reject the proposal. The
card is absent when the archive has no group above the clustering threshold.

### Browser diagnostics

Uploads, HTTP/XHR, reload initiators, SSE and browser failures produce bounded structured diagnostics. The collector transport has its own rate/backoff limits and never logs itself. See [observability.md](observability.md).

## Open

- [ ] Verify face cards on photos with adjacent people and edge-clipped faces: exact detected
      bounds with preserved proportions and neutral padding are implemented; frontend build/lint
      and backend build pass, browser verification is pending.

- [ ] Verify buffered updates in the browser: visibility/collapse/tab gating; filtered
      `Show N new items` with ten existing rows retained; empty lists; edits/removals and
      regrouped faces through Reload; local actions with pending remote changes; drafts,
      failures, reconnection and phone layout. Build/lint, 26 frontend unit tests and the
      backend build pass; browser behavior has not been checked.

- [ ] Shared row limits (`GenericList`): frontend build/lint and backend build pass;
      browser verification pending — Show more/less, independent lists, filter/search reset,
      saved limits, hidden file selections and wiki-links to notes beyond the first five.

- [ ] Verify request reduction with all 16 sections open and an active SSE subscriber:
      single and batch upload, cold/warm previews, expiry, partial failures, retry and reconnect.
      Implementation is present; runtime checks and tests were explicitly skipped on 2026-09-23.

- [ ] Verify the observability integration in the running application; runtime checks and iteration 2 request reduction are tracked in [observability.md](observability.md).

- [ ] **Face detector comparison UI.** The Photo analysis subsection has a **Compare N photos** batch
      action with an in-progress count (the single-photo picker was removed), paged run history that hides fully reviewed photos by default (with a **Show reviewed**
      checkbox), one shared photo with one grouped frame per candidate face and coloured
      model numbers, compact disagreement cards, per-model false-positive correction, hover linkage,
      and shared all-model missed-face controls. Detections are accepted by default; navigation
      finishes the current photo's review. An invalid photo can be excluded and later restored
      without deleting its model output. Build/lint pass; browser verification of queue/results,
      group merging, review persistence, popups and narrow screens is pending. Domain rules:
      [`photo-archive.md`](photo-archive.md).
- [ ] **Face recognizer comparison UI.** The Photo analysis subsection independently shows an
      **Analyze X photos** action for faces missing a model result, runs three recognizers without
      changing person suggestions, and uses confirmed ownership only as optional automatic ground truth
      for F1/precision/recall, thresholds and selected disagreement pairs. Build/lint pass; browser
      verification after the migrations and a worker run is pending. Domain rules:
      [`photo-archive.md`](photo-archive.md).
- [ ] **`TagPicker` → `SearchPicker` + `TagSearchPicker` — run pending.** Build + lint green. Not
      seen in the browser: the five tag pickers (merge chip, icon chip, FilePanel "Add tag…",
      placement row) look and behave as before. Two deliberate changes to check: picking an
      option now closes the panel, and "Add new tag" has its green look everywhere (it was
      scoped to `.tags-row`, so FilePanel's was unstyled).

- [ ] **Local dev connection resilience.** During a photo-analysis browser check the first session
      request briefly returned HTTP 502 and Vite HMR could not open its WebSocket; retry recovered
      the authenticated UI and the feature flow completed, but reproduce the startup condition
      before treating either issue as fixed.

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

- [ ] **Expandable photo badges:** verify 12 px yellow/orange warning indicators inside
      the lower-left crop corner, translucent backgrounds, badge expansion/collapse on hover/focus,
      full labels without clipping, reduced motion and full-photo clicks without tooltips.
      Frontend build/lint and backend build pass; browser verification is pending.

- [ ] **Row removal transitions**: frontend/backend builds, lint and six transition tests
      pass. Browser verification pending for single/bulk deletion, the last row, next-page
      replacement, review actions, failed requests, concurrent refreshes, reduced motion
      and phone layout.

- [ ] **Stable Unsorted face order:** the API sorts by detection time and occurrence ID so regrouping
      cannot replace the visible first ten with another subset merely by changing clusters.
      Regression test (one approval, other statuses and first ten unchanged) and build/lint pass;
      restart the Rider API and repeat the browser scenario.

- [ ] **People review subsection cards:** verify the matching To review, Unsorted faces and Ignored headers, counts, whole-header toggles and persisted collapsed states in the browser.
- [ ] **Ignored face subsections:** verify selection counts, per-face controls,
      full-photo previews, validation actions and naming explanations, desktop/mobile layout
      and both themes. Build/lint pass; browser verification is pending.

- [ ] **Face validation in People Review:** pending and flagged faces in Unsorted, original-photo
      preview, advisory quality warnings, Allow automatic grouping/exclude, retry after failure, and
      combined confirmation/person assignment in Unsorted and Ignored, including excluded faces.
      Build/lint and backend assignment tests pass; browser and live worker checks are pending.
      Also verify the advisory **Photo edge** label replacing **Partial** on existing detections.
      Verify all five validation-status hints on hover, keyboard focus and tap.
      Needs review and action hints now use shorter, outcome-focused wording; person assignment
      explains human-face confirmation in plain language. Build/lint pass; browser check pending.

- [ ] Every `PUT /api/preferences/*` shows `net::ERR_ABORTED` in the browser-pane network log
      although it returns 204 and the value persists — same quirk as the tag accept/reject
      mutations below. The code aborts nothing; find out whether it is the pane, the Vite proxy
      on a bodiless 204, or real.

- [ ] **Single-choice dropdowns on the shared `Dropdown`** (new `components/Select`): all
      remaining native `<select>` elements in `PhotoAnalysisSection` were converted — build +
      lint pass, browser run pending — trigger shows the chosen value (placeholder when none,
      the placeholder doubles as the clearing row), every list opens and picks correctly, the
      event-form row keeps its flexible width.
- [ ] **Jobs section** (`GET /api/jobs`, `JobsSection`): build + lint pass, **browser run
      pending** — a queued job appearing (upload, `suggest-merges`, `suggest-hierarchy`), the
      `Running` badge while the worker has it, the row disappearing once `Done`/`Failed`, the
      retry case (`Error` shown on a re-queued `Pending` row), phone width.
- [ ] **Failed jobs + Retry** (`GET /api/jobs/failed`, `POST /api/jobs/{id}/retry`,
      `POST /api/jobs/failed/retry`): build + lint pass, browser run pending — the Failed block
      appears only with failed jobs, Retry moves a row back to Queued, Retry all empties the
      block, phone width.
- [ ] **Job kind info tooltip** (`kindDescription`, `JobKindHint`): build + lint pass,
      browser run pending — hover, Tab focus, tap on phone width, tooltip not clipped at the
      card edge.
- [ ] **"last run … ago" under *Suggest for review*** (`LastRunHint`,
      `GET /api/jobs/last-completed`): build + lint pass, browser run pending — "never run" on an
      empty history, the hint hiding while queued and updating once the worker finishes, the
      minute tick, both themes, phone width.
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
- [ ] Verify SSE recovery through Vite after hiding/idle and with manual Reconnect:
      the server now flushes an initial comment, and the 1.5 s banner timer keeps a pending
      stream alive. Regression tests and build/lint pass; browser verification is pending.
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
      does — it leans on `AssetList`'s job poll + the `FilePanel` note-ID dependency. Fine so far;
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
      suggestion, a searchable "Merge into…" (`TagSearchPicker`) as fallback when there is none —
      both ask before merging, Confirm, Synthesise (≥2 notes), Delete (asks first for a
      confirmed tag). "Suggest merges" head button queues the `GroupTags` job (see
      *Synthesis pipeline* in [`ai-pipeline.md`](ai-pipeline.md)). Build + lint pass,
      **browser run pending** (incl. phone width — the row wraps with several merge
      buttons, and the picker's dropdown position).
- [ ] Tag hierarchy tree — done in code and verified in the browser (see
      [`archive.md`](archive.md)); **phone width still unchecked** for the drag-and-drop
      (the tree's own row wrapping at phone width was checked and fixed — see archive.md).
- [ ] **"To review"/"To place" merge** (see *One review queue, not two* above). Build + lint
      pass, and a quick browser look confirmed the duplicate display is gone (an unconfirmed
      tag's suggestion no longer also shows as a card) and both row kinds now share one list
      and one look. **Not yet clicked through**: accept/reject on a merged-in placement row,
      and phone width for the wider rows (a confirmed tag with both a Parent and a Child pill
      group).
- [ ] **"To review" mini-graph presentation** (`TagSuggestionGraph`, see *To review -
      List/Graph toggle* above). Build + lint pass; browser-verified: pills show full
      name+confidence with no clipping, ✓/× hit-test correctly on confirmed-tag placement
      rows, the pick-only case (unconfirmed tag choosing its own parent) still confirms +
      places in one click, "Merge into…" and the List view are unaffected. **Not yet
      checked**: phone width for the mini graph (same open item as the flat rows below), and
      whether the accept/reject mutation's `ERR_ABORTED` network quirk (data persists, but
      the row does not disappear without a manual reload) is specific to this view or
      pre-existing in the flat chip rows too.
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
