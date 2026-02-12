# Inbox Panel Implementation Plan

## Metadata
- **Status:** In progress (Phase A complete)
- **Created:** 2026-02-11
- **Goal:** Complete the Inbox panel in SmsOpsHQ (C#) to match Python app logic/workflow and Google Voice–style UI.
- **Phase A (done):** Backend search + GetInboxWithCustomersAsync (single-query with customer, no N+1).

---

## 1. Where things stand

### Python app (xpawndata) — reference

- **API:** `GET /api/threads/inbox` (or `/api/inbox`) with `store_id`, `filter` (open/unread/assigned_to_me), optional `search`.
- **Repo:** `threads_repo.get_inbox()` joins `Thread` with `Customer` (so only threads with a customer appear), filters by store/filter, orders by `LastMessageAt` DESC, limit 50. Search is TODO (not implemented).
- **Response shape:** Array of:
  - `thread_id`, `unread_count`, `status`, `last_message_at`
  - `customer`: `id`, `phone`, `name`, `first_name`, `last_name`
  - `last_message`: `body`, `direction`, `created_at`, etc.
- **UI (inbox_widget.py):**
  - Search bar “Search PawnOps”, + New, Trash.
  - List item: **Avatar** (initial letter), **Row1:** Name as “Last, First” + time (right), **Row2:** Phone + unread badge. Compact height (~50px).
  - Time: today = `HH:mm`, this week = `Day HH:mm`, else `mm/dd HH:mm` (Eastern).

### C# SmsOpsHQ — current

- **API:** `GET /api/inbox?store_id=&filter=&search=&twilio_number_id=` already exists. Builds response with `thread_id`, `customer` (id, phone, name, first_name, last_name), `last_message`, `last_message_at`, `unread_count`, `status`. **Customer is loaded per thread (N+1).** Search is **not** applied in the repository.
- **Repo:** `ThreadRepository.GetInboxAsync()` filters by store, filter (all/open/unread/closed), optional twilio number. **No join with Customer, no search.**
- **Desktop:**
  - **InboxViewModel:** Maps API to `InboxThreadItem` (CustomerName, CustomerPhone, LastMessageBody, LastMessageTime, UnreadCount, Status, CustomerId). Uses single `name` from API; no “Last, First” or FirstName/LastName on the item.
  - **InboxView.xaml:** List with item template: CustomerName + unread badge, timestamp, message preview. **No avatar, no phone row, no “Last, First”.** Layout is not Google Voice style.

### DB

- SQLite DB used by the API is under **SmsOpsHQ.Api** (e.g. `smsops.db`). Queries run via EF Core in `ThreadRepository` and `ThreadsController`; no change to DB path needed for this work.

---

## 2. What “complete Inbox panel” means

1. **Same workflow as Python:** Load inbox with filter (All / Open / Unread / Closed) and optional search; show one row per conversation with **customer details** (name, phone) and last message info; click row opens thread view.
2. **All customer detail in the list:** Each row shows: customer name (Last, First), phone, last message time, unread count, and optionally last message preview — i.e. “all” detail needed for the list, not full customer profile.
3. **Style similar to Google Voice:** Left-aligned avatar (circle with initial), two-line block: line 1 = name + time (right), line 2 = phone (+ optional preview) + unread badge. Clean, compact list.
4. **Query behavior aligned with Python:** Support search by customer name/phone in the backend (Python has a TODO; we implement it in C#). Optionally optimize inbox query to include customer in one go (avoid N+1).

---

## 3. Implementation plan (ordered)

### Phase A — Backend (API + repository)

| Step | Task | Details |
|------|------|--------|
| A1 | **Search in GetInboxAsync** | In `ThreadRepository.GetInboxAsync`, when `search` is not null/empty, filter threads by customer name or phone. Requires join with `Customer` (or two-step: get matching customer IDs then filter threads). Prefer single query with `Include`/join so one DB round-trip. |
| A2 | **Optional: include Customer in inbox query** | Add EF `Include` of Customer (or a dedicated query that returns threads with customer in one go) so `ThreadsController.GetInbox` does not do N+1 `GetByIdAsync` for customers. Improves performance. |

Reference: Python only joins Thread+Customer and does not implement search yet; we add search and keep same filter semantics (open/unread/closed/all).

### Phase B — Desktop ViewModel (data shape + formatting)

| Step | Task | Details |
|------|------|--------|
| B1 | **Extend InboxThreadItem** | Add `FirstName`, `LastName` (from API `customer.first_name` / `customer.last_name`). Add computed or setter for **display name** “Last, First” (or “First Last” if only one). Keep CustomerName for backward compatibility or replace with display name. |
| B2 | **Time formatting** | Format `LastMessageTime` like Python: **today** → time only (e.g. “3:45 PM”); **this week** → “Wed 3:45 PM”; **older** → “02/10 3:45 PM”. Use local time. Can be done in ViewModel when mapping API response. |
| B3 | **Parse API in LoadInboxAsync** | When building `InboxThreadItem`, set `FirstName`, `LastName` from `customer`; set display name (e.g. new property `DisplayName` = “Last, First”). Use formatted time from B2. |

### Phase C — Desktop View (Google Voice style UI)

| Step | Task | Details |
|------|------|--------|
| C1 | **List item template** | Redesign `ListBox.ItemTemplate`: **Left:** Circle avatar (e.g. 36–40px) with first letter of name/phone (“Last” or “First” or phone digit). **Right:** Two rows: (1) **DisplayName** (Last, First) + **LastMessageTime** right-aligned; (2) **CustomerPhone** + unread **badge** (right). Optional: third line or truncate for **LastMessageBody** preview. Match Python compact height (~52–56px). |
| C2 | **Item styling** | Hover state (e.g. light background), cursor Hand, thin bottom border between items. Use existing `Surface`/`Bg`/`Border`/`Primary` resources. Unread row: slightly bolder name if desired. |
| C3 | **Header** | Keep “Inbox” title and **Search**; keep filter pills (All, Open, Unread, Closed). Optionally add **Trash** (Delete all conversations) that calls `DELETE /api/conversations?store_id=` and refreshes list (with confirmation). “+ New” is already in sidebar as Compose. |

### Phase D — Polish and parity

| Step | Task | Details |
|------|------|--------|
| D1 | **Search box** | Ensure Search triggers reload (already bound to SearchCommand / Enter). Backend search (A1) must be used when SearchText is not empty. |
| D2 | **Empty / loading / error** | Keep existing loading overlay and error bar. Optional: empty state message when no threads. |
| D3 | **Tests** | Run existing `ThreadsIntegrationTests` and `ThreadRepositoryTests`; add or adjust tests for search if needed. |

---

## 4. File checklist

| Layer | File | Changes |
|-------|------|--------|
| Core | `IThreadRepository.cs` | No signature change if search already in interface; else add `search` usage note. |
| Infra | `ThreadRepository.cs` | Implement search (join Customer, filter by name/phone); optional Include Customer. |
| Api | `ThreadsController.cs` | Pass `search` to repo; optionally use included Customer to avoid N+1. |
| Desktop | `InboxViewModel.cs` | InboxThreadItem: FirstName, LastName, DisplayName; format time; parse API accordingly. |
| Desktop | `InboxView.xaml` | New item template: avatar + two-line block (name+time, phone+badge); optional Trash. |
| Desktop | `InboxView.xaml.cs` | No change unless Trash confirmation dialog. |

---

## 5. Where to start (recommended order)

1. **Backend search + optional Include** (Phase A) — so desktop can use search and API stays fast.
2. **ViewModel** (Phase B) — add FirstName/LastName/DisplayName and time formatting so the view has the right data.
3. **View** (Phase C) — Google Voice style template and header.
4. **Polish** (Phase D) — Trash, empty state, tests.

You can start with **Phase B + C** (UI and ViewModel only) and leave search as “no filter” until Phase A is done; the list will still show “all customer detail” and look like Google Voice.

---

## 6. References

- Python inbox UI: `xpawndata/Productionssms/app/desktop/desktop/client/ui/inbox_widget.py` (InboxItemWidget, load_inbox).
- Python API: `xpawndata/Productionssms/app/server/server/app/api/routes_threads.py` (get_inbox), `app/db/repos/threads_repo.py` (get_inbox).
- C# API: `SmsOpsHQ.Api/Controllers/ThreadsController.cs` (GetInbox).
- C# Repo: `SmsOpsHQ.Infrastructure/Repositories/ThreadRepository.cs` (GetInboxAsync).
- C# Desktop: `SmsOpsHQ.Desktop/ViewModels/InboxViewModel.cs`, `Views/InboxView.xaml`.
