# Проверка Seller Finance перед пилотом

## Автоматические уровни

1. `dotnet test SellerFinance.slnx --configuration Release` запускает unit, security и API E2E тесты.
2. API E2E поднимает полное ASP.NET Core приложение в окружении `Testing` на отдельной InMemory БД. Основной acceptance-сценарий проходит регистрацию, cookie-сессию, проверку и шифрование Kaspi token на обезличенном JSON:API fixture, sync worker, CSV preview/confirm себестоимости, dashboard с полной Coverage, background export и скачивание по временной ссылке. Дополнительные сценарии проверяют настройки организации, расходы, роли, cross-tenant отказ и удаление данных.
3. `pnpm build` проверяет TypeScript и production bundle React.
4. `dotnet ef migrations script --idempotent` проверяет построение полного PostgreSQL migration script.
5. CI поднимает чистый PostgreSQL 17, применяет всю цепочку миграций и проверяет реальные unique constraints и атомарное подтверждение импорта себестоимости. Локально эти тесты включаются переменной `TEST_POSTGRES_CONNECTION` и должны указывать только на одноразовую тестовую БД: тест очищает схему `public`.
6. PostgreSQL regression намеренно пытается связать sync job с Kaspi connection другой организации и расход с чужим товаром; обе записи должны завершиться `foreign_key_violation`.
7. `compose-smoke` в GitHub Actions собирает `docker-compose.yml`, поднимает PostgreSQL 18 и приложение, затем ждёт успешный `/health/database`. Это проверяет локальный migration/restore smoke-контур без ручной установки PostgreSQL.
8. GitHub Actions выполняет эти проверки при каждом push и pull request.

Worker regression проверяет конкурентную обработку export jobs, восстановление stale export/Telegram leases и схему частичного уникального индекса активных Kaspi sync jobs. Идемпотентный migration script дополнительно проверяется на наличие lease-колонок, дедупликации существующих активных jobs и уникального индекса.
Оба preview/confirm контура импорта используют атомарный claim в PostgreSQL. Security regression покрывает подмену расширения, бинарный CSV, формулы, повторяющиеся заголовки и cross-tenant ссылки; PostgreSQL integration одновременно подтверждает один финансовый preview из двух конкурирующих запросов ровно один раз.

Режим `TEST_USE_INMEMORY` используется только вместе с окружением `Testing`. Production всегда требует `DATABASE_URL` и PostgreSQL.

## Production smoke

## Безопасная проверка реального Kaspi API

До Pilot реальный token нельзя отправлять в Demo/Render или сохранять в репозитории. Проверить доступность API и валидность token можно локально без сохранения response body:

```powershell
$env:KASPI_TOKEN='<token из кабинета Kaspi>'
node scripts/kaspi-connectivity-smoke.mjs
Remove-Item Env:KASPI_TOKEN
```

Скрипт запрашивает не более одной записи за последний час, не читает и не выводит тело ответа и сообщает только HTTP-статус. Успешный результат выглядит как `{"reachable":true,"authenticated":true,"status":200}`. Token нельзя передавать в чат, командную строку с сохранением истории или GitHub Actions.

The smoke script validates `/api-docs` and `/openapi/v1.json`, checks critical paths in the contract, and rejects known secret/configuration names in the published JSON.
It also submits an unsafe request with an attacker `Origin` and `Sec-Fetch-Site: cross-site`; production must reject it with HTTP 403 before endpoint execution.

После успешного Render deploy:

```powershell
node scripts/production-smoke.mjs https://seller-finance.onrender.com 1557ef7
```

Второй аргумент необязателен и задаёт ожидаемые первые семь символов commit. Проверяются:

- HTML и фактический JavaScript asset;
- `/health`, `/health/database`, `/health/ready`;
- revision развернутого commit;
- ответы `401` закрытых business/admin/destructive endpoints;
- CSP, `X-Content-Type-Options` и `Referrer-Policy`.

## Безопасная нагрузочная проверка

```powershell
node scripts/load-smoke.mjs https://seller-finance.onrender.com/health 100 5 800
```

Аргументы: URL, число запросов, параллельность и максимальный P95 в миллисекундах. Скрипт ограничивает запуск максимум 500 запросами и параллельностью 20, чтобы исключить случайный агрессивный тест.

Проверка 12.08.2026 после прогрева: 100 запросов, concurrency 5, P50 278 мс, P95 346 мс, max 431 мс. Это внешний smoke одного лёгкого endpoint, а не доказательство требования dashboard для 100 000 строк. Полный performance gate выполняется на production-классе инфраструктуры с обезличенным набором данных.

## Backup/restore gate

Free Render PostgreSQL не является production-хранилищем и не может закрыть требования RPO/RTO. Перед пилотом с реальными данными обязательны:

1. PostgreSQL на территории Казахстана с ежедневными backup и PITR.
2. Restore последней копии в новую изолированную БД.
3. `pg_restore --list`, сверка количества организаций, заказов и строк заказов.
4. Запуск миграций и smoke против восстановленной БД.
5. Фиксация времени восстановления; результат должен быть не более 4 часов.
6. Удаление тестовой восстановленной БД по утверждённому процессу.

Пока этот drill не выполнен на целевой инфраструктуре, backup/restore считается **не подтверждённым**, даже если команды runbook корректны.

## Analytics performance gate

ТЗ требует dashboard за период до 12 месяцев для tenant с минимум 100 000 order items не дольше 2 секунд при прогретой БД/агрегатах. В CI этот критерий проверяется отдельным тестом:

```powershell
dotnet test SellerFinance.slnx --configuration Release --filter "Category=Performance" --logger "console;verbosity=detailed"
```

Fixture создаёт 10 000 заказов по 10 строк, прогревает согласованный tenant/date snapshot и измеряет повторный расчёт dashboard. Версия снимка хранится в PostgreSQL и атомарно увеличивается в транзакции `SaveChanges`/`SaveChangesAsync` с бизнес-данными организации. Поэтому каждый stateless API instance обнаруживает запись другого экземпляра и перестраивает свой локальный snapshot; regression-тест доказывает увеличение версии и попадание нового заказа в следующий расчёт.

Локальный контрольный прогон 12.08.2026: **30 мс на 100 000 order items** после прогрева (лимит 2 000 мс). Это доказывает gate приложения для прогретых агрегатов. Первый расчёт и производительность самой целевой PostgreSQL дополнительно проверяются на production-классе инфраструктуры перед переносом реальных данных.
