# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Double-entry general ledger posting kernel with fiscal years, periods, account roles, and company-scoped mappings.
- Procure-to-Pay (P2P) workflow: goods receipts, purchase invoices, and payment allocations.
- Order-to-Cash (O2C) workflow: sales orders, delivery notes, and sales invoices.
- POS retail session management, sales receipts, and refunds with automated stock and ledger posting.
- Bounded supplier and customer credit notes with inventory restock and ledger adjustments.
- Ledger-backed financial statements (Profit & Loss and Balance Sheet) derived from journal lines.
- Account-hierarchy rollups on the Profit & Loss and Balance Sheet for classified group accounts.
- Year-end adjustment period (period 13) that accepts only journals explicitly targeted at the fiscal year end.
- Per-period posting locks on individual chart accounts, enforced by the posting kernel, reversals included.
- Bank statement import with reviewed one-to-one matching and a reconciliation report.
- Fixed assets: register, capitalization, monthly straight-line depreciation with replay-safe runs, and disposal with gain or loss.
- Employee expense claims: submit, approve (posts against the employee payable), reject with reason, and pay from the bank.
- Customer credit limits, with invoice blocking over the limit and reviewer overrides that record a reason.
- POS plain-text receipts, card tender settlement against acquirer batches, and till approval for cash variances above a register threshold.
- CRM leads, opportunities and activities, with stage rules and a pipeline summary.

### Fixed

- The Docker build stages use the SDK pinned by `global.json` (10.0.300).

### Changed

- Renamed the project from Stockpile to Merconiq.
- Renamed the .NET solution, projects, assemblies, and root namespaces.
- Updated application, documentation, CI/CD, and container branding.
- Merconiq is now the platform identity; Inventory remains a business domain.
- The profit-and-loss and balance-sheet responses now carry `rollups`; existing fields are unchanged.
- Journal request hashes are unchanged for regular postings; only requests targeted at period 13 hash the target.

## [1.0.0] - 2026-06-12

### Added

- Clean Architecture solution with four projects: `Web`, `Infrastructure`, `Core`, and `Tests`.
- PostgreSQL 16 data layer with Entity Framework Core 10 and `xmin` optimistic concurrency.
- Domain entities for items, stock in hand, locations, suppliers, purchase orders, order
  details, stock transactions, audit log, and webhook subscriptions.
- Generic `Repository<T>` and `UnitOfWork` patterns.
- Service layer: `ItemService`, `StockService`, `PurchaseOrderService`, `SupplierService`,
  `LocationService`, `DemandForecastService`, and `AnomalyDetectionService`.
- MediatR-based CQRS handlers under `Core/Features` for commands and queries.
- ASP.NET Core Identity with `Admin`, `Manager`, and `Staff` roles and seeded admin user.
- Versioned REST API under `/api/v1` for items, stock, forecasting, and anomalies.
- Swagger UI in the Development environment with XML doc integration.
- MudBlazor 9 responsive UI — desktop, tablet, and mobile.
- ML.NET SSA demand forecasting per item with moving-average fallback.
- ML.NET IID spike / drop anomaly detection over stock transactions.
- QuestPDF report generation for purchase orders and stock reports.
- Webhook dispatcher for outbound event notifications.
- Automatic audit log population on every `SaveChangesAsync`.
- Global exception handler returning structured JSON error responses.
- Health checks for liveness and database connectivity.
- Rate limiting — 100 req/min for the API, 10 req/min for AI endpoints.
- Dual authentication: cookie auth for the MVC UI and JWT bearer for API calls.
- Docker and Docker Compose deployment with PostgreSQL.
- GitHub Actions CI: build, test, and publish image to GitHub Container Registry.
- Open-source governance files: `LICENSE`, `CODE_OF_CONDUCT.md`, `CONTRIBUTING.md`,
  `SECURITY.md`, issue and PR templates.
- 184 xUnit tests covering services, handlers, controllers, and integration paths.
