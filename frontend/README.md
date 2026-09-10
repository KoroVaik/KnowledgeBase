# Frontend

React + TypeScript + Vite SPA for the Knowledge Base. `strict: true`, avoid `any`.
Lint is **oxlint** (`.oxlintrc.json`), not ESLint.

Design notes and open items: [`../docs/frontend.md`](../docs/frontend.md).

## Run

```bash
npm run dev      # http://localhost:5173, proxies /api → http://localhost:5244
```

The API must be running too — see [`../CLAUDE.md`](../CLAUDE.md) → "Running locally".

## Checks

```bash
npm run build && npm run lint
```

## Layout

- `api/` — one module per resource, plus `http.ts` (`apiFetch`, `ApiUnreachableError`)
  and `realtime.ts` (the single SSE connection per tab).
- `components/` — screen components.
- `hooks/` — `useConnectionStatus`, `useResourceChanges`.
- `upload/` — the upload queue (`useUploadQueue`) and the sync classifier (`classify.ts`).
- `notes/` — `renderNoteBody.ts`, the `marked` extension that renders `[[wiki-links]]`.
- `format.ts` — shared formatting helpers.
