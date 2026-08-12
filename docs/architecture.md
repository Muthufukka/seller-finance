# Architecture and ERD

```mermaid
flowchart LR
    Browser[React browser] -->|secure cookie /api/v1| API[ASP.NET Core API]
    API --> PostgreSQL[(PostgreSQL)]
    API --> Kaspi[Kaspi Shop API]
    API --> Telegram[Telegram Bot API]
    Sync[Kaspi sync worker] --> PostgreSQL
    Sync --> Kaspi
    Export[Export worker] --> PostgreSQL
```

```mermaid
erDiagram
    AppUser ||--o{ OrganizationUser : memberships
    Organization ||--o{ OrganizationUser : contains
    Organization ||--o{ Product : owns
    Organization ||--o{ Order : owns
    Product ||--o{ ProductCostHistory : costs
    Order ||--o{ OrderLine : lines
    Organization ||--o{ FeeRule : rules
    OrderLine ||--o| ActualFee : override
    Organization ||--o{ Expense : expenses
    Organization ||--o| MarketplaceConnection : kaspi
    MarketplaceConnection ||--o{ SyncJob : jobs
    Organization ||--o{ ExportJob : exports
    Organization ||--o| TelegramConnection : telegram
    Organization ||--o{ NotificationRule : notifications
    Organization ||--o{ AuditLog : audit
```

Tenant isolation выполняется membership-проверкой до business endpoint и обязательным `OrganizationId` predicate при entity lookup. Активная организация хранится как Identity user-claim и попадает в подписанную HttpOnly cookie; клиентские business-запросы не передают tenant. Переключение выполняется отдельным endpoint, который сначала проверяет membership и состояние подписки, затем перевыпускает cookie.

Kaspi, export и Telegram workers используют атомарный conditional claim в PostgreSQL. Только один экземпляр может перевести конкретную запись в `Running`/`Sending`; задания с истёкшим lease восстанавливаются после падения процесса. Частичный уникальный индекс допускает не более одного активного sync job на Kaspi connection, а конкурентная постановка возвращает безопасный conflict вместо дубля.
