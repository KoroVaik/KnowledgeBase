# Що доучити

Список тем, до яких повертаємось окремо. Не документація проєкту — саме черга на
розбір. Стан робіт по коду — у [CHECKLIST.md](CHECKLIST.md).

Позначки:

- `[ ]` — ще не розбирали
- `[~]` — пояснено один раз по ходу задачі, але не вляглось
- `[x]` — можу пояснити сам, без підказки

Формат пункту: **що зрозуміти** → **де це в коді** → **питання для самоперевірки**.
Пункт закривається не «прочитав», а «відповів на питання своїми словами».

Порядок — від фундаменту до частковостей. Верхні пункти пояснюють нижні, тож
братись краще згори.

## ASP.NET Core як фреймворк

- [~] **Пайплайн запиту і middleware.** Що таке ланцюг обробників, чому порядок
      `Use*` має значення, як обробник «з'їдає» запит і не пускає далі.
      Аналогія: `DelegatingHandler` у `HttpClient`, тільки на сервері.
      → `Infrastructure/Hosting/MiddlewarePipeline.cs`
      → *Чому `UseForwardedHeaders()` мусить стояти перед `UseHttpsRedirection()`?*
      → *Що станеться, якщо поставити `UseAuthorization()` перед `UseAuthentication()`?*

- [~] **`UseAuthentication()` робить дві різні роботи.** Не тільки читає cookie й
      наповнює `HttpContext.User`, а ще й питає кожен зареєстрований хендлер
      «це не твій запит?» — так Google-хендлер перехоплює свій `CallbackPath`
      **до** будь-якого контролера.
      → `Program.cs`, `Controllers/Auth/Configuration/AuthRegistration.cs`
      → *Чому `/api/auth/google/callback` не описаний як endpoint у контролері,
        а працює?*
      → *Чому незаповнений `ClientId` поклав би весь API, а не лише вхід?*

- [~] **Схеми автентифікації.** `AddCookie()` / `AddGoogle()` — це іменовані стратегії
      в DI: ім'я → хендлер → опції. `DefaultScheme`, `SignInScheme`, `Challenge` як
      «піди доведи, хто ти» проти `[Authorize]` як «покажи, що вже довів».
      → `AuthRegistration.cs`, `AuthController.StartGoogleSignIn`
      → *Що конкретно робить `options.SignInScheme = Cookie` і чому без нього
        Google-вхід не дав би сесію застосунку?*

- [ ] **DI-контейнер і час життя сервісів.** Singleton / Scoped / Transient — і що
      ламається, коли час життя обрано не той.
      → `LoginAttemptLimiter` зареєстрований Singleton **навмисно**
      → *Чому ліміт входу перестав би працювати, якби він був Scoped?*

- [ ] **Конфігурація.** Шари `appsettings.json` → `appsettings.{Environment}.json` →
      `appsettings.Local.json` → user-secrets → змінні оточення; хто кого перекриває.
      `IOptions` проти `IOptionsSnapshot` проти `section.Get<T>()` руками.
      → `Infrastructure/Hosting/HostingSetup.cs`, `AuthRegistration.cs`
      → *Чому в `AuthRegistration` секція читається `section.Get<AuthOptions>()`
        вручну, хоча поруч уже є `services.Configure<AuthOptions>()`?*
      → *Чому флаг у `appsettings.Local.json` не підхопився без перезапуску,
        хоча `IOptionsSnapshot` начебто вміє перечитувати?*

- [ ] **Хостинг і Kestrel.** Що таке launch-профілі, звідки береться порт, чому
      `--launch-profile http` обов'язковий.
      → `Properties/launchSettings.json`, `HostingSetup.UseAssignedPort`

## OAuth 2.0 і сесії

- [~] **Загальна схема Authorization Code Flow.** Хто кому що передає і в якому
      порядку; чому `code` сам по собі нічого не відкриває; чому обмін `code` на
      токен іде **з сервера**, а не з браузера.
      → *Чому тип клієнта саме Web application, і чому SPA не дають `client_secret`?*
      → *Що з цього браузер бачить, а що ні?*

- [~] **`state` і correlation-cookie.** `state` — не випадковий рядок, а зашифрований
      блоб із `RedirectUri` і correlation-id всередині. Навіщо звіряти його з cookie.
      → *Яку атаку це зупиняє? Що зміг би зробити зловмисник без цієї перевірки?*

- [ ] **PKCE (`code_challenge` / `code_verifier`).** Бачили в редіректі, не розбирали.
      → *Від чого захищає, якщо `state` вже є?*

- [~] **Google каже «хто», а не «чи можна».** Вхід вдається будь-якому акаунту в світі;
      рішення про доступ — наше, allow-list'ом.
      → `GoogleAuthOptions.Allows`, `OnTicketReceived`
      → *Чому порожній allow-list мусить означати «нікого», а не «всіх»?*

- [ ] **Claims, `ClaimsIdentity`, `ClaimsPrincipal`.** З чого складається «користувач»
      у ASP.NET і звідки `User.Identity.Name` бере значення.
      → `AuthController.Me`

- [ ] **Data Protection.** Що саме шифрується (і `state`, і `kb.auth`), чому ключі
      переїхали в Postgres.
      → `AuthRegistration.cs`, міграція `AddDataProtectionKeys`
      → *Що зламалось би при деплої, якби ключі лишились на диску контейнера?*

## Веб-механізми

- [~] **Cookie: атрибути й що вони насправді роблять.** `SameSite` (`Lax` проти `None`),
      `Secure`, `HttpOnly`, `path`, строк життя. Чому `Lax` вистачає, коли повернення
      йде top-level навігацією.
      → *Чому дефолт `None` + `Secure` ламав вхід із телефона по LAN-IP,
        але не на `localhost`?*
      → *Чи розрізняє cookie порти? (відповідь неочевидна)*

- [ ] **CSRF і антифоргері.** Correlation-cookie — окремий випадок; загальна тема
      лишається відкритим пунктом у `CHECKLIST.md` (`.DisableAntiforgery()` на upload).

- [~] **Заголовок `Host`, origin і зворотні проксі.** Бекенд будує `redirect_uri` з
      `Host` вхідного запиту — тому підміна `Host` проксі змінює поведінку OAuth.
      → `frontend/vite.config.ts` (`changeOrigin: false`),
        `HostingSetup.AddProxyAwareHosting`
      → *Чому `X-Forwarded-For` потрібен не тільки для https, а й для ліміту входу?*

- [ ] **Dev проти прод: одна адреса.** Vite-проксі в dev і ASP.NET, що сам віддає SPA
      в проді — і чому свідомо зроблено так, щоб браузер завжди бачив один origin.

## Фронтенд

- [ ] **`useEffect`: навіщо cleanup і прапорець `cancelled`.**
      → `App.tsx`
      → *Що зламається, якщо прибрати `cancelled`?*

- [ ] **`useState` з функцією-ініціалізатором.** Чому `useState(readGoogleError)`,
      а не `useState(readGoogleError())`.
      → `components/LoginForm.tsx`

- [ ] **Чому OAuth-кнопка — це `<a>`, а не `fetch`.**
      → *Що саме станеться, якщо спробувати пройти флоу через `fetch`?*

## Нотатки

- 2026-09-08: усе, позначене `[~]`, розібрано під час задачі «Google-вхід за флагом».
  Пояснення було одноразовим, по ходу — тому пункти лишились відкритими.
