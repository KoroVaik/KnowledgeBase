# AI worker on the home PC

`KnowledgeBase.Worker` is the AI-pipeline process. It polls the `ProcessingJobs` table in
Neon, pulls bytes from the R2 bucket, hands them to the local Ollama, and writes `Note` +
`NoteLink` back to Neon.

It lives here, not in the cloud, because it needs Ollama (`localhost:11434`) and Render
Free has no access to it and sleeps without traffic. The worker is **not** in the prod
image (Render runs the API only). Nothing connects *to* the worker — it only reaches out
to Neon and R2, no inbound connections.

Ollama needs no changes: on Docker Desktop for Windows the container reaches it through
`host.docker.internal` even when it listens only on `127.0.0.1` (Docker Desktop's own
proxy). `Ai__Ollama__BaseUrl` is already set in `docker-compose.yml`.

See [`../../docs/worker.md`](../../docs/worker.md) for the design and
[`../../docs/infra.md`](../../docs/infra.md) for the wider deployment picture.

## One-time setup

### R2 key and credentials

- A separate R2 key **`knowledgebase-worker`** (RW on the bucket) — revoked independently
  of the Render key.
- Copy `worker.env.example` → `worker.env` (gitignored) and fill it: the Neon connection
  string (ADO.NET `key=value` format, **no quotes**, not a URL), `ServiceUrl` /
  `BucketName` / R2 keys.
- `Events__IngestToken` — the same string as `Events__IngestToken` in the Render env
  vars. The worker sends a "new note" hint to `POST /api/events/ingest` and the frontend
  updates the list without a reload. Empty → notes appear only after F5, everything else
  works. Generate: `openssl rand -hex 32` (any long random string).

## Running

```powershell
infra/worker/run-worker.ps1
```

The script checks `worker.env` (creates it from the example and stops if missing), stops
the old container, builds the image from the current code, and starts a new
`knowledgebase-worker` container in the background. No terminal to keep open.

- The host starts as **Production** (no `DOTNET_ENVIRONMENT`) → it reads `appsettings.json`
  + the vars from `worker.env`; `appsettings.Local.json` is not in the image at all.
- `restart: unless-stopped` — the container comes back up after a reboot (if Docker
  Desktop starts on login, which is its default). No Task Scheduler needed.

Useful:

```powershell
docker logs -f knowledgebase-worker          # watch the log
infra/worker/run-worker.ps1 -Logs            # restart and attach to the log
infra/worker/stop-worker.ps1                 # stop and remove the container
```

## Updating after a deploy

Order: **API first** (it applies migrations to Neon on startup), **then** the worker. A
worker brought up against the old schema would fail on the first hit to a new table.

```powershell
git pull
infra/worker/run-worker.ps1        # stop old, rebuild image, start new
```

## If the worker cannot see Ollama

`docker logs knowledgebase-worker` shows `Ollama is not reachable at
http://host.docker.internal:11434` (surfaces when the worker takes a job, not at
startup).

- Is Ollama running at all? `curl http://localhost:11434/api/tags` on the host.
- The same from a container: `docker run --rm curlimages/curl -sS
  http://host.docker.internal:11434/api/tags` — on Docker Desktop this should be `200`
  with no extra flags.
- If you are **not** on Docker Desktop but on native Docker (Linux), `host.docker.internal`
  points at the real host IP and Ollama on `127.0.0.1` will refuse it:
  `setx OLLAMA_HOST 0.0.0.0` + restart Ollama, or move Ollama into the same compose
  network.
