# Pilot readiness audit

Статус аудита: 13 августа 2026. Текущий публичный сервис — `Demo`, а не Pilot/Production. Это намеренное безопасное состояние: `/api/v1/runtime` показывает режим и gates, а реальные Kaspi connections и workers отключены.

## Доказано в коде и автоматических проверках

| Область ТЗ | Статус | Доказательство |
| --- | --- | --- |
| Auth, secure cookie, email-confirmation flow, password reset, организации и membership | Реализовано | ASP.NET Core Identity, HttpOnly/Secure cookie, E2E регистрации/login/logout/refresh/invitations и audit events. |
| Tenant isolation и роли | Реализовано | Tenant определяется из membership, PostgreSQL composite FK и E2E/IDOR проверки. Owner/Admin управляют settings; Analyst — расходы/импорты/экспорт; Viewer — analytics/reports. |
| Kaspi orders | Реализовано для разрешённого API-контракта | AES-256-GCM token storage, X-Auth-Token adapter, background job, recent-window UPSERT, retry/backoff, RequiresAttention, fixture contract tests. |
| Cost/finance engine | Реализовано | ProductCostHistory, CSV/XLSX preview-confirm, historical completion-date selection, fee priority, delivery allocation, expenses, coverage and drill-down. FIN-01…FIN-06 покрыты тестами. |
| Analytics/UI/export | Реализовано | Dashboard, products, orders, ABC 80/15/5, period filters, CSV/XLSX background exports and temporary download token. |
| Notifications/SaaS | Реализовано | Telegram one-time linking, durable delivery/retry/deduplication, plans/limits, SaaS admin and audit log. |
| Browser/API hardening | Реализовано | Origin validation, rate limits, CSP, `X-Frame-Options: DENY`, permissions policy, health/readiness/database probes. |

Перед этим audit были пройдены 133 backend tests и production build frontend. Полный список проверок и команды — в `docs/verification.md`.

## Обязательные внешние гейты до Pilot

Ни один пункт ниже не должен подтверждаться без фактического выполнения владельцем системы:

### Текущее внешнее состояние (проверено 13 августа 2026)

- Render PostgreSQL `seller-finance-db` работает на тарифе Free в регионе Oregon (US West), поэтому не соответствует требованию размещения данных в Казахстане.
- Render указывает дату удаления Free-базы: 11 сентября 2026. Это исключает её использование как долгоживущего хранилища Pilot/Production.
- Web Service использует отдельный секрет `DATABASE_URL`; при ротации credential его необходимо обновлять согласованно с новым internal URL и проверять `/health/database` после deploy.
- Внешние inbound rules базы содержат `0.0.0.0/0`. До появления обоснованного внешнего потребителя правило следует сузить или удалить вместе с миграцией на целевую инфраструктуру.
- 13 августа 2026 выполнена ротация credential: создан новый default credential, `DATABASE_URL` Web Service переключён на его internal URL, deploy успешно завершён, а старый credential удалён. После удаления `/health/database` вернул `healthy`.

- [ ] Перенести PostgreSQL с Render Free в долгоживущую инфраструктуру, физически размещённую в Казахстане; подтвердить `DATA_RESIDENCY=KZ`.
- [ ] Провести профильную юридическую проверку обработки данных; только после неё задать `LEGAL_REVIEW_CONFIRMED=true`.
- [ ] Настроить ежедневные backup/PITR и выполнить restore drill в отдельную БД; только после подтверждения задать `BACKUP_RESTORE_CONFIRMED=true`.
- [x] Ротировать PostgreSQL password, который был опубликован в переписке, и любые временные secrets. Выполнено 13 августа 2026; старый credential удалён, новый проверен через database health. `CREDENTIALS_ROTATED=true` следует установить только в целевой Pilot/Production среде после миграции в Казахстан.
- [ ] Настроить production SMTP с TLS, провести доставку confirmation/reset email и включить `EMAIL_CONFIRMATION_REQUIRED=true`.
- [ ] Получить отдельный реальный Kaspi token от продавца через кабинет Kaspi, сохранить его только через защищённую форму/Pilot secrets, сверить adapter с актуальной официальной документацией и обезличенными fixtures.
- [ ] Провести pilot с 5–10 продавцами: сопоставить результаты с их Excel/P&L, проверить возвраты, комиссии, delivery и Coverage, зафиксировать расхождения.

После выполнения всех пунктов установить `APP_MODE=Pilot`, `SEED_DEMO_DATA=false` и перечисленные подтверждения в Render Environment. До этого приложение fail-closed: readiness и business API в реальном режиме не запускаются.

## Production smoke после каждого deploy

1. Убедиться, что Render обслуживает ожидаемый Git commit.
2. Проверить `/health`, `/health/database`, `/health/ready`.
3. Войти тестовым пользователем, открыть dashboard, выполнить одну безопасную экспортную задачу.
4. Проверить audit event и отсутствие secrets/customer PII в logs.
5. Для Pilot дополнительно проверить email, Kaspi sync и Telegram test delivery.
