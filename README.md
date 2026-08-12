# Seller Finance

Рабочий vertical-slice MVP SaaS-сервиса финансовой аналитики для продавцов Kaspi Магазина. Проект реализует ключевую идею ТЗ: продавец быстро видит прибыль, маржу и качество исходных данных, а финансовые показатели можно разложить до товара и заказа.

## Что уже реализовано

- адаптивный React dashboard с KPI, динамикой, Coverage и проблемами данных;
- раздел товаров и навигационные заготовки заказов/ABC;
- ASP.NET Core API с префиксом `/api/v1`;
- обязательный tenant-заголовок `X-Organization-Id` и закрытие неизвестного tenant;
- расчёт Revenue, COGS, fees, delivery, contribution/operating profit и Coverage только в `decimal`;
- корректное исключение RETURNED/CANCELLED из факта и отсутствие подстановки `cost = 0`;
- точное распределение доставки по выручке строк;
- безопасные demo-endpoints проверки токена и постановки синхронизации в очередь;
- unit-тесты критических кейсов FIN-01, FIN-02, FIN-04 и распределения доставки.

## Локальный запуск

Требования: .NET SDK 10, Node.js 22+ и pnpm.

В первом терминале:

```powershell
dotnet run --project src/SellerFinance.Api --urls http://localhost:5068
```

Во втором терминале:

```powershell
cd src/SellerFinance.Web
pnpm install
pnpm dev
```

Откройте `http://localhost:5173`. Frontend обращается к API через Vite proxy. Demo tenant: `demo-organization`.

## Проверка

```powershell
dotnet test tests/SellerFinance.Tests/SellerFinance.Tests.csproj
cd src/SellerFinance.Web
pnpm build
```

## Публикация на Render

В репозитории есть `render.yaml`. После загрузки проекта в GitHub выберите в Render **New → Blueprint**, подключите репозиторий и примените найденный Blueprint. Render соберёт React и ASP.NET Core в один Docker-контейнер и выдаст публичный HTTPS-адрес.

## Структура

- `src/SellerFinance.Domain` — финансовая модель и расчёты без привязки к инфраструктуре;
- `src/SellerFinance.Api` — REST API и demo-store для первого запуска;
- `src/SellerFinance.Web` — React + TypeScript интерфейс;
- `tests/SellerFinance.Tests` — критические финансовые сценарии.

## Следующий production-этап

Demo-store нужно заменить на PostgreSQL + EF Core migrations; добавить ASP.NET Core Identity, роли OrganizationUsers, AES-256-GCM/envelope encryption токена, durable background jobs, Kaspi adapter с подтверждёнными официальными контрактами, импорт XLSX/CSV с preview, audit log и object storage. Реальный Kaspi token не должен добавляться в Git или логи.
