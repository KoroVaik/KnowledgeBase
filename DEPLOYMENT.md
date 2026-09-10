# Розгортання

Три компоненти, кожен у своєму місці. Стан правди — Postgres; байти — S3-бакет.

```
Браузер ──статика + API──► Render        (KnowledgeBase.Api у Docker)
        ──байти──────────► Cloudflare R2  (bucket knowledgebase-assets)
                            Neon          (Postgres: метадані, нотатки, черга, ключі Data Protection)

Домашній ПК ──► KnowledgeBase.Worker ──► Ollama (localhost:11434)
                                    ├──► Neon + R2 по інтернету
                                    └──► Render /api/events/ingest (хінт «нова нотатка» для SSE)
```

## Структура рішення

`backend/KnowledgeBase.sln`:

| Проєкт | SDK | Що це |
|---|---|---|
| `KnowledgeBase.Core` | `Microsoft.NET.Sdk` | Спільне: EF-модель + міграції, сховище S3, клієнт Ollama, `PipelineWorker`, контракт подій |
| `KnowledgeBase.Api` | `Microsoft.NET.Sdk.Web` | HTTP API + віддача SPA. Єдине, що їде в прод |
| `KnowledgeBase.Worker` | `Microsoft.NET.Sdk.Worker` | AI-пайплайн окремим процесом |

`Core` референситься обома застосунками. Воркер і API — незалежні точки входу.

## Міграції БД

Накатує **тільки API**, на старті (`MigrateDatabase()` у `Program.cs`) — один процес
володіє схемою. Воркер бере `AddDatabase`, але не мігрує: працює з тим, що API вже
створив.

Нова міграція:

```bash
dotnet ef migrations add <Name> \
  --project backend/KnowledgeBase.Core \
  --startup-project backend/KnowledgeBase.Api
```

**Порядок деплою:** спершу API (накатує міграцію в Neon), потім рестарт воркера.
Воркер, піднятий до міграції нових таблиць, впаде на першому зверненні до них.

## KnowledgeBase.Api → Render

- Runtime **Docker**, `Dockerfile` у корені репо, гілка `main`. Render не має
  нативного .NET.
- Образ: `node:24` збирає фронт, `sdk:8.0` публікує `KnowledgeBase.Api`, `dist`
  лягає у `wwwroot`. Воркер в образ не входить (`.dockerignore`).
- Kestrel слухає порт зі змінної `PORT` (передає Render).
- Живий: <https://knowledgebase-9z29.onrender.com/>

### Змінні оточення Render

| Ключ | Призначення |
|---|---|
| `ConnectionStrings__Database` | Neon pooled, формат ADO.NET `ключ=значення` (не URL). Додавати **по одному рядку** — масовий `Add from .env` ріже по першому `=` |
| `Storage__S3__ServiceUrl` / `__BucketName` / `__AccessKeyId` / `__SecretAccessKey` / `__Region` | R2 (`Region=auto`), ключ `knowledgebase-render` |
| `Features__DownloadEnabled` | `true` |
| `Features__GoogleSignInEnabled` + `Auth__Google__ClientId` / `__ClientSecret` / `Auth__Google__AllowedEmails__0` | Google-вхід (окремий OAuth-клієнт від дев-ного) |
| `Events__IngestToken` | Спільний секрет для `POST /api/events/ingest`. Те саме значення — у `worker.env`. Порожній → ендпоінт віддає `503`, живі оновлення нотаток вимкнені |
| `ASPNETCORE_ENVIRONMENT` | `Production` (у Dockerfile уже виставлено) |

Зміна змінної **не** перезапускає сервіс сама — потрібен `Manual Deploy → Deploy
latest commit`.

### Ліміти Free (2026-09-07)

- **Render**: сон після 15 хв без трафіку, підйом ~1 хв; 750 інстанс-годин/міс на весь workspace.
- **Neon**: 0.5 ГБ, 100 CU-годин, 5 ГБ трафіку/міс; сон після 5 хв, підйом сотні мс.
- **R2**: 10 ГБ, безкоштовний вихідний трафік, але **вимагає прив'язати платіжку**.

## KnowledgeBase.Worker → домашній ПК

Контейнер у Docker Desktop, `restart: unless-stopped` — піднімається сам після ребуту,
термінал відкритим тримати не треба. Повна інструкція —
[`infra/worker/README.md`](infra/worker/README.md).

```powershell
infra/worker/run-worker.ps1        # stop old container, build image, start new one
infra/worker/stop-worker.ps1       # stop it
```

- `backend/KnowledgeBase.Worker/Dockerfile` + `infra/worker/docker-compose.yml`.
- Секрети — `infra/worker/worker.env` (gitignored, `env_file`): рядок Neon (ADO.NET,
  без лапок), ключі R2 (окремий ключ `knowledgebase-worker`), `Events__IngestToken`
  (те саме значення, що на Render) для живих оновлень нотаток.
- Ollama: контейнер ходить у `host.docker.internal:11434`. На Docker Desktop це
  працює як є, Ollama чіпати не треба.
- Хост стартує як Production (немає `DOTNET_ENVIRONMENT`).

Оновлення: `git pull` → `infra/worker/run-worker.ps1` (зупинить старий контейнер,
пересобере образ, підніме новий). Порядок — після деплою API.

## Локальний запуск

Див. [`CLAUDE.md`](CLAUDE.md) → «Як запустити». Три-чотири процеси: Postgres у Docker,
API, воркер (за потреби), фронт.
