# Frontend (`frontend/`)

React + TypeScript + Vite SPA. `strict: true`, avoid `any`. Lint is **oxlint**, not
typescript-eslint. See [`architecture.md`](architecture.md) for constraints,
[`backend.md`](backend.md) for the API side.

Per-feature decisions and all frontend Open items live in
[`frontend-features.md`](frontend-features.md). This file is the evergreen part: how the
SPA is structured, which shared components exist, how styling works.

## Structure & conventions

Layout: `api/` (one module per resource + `http.ts` + `realtime.ts`), `components/`,
`hooks/`, `upload/` (queue, classifier, drop-zone state), `notes/` (body renderer),
`preferences/` (per-user UI settings store + `usePreference`),
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
- `AssetList/`, `DeleteNoteDialog/`, `FilePanel/`, `LoginForm/`, `TagSearchPicker/`,
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
  - `SearchPicker/` and `TagSearchPicker/` are not nested inside `TagsSection/`: the tag
    picker is used by `TagsSection`, `TagPlacementSuggestions` and `FilePanel`, and
    `SearchPicker` underneath it is shared with the people review.

No behaviour changed doing any of this - e.g. `NoteRow`'s one existing asymmetry (the active
list shows a loading/error state for the expanded body, the bin only ever shows the ready
state) was kept as-is, not unified.

### UI settings — `usePreference`

Anything the user sets about the layout (a collapsed section, a view toggle) goes through
`usePreference` / `useCollapsibleSection`, which save it per user on the server with a
`localStorage` cache — never a bare `useState` or a raw `localStorage` key. The store is started
from `useAppAuth` at the moment the session is known, not in an effect, so the first render
already has the saved values. Details in [`storage-and-caching.md`](storage-and-caching.md).

## Shared components

Before building any new UI piece, check this list — reuse or extend an existing component
instead of growing a second one (the `new-feature` flow's no-copy-paste rule). The list is
short on purpose; each component's own doc comment carries the usage details.

- **`Dropdown`** — floating-menu primitive: trigger, outside-click / Escape closing,
  open/close transition. Decision below.
- **`Select`** — single-choice dropdown built on `Dropdown`, replacing native `<select>`
  (its option list is an OS widget the page cannot style). `placeholder` shows on the
  closed trigger and doubles as the clearing row. Browser run pending — see the Open item
  in [`frontend-features.md`](frontend-features.md).
- **`ProgressiveImage`** — every image in the app renders through it; shimmer placeholder
  with real download progress. Decision below.
- **`InfoHint`** — an "i" icon tooltip on hover, keyboard focus or tap; not the native
  `title` (it waits ~1 s and never shows on touch). Doc comment in the component.
- **`FacePreview`** — `FullPhotoPreview` (photo with a face box), `FaceCropPreview` (square
  crop of one face) and `PhotoPopupDialog`; shared by `PhotoAnalysisSection` and
  `PeopleReviewSection`.
- **`TagChips`** — confirmed vs unconfirmed tag chips, shared by `NotesList` and
  `FilePanel`; flat file, no hook.
- **`SearchPicker`** — the one searchable picker (input or collapsed chip + `Dropdown` panel,
  sections, an "Add new …" action). It holds no data: the caller turns the typed text into
  sections. Adapters: **`TagSearchPicker`** (`useTagSearch`, server-side tag search; decision in
  [`frontend-features.md`](frontend-features.md) *Tag merge search*) and `PersonNamePicker`
  in `PeopleReviewSection` (local filter over people). A new "pick one X" field is another
  adapter, not another dropdown.

### Progressive image loading — `components/ProgressiveImage`

Every image in the app (FilePanel preview, all PhotoAnalysisSection previews/thumbnails,
PhotoMultiSelect thumbs) renders through `ProgressiveImage`: the bytes are downloaded via
XHR (`useProgressiveImage`), so the placeholder can show **real** download progress — a
shimmering grey gradient with the percent in the middle on large images, shimmer alone on
small thumbs (64 px crops, photo-grid cells, chips). A plain `<img>` would paint the
half-loaded file itself; XHR is the same tool the upload path uses for request progress,
here on the response. The finished picture arrives as an object URL and fades in — the
half-painted state never exists. The shared image cache owns each XHR and object URL;
components subscribe through `useSyncExternalStore`. Multiple crops of one photo share progress
and downloaded bytes. Unused images are evicted by count/byte limits, and object URLs are revoked
on eviction, deletion or session reset. See [storage-and-caching.md](storage-and-caching.md).

- `fetchDownloadUrl` shares requests by stored file name, batches cache misses and respects
  `expiresAtUtc`. `useAssetPreview` depends on the file identity rather than a freshly allocated
  asset object. A `null` URL keeps the placeholder up while the link request is in flight.
- `className` goes on both placeholder and image, so per-context sizing rules fit each
  (`.candidate-thumbnail .progressive-image-loading`, the `:has` rule on
  `.face-frame-preview` that gives it a width before the source aspect ratio is known).
- Bucket GET must stay CORS-readable by JS (`AllowedOrigins: *` in `infra/garage/cors.json`);
  R2's rule is set in the Cloudflare dashboard — see the CORS note in [`infra.md`](infra.md)
  (its PUT-only rule was the prod CORS storm on day one).

### Shared dropdown

`components/Dropdown` owns the common trigger, outside-click / Escape closing and the
open/close transition for floating menus. The panel stays mounted until its reverse
transition finishes, so every consumer expands from and collapses back into its trigger
rather than appearing or disappearing abruptly. `SearchPicker` and the tag `⋯` actions menu
are its first consumers; callers supply only their trigger and panel content, plus the
alignment edge.

## Styling

### Colour tokens — surface vs accent

`--surface` / `--surface-hover` are the raised-panel and hover fills; `--accent-*` is a
line/text accent only, never a large fill. The file panel is `--surface` + a 2 px
`border-left` in `--accent-border` — an earlier `background: var(--accent-bg)` wash went
muddy on the dark ground. Status colours are tokens (`--ok` / `--warn` / `--danger`,
`--danger-bg` / `--danger-border`) with a **separate dark-theme set** — the light-theme
`#15803d` / `#b45309` / `#dc2626` were near-illegible on `#16171d`.

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
(uppercase, `.section-count` pill), body below. Both tones step up from the card's own
(`--surface-2` body, `--surface-hover` strip) rather than reusing `--surface`/`--surface-2`
— reusing those exactly would make the subsection blend into the card instead of reading as
nested inside it. Generic on purpose so any subsection (not just Tags') can opt in. The panel
itself also bleeds `margin-inline: -10px` (a panel inside a panel: -6px) so nesting does not
drift right with depth — depth is carried by the tone steps alone. **Don't reset a panel's
horizontal margin** (e.g. a `margin: 0` on a wrapper): that silently kills the bleed — the
original bug, now `margin-block: 0` in `PhotoAnalysisSection.css`. Used
today by every subsection in `TagsSection`: "To review", "Confirmed" and "Hierarchy".

### Mobile layout

The file list is a `ul` + CSS Grid, not a table (a table forced a 400 px min width and
the Delete button off-screen; it was never tabular data). `h1` needs an explicit
`line-height: 110%` — an inherited percentage becomes a fixed pixel value. Tap targets
≥ 44 px.

### Note body is untrusted-shape Markdown

`.note-body` scopes its own `h1`–`h4` (20 / 17 / 15 / 14 px) and `p` / `ul` / `ol`
spacing. Without the heading scope the model's `# Title` inherits the page's 56 px
marketing `h1` and fills the row.
