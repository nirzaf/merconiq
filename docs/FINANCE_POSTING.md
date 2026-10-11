# Finance posting kernel

**Status:** Implemented. Procurement, sales, payments, POS, fixed assets and expense claims post through
this kernel. Stock valuation records stay separate from journals and reconcile through the posting services.

The finance posting kernel turns Merconiq's chart-of-account configuration into an operational
general ledger. It commits immutable, balanced journals exactly once per business action, gated
by company fiscal periods, and answers statement and trial-balance reads from the posted ledger.

## What owns what

| Component | Location | Responsibility |
| --- | --- | --- |
| `JournalEntry`, `JournalLine` | `Merconiq.Core/Entities` | Posted, append-only general-ledger rows. The DbContext rejects updates and deletes; corrections are new journals, never edits. |
| `FiscalYear`, `FiscalPeriod` | `Merconiq.Core/Entities` | Company-owned posting-date gates. Closing is audited; reopening requires a recorded reason and a separate capability. Period 13 (`IsAdjustment`) is the year-end adjustment period. |
| `FiscalPeriodAccountLock` | `Merconiq.Core/Entities` | Per-period posting lock on one chart account. A locked account rejects every journal that touches it inside that period, reversals included. |
| `AccountPostingRole`, `CompanyAccountMapping` | `Merconiq.Core/Entities` | Named control-account roles mapped to company chart accounts by configuration. Roles: Inventory, GoodsReceivedNotInvoiced, AccountsPayable, AccountsReceivable, Bank, CashOnHand, CostOfGoodsSold, SalesRevenue, TaxPayable, Rounding, OpeningEquity, StockInTransit, CashOverShort, FixedAssets, AccumulatedDepreciation, DepreciationExpense, GainOnDisposal, LossOnDisposal, EmployeeExpensesPayable. |
| `JournalPostingRules` | `Merconiq.Core/Services` | Pure invariants: 2–500 lines, each line exactly one side positive, non-negative amounts already at the company currency scale, exact debit/credit equality, non-zero total, bounded source fields, and the deterministic request hash. |
| `JournalPostingService` | `Merconiq.Infrastructure/Services` | The posting transaction: validation → replay/conflict check → period gate (regular or adjustment) → account lock and account checks → journal number → commit. Also source-linked reversals and the ledger-backed trial balance. |
| `FiscalCalendarService` | `Merconiq.Infrastructure/Services` | Fiscal-year creation with monthly periods, close/reopen, the year-end adjustment period, and the open-period checks used inside the posting transaction. |
| `FiscalAccountLockService` | `Merconiq.Infrastructure/Services` | Applies and releases per-period account locks under the finance-period lock. |
| `AccountPostingPolicyService` | `Merconiq.Infrastructure/Services` | Resolves a role to an active, posting-capable account of the calling company. Posting adapters must use this instead of hard-coded or user-supplied control accounts. |
| `StatementRollupCalculator` | `Merconiq.Core/Services` | Pure hierarchy rollup for group accounts in the P&L and balance sheet. |

## Posting invariants

1. **Balanced and exact.** Sum of debits equals sum of credits exactly, at the company's declared
   currency scale (`Company.CurrencyScale`, default 2). Amounts must arrive pre-rounded; the kernel
   never rounds implicitly.
2. **Immutable.** Journal rows are append-only at the application layer (change-tracker guard) and
   carry account code/name snapshots so master-data edits never rewrite history.
3. **Exactly once per source action.** `(TenantId, CompanyId, SourceDocumentType, SourceDocumentId,
   SourceAction, SourceVersion)` is a database-unique key. A retry with the same payload replays the
   committed result (`Replayed = true`); a retry with a different payload is rejected with a conflict
   (the committed journal's `RequestHash` distinguishes the two). A request targeted at an adjustment
   period appends that target to its hash, so regular journals keep their committed hashes unchanged.
4. **Period-gated.** A journal dated on a day maps to the one regular open period that covers it. A
   journal explicitly targeted at the adjustment period must be dated on the fiscal year end and that
   period must be open. The gate runs inside the posting transaction under the same advisory-lock family
   (`finance-period:{companyId}`) as period close/reopen, so a close cannot race a posting.
5. **Company-scoped, unlocked accounts.** Every account must be company-owned, active, a posting
   (non-group) account, and not locked for the posting period. Cross-company, inactive or locked
   references fail before anything is written.
6. **Base currency only.** Journals post in the company's `BaseCurrency`.
7. **Atomic.** Journal, lines, audit rows and the document number allocation commit in one
   database transaction through the existing unit of work. A failure after the journal insert —
   including a failure in the audit phase — rolls back all effects.
8. **Reversals are journals.** `ReverseAsync` posts a new journal with swapped debit/credit lines
   that references the original (`ReversalOfJournalEntryId`) and carries a mandatory reason. A
   reversal inherits the original's adjustment-period target. Reversals cannot be reversed, and
   fixed-asset journals cannot be reversed generically (see below).
9. **Module journals key their replays.** Each adapter's journals use its own source document type, so
   a retry replays instead of posting twice. Fixed-asset and expense-claim journals are corrected through
   their own workflows, not through generic reversal.

## HTTP API

All routes are company-capability gated and versioned (`api/v1`):

| Endpoint | Capability | Behavior |
| --- | --- | --- |
| `GET /companies/{companyId}/fiscal-years` | `View` | Fiscal years with ordered periods, including `IsAdjustment`. |
| `POST /companies/{companyId}/fiscal-years` | `Administer` | Create a fiscal year and monthly periods; ranges may not overlap. |
| `POST /companies/{companyId}/fiscal-years/{fiscalYearId}/adjustment-period` | `Administer` | Create period 13 for the year; one per year, dated on the year end. |
| `POST /companies/{companyId}/fiscal-periods/{periodId}/close` | `Approve` | Close an open period. |
| `POST /companies/{companyId}/fiscal-periods/{periodId}/reopen` | `Administer` | Reopen with a mandatory reason (audited). |
| `GET /companies/{companyId}/fiscal-periods/{periodId}/account-locks` | `View` | Account lock rows for one period. |
| `POST /companies/{companyId}/fiscal-periods/{periodId}/account-locks` | `Approve` | Lock one posting account for the period with a reason. |
| `POST /companies/{companyId}/fiscal-periods/{periodId}/account-locks/{chartOfAccountId}/release` | `Administer` | Release a lock with a reason (audited). |
| `GET /companies/{companyId}/journals` | `View` | Paged journal headers, optional posting-date window. |
| `GET /companies/{companyId}/journals/{id}` | `View` | One journal with its lines. |
| `POST /companies/{companyId}/journals` | `Post` | Commit one journal for a named source action; optional `fiscalPeriodId` targets the adjustment period. Replays return `Replayed: true`. |
| `POST /companies/{companyId}/journals/{id}/reversals` | `Reverse` | Post one source-linked reversal. |
| `GET /companies/{companyId}/journals/trial-balance` | `View` | Ledger-backed trial balance; debits always equal credits. |
| `GET /companies/{companyId}/statements/profit-and-loss` | `View` | Income statement with group `rollups`. |
| `GET /companies/{companyId}/statements/balance-sheet` | `View` | Balance sheet with group `rollups`; must balance. |
| `GET/PUT /companies/{companyId}/account-posting-mappings` | `View` / `Administer` | Configure role-to-account mappings. |

Posting validation failures return `400` with the kernel's message; conflicting retries return
`409`.

## Journal numbers

Journal numbers come from the existing tenant-scoped document sequences with document type
`JournalEntry`, prefix `JV`, and period = posting year (for example `JV2026-000001`).

## Business documents wired to the ledger

| Document | Journal effect | Service |
| --- | --- | --- |
| Goods receipt (approved PO) | Dr Inventory / Cr GRNI, exact moving-average value booked to stock | `GoodsReceiptService` |
| Purchase invoice (matches receipt lines once) | Dr GRNI / Cr AP — GRNI clears to zero, payable recognized | `PurchaseInvoiceService` |
| Supplier payment (fully allocated) | Dr AP / Cr Bank or Cash | `PaymentService` |
| Customer receipt (fully allocated) | Dr Bank or Cash / Cr AR | `PaymentService` |
| Delivery (approved sales order) | Dr COGS / Cr Inventory at the exact issued moving-average value | `SalesService` |
| Sales invoice (matches delivery lines once) | Dr AR / Cr Sales revenue; blocked over a customer's credit limit unless a reviewer overrides it with a reason | `SalesService` |
| Supplier and sales credit notes | Dr AP / Cr Inventory; Dr Sales / Cr AR plus COGS reversal | `CreditNoteService` |
| POS checkout (open shift, mixed tenders) | Dr Cash/Bank per tender (card and bank transfer post to Bank) / Cr Sales revenue, plus exact COGS | `PosService` |
| POS refund (bounded by sold balance) | Dr Sales / Cr Cash, plus Dr Inventory / Cr COGS for restocked value | `PosService` |
| POS shift close variance | Dr or Cr Cash over/short against Cash on hand, once. A variance above the register's threshold is held and posts only when a reviewer approves it | `PosService`, `TillApprovalService` |
| Fixed asset capitalization | Dr Fixed assets at cost / Cr the chosen payment or payable account | `FixedAssetService` |
| Fixed asset monthly depreciation (per whole month, full-month convention) | Dr Depreciation expense / Cr Accumulated depreciation, keyed by asset and month so reruns replay | `FixedAssetService` |
| Fixed asset disposal | Dr proceeds, Dr accumulated depreciation, Dr loss / Cr cost, Cr gain; requires depreciation through the disposal date | `FixedAssetService` |
| Expense claim approval | Dr each expense account / Cr Employee expenses payable | `ExpenseClaimService` |
| Expense claim payment | Dr Employee expenses payable / Cr the chosen bank account | `ExpenseClaimService` |

Every document is immutable once posted, idempotent per `Idempotency-Key`/request key or source action,
gated by open fiscal periods and company base currency, and reserves account assignments through the
posting policy. Receipt, delivery, invoice and payment rows are append-only at the persistence layer.
Purchase-order approval, sales-order creation, CRM activity, and bank or card reconciliation never post.

## Reconciliation and reporting without postings

These read posted ledger lines or record reviewer decisions, and never write journals:

- **Bank statements:** a statement must foot before import; reviewers match statement lines to bank-account
  ledger lines one-to-one. The report shows adjusted bank and book balances and the difference.
- **Card settlements:** acquirer batches (gross, fee, net) are matched to card tenders, many-to-one, without
  exceeding the batch's gross. The report lists card tenders still awaiting settlement.
- **Statements:** P&L and balance sheet are projections; group rollups sum same-class leaves at any depth.
- **Receivables and payables aging:** open invoices less payment allocations, bucketed by days past due.
- **Customer credit exposure:** open invoices less allocations and posted sales credit notes.
- **POS receipts:** plain-text rendering of a committed sale, width-limited and control-character safe.

## v1 constraints (declared, tested, forward-compatible)

Whole-unit base stock with integral purchase factors; full-line invoice matching (partial billing = split
the receipt/delivery); payments fully allocated (advance-to-invoice allocation is a future increment).
Fixed-asset depreciation is straight-line by whole months only; impairment, revaluation and custody
transfers are not implemented. Expense claims carry the claimant's name, because no employee master exists.

## What remains outside the posted core

- No tax journals on sales or purchase documents; tax rules compute amounts only.
- No price lists, serial-number tracking, FIFO or lot costing, or quality holds beyond stock quarantine.
- No multi-currency journals, intercompany documents, budgets or statutory export packs.
- No POS offline mode.
- No separation-of-duties enforcement. Approvals use capability ceilings; the owner has not yet set SoD rules.
- No draft documents or document editing beyond the workflows listed above. Posted means posted.

## Acceptance evidence

PostgreSQL integration tests (opt-in via `RUN_POSTGRES_TESTS=true`) cover the kernel
(`FinancePostingPostgreSqlTests`), goods receipts (`GoodsReceiptPostgreSqlTests`), invoice/payment
(`PurchaseInvoicePaymentPostgreSqlTests`), order-to-cash (`SalesOrderToCashPostgreSqlTests`), period 13
(`AdjustmentPeriodPostgreSqlTests`), account locks (`FiscalAccountLocksPostgreSqlTests`), statement
rollups (`FinanceStatementRollupsPostgreSqlTests`), bank reconciliation (`BankReconciliationPostgreSqlTests`),
fixed assets (`FixedAssetPostgreSqlTests`), expense claims (`ExpenseClaimPostgreSqlTests`), CRM
(`CrmPipelinePostgreSqlTests`), and the POS receipt, card-settlement and till-approval cases in
`PosRetailPostgreSqlTests`. Pure invariants are covered in `tests/Merconiq.Tests/Core/Services/`, including
`JournalPostingRulesTests`, `JournalRequestHashCompatibilityTests`, `StatementRollupCalculatorTests`,
`FixedAssetDepreciationScheduleTests`, `BankReconciliationRulesTests`, `CardSettlementRulesTests`,
`CustomerCreditRulesTests`, `PosReceiptFormatterTests` and `ExpenseClaimRulesTests`.
The kernel suite covers balanced persistence with snapshots, unbalanced rejection with nothing persisted,
missing/closed-period rejection and authorized reopen, group/foreign-account rejection, idempotent replay,
conflicting-payload conflict, concurrent duplicate action producing exactly one journal, single source-linked
reversal with re-reversal blocked, independently calculated trial-balance fixture, append-only enforcement,
fault injection after the journal insert rolling back all effects, and tenant isolation.
