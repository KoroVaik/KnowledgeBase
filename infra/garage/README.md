# Garage — S3-сховище для медіа

Однонодовий Garage у Docker. Байти нотаток лежать тут, на домашньому ПК;
бекенд у хмарі лишається stateless.

## Запуск

```bash
cp .env.example .env   # заповнити: openssl rand -hex 32 / openssl rand -base64 32
docker compose up -d
```

Порт S3-API (`3900`) відкритий на всі інтерфейси: байти в бакет кладе **браузер**,
тож телефон у тій же мережі мусить до нього достукатись. Адмін-порт (`3903`) лишився
на `127.0.0.1` — до нього ходить тільки цей же хост.

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

## CORS: дозвіл браузеру писати в бакет напряму

Потрібен рівно для прямого upload'у. Скачування ним не користується — там звичайна
навігація по підписаному посиланню, а не `fetch`, і браузер дозволу не питає.

Дозвіл видає **сховище**, не ASP.NET: запит іде на порт 3900, тобто на чужий origin,
і хто його приймає, той і вирішує. Окремої CLI-команди в Garage немає — правило
ставиться через S3 API.

Правило свідомо максимально широке: `*` в origin, методах і заголовках. Це домашнє
сховище, а вузький список origin'ів означав би перезаливку правила щоразу, коли
зміниться адреса ПК або з'явиться ще один пристрій.

Зміна конфігурації бакета — операція власника: з RW-ключем `PutBucketCors` відповідає
`AccessDenied: Operation is not allowed for this key`. Тому ключу бекенду видається
`--owner` **назавжди**, а не на час правки:

```bash
docker exec knowledgebase-garage /garage bucket allow --owner knowledgebase-assets --key knowledgebase-backend
```

```bash
docker run --rm --network container:knowledgebase-garage -e AWS_ACCESS_KEY_ID=GK... -e AWS_SECRET_ACCESS_KEY=... -v "$PWD/cors.json:/cors.json:ro" amazon/aws-cli s3api put-bucket-cors --endpoint-url http://localhost:3900 --region garage --bucket knowledgebase-assets --cors-configuration file:///cors.json
```

`--network container:` кладе aws-cli в мережевий простір самого Garage, тож
`localhost:3900` там означає те саме, що й усередині контейнера.

Перевірити правило: `s3api get-bucket-cors`. На самі запити браузера воно діє й без
owner-прав: preflight неавтентифікований, ключа в ньому немає.
Прибрати: `s3api delete-bucket-cors`.

## Підключення бекенду

Локально ключі живуть у `backend/KnowledgeBase.Api/appsettings.Local.json` (і, якщо
ганяєш воркер, у `backend/KnowledgeBase.Worker/appsettings.Local.json`) — файли поза
git, читаються тільки в Development:

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

`ServiceUrl` — **LAN-адреса цього ПК**, а не `localhost`: хост входить у підпис SigV4,
тож посилання діє рівно на тому імені, на якому його підписали, і з `localhost` телефон
ліз би сам у себе. Адреса видається по DHCP — якщо зміниться, правити тут і в
`appsettings.Local.json` воркера. Резервація на роутері позбавляє цієї мороки.

Провайдера більше не обирають: бакет — єдине сховище (браузер ходить у нього напряму),
і без цих ключів застосунок не стартує (`ValidateOnStart`). Локально вони живуть у
цьому файлі, тож звичайний запуск уже йде проти Garage:

```bash
dotnet run --project backend/KnowledgeBase.Api --launch-profile http
```

На Render ключі задаються змінними оточення (`Storage__S3__ServiceUrl` тощо) — подвійне
підкреслення замість вкладеності.

`Region` формально має збігатися з `s3_region` у `garage.toml`, але Garage v2.3.0 його
не перевіряє — підпис із будь-яким регіоном проходить. Значення лишається в конфізі
заради AWS і R2, де регіон справді звіряють.
