# Local log collector

`docker-compose.yml` runs Seq 2026.1 with a persistent data volume. The UI and ingestion
ports bind to loopback. Application logging works without this container.

## Setup

1. Copy `.env.example` to `.env` (gitignored).
2. Choose an admin password and hash it with Seq's interactive command:
   `docker run --rm -it datalust/seq:2026.1 config hash`.
   Put the hash in `SEQ_ADMIN_PASSWORD_HASH` in `.env`. Do not commit it.
3. Run `docker compose up -d` from this folder. Open `http://localhost:5340` and sign in
   as `admin` with the chosen password.
4. Create separate ingestion-only API keys for the API and worker under **Data > Ingestion**.
   Require an API key for ingestion. Store each key as `Observability:SeqApiKey` in that
   project's user-secrets or as `Observability__SeqApiKey` in its environment.
5. Set `Observability__SeqUrl=http://localhost:5341` for native local processes. The worker
   container can use `http://host.docker.internal:5341` on Docker Desktop. Restart the
   application processes after configuration changes.
6. In Seq create a signal for `@Level in ['Debug', 'Verbose']`, retain it for 24 hours,
   and add an all-events retention policy of 7 days. Set a minimum free-space reserve
   under **Settings > System** so ingestion stops before the volume fills.
7. Paste the filters/queries from `queries.sql` into Seq and save them. Verify ingestion
   before relying on remote logs.

`docker compose down` stops the collector while preserving data. No cleanup command here
removes the persistent volume. The default application file retention separately limits
local log files; Seq's retention must be configured in Seq.

## Production

Render cannot reach a collector at a home PC's `localhost`. Configure a reachable HTTPS
ingestion endpoint with an ingestion-only key. Expose only ingestion through the chosen
network route, keep the UI private, and store credentials through environment/user-secrets.
No tunnel or production configuration is created by this change. Until configured, use
Render stdout and the worker's rotating files (`worker-logs` Docker volume).

Official references: [Docker setup](https://datalust.co/docs/docker-deployment-overview),
[initial password](https://blog.datalust.co/setting-an-initial-password-when-deploying-seq-to-docker/),
[retention](https://datalust.co/docs/retention-policies).
