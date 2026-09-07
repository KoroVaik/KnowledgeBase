# Postgres — база для розробки

Той самий рушій, що й у проді (Neon), щоб не ловити багів класу «локально працює,
у хмарі ні». Дані — у volume `postgres-data`, переживають `docker compose down`.

## Запуск

```bash
cd infra/postgres && docker compose up -d
```

Рядок підключення для локального запуску вже лежить у `backend/appsettings.Development.json`.
Пароль тут фіксований і навмисне не секрет: порт прив'язаний до `127.0.0.1`, база
одноразова, а справжній рядок у проді приходить зі змінної оточення
`ConnectionStrings__Database`.

## Корисне

```bash
docker exec -it knowledgebase-postgres psql -U knowledgebase -d knowledgebase
```

`\dt` — список таблиць, `\d "Assets"` — структура таблиці, `\q` — вихід.

Знести дані начисто (міграції накотяться заново на старті бекенду):

```bash
docker compose down -v && docker compose up -d
```
