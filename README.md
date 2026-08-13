# Seller Finance

Pilot-ready multi-tenant SaaS для управленческой аналитики продавцов Kaspi Магазина. Backend — ASP.NET Core 10, frontend — React/Vite, БД — PostgreSQL.

## Возможности

- Identity cookie auth, организации, Owner/Admin/Analyst/Viewer и tenant isolation;
- зашифрованное AES-256-GCM подключение Kaspi, фоновые sync jobs и UPSERT заказов;
- история себестоимости, ручное изменение и CSV/XLSX preview-confirm import;
- версионные комиссии, фактические удержания, доставка и расходы;
- dashboard, товары, заказы, ABC 80/15/5 и drill-down расчёта;
- фоновые CSV UTF-8/XLSX exports с временной ссылкой;
- Telegram linking и правила уведомлений;
- Trial/Start/Pro/Business, лимиты и SaaS Admin;
- audit log, rate limiting, security headers и health/readiness probes.

## Локальный запуск

Требования: .NET SDK 10, Node.js 24+, PostgreSQL.

```powershell
$env:DATABASE_URL='postgresql://postgres:postgres@localhost:5432/seller_finance'
$env:TOKEN_ENCRYPTION_KEY='<base64-encoded 32-byte key>'
dotnet run --project src/SellerFinance.Api
```

Для frontend-разработки:

```powershell
cd src/SellerFinance.Web
pnpm install --frozen-lockfile
pnpm dev
```

Миграции применяются приложением при старте. Создание новой миграции:

```powershell
dotnet ef migrations add Name --project src/SellerFinance.Api --startup-project src/SellerFinance.Api
```

## Переменные окружения

Обязательные:

- `DATABASE_URL` — PostgreSQL connection URL;
- `TOKEN_ENCRYPTION_KEY` — случайный 32-байтовый ключ в Base64. Не менять после сохранения Kaspi token без процедуры re-encryption.
- `PUBLIC_BASE_URL` — доверенный публичный HTTPS origin без path/query, например `https://seller-finance.example`; используется для CORS/origin-проверки и ссылок в письмах/уведомлениях.
- `APP_MODE` — явный режим `Demo`, `Pilot` или `Production`. Для production-домена обязателен; публичный `seller-finance.onrender.com` распознаётся как известная Demo-среда для безопасного перехода.

Опциональные:

- `SAAS_ADMIN_EMAIL` — email администратора SaaS;
- `TELEGRAM_BOT_TOKEN` — token от BotFather;
- `TELEGRAM_BOT_USERNAME` — имя бота без `@`;
- `TELEGRAM_WEBHOOK_SECRET` — случайный secret token для заголовка Telegram webhook;
- `EMAIL_CONFIRMATION_REQUIRED=true` — требует подтверждённый email для входа;
- `EMAIL_SMTP_HOST`, `EMAIL_FROM` — SMTP host и валидный адрес отправителя;
- `EMAIL_SMTP_PORT` (по умолчанию `587`), `EMAIL_SMTP_TLS` (в production обязательно `true`), `EMAIL_SMTP_TIMEOUT_SECONDS` (по умолчанию `15`) — параметры SMTP;
- `EMAIL_SMTP_USER`, `EMAIL_SMTP_PASSWORD` — необязательная пара SMTP credentials: задаются только вместе;
- `EMAIL_FROM_NAME` (по умолчанию `Seller Finance`) — отображаемое имя отправителя.
- `SEED_DEMO_DATA=true` — разрешён только вместе с `APP_MODE=Demo`; добавляет исключительно синтетический набор при пустой БД.

Секреты задаются только в Render Environment и не добавляются в Git или сообщения.

## Проверка

```powershell
dotnet test SellerFinance.slnx
cd src/SellerFinance.Web
pnpm build
```

Production probes:

- `/health` — процесс приложения;
- `/health/database` — PostgreSQL;
- `/health/ready` — БД и ключ шифрования.

## Подключение Kaspi

1. В кабинете Kaspi откройте **Настройки → Токен API → Сформировать**.
2. В Seller Finance откройте **Интеграции**.
3. Вставьте token в форму. Он проверяется запросом к официальному API и сохраняется только зашифрованным.
4. Запустите синхронизацию. Повторный запуск обновляет существующие external order IDs.

## Production

Production API documentation is available at `/api-docs`; the machine-readable contract is `/openapi/v1.json`. Business endpoints use the secure HttpOnly cookie session and resolve the tenant from authenticated organization membership.

Render собирает React и API одним Dockerfile. Подробные процедуры находятся в [production runbook](docs/production-runbook.md), архитектура и ERD — в [architecture](docs/architecture.md).

Перед реальными данными продавцов необходимо использовать долгоживущую БД в Казахстане, пройти правовую проверку и заменить ранее раскрытые credentials.
Текущий публичный Render работает как `Demo`: интерфейс требует согласие на использование только вымышленных данных, а создание, проверка и синхронизация Kaspi connections технически отключены.
