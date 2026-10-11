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
| Multi-company, tenancy, users, capabilities | **Supported** | Tenant isolation, company membership capability gates and audit are enforced and tested across the suite. |
| General ledger kernel | **Supported** | Immutable balanced journals, source-action idempotency, reversals, period gates, trial balance — `FinancePostingPostgreSqlTests`. |
| Fiscal calendar | **Supported** | Years/monthly periods with audited close and authorized reopen; year-end adjustment period 13 (targeted postings dated on the year end, open after December closes); per-period account posting locks, with reversals blocked — `AdjustmentPeriodPostgreSqlTests`, `FiscalAccountLocksPostgreSqlTests`, `JournalRequestHashCompatibilityTests`. |
| Procure-to-pay | **Supported** | Approved PO → goods receipt (valued stock + GRNI) → supplier invoice (GRNI clearing, AP) → fully-allocated payment; over-receipt, duplicate supplier numbers, double-matching, concurrent over-allocation all rejected — `GoodsReceiptPostgreSqlTests`, `PurchaseInvoicePaymentPostgreSqlTests`. |
| Order-to-cash | **Supported** | Sales order (no posting) → delivery (exact moving-average COGS) → sales invoice (revenue + AR) → customer receipt; over-delivery, double-matching, over-allocation rejected — `SalesOrderToCashPostgreSqlTests`. |
| Point of sale | **Partial** | Registers, shifts, idempotent mixed-tender checkout with exact COGS, bounded refunds, cash over/short close; plain-text receipts (`PosRetailPostgreSqlTests`, `PosReceiptFormatterTests`); card tenders settled against acquirer batches; variances above a register threshold wait for approval. Still open: offline mode, PDF receipt rendering. |
| Stock / warehouse | **Partial** | Receive/transfer/sell/return, counts, reservations, quarantine, lot/expiry, moving-average valuation are the codebase's strongest area (pre-existing suites); serial-number tracking, QC holds beyond quarantine and FIFO/lot costing are not implemented. |
| Payment / banking | **Partial** | Pay/receive with allocations, party-level advances (which reduce open balances and cap allocations), and internal transfers between posting accounts; bank-statement reconciliation with reviewed one-to-one matching and an adjusted-balance report — `BankReconciliationPostgreSqlTests`. Advance-to-invoice allocation is not implemented. |
| Purchase returns / credit notes | **Supported** | Supplier credit notes bounded by accepted-minus-returned, stock issued back, Dr AP / Cr Inventory; invoice-matched receipt lines rejected — `CreditNotesAndStatementsPostgreSqlTests`. |
| Sales returns / credit notes | **Supported** | Sales credit notes bounded by delivered-minus-returned with restock dispositions, Dr Sales / Cr AR plus COGS reversal — `CreditNotesAndStatementsPostgreSqlTests`. |
| Price lists, taxes on documents | **Planned** | Tax-rule configuration and deterministic amount calculation exist; effective-dated tax journals on sales/purchase documents are not wired, and price lists do not exist yet. |
| Quotations, credit control | **Supported** | Quotations with validity windows convert boundedly and idempotently into sales orders (`AgingAndQuotationPostgreSqlTests`); customer credit limits block over-limit invoices unless a reviewer with Approve overrides with a recorded reason, and exposure nets allocations and credit notes — credit-limit cases in `SalesOrderToCashPostgreSqlTests`, and `CustomerCreditRulesTests`. Dunning and credit scoring are not in scope. |
| CRM, portals | **Partial** | Leads (qualify, disqualify, convert), opportunities (stages, win against an existing customer, lose with reason), exactly-one-target activities, and pipeline totals — `CrmPipelinePostgreSqlTests`. Customer and supplier portals (TS-16 #310, #311) are not implemented. |
| Assets / depreciation, expenses | **Partial** | Asset register, capitalization against a chosen payment account, monthly straight-line depreciation with replay-safe runs, disposal with exact gain or loss, and expense claims with approval and payment — `FixedAssetPostgreSqlTests`, `ExpenseClaimPostgreSqlTests`. Impairment, revaluation, custody transfer, claim spending policy and employee master data are not implemented. |
| Projects, timesheets | **Partial** | Projects, tasks, timesheets with approval (approved lines are immutable billable evidence) and approved-only profitability reads; SLA tracking and invoicing of project time are open (TS-18 #317–#322). |
| Manufacturing / MRP / quality | **Planned** | TS-19 (#324–#330). |
| HR, payroll | **Partial** | Employee master, attendance (one open record per employee-day), leave with overlap-guarded approval and balances; monthly pay runs posting Dr salary expense / Cr bank / Cr deductions payable once per month with structure snapshots (`ProjectsHrPayrollPostgreSqlTests`). Payslip privacy controls, holiday calendars and country rule packs remain open (TS-20 #332–#346). |
| FX, intercompany, budgets, statutory packs | **Planned** | TS-21 (#348–#353). |
| Financial statements beyond trial balance | **Supported** | Profit & loss and balance sheet are ledger-backed, balance-checked, and show group rollups at any depth — `FinanceStatementRollupsPostgreSqlTests`, `StatementRollupCalculatorTests`; receivables and payables aging — `AgingAndQuotationPostgreSqlTests`. |
| Integrations / extensibility | **Partial** | Versioned REST, webhooks with durable delivery, imports/exports, forecasting exist; webhook dead-letter/signature hardening is tracked under TS-22 (#355–#361). |
| ERPNext extensibility (DocTypes, custom fields, report builder) | **Explicitly excluded** | Business-capability parity only; no Frappe platform compatibility by owner decision (review §11). |
| Website / e-commerce, subscriptions, helpdesk | **Explicitly excluded** | Not selected for scope. |

## Declared gaps still open (as of this statement)

These keep their rows Partial or Planned. Each needs its own design before implementation:

- POS offline mode and PDF receipt rendering.
- Serial-number tracking, QC holds beyond quarantine, FIFO and lot costing.
- Advance-to-invoice allocation.
- Effective-dated tax journals on documents and price lists.
- CRM customer and supplier portals (TS-16 #310–#311).
- Asset impairment, revaluation and custody transfer; expense claim spending policy; employee master.
- Projects, manufacturing, HR and payroll, FX/intercompany/budgets/statutory packs (TS-18 to TS-21).
- Separation-of-duties rules (for example, a cashier may not approve their own held till close); the owner has not decided these.
- Webhook dead-letter and signature hardening (TS-22).

## Rules for changing a row

A row moves to **Supported** only when the capability has: company-scoped authorization, an
immutable posting path through the finance kernel where money moves, executable PostgreSQL
tests proving the golden-fixture invariants, and operator-usable API/UI. A row is **Partial**
while any declared gap above remains. No parity percentage is claimed anywhere in this
repository until an owner-pinned ERPNext tag comparison is complete (TS-23, #368).
