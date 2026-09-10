# Infrastructure & deployment

The cloud backend is **stateless**; file bytes physically stay on the home PC; the
browser goes straight to them.

```
Browser ──static + API──► Render        (KnowledgeBase.Api in Docker)
        ──bytes──────────► Cloudflare R2  (bucket knowledgebase-assets)
                            Neon          (Postgres: metadata, notes, queue, Data Protection keys)

Home PC ──► KnowledgeBase.Worker ──► Ollama (localhost:11434)
                                ├──► Neon + R2 over the internet
                                └──► Render /api/events/ingest  ("new note" hint for SSE)
```

## Solution structure

`backend/KnowledgeBase.sln` — see [`architecture.md`](architecture.md). `Core` is
referenced by both apps; Api and Worker are independent entry points.

## DB migrations

Applied by the **API only**, on startup (`MigrateDatabase()`). The worker runs with
whatever the API has created. See [`database.md`](database.md).

**Deploy order: API first** (it applies the migration to Neon), then restart the worker.
A worker brought up against the old schema fails on the first hit to a new table.

## `KnowledgeBase.Api` → Render

- Runtime **Docker**, `Dockerfile` at the repo root, branch `main`. Render has no native
  .NET.
- Multi-stage image: `node:24-alpine` builds the frontend, `sdk:8.0-alpine` publishes
  `KnowledgeBase.Api`, `aspnet:8.0-alpine` runs it; `dist` lands in `wwwroot`. The worker
  is not in the image (`.dockerignore`).
- Kestrel listens on `PORT` (Render passes it).
- Live: <https://knowledgebase-9z29.onrender.com/>
- `ForwardedHeaders` **before** `UseHttpsRedirection` — behind a proxy it is a redirect
  loop otherwise. Verified on a Production build: a request with `X-Forwarded-Proto:
  https` → 200, without it → 307.

### Render env vars

| Key | Purpose |
|---|---|
| `ConnectionStrings__Database` | Neon pooled, ADO.NET `key=value` format (not URL). Add rows with a `=` in the value **one at a time** — bulk `Add from .env` splits on the first `=`. |
| `Storage__S3__ServiceUrl` / `__BucketName` / `__AccessKeyId` / `__SecretAccessKey` / `__Region` | R2 (`Region=auto`), key `knowledgebase-render`. |
| `Auth__Google__ClientId` / `__ClientSecret` / `Auth__Google__AllowedEmails__0` | Google sign-in (a separate OAuth client from the dev one). Their presence is the whole on/off — there is no feature flag. |
| `Events__IngestToken` | Shared secret for `POST /api/events/ingest`. Same value in `worker.env`. Empty → the endpoint returns `503`, live note updates are off. |
| `ASPNETCORE_ENVIRONMENT` | `Production` (already set in the Dockerfile). |

**Changing an env var does not restart the service** — needs `Manual Deploy → Deploy
latest commit`.

### Free-tier limits (2026-09-07)

- **Render**: sleeps after 15 min without traffic, wakes ~1 min; 750 instance-hours/month
  across the whole workspace (prod + staging cannot both be free).
- **Neon**: 0.5 GB, 100 CU-hours, 5 GB traffic/month; sleeps after 5 min, wakes in
  hundreds of ms.
- **R2**: 10 GB, free egress, but **requires a payment method on file**.

## Neon (Postgres)

- Project `hidden-thunder-38922758`, branch `production`. `neon link` created `.neon` and
  pulled `DATABASE_URL` / `DATABASE_URL_UNPOOLED` / `NEON_BRANCH` into `.env.local` (both
  gitignored). `neon.ts` holds the branch policy as code — currently an empty
  `defineConfig({})` (no deviation from project defaults).
- **Connection-string traps:** Npgsql does not understand the URL format Neon returns
  (`postgresql://user:pass@host/db`) — ADO.NET wants `key=value`. Translated by hand to
  `Host=...-pooler...;Database=neondb;Username=...;Password=...;SSL Mode=Require`;
  `channel_binding=require` dropped (no such Npgsql option, SCRAM channel binding
  self-enables). Locally it stays `appsettings.Development.json` with Postgres in Docker.
- Agent tooling stays in-repo: 7 Neon skills in `.claude/skills/` (+ `skills-lock.json`),
  the MCP server in **local** scope (`~/.claude.json` under this folder's key), not user
  scope. The account-wide API key was revoked and replaced with a project-scoped key
  (id 3319419) that cannot create projects or mint keys.

## Object storage — R2 (prod) and Garage (local)

- **Prod: Cloudflare R2.** Key `knowledgebase-render` (separate from the worker's
  `knowledgebase-worker`, revoked independently).
- **Local: single-node Garage** in Docker (S3-compatible, Rust, `dxflrs/garage:v2.3.0`),
  `infra/garage/`. Not MinIO — its community repo was archived in Feb 2026, no security
  patches. Doubles as the second copy. Also the prod bytes' physical home, reached via a
  tunnel. Details in [`../infra/garage/README.md`](../infra/garage/README.md).
  - Port 3900 open on all interfaces (3903 admin stays on loopback): the **browser** puts
    bytes in the bucket, so a phone on the LAN must reach it.
  - `rpc_secret` / `admin_token` in env, not the config file — a bind-mounted config on
    Windows looks world-readable to Garage and it refuses to start.
  - No `root_domain` — vhost-style needs wildcard DNS no tunnel provides; path-style only.
- **Public HTTPS tunnel: Tailscale Funnel** (`*.ts.net`, no domain needed).
  `tailscale funnel --bg 3900` → `https://rospc.tail11818d.ts.net`. Cloudflare Tunnel was
  rejected: needs an own domain on their NS and caps request bodies at 100 MB on Free.
  Enabled via a node attribute (`nodeAttrs` + `funnel`) in the tailnet policy — JSON
  editor only. The hostname is public (Certificate Transparency); storage security is the
  keys alone.
- **CORS is on the bucket**, not `AddCors` in ASP.NET — the browser uploads to a
  different origin, and whoever receives the request grants the permission. Set via the
  S3 API (`PutBucketCors`; no `garage bucket` command for it), which needs **owner**
  rights — so the backend key is `--owner` **forever**, not just for the edit. The rule
  is deliberately wide (`*` in origin, methods, headers): home storage, and a narrow list
  would mean re-uploading the rule every time the PC address changes or a device is
  added. Rule file: `infra/garage/cors.json`. R2 has the same rule.
- **R2 was one code change, not zero:** `UseChunkEncoding = false` on `PutObjectRequest`
  — R2 does not do `STREAMING-AWS4-HMAC-SHA256-PAYLOAD`. Garage is unaffected.

### AWS SDK v4 traps (all three)

1. `RequestChecksumCalculation` defaults to `WHEN_SUPPORTED` → a chunked body with a
   checksum trailer, Garage answers `Invalid payload signature`. Set `WHEN_REQUIRED` in
   the client config.
2. Chunked signing breaks R2 → `UseChunkEncoding = false` on the request (see above).
3. `GetPreSignedUrlRequest.Protocol` defaults to `HTTPS` and **ignores the `ServiceUrl`
   scheme** → local Garage on plain http, the first signed URL pointed at
   `https://localhost:3900`. The protocol is now derived from the `ServiceUrl` scheme.
   Would never have surfaced in prod (R2 is https) — a bug you only catch locally.

## Worker → home PC

Docker container, `restart: unless-stopped` — comes back up after a reboot, no terminal
to keep open. `backend/KnowledgeBase.Worker/Dockerfile` + `infra/worker/docker-compose.yml`
(`env_file: worker.env`, `Ai__Ollama__BaseUrl=http://host.docker.internal:11434`,
`extra_hosts: host-gateway`). Scripts `infra/worker/run-worker.ps1` / `stop-worker.ps1`.
Host starts as Production. Secrets in `infra/worker/worker.env` (gitignored). Full
instructions: [`../infra/worker/README.md`](../infra/worker/README.md).

`host.docker.internal:11434` reaches the native Ollama on Docker Desktop with no changes
(its own proxy). On native Linux Docker it points at the real host IP and Ollama on
`127.0.0.1` would refuse it.

## CI

- `.github/workflows/frontend-ci.yml`: `npm ci`, lint, build.
- `.github/workflows/backend-ci.yml`: `dotnet restore` + `build -c Release` of the whole
  `KnowledgeBase.sln` (Core + Api + Worker).

Both green on GitHub.

## Local run

See [`../CLAUDE.md`](../CLAUDE.md) → "Running locally". Three-to-four processes: Postgres
in Docker, API, worker (as needed), frontend.

## Open

- [ ] **Orphan sweep.** A bucket object whose `confirm` never arrived stays forever and
      is invisible to the API. Needs a bucket lifecycle rule or a command reconciling the
      object list against the table. `IAssetStorage.ListAsync` is kept for this.
- [ ] Two `.jpg` files uploaded before the metadata table existed are gone from the list
      (no rows). Not seeded back — they are debug files, they go with the orphan sweep.
- [ ] **Frontend must survive the store being unavailable.** PC off → the API is alive
      and serves listings, but bytes do not load — and now `/link` does a `HeadObject`,
      so with the PC off a download is a `500`, not a clear refusal.
- [ ] Phone-over-LAN upload verified from the PC (Garage reachable, preflight OK) but
      **not from the phone itself** yet.
- [ ] R2 CORS end-to-end browser upload on prod — the rule is set, not yet run through.
- [ ] Remove the dead `Features__DirectAssetAccessEnabled`, `Features__DownloadEnabled` and
      `Features__GoogleSignInEnabled` env vars from Render — the whole `Features` section is
      gone from code, ASP.NET ignores the unknown keys. Google stays on via `Auth__Google__*`
      (also in [`backend.md`](backend.md)).
- [ ] `dotnet test` in backend-CI once the first test project exists.
- [ ] **CD**: `push to main → build + lint + test → green → curl the Render Deploy Hook`.
      Not Render auto-deploy — it would ship a broken build, it knows nothing about
      tests. Deploy Hook URL in repo secrets.
- [ ] Separate pipeline for the frontend on Cloudflare Pages, if it stops being served
      from ASP.NET.
- [ ] If the frontend and API ever move to different addresses — allowed origins from
      config, not hardcoded.
- [ ] Backup of `data/` and the Garage volume.
- [ ] Large-file streaming instead of buffering (the 25 MB limit is checked after the
      request body is already buffered).
- [ ] Validation of accepted file types.
