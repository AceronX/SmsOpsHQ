# XPD Sync Consolidation — Remove XPD_* Mirror Tables

## Metadata
- **Status:** Draft
- **Author(s):** SmsOpsHQ Dev
- **Created:** 2026-02-09
- **Updated:** 2026-02-09

---

## Overview

The current codebase has **two parallel customer models**: a `Customers` table (owned by SmsOps HQ for SMS messaging) and `XPD_Customers` (a mirror of the XPawn MS Access pawn-shop database). The same pattern repeats for tickets, items, and payments — each has an `XPD_*` prefix table that's a direct copy of the Access data.

This creates duplication, confusion, and extra complexity. Customer name, phone, address, and notes can exist in both places. Queries join across the two worlds via `Customers.CustomerKey → XPD_Customers.Key`, and many controllers use raw SQL against `XPD_*` tables directly.

**Goal:** Eliminate all `XPD_*` mirror tables. Sync from XPawn directly into our own clean schema. One customer table, one tickets table, one items table, one payments table, one phone index table — no prefix, no duplication.

---

## Goals

1. **Remove all `XPD_*` tables** — `XPD_Customers`, `XPD_Tickets`, `XPD_Items`, `XPD_PawnPayments`, `XPD_CustomerPhones`.
2. **Merge XPD customer data into `Customers`** — sync upserts by `CustomerKey` (the XPawn PK).
3. **Create clean `Tickets`, `Items`, `PawnPayments`, `CustomerPhones` tables** in our schema — sync writes here instead of `XPD_*`.
4. **Fix all consumers** — controllers, repositories, services, raw SQL queries, `IdentityResolver`, `ReminderService`, `TicketRepository`, `CustomersController`.
5. **Keep VBScript streaming unchanged** — the `stream_table.vbs` + `cscript.exe` pipeline stays the same; only the target tables change.
6. **Ensure sync button in Settings/Database tab works end-to-end.**

---

## Proposed Solution

### High-Level Approach

The sync pipeline already works: `cscript.exe` runs `stream_table.vbs`, which streams JSON rows from the XPawn Access DB. The `XpdSyncService` reads those rows and batch-upserts them. The **only change** is: instead of writing to `XPD_Customers`, `XPD_Tickets`, etc., we write to `Customers`, `Tickets`, `Items`, `PawnPayments`, and `CustomerPhones`.

For customers specifically, we merge XPawn data into the existing `Customers` table. A customer can already exist (created by SMS inbound) with a phone but no `CustomerKey`. During sync, if a customer's phone matches, we link them; otherwise we create new rows. For non-customer pawn data (tickets, items, payments), these are new clean tables replacing the `XPD_*` versions.

### Key Components

- **`Customers` table (enhanced):** Add XPawn-specific columns (`MiddleName`, `ResPhone`, `BusPhone`, `EMailAddress`, `DOB`, `SSN`, `IDNo`, `IDIssueState`, `FirstTransaction`, `LastTransaction`, `Warning`, `SyncedAt`). Keep all existing SMS columns (`PhoneE164`, `StoreId`, `TagsJson`, etc.). `CustomerKey` is the link to XPawn (nullable for SMS-only customers, populated for synced customers).
- **`Tickets` table (new, replaces `XPD_Tickets`):** Same columns as `XpdTicketEntity` but clean name. FK to `Customers` via `CustomerKey`.
- **`Items` table (new, replaces `XPD_Items`):** Same columns as `XpdItemEntity`. FK to `Tickets` via `TicketKey`.
- **`PawnPayments` table (new, replaces `XPD_PawnPayments`):** Same columns as `XpdPawnPaymentEntity`. FK to `Tickets` via `TicketKey`.
- **`CustomerPhones` table (new, replaces `XPD_CustomerPhones`):** Same structure. FK to `Customers` via `CustomerKey`.
- **`XpdSyncService` (updated):** Upserts into the new tables instead of `XPD_*`.
- **All consumers updated:** `CustomersController`, `TicketRepository`, `IdentityResolver`, `ReminderService`, `ReminderScheduler`, count helpers, etc.

### Architecture Diagram

```
  XPawn Access DB (.XPD file)
         │
         │  cscript.exe + stream_table.vbs
         │  (streams JSON per row via stdout)
         ▼
  ┌─────────────────────┐
  │   XpdSyncService    │
  │   (C# — unchanged   │
  │    streaming logic)  │
  └────────┬────────────┘
           │  INSERT OR REPLACE into:
           ▼
  ┌──────────────────────────────────┐
  │        SQLite (our DB)           │
  │                                  │
  │  Customers  ← merged (SMS+XPD)  │
  │  Tickets    ← was XPD_Tickets   │
  │  Items      ← was XPD_Items     │
  │  PawnPayments ← was XPD_PawnPay │
  │  CustomerPhones ← was XPD_CP    │
  │                                  │
  │  (Threads, Messages, Templates,  │
  │   etc. — unchanged)              │
  └──────────────────────────────────┘
```

---

## Design Considerations

### 1. Customer Merge Strategy

**Context:** Currently `Customers` is keyed by `CustomerId` (auto-increment) with optional `CustomerKey`. `XPD_Customers` is keyed by `Key` (XPawn PK). We need a single table.

**Options:**

- **Option A: Upsert by `CustomerKey` into `Customers`, keep `CustomerId` as PK**
  - Pros: Minimal change to existing SMS flows; `CustomerId` stays as the app-level PK; SMS-only customers (no XPawn match) keep working.
  - Cons: Sync must check if a `CustomerKey` already exists before insert.

- **Option B: Use `CustomerKey` (XPawn Key) as the sole PK**
  - Pros: Simplest sync (direct INSERT OR REPLACE by Key).
  - Cons: SMS-only customers have no XPawn Key; breaks existing `CustomerId`-based messaging flows.

**Recommendation:** **Option A.** Keep `CustomerId` as PK, keep `CustomerKey` as a unique nullable column. Sync does `INSERT OR REPLACE` matching on `CustomerKey`. SMS customers without an XPawn match keep `CustomerKey = NULL`. This is backward-compatible.

### 2. Table Naming

**Context:** Should the new tables be `Tickets` / `Items` / `PawnPayments` (clean) or keep `XPD_` prefix?

**Options:**

- **Option A: Clean names (`Tickets`, `Items`, `PawnPayments`, `CustomerPhones`)**
  - Pros: No confusion; these are "our" tables now. Professional.
  - Cons: More renames in code.

- **Option B: Keep `XPD_` prefix**
  - Pros: Less code change.
  - Cons: Defeats the purpose; still looks like "mirror" tables.

**Recommendation:** **Option A.** Clean names. The whole point is to own the data.

### 3. Entity Naming in C#

**Context:** Current entities are `XpdCustomerEntity`, `XpdTicketEntity`, etc. After removing XPD_ prefix from tables, should entities also be renamed?

**Options:**

- **Option A: Rename to `TicketEntity`, `ItemEntity`, `PawnPaymentEntity`, `CustomerPhoneEntity`**
  - Pros: Clean, consistent, no "Xpd" prefix confusion.
  - Cons: Touches many files.

- **Option B: Keep entity names as-is, just change `ToTable()` mapping**
  - Pros: Less code churn.
  - Cons: Misleading — entity says "Xpd" but table doesn't.

**Recommendation:** **Option A.** Rename entities. This is a one-time cleanup.

### 4. Customer Columns Merge

**Context:** `Customers` currently has: `PhoneE164`, `CellPhone`, `HomePhone`, `WorkPhone`, `FirstName`, `LastName`, `Address`, `City`, `State`, `Zip`, `SinceDate`, `TagsJson`, `Notes`. `XPD_Customers` has: `ResPhone`, `BusPhone`, `MiddleName`, `EMailAddress`, `DOB`, `SSN`, `IDNo`, `IDIssueState`, `FirstTransaction`, `LastTransaction`, `Warning`. We need to merge.

**Recommendation:** Add the XPD-only columns (`MiddleName`, `ResPhone`, `BusPhone`, `EMailAddress`, `DOB`, `SSN`, `IDNo`, `IDIssueState`, `FirstTransaction`, `LastTransaction`, `Warning`, `SyncedAt`) to the `Customers` table. During sync, these columns are populated. For SMS-only customers, they remain null.

---

## Lifecycle of Code for Key Use Case: "User clicks Sync in Settings"

1. **User fills in XPD path, MDW path, User, Password in Settings/Database tab and clicks "Run sync now".**
2. **Desktop → API:** `POST /api/sync/full` with `SyncRunOptions { XpdPath, MdwPath, XpdUser, XpdPassword }`.
3. **API (SyncController):** Validates HQ user, starts `XpdSyncService.FullSyncAsync()` in background.
4. **XpdSyncService.SyncCustomersAsync():**
   - Streams customers from XPawn via VBScript.
   - For each row: `INSERT OR REPLACE INTO Customers (...) WHERE CustomerKey = {key}` — upserts XPawn data into the Customers table, matching on `CustomerKey`.
5. **XpdSyncService.SyncTicketsAsync():**
   - Streams tickets → `INSERT OR REPLACE INTO Tickets (Key, CustomerKey, ...)`.
6. **XpdSyncService.SyncItemsAsync():**
   - Streams items → `INSERT OR REPLACE INTO Items (Key, TicketKey, ...)`.
7. **XpdSyncService.SyncPaymentsAsync():**
   - Streams payments → `INSERT OR REPLACE INTO PawnPayments (Key, TicketKey, ...)`.
8. **XpdSyncService.RebuildPhoneIndexAsync():**
   - Reads all customers with `ResPhone` / `BusPhone`, normalizes phones, inserts into `CustomerPhones`.
9. **Desktop polls** `GET /api/sync/progress` every 500ms, updates progress bar.
10. **Sync completes.** Desktop shows "Sync completed in Xs" with counts.

### Error Scenarios
- **VBScript fails to start:** `InvalidOperationException` → caught → `SyncResult.Error` → shown in UI.
- **Bad XPD credentials:** VBScript outputs `{"error": "..."}` → caught → error propagated.
- **Database write fails:** Exception caught → `SyncResult.Error` set → UI shows error.

---

## Detailed Design

### Schema Changes (EF Core Migration)

#### New columns on `Customers`:

```sql
ALTER TABLE Customers ADD COLUMN MiddleName TEXT;
ALTER TABLE Customers ADD COLUMN ResPhone TEXT;
ALTER TABLE Customers ADD COLUMN BusPhone TEXT;
ALTER TABLE Customers ADD COLUMN EMailAddress TEXT;
ALTER TABLE Customers ADD COLUMN DOB TEXT;
ALTER TABLE Customers ADD COLUMN SSN TEXT;
ALTER TABLE Customers ADD COLUMN IDNo TEXT;
ALTER TABLE Customers ADD COLUMN IDIssueState TEXT;
ALTER TABLE Customers ADD COLUMN FirstTransaction TEXT;
ALTER TABLE Customers ADD COLUMN LastTransaction TEXT;
ALTER TABLE Customers ADD COLUMN Warning TEXT;
ALTER TABLE Customers ADD COLUMN SyncedAt TEXT;
```

#### New table `Tickets` (replaces `XPD_Tickets`):

```sql
CREATE TABLE Tickets (
    Key           INTEGER PRIMARY KEY,
    CustomerKey   INTEGER NOT NULL,
    TransNo       INTEGER,
    Type          INTEGER,
    Active        INTEGER,
    Amount        REAL,
    CurrentBalance REAL,
    IssueDate     TEXT,
    DueDate       TEXT,
    DateClosed    TEXT,
    HowClosed     TEXT,
    Status        TEXT,
    Notes         TEXT,
    Item          TEXT,
    OperatorInitials TEXT,
    GunTicket     INTEGER,
    LostTicket    INTEGER,
    PaidTillDate  TEXT,
    LastDate      TEXT,
    ChargesDue    REAL,
    StandardCharges REAL,
    StandardPU    REAL,
    FullTermPU    REAL,
    FulltermRenew REAL,
    SyncedAt      TEXT
);
CREATE INDEX IX_Tickets_CustomerKey ON Tickets(CustomerKey);
CREATE INDEX IX_Tickets_Active ON Tickets(Active);
CREATE INDEX IX_Tickets_Type ON Tickets(Type);
CREATE INDEX IX_Tickets_DueDate ON Tickets(DueDate);
```

#### New table `Items` (replaces `XPD_Items`):

```sql
CREATE TABLE Items (
    Key           INTEGER PRIMARY KEY,
    TicketKey     INTEGER NOT NULL,
    PrintedDetail TEXT,
    CategoryCode  TEXT,
    SerialNo      TEXT,
    Cost          REAL,
    ItemStatus    TEXT,
    Notes         TEXT,
    Mfg           TEXT,
    Model         TEXT,
    Color         TEXT,
    Size          TEXT,
    Weight        TEXT,
    Karat         TEXT,
    SyncedAt      TEXT
);
CREATE INDEX IX_Items_TicketKey ON Items(TicketKey);
```

#### New table `PawnPayments` (replaces `XPD_PawnPayments`):

```sql
CREATE TABLE PawnPayments (
    Key               INTEGER PRIMARY KEY,
    TicketKey         INTEGER NOT NULL,
    PaymentDate       TEXT,
    PawnPmtType       INTEGER,
    PaymentStatus     TEXT,
    TotalDueAmount    REAL,
    NetDueAmount      REAL,
    NetPaymentAmount  REAL,
    Cash              REAL,
    Check_            REAL,
    CreditCard        REAL,
    DebitCard         REAL,
    InterestChargePaid REAL,
    ServiceChargePaid  REAL,
    PrincipalPaid     REAL,
    NewCurrentBalance REAL,
    NewDueDate        TEXT,
    OldDueDate        TEXT,
    OperatorInitials  TEXT,
    Method            TEXT,
    Note              TEXT,
    SyncedAt          TEXT
);
CREATE INDEX IX_PawnPayments_TicketKey ON PawnPayments(TicketKey);
CREATE INDEX IX_PawnPayments_PaymentDate ON PawnPayments(PaymentDate);
```

#### New table `CustomerPhones` (replaces `XPD_CustomerPhones`):

```sql
CREATE TABLE CustomerPhones (
    CustomerKey     INTEGER NOT NULL,
    PhoneNormalized TEXT NOT NULL,
    PhoneOriginal   TEXT,
    PhoneType       TEXT NOT NULL,
    PRIMARY KEY (CustomerKey, PhoneNormalized, PhoneType)
);
CREATE INDEX IX_CustomerPhones_PhoneNormalized ON CustomerPhones(PhoneNormalized);
```

#### Drop old tables:

```sql
DROP TABLE IF EXISTS XPD_Customers;
DROP TABLE IF EXISTS XPD_Tickets;
DROP TABLE IF EXISTS XPD_Items;
DROP TABLE IF EXISTS XPD_PawnPayments;
DROP TABLE IF EXISTS XPD_CustomerPhones;
```

### Entity Changes

| Old Entity                | New Entity              | Table           |
|---------------------------|-------------------------|-----------------|
| `CustomerEntity` (keep)   | `CustomerEntity` (add XPD columns) | `Customers` |
| `XpdCustomerEntity` (delete) | — merged into above — | — |
| `XpdTicketEntity` (delete)   | `TicketEntity` (new)    | `Tickets`       |
| `XpdItemEntity` (delete)     | `ItemEntity` (new)      | `Items`         |
| `XpdPawnPaymentEntity` (delete) | `PawnPaymentEntity` (new) | `PawnPayments` |
| `XpdCustomerPhoneEntity` (delete) | `CustomerPhoneEntity` (new) | `CustomerPhones` |

### Files to Change

| File | Change |
|------|--------|
| **Entities** | |
| `CustomerEntity.cs` | Add XPD columns (MiddleName, ResPhone, BusPhone, DOB, SSN, etc., SyncedAt) |
| `XpdCustomerEntity.cs` | **DELETE** |
| `XpdTicketEntity.cs` | Rename to `TicketEntity.cs`, remove `Xpd` prefix |
| `XpdItemEntity.cs` | Rename to `ItemEntity.cs`, remove `Xpd` prefix |
| `XpdPawnPaymentEntity.cs` | Rename to `PawnPaymentEntity.cs`, remove `Xpd` prefix |
| `XpdCustomerPhoneEntity.cs` | Rename to `CustomerPhoneEntity.cs`, remove `Xpd` prefix |
| **DbContext** | |
| `AppDbContext.cs` | Remove `XpdCustomers` DbSet. Add `Tickets`, `Items`, `PawnPayments`, `CustomerPhones` DbSets. Update all `Configure*` methods. Remove `ConfigureXpdCustomers`. Rename `ConfigureXpdTickets` → `ConfigureTickets`, etc. |
| **Sync** | |
| `XpdSyncService.cs` | `SyncCustomersAsync` → upsert into `Customers` by `CustomerKey`. Rename XPD_ table references in all SQL. Update entity types. |
| `IXpdSyncService.cs` | Update comments (remove "mirror table" language) |
| **Repositories** | |
| `TicketRepository.cs` | Change `FROM XPD_Tickets` → `FROM Tickets` |
| `CustomerRepository.cs` | No major change (already uses `Customers`) |
| **Services** | |
| `IdentityResolver.cs` | Change `XpdCustomerPhones` → `CustomerPhones` |
| `ReminderService.cs` | Change `XpdTickets` / `XpdCustomers` → `Tickets` / `Customers` joins |
| `ReminderScheduler.cs` | Same — update XPD references |
| **Controllers** | |
| `CustomersController.cs` | Remove raw SQL against `XPD_Customers`. Use `Customers` table for notes. Change `FROM XPD_Customers` → `FROM Customers`. Change `FROM XPD_Tickets` → `FROM Tickets`. |
| `TicketsController.cs` | Update if any XPD references |
| `SyncController.cs` | Update comments |
| **Desktop** | |
| `SettingsViewModel.cs` | No change needed (already works via API) |
| **Tests** | |
| Update test files that reference XPD entity names |
| **Migration** | |
| Create new EF Core migration for the schema change |

### API Endpoints (No Changes)

The sync API stays the same:

- `POST /api/sync/full` — trigger sync (unchanged)
- `GET /api/sync/progress` — poll progress (unchanged)
- `GET /api/sync/status` — get status (unchanged)
- `GET /api/sync/counts` — get table counts (update table names internally)
- `GET /api/sync/config` — get config (unchanged)

### Sync Service Logic (Updated)

#### Customer Sync (key change):

```csharp
// OLD: INSERT OR REPLACE INTO XPD_Customers (Key, LastName, ...)
// NEW: Upsert into Customers by CustomerKey

private async Task<int> SyncCustomersAsync(AppDbContext db, CancellationToken ct)
{
    string now = DateTime.UtcNow.ToString("o");
    int count = 0;

    await foreach (JsonElement row in StreamVbscriptAsync("customers", ct))
    {
        int xpawnKey = row.GetInt("key");

        // Upsert: if CustomerKey exists, update; otherwise insert
        string sql = @"
            INSERT INTO Customers (CustomerKey, LastName, FirstName, MiddleName,
                Address, City, State, Zip, ResPhone, BusPhone, EMailAddress,
                DOB, SSN, IDNo, IDIssueState, Notes, FirstTransaction,
                LastTransaction, Warning, SyncedAt, PhoneE164, StoreId, CreatedAt, UpdatedAt)
            VALUES ({0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18},{19},{20},{21},{22},{23})
            ON CONFLICT(CustomerKey) DO UPDATE SET
                LastName=excluded.LastName, FirstName=excluded.FirstName,
                MiddleName=excluded.MiddleName, Address=excluded.Address,
                City=excluded.City, State=excluded.State, Zip=excluded.Zip,
                ResPhone=excluded.ResPhone, BusPhone=excluded.BusPhone,
                EMailAddress=excluded.EMailAddress, DOB=excluded.DOB,
                SSN=excluded.SSN, IDNo=excluded.IDNo,
                IDIssueState=excluded.IDIssueState, Notes=excluded.Notes,
                FirstTransaction=excluded.FirstTransaction,
                LastTransaction=excluded.LastTransaction,
                Warning=excluded.Warning, SyncedAt=excluded.SyncedAt,
                UpdatedAt=excluded.UpdatedAt";

        // Execute...
        count++;
    }
    return count;
}
```

#### Ticket/Item/Payment Sync (simple rename):

```csharp
// OLD: INSERT OR REPLACE INTO XPD_Tickets ...
// NEW: INSERT OR REPLACE INTO Tickets ...
// (Same structure, just table name changes)
```

---

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| Customer merge conflict (SMS customer + XPD customer = same person, different rows) | Med | Med | Upsert by `CustomerKey`; SMS-only customers keep `CustomerKey=NULL`; unique index on `CustomerKey` prevents duplicates |
| Existing data loss during migration | High | Low | Migration copies data from `XPD_*` → new tables before dropping old tables |
| Consumers still reference old `XPD_` table names in raw SQL | High | Med | Grep all `.cs` files for "XPD_" — comprehensive search-and-replace |
| `stream_table.vbs` output format mismatch | Med | Low | VBScript is unchanged; JSON field names stay the same |
| Reminder system breaks if join tables change | High | Med | Test ReminderService + ReminderScheduler end-to-end after migration |

### Technical Debt Resolved
- Removes dual-customer confusion (Customers vs XPD_Customers)
- Removes all `XPD_` prefixed tables (5 tables deleted)
- Removes `XpdCustomerEntity` (merged into `CustomerEntity`)
- Eliminates raw SQL queries that cross between the two customer models

---

## Implementation Plan (Ordered Steps)

### Phase 1: Schema + Entities
1. Add XPD columns to `CustomerEntity.cs`
2. Rename `XpdTicketEntity` → `TicketEntity`, `XpdItemEntity` → `ItemEntity`, etc.
3. Delete `XpdCustomerEntity.cs`
4. Update `AppDbContext.cs` — new DbSets, new Configure methods, new table names
5. Create EF Core migration

### Phase 2: Sync Service
6. Update `XpdSyncService.cs` — all SQL targets new table names
7. Update `IXpdSyncService.cs` comments
8. Update count helpers (table name strings)

### Phase 3: Consumers
9. Update `TicketRepository.cs` — `FROM Tickets` instead of `FROM XPD_Tickets`
10. Update `IdentityResolver.cs` — `CustomerPhones` instead of `XPD_CustomerPhones`
11. Update `ReminderService.cs` — joins use `Tickets` / `Customers`
12. Update `ReminderScheduler.cs` — same
13. Update `CustomersController.cs` — all raw SQL uses new table names
14. Update `TicketsController.cs` if needed
15. Update `SyncController.cs` comments

### Phase 4: Cleanup
16. Remove old migration files if desired (optional)
17. Update tests
18. Verify sync end-to-end from Settings/Database tab

---

## Open Questions

1. Should `CustomerKey` have a UNIQUE index on `Customers`? (Recommended: yes, unique + nullable — SQLite allows multiple NULLs in unique indexes.)
2. Should we keep `StoreId` required on `Customers` for synced customers? (XPawn doesn't have a store concept — could default to store 1 or make it nullable for sync-only customers.)
3. Should the phone-index rebuild also index `PhoneE164` from the `Customers` table (in addition to `ResPhone`/`BusPhone`)?

---

## References
- `schema.json` — XPD database schema (Customers, Tickets, Items, PawnPayments)
- `XpdSyncService.cs` — current sync implementation
- `AppDbContext.cs` — current EF Core schema
- `CustomersController.cs` — primary consumer of both customer tables
