# Garage — S3-сховище для медіа

Однонодовий Garage у Docker. Байти нотаток лежать тут, на домашньому ПК;
бекенд у хмарі лишається stateless.

## Запуск

```bash
cp .env.example .env   # заповнити: openssl rand -hex 32 / openssl rand -base64 32
docker compose up -d
```

Порти прив'язані до `127.0.0.1` — назовні нічого не стирчить, поки перед Garage
не поставлено тунель.

## Перше налаштування кластера

Garage не приймає запити, поки вузлу не призначено місце в layout — свіжий
контейнер відповідатиме `no proper storage nodes available`, і це не поламаний
конфіг. Робиться один раз, стан лежить у volume `garage-meta`:

```bash
docker exec knowledgebase-garage /garage layout assign $(docker exec knowledgebase-garage /garage node id -q | cut -d@ -f1) -z home -c 50G
docker exec knowledgebase-garage /garage layout apply --version 1
docker exec knowledgebase-garage /garage bucket create knowledgebase-assets
docker exec knowledgebase-garage /garage key create knowledgebase-backend
docker exec knowledgebase-garage /garage bucket allow --read --write knowledgebase-assets --key knowledgebase-backend
```

`key create` друкує Key ID і Secret key — це вони йдуть у `Storage:S3` бекенду
(user-secrets локально, змінні оточення на Render). Другий раз секрет не показується:
`key info --show-secret`.

У Git Bash `docker exec` ламається на `/garage` — MSYS підставляє Windows-шлях.
Лікується `export MSYS_NO_PATHCONV=1`.

## Підключення бекенду

Локально ключі живуть у `backend/appsettings.Local.json` — файл поза git, читається
тільки в Development:

```json
{
  "Storage": {
    "S3": {
      "ServiceUrl": "http://localhost:3900",
      "BucketName": "knowledgebase-assets",
      "Region": "garage",
      "AccessKeyId": "GK...",
      "SecretAccessKey": "..."
    }
  }
}
```

`Provider` там навмисне не заданий: дефолт лишається `Local`, тобто звичайний
`dotnet run` пише файли в `backend/data/assets`. Прогін проти Garage — на вимогу:

```bash
cd backend && Storage__Provider=S3 dotnet run --launch-profile http
```

На Render те саме задається змінними оточення (`Storage__Provider`,
`Storage__S3__ServiceUrl` тощо) — подвійне підкреслення замість вкладеності.

`Region` має збігатися з `s3_region` у `garage.toml`: SigV4 підписує регіон.
