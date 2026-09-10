# Postgres — the dev database

The same engine as prod (Neon), so we do not hit "works locally, not in the cloud" bugs.
Data lives in the `postgres-data` volume and survives `docker compose down`.

## Running

```bash
cd infra/postgres && docker compose up -d
```

The connection string for local runs is already in
`backend/appsettings.Development.json`. The password here is fixed and deliberately not a
secret: the port is bound to `127.0.0.1`, the database is disposable, and the real string
in prod comes from the `ConnectionStrings__Database` env var.

## Useful

```bash
docker exec -it knowledgebase-postgres psql -U knowledgebase -d knowledgebase
```

`\dt` — list tables, `\d "Assets"` — one table's structure, `\q` — quit.

Wipe the data clean (migrations re-apply on the next backend start):

```bash
docker compose down -v && docker compose up -d
```
