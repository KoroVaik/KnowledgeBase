# AI-воркер на домашньому ПК

`KnowledgeBase.Worker` — окремий процес AI-пайплайну. Опитує таблицю `ProcessingJobs`
у Neon, бере байти з бакета R2, віддає локальній Ollama, пише `Note` + `NoteLink`
назад у Neon.

Живе тут, а не в хмарі, бо йому потрібна Ollama (`localhost:11434`), а Render Free
до неї не має доступу і засинає без трафіку. У прод-образ (Render) воркер свідомо
не входить — там тільки API. Ніхто на воркер «не дивиться»: він лише сам виходить у
Neon і R2, вхідних з'єднань не має.

Ollama чіпати не треба: на Docker Desktop for Windows контейнер дотягується до неї
через `host.docker.internal` навіть коли вона слухає лише `127.0.0.1` (це робить
проксі самого Docker Desktop). `Ai__Ollama__BaseUrl` уже виставлений у
`docker-compose.yml`.

## Разове налаштування

### Ключ R2 і креденшели

- Окремий ключ R2 **`knowledgebase-worker`** (RW на бакет) — щоб відкликати незалежно
  від ключа Render.
- Скопіювати `worker.env.example` → `worker.env` (gitignored) і заповнити: рядок
  підключення Neon (формат ADO.NET `ключ=значення`, **без лапок**, не URL),
  `ServiceUrl` / `BucketName` / ключі R2.
- `Events__IngestToken` — той самий рядок, що `Events__IngestToken` у змінних Render.
  Воркер шле хінт «нова нотатка» на `POST /api/events/ingest`, і фронт оновлює список
  без перезавантаження. Порожній → нотатки з'являються лише після F5, решта працює.
  Згенерувати: `openssl rand -hex 32` (будь-який довгий випадковий рядок).

## Запуск

```powershell
infra/worker/run-worker.ps1
```

Скрипт: перевіряє `worker.env` (якщо нема — створює з прикладу й зупиняється),
зупиняє старий контейнер, збирає образ із поточного коду і піднімає новий контейнер
`knowledgebase-worker` у фоні. Термінал тримати відкритим не треба.

- Хост стартує як **Production** (немає `DOTNET_ENVIRONMENT`) → бере `appsettings.json`
  + змінні з `worker.env`, `appsettings.Local.json` в образ не потрапляє взагалі.
- `restart: unless-stopped` — контейнер сам підніметься після ребуту (якщо Docker
  Desktop стартує на вході в систему — це його дефолт). Task Scheduler не потрібен.

Корисне:

```powershell
docker logs -f knowledgebase-worker          # дивитись лог
infra/worker/run-worker.ps1 -Logs            # перезапустити і одразу підчепитись до логу
infra/worker/stop-worker.ps1                 # зупинити й прибрати контейнер
```

## Оновлення після деплою

Порядок: **спершу** викочується API (він накатує міграції в Neon на старті), **потім**
оновлюється воркер. Воркер, піднятий проти БД зі старою схемою, падав би на перших
зверненнях до нових таблиць.

```powershell
git pull
infra/worker/run-worker.ps1        # зупинить старий, пересобере образ, підніме новий
```

## Якщо воркер не бачить Ollama

`docker logs knowledgebase-worker` покаже `Ollama is not reachable at
http://host.docker.internal:11434` (виринає, коли воркер бере job, не на старті).

- Ollama взагалі запущена? `curl http://localhost:11434/api/tags` на хості.
- Те саме з контейнера: `docker run --rm curlimages/curl -sS
  http://host.docker.internal:11434/api/tags` — на Docker Desktop має бути `200` без
  жодних додаткових прапорців.
- Якщо ти **не** на Docker Desktop, а на нативному Docker (Linux) — там
  `host.docker.internal` веде на реальний IP хоста, і Ollama на `127.0.0.1` його не
  прийме: `setx OLLAMA_HOST 0.0.0.0` + рестарт Ollama, або перенеси Ollama в ту саму
  compose-мережу.
