# Finance posting kernel

**Status:** Implemented backend slice (G1 finance kernel). Procurement, sales, stock and POS
documents do **not** yet post through this kernel; wiring them in is the next delivery step.

The finance posting kernel turns Merconiq's chart-of-account configuration into an operational
general ledger. It commits immutable, balanced journals exactly once per business action, gated
by company fiscal periods, and answers trial-balance reads from the posted ledger.

## What owns what

| Component | Location | Responsibility |
| --- | --- | --- |
| `JournalEntry`, `JournalLine` | `Merconiq.Core/Entities` | Posted, append-only general-ledger rows. The DbContext rejects updates and deletes; corrections are new journals, never edits. |
| `FiscalYear`, `FiscalPeriod` | `Merconiq.Core/Entities` | Company-owned posting-date gates. Closing is audited; reopening requires a recorded reason and a separate capability. |
| `AccountPostingRole`, `CompanyAccountMapping` | `Merconiq.Core/Entities` | Named control-account roles (Inventory, GRNI, AP, AR, Bank, Cash, COGS, Sales, TaxPayable, Rounding, OpeningEquity, StockInTransit) mapped to company chart accounts by configuration. |
| `JournalPostingRules` | `Merconiq.Core/Services` | Pure invariants: 2–500 lines, each line exactly one side positive, non-negative amounts already at the company currency scale, exact debit/credit equality, non-zero total, bounded source fields, and the deterministic request hash. |
| `JournalPostingService` | `Merconiq.Infrastructure/Services` | The posting transaction: validation → replay/conflict check → period gate → account checks → journal number → commit. Also source-linked reversals and the ledger-backed trial balance. |
| `FiscalCalendarService` | `Merconiq.Infrastructure/Services` | Fiscal-year creation with monthly periods, close/reopen, and the open-period check used inside the posting transaction. |
| `AccountPostingPolicyService` | `Merconiq.Infrastructure/Services` | Resolves a role to an active, posting-capable account of the calling company. Posting adapters must use this instead of hard-coded or user-supplied control accounts. |
| API | `JournalsController`, `FiscalPeriodsController`, `AccountPostingMappingsController` | Company-capability-gated endpoints (see below). |

## Posting invariants

1. **Balanced and exact.** Sum of debits equals sum of credits exactly, at the company's declared
   currency scale (`Company.CurrencyScale`, default 2). Amounts must arrive pre-rounded; the kernel
   never rounds implicitly.
2. **Immutable.** Journal rows are append-only at the application layer (change-tracker guard) and
   carry account code/name snapshots so master-data edits never rewrite history.
3. **Exactly once per source action.** `(TenantId, CompanyId, SourceDocumentType, SourceDocumentId,
   SourceAction, SourceVersion)` is a database-unique key. A retry with the same payload replays the
   committed result (`Replayed = true`); a retry with a different payload is rejected with a conflict
   (the committed journal's `RequestHash` distinguishes the two).
4. **Period-gated.** The posting date must fall inside an open fiscal period. The gate runs inside
   the posting transaction under the same advisory-lock family (`finance-period:{companyId}`) as
   period close/reopen, so a close cannot race a posting.
5. **Company-scoped accounts.** Every account must be company-owned, active, and a posting
   (non-group) account. Cross-company or inactive references fail before anything is written.
6. **Base currency only.** Journals post in the company's `BaseCurrency`.
7. **Atomic.** Journal, lines, audit rows and the document number allocation commit in one
   database transaction through the existing unit of work. A failure after the journal insert —
   including a failure in the audit phase — rolls back all effects.
8. **Reversals are journals.** `ReverseAsync` posts a new journal with swapped debit/credit lines
   that references the original (`ReversalOfJournalEntryId`) and carries a mandatory reason.
   Reversals cannot be reversed; repeated reversal requests replay the first reversal.

## HTTP API

All routes are company-capability gated and versioned (`api/v1`):

| Endpoint | Capability | Behavior |
| --- | --- | --- |
| `GET /companies/{companyId}/fiscal-years` | `View` | Fiscal years with ordered periods. |
| `POST /companies/{companyId}/fiscal-years` | `Administer` | Create a fiscal year and monthly periods; ranges may not overlap. |
| `POST /companies/{companyId}/fiscal-periods/{periodId}/close` | `Approve` | Close an open period. |
| `POST /companies/{companyId}/fiscal-periods/{periodId}/reopen` | `Administer` | Reopen with a mandatory reason (audited). |
| `GET /companies/{companyId}/journals` | `View` | Paged journal headers, optional posting-date window. |
| `GET /companies/{companyId}/journals/{id}` | `View` | One journal with its lines. |
| `POST /companies/{companyId}/journals` | `Post` | Commit one journal for a named source action; replays return `Replayed: true`. |
| `POST /companies/{companyId}/journals/{id}/reversals` | `Reverse` | Post one source-linked reversal. |
| `GET /companies/{companyId}/journals/trial-balance` | `View` | Ledger-backed trial balance; debits always equal credits. |
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
| Sales invoice (matches delivery lines once) | Dr AR / Cr Sales revenue | `SalesService` |

Every document is immutable once posted, idempotent per `Idempotency-Key`/request key, gated by
open fiscal periods and company base currency, and reserves account assignments through the
posting policy. Receipt, delivery, invoice and payment rows are append-only at the persistence
layer. Purchase-order approval and sales-order creation never post.

v1 constraints (declared, tested, forward-compatible): whole-unit base stock with integral
purchase factors; full-line invoice matching (partial billing = split the receipt/delivery);
payments fully allocated (advances and internal transfers are future increments); no tax
journals, credit notes, returns or POS yet.

## What remains outside the posted core

- No P&L, balance sheet, account-hierarchy rollup or multi-currency statements — only the
  ledger-backed trial balance.
- No procurement returns/rejections to stock (TS-08), credit notes, or sales returns.
- No quotation stage, credit-control, reservation-backed promising, or POS.
- No draft documents, approval workflows, or document editing. Posted means posted.
- No chart taxonomy or default accounts: companies still supply their own codes and mappings.

## Acceptance evidence

PostgreSQL integration tests (opt-in via `RUN_POSTGRES_TESTS=true`) cover the kernel
(`FinancePostingPostgreSqlTests`), goods receipts (`GoodsReceiptPostgreSqlTests`), invoice/payment
(`PurchaseInvoicePaymentPostgreSqlTests`) and order-to-cash (`SalesOrderToCashPostgreSqlTests`).
The kernel suite covers: balanced persistence with snapshots, unbalanced
rejection with nothing persisted, missing/closed-period rejection and authorized reopen,
group/foreign-account rejection, idempotent replay, conflicting-payload conflict, concurrent
duplicate action producing exactly one journal, single source-linked reversal with re-reversal
blocked, independently calculated trial-balance fixture, append-only enforcement, fault injection
after the journal insert rolling back all effects, and tenant isolation.
`tests/Merconiq.Tests/Core/Services/JournalPostingRulesTests.cs` covers the pure invariants and
request-hash semantics.
