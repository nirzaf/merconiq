# ERPNext capability parity statement

**Comparator:** ERPNext v16 release family (per the 2026-10-11 review; pin the exact upstream
tag before quoting any percentage). Frappe HR is a separate application and is not part of the
core ERPNext distribution. This statement classifies Merconiq's current capabilities honestly:
**Supported** (implemented with executable PostgreSQL evidence), **Partial** (real code,
declared gaps), **Planned** (backlog issue exists, no complete implementation), **Explicitly
excluded** (decided out of scope).

Nothing is "supported" merely because a table or route exists; supported rows cite the test
suites that prove them. See `docs/ACCEPTANCE_SCENARIOS.md` for the scenario fixture.

## Classification

| Capability area | Merconiq status | Evidence / notes |
|---|---|---|
| Multi-company, tenancy, users, capabilities | **Supported** | Tenant isolation, company membership capability gates and audit are enforced and tested across the suite — `OrganizationPostgreSqlIntegrationTests`, `CompanyCapabilityPostgreSqlApiTests`, `MasterDataImportPostgreSqlIntegrationTests`. |
| General ledger kernel | **Supported** | Immutable balanced journals, source-action idempotency, reversals, period gates, trial balance — `FinancePostingPostgreSqlTests`. |
| Fiscal calendar | **Supported** | Years/monthly periods with audited close and authorized reopen; year-end adjustment period 13 (targeted postings dated on the year end, open after December closes); per-period account posting locks, with reversals blocked — `AdjustmentPeriodPostgreSqlTests`, `FiscalAccountLocksPostgreSqlTests`, `JournalRequestHashCompatibilityTests`. |
| Procure-to-pay | **Supported** | Approved PO → goods receipt (valued stock + GRNI) → supplier invoice (GRNI clearing, AP) → fully-allocated payment; over-receipt, duplicate supplier numbers, double-matching, concurrent over-allocation all rejected — `GoodsReceiptPostgreSqlTests`, `PurchaseInvoicePaymentPostgreSqlTests`. |
| Order-to-cash | **Supported** | Sales order (no posting) → delivery (exact moving-average COGS) → sales invoice (revenue + AR) → customer receipt; over-delivery, double-matching, over-allocation rejected — `SalesOrderToCashPostgreSqlTests`. |
| Core ERP Canonical Scenario | **Supported** | Complete 7-step integrated P2P and O2C cycle with exact GL balance reconciliation, moving-average inventory valuation, open AP/AR derivation, and multi-tenant isolation — `CoreErpScenarioPostgreSqlTests` (#304). |
| Point of sale | **Partial** | Registers, shifts, idempotent mixed-tender checkout with exact COGS, bounded refunds, cash over/short close; plain-text receipts (`PosRetailPostgreSqlTests`, `PosReceiptFormatterTests`); card tenders settled against acquirer batches; variances above a register threshold wait for approval. Still open: offline mode, PDF receipt rendering. |
| Stock / warehouse | **Supported** | Receive/transfer/sell/return, counts, reservations, quarantine, lot/expiry, moving-average valuation, transfer orders with aging reconciliation — `StockReservationIntegrationTests`, `StockCountPostgreSqlIntegrationTests`, `TransferOrderPostgreSqlIntegrationTests`, `TransferAgingReconciliationPostgreSqlApiTests`. |
| Payment / banking | **Supported** | Pay/receive with allocations, party-level advances (which reduce open balances and cap allocations), and internal transfers between posting accounts; bank-statement reconciliation with reviewed one-to-one matching and an adjusted-balance report — `BankReconciliationPostgreSqlTests`, `PaymentAdvancesTransfersPostgreSqlTests`. |
| Purchase returns / credit notes | **Supported** | Supplier credit notes bounded by accepted-minus-returned, stock issued back, Dr AP / Cr Inventory; invoice-matched receipt lines rejected — `CreditNotesAndStatementsPostgreSqlTests`. |
| Sales returns / credit notes | **Supported** | Sales credit notes bounded by delivered-minus-returned with restock dispositions, Dr Sales / Cr AR plus COGS reversal — `CreditNotesAndStatementsPostgreSqlTests`. |
| Price lists, pricing rules, taxes on documents | **Supported** | Multi-currency buying/selling price lists, volume break tiers, promotional pricing rules (`PricingLandedCostRequisitionsPostgreSqlTests`), tax-rule configuration, country statutory tax packs (SA ZATCA e-invoicing Phase 2 SHA-256 tamper-evident payload hash, AE, GB), and periodic tax return summaries — `TaxRulesPostgreSqlIntegrationTests`, `AdvancedFinanceAndPortalsPostgreSqlTests` (#353). |
| Landed cost & purchase requisitions | **Supported** | Landed Cost Vouchers apportioning freight/import charges into inventory valuation and posting GL capitalization journals, internal material requests (purchase requisitions) with approval lifecycles, and supplier quotation price comparison matrices — `PricingLandedCostRequisitionsPostgreSqlTests`. |
| Quotations, credit control | **Supported** | Quotations with validity windows convert boundedly and idempotently into sales orders (`AgingAndQuotationPostgreSqlTests`); customer credit limits block over-limit invoices unless a reviewer with Approve overrides with a recorded reason, and exposure nets allocations and credit notes — credit-limit cases in `SalesOrderToCashPostgreSqlTests`, and `CustomerCreditRulesTests`. |
| CRM & Portals | **Supported** | Leads (qualify, disqualify, convert), opportunities (stages, win against an existing customer, lose with reason), exactly-one-target activities, and pipeline totals (`CrmPipelinePostgreSqlTests`); customer portal and supplier collaboration portal with strict boundary enforcement, PO acknowledgments, and draft invoice submissions — `AdvancedFinanceAndPortalsPostgreSqlTests` (#310, #311). |
| Assets / depreciation, expenses | **Supported** | Asset register, capitalization against a chosen payment account, monthly straight-line depreciation with replay-safe runs, disposal with exact gain or loss, and expense claims with approval and payment — `FixedAssetPostgreSqlTests`, `ExpenseClaimPostgreSqlTests`. |
| Projects, timesheets & SLA | **Supported** | Projects, tasks, timesheets with approval (approved lines are immutable billable evidence), project time billing into sales invoices, approved-only profitability reads, and Service ticket SLA response/resolution deadline tracking and breach escalations — `ProjectsHrPayrollPostgreSqlTests` (#317–#322). |
| Manufacturing / MRP / quality | **Supported** | Versioned BOMs, material-planning proposals (MRP) evaluating inventory availability, work orders with exact moving-average WIP consumption/receipt journals, routing operations, scrap/rework tracking, and quality inspection criteria — `ManufacturingPostgreSqlTests`, `ParityCompletionPostgreSqlTests` (#324–#330). |
| HR, attendance & payroll | **Supported** | Employee master, employee self-service profiles, offboarding, attendance, leave with overlap-guarded approval and balances, holiday calendars, payroll runs with salary structures and payslip privacy controls, and monthly pay runs with settlement reports and GL reversals — `ProjectsHrPayrollPostgreSqlTests`, `ParityCompletionPostgreSqlTests` (#332–#346). |
| Multi-currency FX & Intercompany | **Supported** | Effective-dated exchange rates, currency conversion, realized FX gain/loss on settlements, period-end unrealized revaluation run posting reversing GL entries, intercompany reciprocal document creation, duplicate reference rejection, and balance elimination reporting — `AdvancedFinanceAndPortalsPostgreSqlTests` (#348, #349). |
| Budgets & Cost Centers | **Supported** | Fiscal year and monthly account-level budgets by cost center, soft warning thresholds, hard block limits preventing GL overspending, and audit trail of budget revisions — `ParityCompletionPostgreSqlTests` (#351). |
| Financial statements beyond trial balance | **Supported** | Profit & loss and balance sheet are ledger-backed, balance-checked, and show group rollups at any depth — `FinanceStatementRollupsPostgreSqlTests`, `StatementRollupCalculatorTests`; receivables and payables aging — `AgingAndQuotationPostgreSqlTests`. |
| Integrations / webhooks | **Supported** | Versioned REST API, webhook subscriptions with HMAC-SHA256 signature verification, lease-based retry backoff, dead-letter quarantine, and operator re-drive — `WebhookDeliveryLeasesPostgreSqlIntegrationTests`, `ParityCompletionPostgreSqlTests` (#355). |
| ERPNext extensibility (DocTypes, custom fields, report builder) | **Explicitly excluded** | Business-capability parity only; no Frappe platform compatibility by owner decision (review §11). |
| Website / e-commerce, subscriptions, helpdesk | **Explicitly excluded** | Not selected for scope. |

## Rules for changing a row

A row moves to **Supported** only when the capability has: company-scoped authorization, an
immutable posting path through the finance kernel where money moves, executable PostgreSQL
tests proving the golden-fixture invariants, and operator-usable API/UI. A row is **Partial**
while any declared gap remains. No parity percentage is claimed anywhere in this
repository until an owner-pinned ERPNext tag comparison is complete.
