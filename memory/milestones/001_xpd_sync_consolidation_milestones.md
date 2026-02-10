# XPD Sync Consolidation — Milestones

**Design doc:** `memory/design/001_xpd_sync_consolidation.md`

---

## Milestone 1: Rename Pawn Entities (No Schema Change Yet)

Rename the C# entity classes and DbSet names only. Keep the old `XPD_*` table names via `ToTable()` so the app still works with the existing DB. This is a safe, compile-only refactor.

### Files to change:
- [x] **Rename `XpdTicketEntity` → `TicketEntity`** — rename file `XpdTicketEntity.cs` → `TicketEntity.cs`, rename class, update comment
- [x] **Rename `XpdItemEntity` → `ItemEntity`** — rename file `XpdItemEntity.cs` → `ItemEntity.cs`, rename class, update comment
- [x] **Rename `XpdPawnPaymentEntity` → `PawnPaymentEntity`** — rename file `XpdPawnPaymentEntity.cs` → `PawnPaymentEntity.cs`, rename class, update comment
- [x] **Rename `XpdCustomerPhoneEntity` → `CustomerPhoneEntity`** — rename file `XpdCustomerPhoneEntity.cs` → `CustomerPhoneEntity.cs`, rename class, update comment
- [x] **Update `AppDbContext.cs`** — change DbSet types and property names: `XpdTickets` → `Tickets`, `XpdItems` → `Items`, `XpdPawnPayments` → `PawnPayments`, `XpdCustomerPhones` → `CustomerPhones`. Keep `ToTable("XPD_Tickets")` etc. for now. Rename Configure methods.
- [x] **Update `XpdSyncService.cs`** — replace all entity type references (`XpdTicketEntity` → `TicketEntity`, etc.) and DbSet references (`db.XpdCustomers` → removed later, `db.XpdCustomerPhones` → `db.CustomerPhones`, etc.)
- [x] **Update `IdentityResolver.cs`** — `XpdCustomerPhones` → `CustomerPhones`, `XpdCustomerPhoneEntity` → `CustomerPhoneEntity`
- [x] **Update `ReminderService.cs`** — `XpdTickets` → `Tickets`, `XpdCustomers` → keep for now (deleted in M2)
- [x] **Update `ReminderScheduler.cs`** — same as above
- [x] **Update `IdentityResolverTests.cs`** — `XpdCustomerPhoneEntity` → `CustomerPhoneEntity`, `XpdCustomerPhones` → `CustomerPhones`

### Verify:
- Project compiles with no errors (`dotnet build`)
- Existing tests pass (`dotnet test`)
- No runtime change — same DB tables, same behavior

---

## Milestone 2: Merge XPD Customer Columns into `CustomerEntity`

Add all XPD-only columns to `CustomerEntity` so we can later sync customers directly into the `Customers` table. Delete `XpdCustomerEntity`. Update consumers.

### Files to change:
- [x] **`CustomerEntity.cs`** — Add properties: `MiddleName`, `ResPhone`, `BusPhone`, `EMailAddress`, `DOB` (string), `SSN`, `IDNo`, `IDIssueState`, `FirstTransaction` (string), `LastTransaction` (string), `Warning`, `SyncedAt` (string)
- [x] **`Customer.cs` (Core entity)** — Add matching properties, update comment (remove "XPD_Customers.Key" reference)
- [x] **`CustomerRepository.cs`** — Update `MapToDomain()` to include new fields
- [x] **Delete `XpdCustomerEntity.cs`**
- [x] **`AppDbContext.cs`** — Remove `DbSet<XpdCustomerEntity> XpdCustomers`. Remove `ConfigureXpdCustomers()`. Add new column mappings in `ConfigureCustomers()`. Make `CustomerKey` unique index (SQLite allows multiple NULLs).
- [x] **Update `XpdSyncService.cs`** — remove references to `XpdCustomerEntity` and `db.XpdCustomers`. The `SyncCustomersAsync` method will temporarily be stubbed (actual SQL update in M4). `RebuildPhoneIndexAsync` — query `db.Customers` instead of `db.XpdCustomers`.
- [x] **Update `ReminderService.cs`** — change `db.XpdCustomers` joins to `db.Customers` (join on `c.CustomerKey == t.CustomerKey`)
- [x] **Update `ReminderScheduler.cs`** — same join change

### Verify:
- [x] Project compiles
- [x] Tests pass
- [x] No `XpdCustomerEntity` references remain in non-migration code

---

## Milestone 3: EF Core Migration — New Schema

Create the EF Core migration that:
1. Adds new columns to `Customers`
2. Renames `XPD_Tickets` → `Tickets`, `XPD_Items` → `Items`, `XPD_PawnPayments` → `PawnPayments`, `XPD_CustomerPhones` → `CustomerPhones`
3. Copies data from `XPD_Customers` into `Customers` (for existing DBs)
4. Drops `XPD_Customers`

### Steps:
- [x] **`AppDbContext.cs`** — Change `ToTable()` calls: `"XPD_Tickets"` → `"Tickets"`, `"XPD_Items"` → `"Items"`, `"XPD_PawnPayments"` → `"PawnPayments"`, `"XPD_CustomerPhones"` → `"CustomerPhones"`
- [x] **Generate migrations** — Split into three for safety:
  1. `AlignXpdSchemaColumns` — column renames (Brand→Mfg, Metal→Karat) + new indexes
  2. `MergeXpdCustomerColumns` — add XPawn columns to Customers, migrate data from XPD_Customers, drop XPD_Customers, unique CustomerKey index
  3. `ConsolidateXpdTables` — rename XPD_Tickets→Tickets, XPD_Items→Items, XPD_PawnPayments→PawnPayments, XPD_CustomerPhones→CustomerPhones
- [x] **Edit migration manually** — Fixed data migration SQL (column name `Email` → `EMailAddress` in MergeXpdCustomerColumns to match DB state after AlignXpdSchemaColumns)
- [x] **Run migration** — `dotnet ef database update` applied successfully

### Verify:
- [x] Migration applies cleanly on existing DB
- [x] SQLite DB has: `Customers` (with new columns), `Tickets`, `Items`, `PawnPayments`, `CustomerPhones`
- [x] No `XPD_*` tables remain
- [x] App starts without errors (verified: health endpoint returns OK)
- [x] All 383 tests pass

---

## Milestone 4: Update Sync Service — Write to New Tables

Update `XpdSyncService` to write to the new table names. The customer sync now upserts into `Customers` by `CustomerKey`.

### Files to change:
- [x] **`XpdSyncService.cs` — `SyncCustomersAsync()`** — implemented upsert via `INSERT INTO Customers (...) ON CONFLICT(CustomerKey) DO UPDATE SET ...`. Preserves SMS-originated fields (PhoneE164, CellPhone, TagsJson) on conflict.
- [x] **`XpdSyncService.cs` — `SyncTicketsAsync()`** — changed `XPD_Tickets` → `Tickets` in SQL
- [x] **`XpdSyncService.cs` — `SyncItemsAsync()`** — changed `XPD_Items` → `Items` in SQL
- [x] **`XpdSyncService.cs` — `SyncPaymentsAsync()`** — changed `XPD_PawnPayments` → `PawnPayments` in SQL
- [x] **`XpdSyncService.cs` — `RebuildPhoneIndexAsync()`** — changed `DELETE FROM XPD_CustomerPhones` → `DELETE FROM CustomerPhones`, changed `INSERT OR IGNORE INTO XPD_CustomerPhones` → `INSERT OR IGNORE INTO CustomerPhones`
- [x] **`XpdSyncService.cs` — Count helpers** — changed table name strings: `"XPD_Tickets"` → `"Tickets"`, `"XPD_Items"` → `"Items"`, `"XPD_PawnPayments"` → `"PawnPayments"`, `"XPD_CustomerPhones"` → `"CustomerPhones"`
- [x] **`IXpdSyncService.cs`** — updated comments (removed "mirror table" language)
- [x] Also fixed: removed duplicate `_lastError = null;` line, updated class-level comment

### Verify:
- [x] Project compiles (0 errors, 0 warnings)
- [x] All 383 tests pass
- [x] App starts without errors
- [ ] **Manual test:** Run sync from **Settings > Database tab** with valid XPD credentials
- [ ] **Manual test:** Confirm customers appear in `Customers` table (with `CustomerKey` set, `SyncedAt` populated)
- [ ] **Manual test:** Confirm tickets, items, payments appear in `Tickets`, `Items`, `PawnPayments`
- [ ] **Manual test:** Confirm phone index rebuilt in `CustomerPhones`
- [ ] **Manual test:** Progress bar updates and shows "Sync completed in Xs"

---

## Milestone 5: Update All Consumers — Controllers & Repositories

Fix every raw SQL query and LINQ join that still references `XPD_*` table names.

### Files to change:
- [x] **`CustomersController.cs`** — replaced all `XPD_Tickets` → `Tickets`, `XPD_Items` → `Items`, `XPD_PawnPayments` → `PawnPayments` in raw SQL (late customers query, PFX customers query, connectivity test CountTable calls, item descriptions query). Updated the `AllowedTableNames` string array from `"XPD_Tickets", "XPD_Items", "XPD_PawnPayments"` → `"Tickets", "Items", "PawnPayments"`. Also updated log messages and comments to remove "XPD" prefix.
- [x] **`TicketRepository.cs`** — changed `FROM XPD_Tickets` → `FROM Tickets` in both `GetByCustomerKeysAsync` and `GetByKeyAsync` queries. Updated class-level comment and catch block comments.
- [x] **`TicketsController.cs`** — updated comment `// Load ticket from XPD_Tickets` → `// Load ticket from Tickets table`
- [x] **`SyncController.cs`** — no `XPD_` references found; no changes needed.

### Verify:
- [x] Project compiles (0 warnings, 0 errors)
- [x] All 383 unit tests pass
- [ ] **Manual test via Desktop app:**
  - [ ] Open a customer context (click a customer) — tickets and notes load correctly
  - [ ] Late customers list loads
  - [ ] PFX customers list loads
  - [ ] Search for a customer by phone — XPD data appears
  - [ ] Test DB connection button works (shows correct counts)
- [ ] All API endpoints return correct data

---

## Milestone 6: Update Core Interfaces, Comments, and Remaining References

Clean up all remaining "XPD" references in comments, interface docs, and core entities.

### Files to change:
- [x] **`IIdentityResolver.cs`** — updated comments: "XPD customer identities" → "pawn customer identities", "XPD_CustomerPhones" → "CustomerPhones"
- [x] **`IdentityResolver.cs`** — updated class comment: "XPD_CustomerPhones index table" → "CustomerPhones index table"; method comment same
- [x] **`Ticket.cs` (Core entity)** — updated comments: "XPD mirror data" → "synced from XPawn", "XPD primary key" → "Pawn system primary key", "FK to XPD customer" → "FK to Customer", "strings from XPD" → "strings from XPawn"
- [x] **`Customer.cs` (Core entity)** — no `XPD_` references found (already clean)
- [x] **`PhoneUtils.cs`** — updated comment: "XPD_CustomerPhones.PhoneNormalized" → "CustomerPhones.PhoneNormalized"
- [x] **`AppDbContext.cs`** — updated comment: "XPD_Customers has been consolidated" → "XPawn customer data has been consolidated"
- [x] **`IXpdSyncService.cs`** — already clean from M4 (verified: zero `XPD_` matches)

### Verify:
- [x] `XPD_` search across all .cs files outside Migrations returns **zero results**
- [x] Project compiles (0 errors)
- [x] All 383 tests pass

---

## Milestone 7: Update Tests

Fix all test files that reference old entity names or XPD table names.

### Files checked:
- [x] **`IdentityResolverTests.cs`** — already done in M1 (entity rename), verified: zero `XPD_` refs
- [x] **All test files** — searched `SmsOpsHQ.Tests/` for `XPD_`, `XpdCustomerEntity`, `XpdTicketEntity`, `XpdItemEntity`, `XpdPawnPaymentEntity`, `XpdCustomerPhoneEntity` — **zero matches**
- [x] Remaining `Xpd` references (e.g. `XpdConcurrencyLimiter`, `IsXpdEnabled`) are intentional class/property names for the XPawn Desktop feature, not old table names — no changes needed

### Verify:
- [x] `dotnet test` — all 383 tests pass
- [x] `XPD_` search in all .cs files outside Migrations → zero results
- [x] Old entity name search outside Migrations → zero results (only in historical Migration Designer snapshots)

---

## Milestone 8: End-to-End Verification

Full manual test of the complete sync workflow and all dependent features.

### Test checklist:
- [ ] **Settings > Database tab:** Fill in XPD path, MDW path, user, password
- [ ] **Click "Run sync now"** — progress bar updates, completes successfully
- [ ] **Click "Test DB Connection"** — shows correct counts (Customers, Tickets, Items, Payments)
- [ ] **Sync status** — "Last sync" time updates, counts are accurate
- [ ] **Customer search** — finds customers by name and phone
- [ ] **Customer context** — clicking a customer shows tickets (active + closed), notes, balance
- [ ] **Late customers list** — shows customers with overdue tickets
- [ ] **PFX customers list** — shows forfeited ticket customers
- [ ] **Phone lookup** — looking up a customer by phone returns pawn data
- [ ] **Append note** — adding a note to a customer persists correctly
- [ ] **Reminders** — scheduler can load tickets and send reminders (if configured)
- [ ] **Identity resolution** — inbound SMS resolves to correct customer via phone index
- [ ] **Run sync a second time** — upserts correctly (no duplicates, updated data)

### Notes:
M8 is a manual testing milestone. All automated checks (build, unit tests, `XPD_` sweep) have passed. The checklist above should be worked through using the Desktop app and API endpoints to confirm end-to-end correctness.

---

## Summary

| # | Milestone | Scope | Risk |
|---|-----------|-------|------|
| 1 | Rename pawn entities (C# only) | Entity + DbSet renames, keep old table names | Low — compile-only |
| 2 | Merge XPD customer into CustomerEntity | Delete XpdCustomerEntity, add columns, update joins | Med — consumer changes |
| 3 | EF Core migration — new schema | Table renames, data migration, drop XPD_Customers | Med — schema change |
| 4 | Update sync service | Rewrite SQL targets in XpdSyncService | Med — sync correctness |
| 5 | Update controllers & repositories | Fix all raw SQL in CustomersController, TicketRepository | Med — many raw SQL strings |
| 6 | Clean up comments & interfaces | Remove all "XPD_" from comments/docs | Low — text only |
| 7 | Update tests | Fix test references | Low — test code only |
| 8 | End-to-end verification | Manual testing of full workflow | — |
