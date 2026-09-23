# Observability

Structured diagnostics cover the browser, API, job queue, worker and SSE. Request reduction
now shares previews, coalesces reads and batches asset operations; post-change runtime
measurement remains pending.

## Decisions

- Application code uses `ILogger<T>`; Serilog writes compact JSON to stdout, rotating local
  files and optionally Seq. Logging does not write events into the application database.
- Common properties: `Service`, `Environment`, `Release`, `TraceId`, `SpanId`, `SessionId`,
  `UploadBatchId`, `UploadId`, `AssetId`, `JobId`, `CausationId`. A browser session is one page
  lifetime, not an authentication credential. `Observability:Release` / `VITE_RELEASE` can
  carry the deployed commit; without overrides the server uses assembly version and the
  browser uses its Vite build mode as a fallback release label.
- `apiFetch` sends W3C `traceparent` and bounded diagnostic identifiers to the same-origin
  API. Direct bucket PUT/GET operations are recorded locally without adding headers to a
  signed URL. HTTP completion timing ends at response headers; diagnostic-mode
  `http.body-read` measures subsequent JSON download/parsing. Neither duplicates the body.
- Reload initiators retain `Source` and `Trigger` (`mount`, `upload`, `poll`, `sse`,
  `reconnect`, `navigation`, `action`, `asset-change`, `url-change`). Comparison views retain
  the triggering context across their revision state update. Generic API calls outside
  an annotated initiator remain tagged `api/action`.
- `ProcessingJobs.DiagnosticContext` is nullable JSON text (maximum 2000 characters).
  An EF save interceptor captures context for new jobs and explicit retries, logs enqueue
  only after a successful save, and preserves the original context for automatic retries
  and analyzer-unavailable requeues. Old jobs work with empty context. `ParentJobId` names
  the job that enqueued a successor; it does not claim to describe every input of a global
  aggregation. `AssetId` and normal job payloads retain domain provenance.
- A worker attempt continues the stored trace. Change hints carry an `EventId`, context
  and trace parent through the HTTP bridge and SSE. Browser reloads link back with
  `CausationId`. Events remain hints, with no replay or delivery guarantee.
- API controller requests get one completion record including status, route template and
  duration; exceptions add a separate error record. SSE open/close/delivery events are
  explicit, so a long stream should not be treated as a slow ordinary request.
- Database diagnostics report duration and execution kind, without SQL or parameters.
  Successful database and storage calls are Debug; database calls >=500 ms and storage
  calls >=1000 ms are Warning. HttpClient calls record host/path, status and elapsed time.
  Framework SQL command logging is disabled in favor of the interceptor.

## Bounded delivery and privacy

- Browser: at most 500 queued events plus 500 recent events for a local crash report.
  Delivery uses at most 50 events / 48 KB per request, once per 2 seconds, one request in
  flight, and a 5-second timeout. Failures back off to 60 seconds. Buffered records expire
  after 2 minutes. Authentication failures pause delivery for 30 seconds. Diagnostics
  delivery bypasses `apiFetch`, so it cannot recursively log itself.
- Under overload, ordinary events are dropped before warnings/errors. Per-source/route
  counters still report starts, completions, failures and peak duration every 10 seconds.
  More than 50 starts per group per window produces `http.storm`, including requests that
  have not returned. `diagnostics.dropped` makes event loss visible. Counters have bounded
  cardinality with an `other` bucket. This is diagnostic sampling, not an audit log.
- API ingestion requires the existing authenticated session, allows only known scalar
  fields, limits requests to 64 KB and 50 events, and rate-limits each user to 60 batches
  per minute. It does not publish resource events or access the database.
- Backend: the async sink holds at most 10,000 events and never blocks the application
  when full; a stderr `logging.dropped` record reports discarded events. The optional Seq
  sink has its own 5,000-event limit. Collector outages can lose remote events; stdout
  and local files are independent destinations, not a guaranteed forwarding spool.
- Normal files rotate daily or at 20 MB, retaining at most 7 files / 7 days. Debug files
  rotate hourly or at 10 MB, retaining at most 24 files / 24 hours. Limits are per service;
  size-based rotation can shorten retention. Worker Docker logs use a persistent volume.
  Render container files remain ephemeral; use remote ingestion or platform stdout there.
- No request/response bodies, photo bytes, note content or SQL parameters are collected.
  URL query strings, credential-like properties and credential patterns inside error text
  are redacted before output. The browser export contains only the same sanitized records.

## Configuration

| Setting | Default | Purpose |
|---|---|---|
| `Observability:MinimumLevel` | `Information` | Set `Debug` for a short investigation |
| `Observability:LogDirectory` | `logs` | Relative to process working directory; empty disables file output |
| `Observability:SeqUrl` | unset | Enable remote ingestion; native local process uses `http://localhost:5341` |
| `Observability:SeqApiKey` | unset | Supply through user-secrets or environment only |
| `Observability:Release` | assembly version | Deployment revision |
| `VITE_DIAGNOSTICS_ENABLED` | true | `false` disables browser event recording/delivery |
| `VITE_DIAGNOSTICS_DEBUG` | false | `true` includes request starts, JSON timings and supported long-task measurements |
| `VITE_RELEASE` | Vite build mode | Frontend deployment revision |

Detailed diagnostics require both `Observability:MinimumLevel=Debug` and
`VITE_DIAGNOSTICS_DEBUG=true`. Client timestamps are not trusted for elapsed-time
comparisons across machines; individual durations use monotonic clocks.

Vite settings are build-time settings; rebuild production assets after changing them.
Change server settings through environment/user-secrets and restart the process.
The API must apply `AddJobDiagnosticContext` before the upgraded worker can run.
The worker's existing migration wait handles that ordering.

## Local Seq

See [`../infra/observability/README.md`](../infra/observability/README.md). A collector is
optional: JSON stdout/files work immediately after restarting the application. Seq
configuration and a real ingestion check are separate from building the code.

## Investigating an upload

1. Start with `Event = 'http.storm'` or an `upload.started` record; copy `UploadId`,
   `UploadBatchId` or `SessionId`.
2. Follow upload-link, bucket PUT, confirm, `Job queued`, attempt outcome and SSE EventId.
3. Group browser `http.completed` records by `Source`, `Trigger` and `Route`. For overload
   totals use `http.summary`/`http.storm`: detailed records may have been dropped.
4. Filter by `AssetId` to find repeated preview link requests and full-photo downloads.
5. Compare API request durations with database/storage diagnostics, JSON body timings and
   browser long tasks. Long-task support varies by browser and requires diagnostic mode.
6. Repeat the same upload and UI scenario after request reduction, with the same diagnostic
   level, and compare request counts, concurrency, elapsed time and dropped-event counts.

Ready-to-save Seq queries are in [`../infra/observability/queries.sql`](../infra/observability/queries.sql).

## Request-reduction baseline and implementation

The pre-refactor connected single-photo run with all 16 sections expanded recorded 267
preview-link requests for 65 existing unique assets and 267 browser bucket GET operations
in the upload plus roughly one-minute observation window. This demonstrated duplicated work;
no UI freeze or bucket byte total was measured. The earlier multi-photo run lacked an SSE
subscriber, so it is not a valid connected-batch load baseline.

Implementation now shares signed links and image bytes, coordinates resource reads over
200 ms, limits upload transfers to four, adds asset batches of at most 50 items, combines job
status reads, and preserves offline SSE backoff during pointer activity. Batch logs retain
per-item upload correlation. These are implementation changes, not measured improvements.
Tests and runtime/browser checks were explicitly skipped by the owner on 2026-09-23.

## Open

- [ ] Verify the migration and upload -> worker -> SSE -> reload correlation against the
      running application; includes an authenticated collector call and denied anonymous call.
- [ ] Configure/start local Seq, create ingestion credentials, set retention and verify
      browser/API/worker events together. Production needs a reachable HTTPS ingestion
      endpoint with an ingestion-only key; keep the administrative UI private.
- [ ] Measure request counts and logging overhead for one photo and a representative batch;
      compare diagnostic enabled/disabled and collector unavailable.
- [ ] Measure the implemented request reduction with all sections open and an active SSE
      subscriber: single/batch, cold/warm cache, expired links, partial failures, retry and reconnect.
      Compare unchanged-photo link/GET counts, request concurrency and UI responsiveness.
- [ ] `schema.puml` predates several existing `JobKind` values; its logging field is synced,
      but the unrelated enum omissions still need a separate diagram refresh.
