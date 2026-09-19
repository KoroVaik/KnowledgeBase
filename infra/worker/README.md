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

### ArcFace embedding model

Face embeddings come from `w600k_r50.onnx` (insightface `buffalo_l`, ~174 MB, not in git).
Nothing to do by hand: on start the worker downloads `buffalo_l.zip` (~280 MB) from the
insightface release (`FaceAnalysis__ModelDownloadUrl`) into the `models` volume under
`/models/arcface/` if the file is missing. The first start after a fresh volume is slow.

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

Automatic: a push to `main` that touches Core, Worker or `infra/worker` runs
`.github/workflows/worker-cd.yml` on the self-hosted runner below, which rebuilds and
restarts the container. A failed build leaves the running worker as it was.

Order does not matter: on startup the worker waits (checking every 30 s) until the API has
applied every migration the worker knows about, and only then takes jobs. The log shows
`Waiting for the API to apply N migration(s)` meanwhile.

`run-worker.ps1` stays for the manual cases: the first start, running uncommitted local
code, or when the runner is off. Both manage the same compose project, so whichever ran
last wins.

## Self-hosted runner (one-time setup)

The GitHub equivalent of a private Azure DevOps agent: a Windows service that dials out to
GitHub and asks for jobs. **Only safe while the repo is private** — on a public repo a
stranger's pull request could run code on this PC.

1. GitHub → repo **Settings → Actions → Runners → New self-hosted runner → Windows**.
   Run the download/extract commands it shows, e.g. into `C:\actions-runner`.
2. Configure it with the token from that page:

   ```powershell
   .\config.cmd --url https://github.com/KoroVaik/KnowledgeBase --token <TOKEN> --runasservice
   ```

   When asked for the service account, give **your own Windows account** (and its
   password), not the default `NT AUTHORITY\NETWORK SERVICE` — that account cannot reach
   Docker Desktop.
3. Create `C:\actions-runner\.env` (the runner passes it to every job as env vars) with the
   full path to the filled `worker.env`:

   ```
   WORKER_ENV_FILE=C:\path\to\KnowledgeBase\infra\worker\worker.env
   ```

   Restart the service (`Restart-Service "actions.runner.*"`) so it picks the file up.
4. Actions tab → **Worker CD → Run workflow** to check it end to end.

Docker Desktop starts on user login, so after a reboot a deploy only works once you have
logged in; until then the job waits in the queue.

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
