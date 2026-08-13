# Перенос PostgreSQL в Казахстан

## Рекомендуемый путь

Для Pilot выбрать отдельную PostgreSQL-инфраструктуру в Казахстане, а не продлевать текущую Render Free database. Практичный кандидат — PS Cloud: его официальная документация описывает virtual servers в зоне `kz-ala-1`, а компания указывает дата-центры в Алматы и Астане.

Перед заказом запросить у провайдера письменное подтверждение именно для выбранного проекта:

1. физическое размещение primary PostgreSQL и всех backup/PITR-реплик в Казахстане;
2. доступный SLA, RPO/RTO и retention для WAL/PITR;
3. шифрование in transit, отдельная private network и возможность IP allow-list;
4. процедуру выгрузки/удаления данных по завершении Pilot.

До получения этих подтверждений нельзя задавать `DATA_RESIDENCY=KZ` или переводить приложение из `Demo` в `Pilot`.

## Целевая схема

```text
Internet → Render Web Service (HTTPS)
                 │ private TLS connection
                 ▼
       PostgreSQL primary in Kazakhstan
                 │
          PITR / daily backups
                 ▼
        isolated restore database in Kazakhstan
```

PostgreSQL не должен иметь публичного ingress. Внешний доступ для администратора — только через VPN/bastion с временным allow-list; приложение использует отдельного DB user с минимальными правами.

## Порядок миграции

1. Создать целевой PostgreSQL и отдельную restore database в Казахстане. Не передавать URL, пароли или Kaspi token в чат и GitHub.
2. Включить TLS и backup/PITR, зафиксировать retention и ответственных.
3. На целевой БД применить миграции из неизменяемого Git commit:

```powershell
dotnet ef database update --project src/SellerFinance.Api
```

4. Выполнить restore drill в отдельную restore database, затем проверить архив, миграции, количество организаций/заказов и `/health/database` на временном сервисе.
5. Только после успешного drill создать новый DB credential для приложения и сохранить его как `DATABASE_URL` в Render Environment. Старую Render БД не удалять до smoke и сверки данных.
6. Выполнить deploy, проверить `/health`, `/health/database`, `/health/ready`, логин, dashboard и тестовый экспорт.
7. После подписанного подтверждения legal review, SMTP delivery и успешной проверки Kaspi включить `APP_MODE=Pilot`, `SEED_DEMO_DATA=false`, `DATA_RESIDENCY=KZ`, `BACKUP_RESTORE_CONFIRMED=true`, `CREDENTIALS_ROTATED=true` и остальные подтверждения.

## Evidence для закрытия гейта

- договор/письмо провайдера с локацией primary и backup в Казахстане;
- успешный restore drill с временем восстановления не более четырёх часов;
- health/smoke после переключения `DATABASE_URL`;
- audit record смены секретов без значений секретов;
- письменное одобрение legal review.

Ссылки для проверки кандидата: [PS Cloud дата-центры](https://www.ps.kz/about/data-center), [создание virtual server](https://docs.ps.kz/ru/cloud/cloud-server/quickstart/create-vm).
