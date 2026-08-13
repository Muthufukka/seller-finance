# Seller Finance production runbook

Автоматические E2E, production smoke и load-проверки описаны в `docs/verification.md`.

## Deploy

Public API documentation: `/api-docs`. Machine-readable OpenAPI contract: `/openapi/v1.json`. After every deploy verify critical routes and confirm that the contract contains no secrets or connection strings.

1. Убедиться, что CI-тесты и frontend build успешны.
2. Проверить обязательные Render settings: `DATABASE_URL`, `TOKEN_ENCRYPTION_KEY`, `PUBLIC_BASE_URL` с HTTPS origin и явный `APP_MODE=Pilot|Production`. `SEED_DEMO_DATA` обязан быть выключен. Перед включением `EMAIL_CONFIRMATION_REQUIRED=true` задать и проверить `EMAIL_SMTP_HOST`, `EMAIL_SMTP_PORT`, `EMAIL_SMTP_TLS=true`, `EMAIL_FROM` и парные `EMAIL_SMTP_USER`/`EMAIL_SMTP_PASSWORD`.
3. Выполнить deploy неизменяемого Git commit.
4. Проверить `/health`, `/health/database`, `/health/ready`.
5. Проверить вход, `/api/v1/session`, dashboard и одну тестовую выгрузку.
6. В Events убедиться, что активен ожидаемый commit.

## Backup и restore

Production БД должна поддерживать автоматические резервные копии и point-in-time recovery. Free Render PostgreSQL для production запрещён.

Публичная демонстрация использует `APP_MODE=Demo`: Kaspi connection/sync workers и mutating endpoints отключены, регистрация требует явного подтверждения синтетических данных, а режим отображается в UI и `/health`. Перевод в Pilot/Production выполняется только одновременно с KZ-hosted БД, правовой проверкой и ротацией secrets.

Ручной backup:

```powershell
pg_dump --format=custom --no-owner --no-acl --file=seller-finance.dump "$env:DATABASE_URL"
```

Проверка архива:

```powershell
pg_restore --list seller-finance.dump
```

Restore выполняется только в новую тестовую БД:

```powershell
pg_restore --clean --if-exists --no-owner --no-acl --dbname="$env:RESTORE_DATABASE_URL" seller-finance.dump
```

После restore проверить миграции, количество организаций/заказов и `/health/database`. Переключение production connection допускается только после письменного подтверждения владельца системы.

## Kaspi sync incident

- `401/403`: статус `RequiresAttention`; попросить владельца заменить token. Не повторять бесконечно.
- `429`: дождаться exponential backoff, не запускать параллельные jobs.
- `5xx`: проверить состояние Kaspi, повторные попытки ограничены пятью.
- Расхождение заказов: повторно синхронизировать recent window; UPSERT не создаёт дубли.
- Никогда не копировать token, покупательские данные или полный response body в тикеты/логи.

## Database incident

1. Остановить mutations или временно приостановить service.
2. Зафиксировать время и последний успешный backup.
3. Восстановить backup в отдельную БД.
4. Выполнить smoke и сверку counts.
5. Переключить `DATABASE_URL`, затем сохранить старую БД read-only до завершения расследования.

## Key rotation

Kaspi tokens зашифрованы `TOKEN_ENCRYPTION_KEY`. Нельзя просто заменить ключ: сначала расшифровать записи старым ключом, зашифровать новым и только затем удалить старый. При утрате ключа token восстановить невозможно — продавцы подключают Kaspi заново.

## Telegram

Webhook URL: `https://<domain>/api/v1/telegram/webhook`. При регистрации webhook передать `TELEGRAM_WEBHOOK_SECRET` параметром `secret_token`; Telegram затем присылает его в заголовке `X-Telegram-Bot-Api-Secret-Token`. Не помещать secret в URL. Bot token хранится только в Render. При компрометации token отозвать его через BotFather, заменить secret и заново установить webhook.

Событийные сообщения сначала сохраняются в durable queue. Повторные события дедуплицируются, ошибки Telegram повторяются с backoff до пяти попыток, а итоговый статус доступен через `GET /api/v1/telegram`.

## Retention

- export file: 1 час, затем bytea очищается worker-ом;
- import preview: 24 часа;
- audit log: не содержит секретов и данных покупателя;
- backup retention определяется production-планом БД и правовыми требованиями.
