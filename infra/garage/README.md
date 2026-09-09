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

## CORS: дозвіл браузеру писати в бакет напряму

Потрібен рівно для прямого upload'у. Скачування ним не користується — там звичайна
навігація по підписаному посиланню, а не `fetch`, і браузер дозволу не питає.

Дозвіл видає **сховище**, не ASP.NET: запит іде на `localhost:3900`, тобто на чужий
origin, і хто його приймає, той і вирішує. Окремої CLI-команди в Garage немає —
правило ставиться через S3 API.

Ключ бекенду має лише RW, а зміна конфігурації бакета — операція власника: з RW-ключем
`PutBucketCors` відповідає `AccessDenied: Operation is not allowed for this key`. Тому
owner видається на час правки і одразу забирається — постійно бекенду він не потрібен.

```bash
docker exec knowledgebase-garage /garage bucket allow --owner knowledgebase-assets --key GK...
```

```bash
docker run --rm --network container:knowledgebase-garage -e AWS_ACCESS_KEY_ID=GK... -e AWS_SECRET_ACCESS_KEY=... -v "$PWD/cors.json:/cors.json:ro" amazon/aws-cli s3api put-bucket-cors --endpoint-url http://localhost:3900 --region garage --bucket knowledgebase-assets --cors-configuration file:///cors.json
```

```bash
docker exec knowledgebase-garage /garage bucket deny --owner knowledgebase-assets --key GK...
```

`--network container:` кладе aws-cli в мережевий простір самого Garage, тож
`localhost:3900` там означає те саме, що й усередині контейнера — інакше довелось би
відв'язувати порт від loopback.

Перевірити правило: `s3api get-bucket-cors` — теж потребує owner. На самі запити
браузера воно діє й без нього: preflight неавтентифікований, ключа в ньому немає.
Прибрати: `s3api delete-bucket-cors`.

`cors.json` дозволяє лише `PUT` і лише з `http://localhost:5173`. Захід із телефона по
LAN-IP цим правилом не покритий — там знадобиться другий origin у списку.

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

Провайдера більше не обирають: бакет — єдине сховище (браузер ходить у нього напряму),
і без цих ключів застосунок не стартує (`ValidateOnStart`). Локально вони живуть у
цьому файлі, тож звичайний запуск уже йде проти Garage:

```bash
cd backend && dotnet run --launch-profile http
```

На Render ключі задаються змінними оточення (`Storage__S3__ServiceUrl` тощо) — подвійне
підкреслення замість вкладеності.

`Region` формально має збігатися з `s3_region` у `garage.toml`, але Garage v2.3.0 його
не перевіряє — підпис із будь-яким регіоном проходить. Значення лишається в конфізі
заради AWS і R2, де регіон справді звіряють.
