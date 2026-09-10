# Garage — S3 storage for media

A single-node Garage in Docker. Note bytes live here, on the home PC; the cloud backend
stays stateless. See [`../../docs/infra.md`](../../docs/infra.md) for why Garage (not
MinIO) and how the tunnel and prod fit together.

## Running

```bash
cp .env.example .env   # fill in: openssl rand -hex 32 / openssl rand -base64 32
docker compose up -d
```

The S3-API port (`3900`) is open on all interfaces: the **browser** puts bytes in the
bucket, so a phone on the same network must reach it. The admin port (`3903`) stays on
`127.0.0.1` — only this host talks to it.

## First cluster setup

Garage refuses requests until the node is assigned a place in the layout — a fresh
container answers `no proper storage nodes available`, and that is not a broken config.
Done once; the state lives in the `garage-meta` volume:

```bash
docker exec knowledgebase-garage /garage layout assign $(docker exec knowledgebase-garage /garage node id -q | cut -d@ -f1) -z home -c 50G
docker exec knowledgebase-garage /garage layout apply --version 1
docker exec knowledgebase-garage /garage bucket create knowledgebase-assets
docker exec knowledgebase-garage /garage key create knowledgebase-backend
docker exec knowledgebase-garage /garage bucket allow --read --write knowledgebase-assets --key knowledgebase-backend
```

`key create` prints a Key ID and Secret key — those go into the backend's `Storage:S3`
(user-secrets locally, env vars on Render). The secret is not shown a second time:
`key info --show-secret`.

In Git Bash `docker exec` breaks on `/garage` — MSYS rewrites it to a Windows path. Fix
with `export MSYS_NO_PATHCONV=1`.

## CORS: letting the browser write to the bucket directly

Needed only for the direct upload. Downloads do not use it — that is a plain navigation
to a signed URL, not `fetch`, and the browser asks no permission.

The permission is granted by the **store**, not ASP.NET: the request goes to port 3900,
a different origin, and whoever receives it decides. Garage has no CLI command for it —
the rule is set via the S3 API.

The rule is deliberately as wide as possible: `*` in origin, methods and headers. This is
home storage, and a narrow origin list would mean re-uploading the rule every time the PC
address changes or another device appears.

Changing a bucket's configuration is an owner operation: with the RW key `PutBucketCors`
answers `AccessDenied: Operation is not allowed for this key`. So the backend key is
given `--owner` **for good**, not just for the edit:

```bash
docker exec knowledgebase-garage /garage bucket allow --owner knowledgebase-assets --key knowledgebase-backend
```

```bash
docker run --rm --network container:knowledgebase-garage -e AWS_ACCESS_KEY_ID=GK... -e AWS_SECRET_ACCESS_KEY=... -v "$PWD/cors.json:/cors.json:ro" amazon/aws-cli s3api put-bucket-cors --endpoint-url http://localhost:3900 --region garage --bucket knowledgebase-assets --cors-configuration file:///cors.json
```

`--network container:` puts aws-cli in Garage's own network namespace, so `localhost:3900`
there means the same as inside the container.

Check the rule: `s3api get-bucket-cors`. It applies to browser requests even without
owner rights: the preflight is unauthenticated, there is no key in it. Remove it:
`s3api delete-bucket-cors`.

## Connecting the backend

Locally the keys live in `backend/KnowledgeBase.Api/appsettings.Local.json` (and, if you
run the worker, `backend/KnowledgeBase.Worker/appsettings.Local.json`) — files outside
git, read only in Development:

```json
{
  "Storage": {
    "S3": {
      "ServiceUrl": "http://192.168.1.40:3900",
      "BucketName": "knowledgebase-assets",
      "Region": "garage",
      "AccessKeyId": "GK...",
      "SecretAccessKey": "..."
    }
  }
}
```

`ServiceUrl` is the **LAN address of this PC**, not `localhost`: the host is part of the
SigV4 signature, so a link works only on the name it was signed for, and with `localhost`
a phone would reach itself. The address is handed out by DHCP — if it changes, fix it
here and in the worker's `appsettings.Local.json`. A router reservation removes the
hassle.

There is no provider choice any more: the bucket is the only store (the browser goes to
it directly), and without these keys the app does not start (`ValidateOnStart`). Locally
they live in this file, so a normal run already goes against Garage:

```bash
dotnet run --project backend/KnowledgeBase.Api --launch-profile http
```

On Render the keys are env vars (`Storage__S3__ServiceUrl` etc.) — double underscore
instead of nesting.

`Region` formally has to match `s3_region` in `garage.toml`, but Garage v2.3.0 does not
check it — a signature with any region passes. The value stays in config for AWS and R2,
where the region really is verified.
