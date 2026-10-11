# Core ERP acceptance scenarios

**Status:** Canonical acceptance fixture for the posted financial core (per review finding F-06
and acceptance issue #304). Every scenario below maps to an executable PostgreSQL integration
test in `tests/Merconiq.Tests/Integration/` (opt-in via `RUN_POSTGRES_TESTS=true`) plus the pure
kernel tests in `tests/Merconiq.Tests/Core/Services/`. A scenario is **passed** only while its
test passes at the recorded SHA; rerun the suite before claiming it.

## Golden fixture — procure-to-pay and order-to-cash

Single company, no tax, base currency USD, whole-unit quantities. Independently calculated
expected values; each step's journal effect is asserted by the test suites.

| Step | Business action | Expected GL debit | Expected GL credit | Test |
|---|---|---:|---:|---|
| 1 | Opening balance (inventory 600 / equity 600; the kernel suite also covers cash/equity) | Inventory 600 | Opening Equity 600 | `SalesOrderToCashPostgreSqlTests`, `PosRetailPostgreSqlTests` |
| 2 | Approved PO delivers 60 × cost 10 | Inventory 600 | GRNI 600 | `GoodsReceiptPostgreSqlTests` |
| 3 | Supplier bill matches the receipt | GRNI 600 | AP 600 | `PurchaseInvoicePaymentPostgreSqlTests` |
| 4 | Pay supplier 400 | AP 400 | Bank 400 | `PurchaseInvoicePaymentPostgreSqlTests` |
| 5 | Deliver/sell 10 × cost 10 | COGS 100 | Inventory 100 | `SalesOrderToCashPostgreSqlTests` |
| 6 | Invoice customer 10 × price 15 | AR 150 | Sales 150 | `SalesOrderToCashPostgreSqlTests` |
| 7 | Receive customer payment 100 | Bank 100 | AR 100 | `SalesOrderToCashPostgreSqlTests` |

Expected closing positions asserted in the suites: stock 50 units / value 500; AP open 200;
AR open 50; GRNI 0; trial balance debits equal credits; inventory and GRNI net movements
reconcile to the stock ledger.

## Kernel invariants (every scenario must keep holding)

1. **Balanced and exact.** Debits equal credits at the company currency scale; unbalanced,
   out-of-scale, both-sides or zero-value lines are rejected and persist nothing.
2. **Immutable ledger.** Journal rows and all posted business documents are append-only;
   updates and deletes throw. Corrections are source-linked reversals/credit notes.
3. **Exactly once.** Every posting action is idempotent per source key or `Idempotency-Key`:
   identical retries replay the committed result; conflicting payloads are rejected with `409`.
4. **Concurrent single-posting.** Two workers racing the same source action produce exactly one
   effect; concurrent payments cannot over-allocate an open invoice.
5. **Period control.** Posting outside a fiscal period, or into a closed period, is rejected;
   an authorized reopen (recorded reason, audited) re-enables posting; close cannot race posting.
6. **Failure atomicity.** Fault injection after the journal insert (or after stock intake, before
   the journal) rolls back every effect together; the tenant stays healthy for a clean retry.
7. **Tenancy and company scope.** Cross-tenant reads are empty; cross-company accounts,
   invoices, locations and payments are rejected or indistinguishable from missing.
8. **Authorization.** Posting, reversal, close and reopen each require their own company
   capability (`Post`, `Reverse`, `Approve`, `Administer`); auditors read with `View`.

## Companion edge scenarios (all executable)

- Partial receipts 60 + 40 from an order of 100; a third receipt is rejected (no tolerance).
- Duplicate checkout/delivery/invoice/payment request keys replay without second effects.
- Same source action posted by two concurrent workers → one effect, one replay.
- Fault after stock movement and before the journal → neither persists.
- Invoice double-matching a receipt or delivery line → conflict.
- Supplier invoice number unique per company and supplier; sales invoice number unique per company.
- Payment/receipt over-allocation (sequential and concurrent) → rejected, no negative balances.
- POS: mixed cash/card tenders equal the sale total; card is a bank-mapped clearing tender;
  refunds are bounded by sold-minus-refunded; shift close posts counted-vs-expected variance to
  cash over/short exactly once; a closed shift cannot transact.
- Sales-order progress and PO obligations derive from posted lines, never mutable counters.
- Reversal of a journal posts swapped lines once and cannot itself be reversed.

## Operator and release qualification (open, non-automated)

The following remain open owner/reviewer gates and are **not** claimed by this document:

- Clean install, migrate/upgrade/restart/backup-restore rehearsal on a sanitized fixture.
- Browser/mobile accessibility review of the workspaces and authorized document exports.
- Throughput and concurrent post/close load evidence.
- Named accounting-policy and pilot reviewers signing off the chart, posting policies and
  supported-scope statement (`docs/ERPNEXT_PARITY.md`).
- Version-pinned capability-matrix review before any parity or release claim.
