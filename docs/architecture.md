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

Tenant isolation выполняется membership-проверкой до business endpoint и обязательным `OrganizationId` predicate при entity lookup. `X-Organization-Id` является только селектором одной из организаций авторизованного пользователя и никогда не считается доказательством доступа.
