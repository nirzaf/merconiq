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
| General ledger kernel | **Supported** | Immutable balanced journals, source-action idempotency, reversals, period gates, trial balance — `FinancePostingPostgreSqlTests` (13 tests). |
| Fiscal calendar | **Partial** | Years/monthly periods with audited close + authorized reopen; period 13/adjustment periods and per-period account locking are future work. |
| Procure-to-pay | **Supported** | Approved PO → goods receipt (valued stock + GRNI) → supplier invoice (GRNI clearing, AP) → fully-allocated payment; over-receipt, duplicate supplier numbers, double-matching, concurrent over-allocation all rejected — `GoodsReceiptPostgreSqlTests`, `PurchaseInvoicePaymentPostgreSqlTests`. |
| Order-to-cash | **Supported** | Sales order (no posting) → delivery (exact moving-average COGS) → sales invoice (revenue + AR) → customer receipt; over-delivery, double-matching, over-allocation rejected — `SalesOrderToCashPostgreSqlTests`. |
| Point of sale | **Partial** | Registers, shifts, idempotent mixed-tender checkout with exact COGS, bounded refunds, cash over/short close — `PosRetailPostgreSqlTests`. Offline mode, receipt PDF/printing, card-settlement reconciliation, till-approval workflow remain open. |
| Stock / warehouse | **Partial** | Receive/transfer/sell/return, counts, reservations, quarantine, lot/expiry, moving-average valuation are the codebase's strongest area (pre-existing suites); serial-number tracking, QC holds and FIFO/lot costing are not implemented. |
| Payment / banking | **Partial** | Pay/receive with allocations and cash/bank journals; unapplied advances, internal transfers and bank-statement reconciliation are not implemented. |
| Purchase returns / credit notes | **Planned** | TS-08; refund-style bounded returns exist only on the POS side today. |
| Sales returns / credit notes | **Planned** | TS-13 residual; POS refunds are the only return path implemented. |
| Price lists, taxes on documents | **Planned** | Tax-rule configuration and deterministic amount calculation exist; effective-dated tax journals on sales/purchase documents are not wired. |
| Quotations, credit control | **Planned** | TS-11/TS-12 residuals; customer master with payment terms exists. |
| CRM, portals | **Planned** | TS-16 (#308–#311). |
| Assets / depreciation, expenses | **Planned** | TS-17 (#312–#315). |
| Projects, timesheets, SLA | **Planned** | TS-18 (#317–#322). |
| Manufacturing / MRP / quality | **Planned** | TS-19 (#324–#330). |
| HR, payroll | **Planned** | TS-20 (#332–#346; Frappe HR is a separate upstream app). |
| FX, intercompany, budgets, statutory packs | **Planned** | TS-21 (#348–#353). |
| Financial statements beyond trial balance | **Planned** | P&L, balance sheet, aging and hierarchy rollups derive from the immutable ledger but are not built. |
| Integrations / extensibility | **Partial** | Versioned REST, webhooks with durable delivery, imports/exports, forecasting exist; webhook dead-letter/signature hardening is tracked under TS-22 (#355–#361). |
| ERPNext extensibility (DocTypes, custom fields, report builder) | **Explicitly excluded** | Business-capability parity only; no Frappe platform compatibility by owner decision (review §11). |
| Website / e-commerce, subscriptions, helpdesk | **Explicitly excluded** | Not selected for scope. |

## Rules for changing a row

A row moves to **Supported** only when the capability has: company-scoped authorization, an
immutable posting path through the finance kernel where money moves, executable PostgreSQL
tests proving the golden-fixture invariants, and operator-usable API/UI. A row is **Partial**
while any declared gap above remains. No parity percentage is claimed anywhere in this
repository until an owner-pinned ERPNext tag comparison is complete (TS-23, #368).
