# Backend (`KnowledgeBase.Api`)

The HTTP API and SPA host. The only project that ships to prod. See
[`architecture.md`](architecture.md) for the process split and constraints,
[`database.md`](database.md) for the EF model, [`infra.md`](infra.md) for deployment.

## Decisions

### Structure — vertical slices

`Controllers/<feature>/` holds the controller at the folder root, with subfolders for
its helpers (`Contracts/`, `Services/`, `Configuration/`, later `Validators/`). Anything
shared by no single controller lives in `Infrastructure/` (`Hosting/`, `Features/`).
To change one feature you open one folder, DI registration included. `Program.cs` is
~30 lines — a list of features.

- A mid-way layout that split by layer at the top level (`Endpoints/` + `Contracts/` +
  `Services/`) was rejected: across 30 endpoints each feature would be smeared over five
  folders.
- Named `Controllers/` (not `Features/`) on purpose — matches the owner's work project.
  ASP.NET does not care where controllers sit; the folders are for humans.

### MVC controllers, not minimal API

Decision 2026-09-06. `[ApiController]`, `[Route("api/[controller]")]`, `[Authorize]` on
the class, `[AllowAnonymous]` on `login`. Reason: same style as the owner's job — habits
read both ways.

- `LowercaseUrls = true`: the `[controller]` token takes the class name verbatim, so the
  schema was emitting `/api/Auth/login`. Routing is case-insensitive so calls worked, but
  a generated client would carry that casing.
- **Minimal-API trap, kept as a note if we ever go back:** a handler whose only parameter
  is `HttpContext` matches the `RequestDelegate` overload of `MapPost`, and the returned
  `IResult` is silently dropped — `logout` would return 200 with an empty body instead of
  204. Caught by analyzer `ASP0016`, fixed with a `(Delegate)` cast. Controllers do not
  have this trap.

### Swagger docs

`GenerateDocumentationFile` + `IncludeXmlComments`, `///` on controllers and actions,
`[ProducesResponseType]` with types. `NoWarn 1591` so the compiler does not demand `///`
on every public type — only endpoints need it. **The `///` comments feed Swagger; keep
them.** (The comment-trimming policy in `CLAUDE.md` is about `//` noise, not these.)

### Real-time updates — SSE

Server-Sent Events, not WebSocket: the channel is one-way, and SSE is a plain GET the
server just never finishes, with `EventSource` reconnecting on its own.

- `Infrastructure/RealTime/` (contract in `Core/RealTime/`): `IChangeNotifier`
  (singleton) fans `ChangeEvent(Resource, Action, Id)` out to subscriptions, each with
  its own `Channel.CreateBounded(32, DropOldest)` so a client that stopped reading cannot
  wedge someone else's upload.
- `GET /api/events`, `[Authorize]`, `: ping` every 20 s — keeps proxies from timing out
  **and** detects a dead socket (the write fails and the loop exits on the token).
- **An event is a signal, not data.** There is no event log, so a missed event cannot be
  replayed; the client just re-reads the collection after every (re)connect. A lost event
  is therefore harmless.

### Auth

- **Cookie session, password from config.** The identity source is decoupled from the
  session, so Google OAuth only replaces the login endpoint.
- **Login rate limit:** 5 *failed* attempts per minute, per source address. Done by hand,
  not middleware — middleware would spend an attempt on every request, successful logins
  included. The check runs *before* the password comparison, so an exhausted limit gives
  a guesser no signal. 429 carries the reason in the body. Needs `ForwardedHeaders` too:
  behind a proxy every request arrives from one address otherwise.
- `OnRedirectToLogin` → 401 instead of 302: the cookie scheme default redirects to a
  login page, and `fetch()` would read that HTML as success.
- Password hash: PBKDF2 via `PasswordHasher<T>` (in-framework, no NuGet). Generate with
  `dotnet run -- hash-password <pw>`, store in user-secrets.
- **Google OAuth** behind `Features:GoogleSignInEnabled` (default `false`). The handler
  has `SignInScheme = cookie`, so it issues the same `kb.auth` and `/api/auth/me`,
  logout, `[Authorize]` are unchanged. Package
  `Microsoft.AspNetCore.Authentication.Google`.
  - The scheme registers **only** when `ClientId`/`ClientSecret` are present — an empty
    `ClientId` fails the OAuth options validator and would take down the whole API.
  - `CallbackPath = /api/auth/google/callback` (not the default `/signin-google`): Vite
    only proxies `/api`.
  - Correlation cookie: `SameSite = Lax` **and** `SecurePolicy = SameAsRequest`. The
    defaults (`None` + `Secure`) require https and break the flow on plain http (a phone
    on the LAN).
  - Allow-list `Auth:Google:AllowedEmails` — **empty means nobody**: Google auth itself
    succeeds for any account in the world. Checked in `OnTicketReceived` (not
    `OnCreatingTicket` — `Fail()` is ignored there), rejection via `HandleResponse()` +
    redirect to `/?authError=…`.
  - `/api/features` returns the **effective** state (`flag && IsConfigured`).
  - Vite proxy needs `changeOrigin: false` — the backend builds `redirect_uri` from the
    `Host` header. See [`frontend.md`](frontend.md).

### Data Protection keys → database

They encrypt the auth cookie and by default sit on disk; Render's disk is ephemeral, so
that meant a logout every deploy. Package
`Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`,
`KnowledgeBaseDbContext : IDataProtectionKeyContext`, `PersistKeysToDbContext` registered
in `AddAuthFeature`. Table `DataProtectionKeys` via migration `AddDataProtectionKeys`.

### SPA hosting

`UseStaticFiles` + fallback to `index.html` — an auth prerequisite, not cosmetics: one
address means the cookie is configured in its final form, and prod needs no CORS at all.
The fallback is deliberately double: `MapFallback("/api/{**path}")` returns 404, because
a blanket catch-all would answer a typo in an API path with `index.html` at status 200
and `fetch()` would read the markup as success.

### Antiforgery

Moot — there is no form binding in the API. `upload-link` and `confirm` take JSON, bytes
go to the bucket past ASP.NET.

### Password in plaintext — deferred on purpose

During local dev the password sits as a plain default (`Password`) in
`Options/AuthOptions.cs`, so it is in git history. Deploy currently ships with it. The
repo is public, so this is the remaining blocker for treating the public URL as safe.
Fix when it matters: restore `PasswordHasher<T>`, hash in config (`Auth:PasswordHash`),
the `hash-password` CLI command, constant-time comparison. (Deferred knowingly — do not
re-raise every task.)

## Open

- [ ] Input-model validation (FluentValidation in `Controllers/<feature>/Validators/`).
      Nothing to validate yet — `LoginRequest` has one field. Relevant once note creation
      exists.
- [ ] `GET /api/assets/{fileName}` with `Features:DownloadEnabled=false` returns **406**,
      not 403: the action has `[Produces("application/octet-stream")]` and the disabled
      branch returns a JSON object, so content negotiation fails.
- [ ] Orphan sweep: reconcile the metadata table against the store. Bytes with no row
      (upload died mid-way) are invisible to the API. `IAssetStorage.ListAsync` is kept
      for exactly this; the controller no longer calls it.
- [ ] A 401 during upload does not drop the frontend to "logged out" — the session went
      stale and the user sees an upload error. Needs a shared 401 handler. (Also breaks
      `GET /api/events` → the connection banner says "no server" though the server is
      alive and just does not recognise us.) Partly a [`frontend.md`](frontend.md) item.
- [ ] Remove the dead `Features__DirectAssetAccessEnabled` env var from Render. The flag
      is gone from code; ASP.NET ignores the unknown key, so this is cleanup, not a
      blocker.
- [ ] xUnit + `WebApplicationFactory` integration tests — see [`../CLAUDE.md`] and the
      Tests section of [`archive.md`](archive.md). `IAssetStorage` is now swappable for a
      fake via `WithWebHostBuilder`.
- [ ] Show the Google account picker every time —
      `GoogleChallengeProperties { Prompt = "select_account" }`. Deliberately not done:
      silent re-login is nicer for a single-user app. Revisit only if a second Google
      account starts causing confusion.
- [ ] GitHub OAuth as a second identity source — after Google, likely redundant. Kept as
      an option, not a plan.
