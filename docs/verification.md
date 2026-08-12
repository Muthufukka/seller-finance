# Проверка Seller Finance перед пилотом

## Автоматические уровни

1. `dotnet test SellerFinance.slnx --configuration Release` запускает unit, security и API E2E тесты.
2. API E2E поднимает полное ASP.NET Core приложение в окружении `Testing` на отдельной InMemory БД. Сценарий проходит регистрацию, cookie-сессию, настройки организации, расход, cross-tenant отказ и удаление данных.
3. `pnpm build` проверяет TypeScript и production bundle React.
4. `dotnet ef migrations script --idempotent` проверяет построение полного PostgreSQL migration script.
5. GitHub Actions выполняет эти проверки при каждом push и pull request.

Режим `TEST_USE_INMEMORY` используется только вместе с окружением `Testing`. Production всегда требует `DATABASE_URL` и PostgreSQL.

## Production smoke

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
