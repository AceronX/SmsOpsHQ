# Inbox + Reminders — Implementation Plan (C# SmsOpsHQ)

**Version:** 2.0  
**Date:** 2026-02-13  
**Goal:** Keep **Inbox** for conversations only (All, Open, Unread, **Closed**). Add **Reminders** as a **separate sidebar item** below Inbox with its own screen. Align UI with Google Voice–style and ensure high performance.

---

## 1. Scope and Constraints

### 1.1 In Scope
- **Inbox panel:** Four tabs — **All**, **Open**, **Unread**, **Closed**. Conversations only (threads). No Reminders tab.
- **Sidebar:** Add **Reminders** as a new nav item **below Inbox**. Clicking it opens a dedicated Reminders screen.
- **Reminders screen:** List of sent reminders (from `GET /api/reminders/sent`). Google Voice–style rows. Clicking a row opens reminder detail + customer context. No time-based sub-filters (Due Today, 7 Days, etc.) for now.
- **UI:** Google Voice–style (avatar, “Last, First” or customer name, phone, time), clean and easy to manage.
- **Performance:** Single-query patterns, no N+1, fast desktop response.

### 1.2 Out of Scope (Later)
- Time-based reminder filtering (Due Today, 7 Days, 10 Days, 14 Days).
- Trash / delete conversations (can add later with confirmation).

### 1.3 Context Panel (Right Panel) — Same in Inbox and Reminders
When the user clicks a **customer** (from Inbox = conversation row, or from Reminders = reminder row), the **right panel** must show the **Context Panel (Customer Info)** with the same content in both flows:

| Display | Description |
|--------|--------------|
| **Customer name** | Full name (e.g. “Last, First”) |
| **Phone** | Customer phone number |
| **Active tickets** | List/grid of active pawn tickets (trans #, amount, balance, due date, items) |
| **Items** | Item-level info as needed |
| **Notes** | Customer notes (and ticket/item notes if applicable) |
| **Payment history** | Late rate, risk level, history summary |
| **etc.** | Any other customer context (address, all-time tickets, etc.) |

- **Inbox → click thread:** Main area = Thread view (messages + compose); **right panel = Context Panel** (customer info above). Already implemented via `ThreadView` + `CustomerPanelViewModel` / `CustomerPanelView`.
- **Reminders → click reminder:** Main area = Reminder detail (reminder message card + Back); **right panel = Context Panel** (same customer info). Implement via `ReminderDetailView` reusing **CustomerPanelView** (load customer by phone when needed: `GetCustomerByPhone` or resolve to `CustomerId` and use `GetCustomerContext`).

### 1.4 Threads Table — Why It Exists
- **Threads** = SMS conversation threads (one per customer/store). **Messages** = individual SMS. Populated when Twilio sends/receives. Use for Inbox only.
- **Reminders** = sent reminder records in **SmsReminders**. Shown only on the Reminders screen. Not a type of thread.

---

## 2. Architecture Overview

### 2.1 Navigation (Sidebar)

| Sidebar item   | ViewModel           | Content |
|----------------|---------------------|--------|
| Inbox          | InboxViewModel      | Conversation list (All / Open / Unread / Closed) |
| **Reminders**  | **RemindersViewModel** | Sent reminders list (new) |
| Late Customers | LateCustomersViewModel | (existing) |
| PFX Report     | PfxCustomersViewModel  | (existing) |
| Templates      | TemplatesViewModel     | (existing) |
| Settings       | SettingsViewModel       | (existing) |

**Order:** Compose → **Inbox** → **Reminders** → Late Customers → PFX Report → Templates → (divider) → Settings.

### 2.2 Inbox Panel (Conversations Only)

| Filter | API | Table(s) |
|--------|-----|----------|
| All    | `GET /api/inbox?filter=all`   | Threads (+ Customers, Messages) |
| Open   | `GET /api/inbox?filter=open`  | Threads (+ Customers, Messages) |
| Unread | `GET /api/inbox?filter=unread`| Threads (+ Customers, Messages) |
| Closed | `GET /api/inbox?filter=closed`| Threads (+ Customers, Messages) |

- **Single item type:** `InboxThreadItem` only. No reminder items in Inbox.
- **Improvements:** Add DisplayName (“Last, First”), formatted time (today / this week / older), and Google Voice–style list template (avatar, two lines, unread badge).

### 2.3 Reminders Screen (Separate)

| Purpose | API | Table(s) |
|---------|-----|----------|
| List    | `GET /api/reminders/sent?limit=200` | SmsReminders (+ Customers, Tickets) |
| Detail  | `GET /api/customer/by-phone?phone=` | (for customer context when row clicked) |

- **Item type:** `ReminderListItem` (or similar) for list rows.
- **Click:** Open ReminderDetailViewModel/View (reminder message + customer context panel).

---

## 3. Backend (API) — No Breaking Changes

### 3.1 Inbox API (Existing)
- **Endpoint:** `GET /api/inbox?store_id=&filter=&search=&twilio_number_id=`
- **Filter:** `all` | `open` | `unread` | `closed`. No change.

### 3.2 Reminders API (Existing)
- **Endpoint:** `GET /api/reminders/sent?limit=100` (or 200).
- **Response:** Array of `SentReminderItem`: ReminderId, TicketKey, CustomerKey, DueDate, Phone, ReminderType, Status, Message, SentAt, TwilioSid, CustomerName, TransNo.
- **Optional:** Store scoping and index on `SmsReminders(Status, CreatedAt DESC)` if needed.

---

## 4. Desktop Implementation

### 4.1 Sidebar: Add Reminders Below Inbox

**File:** `SmsOpsHQ.Desktop/MainWindow.xaml`

- After the **Inbox** button block, add a **Reminders** button:
  - `Command="{Binding NavigateCommand}"` `CommandParameter="Reminders"`
  - Icon (e.g. alarm/clock style if available in icon font), label "Reminders".
  - Same `NavButton` style and `DataTrigger` for `SelectedNavItem="Reminders"`.

**File:** `SmsOpsHQ.Desktop/ViewModels/MainViewModel.cs`

- In `Navigate(string section)`, add:
  - `case "Reminders": _navigation.NavigateTo<RemindersViewModel>(); break;`

### 4.2 Inbox Panel — Keep Closed; No Reminders

**File:** `SmsOpsHQ.Desktop/ViewModels/InboxViewModel.cs`

- **Filters:** `SelectedFilter` allowed values: `"all" | "open" | "unread" | "closed"`. Default e.g. `"open"`.
- **List:** Keep `ObservableCollection<InboxThreadItem> Threads`. No `InboxItemBase` or `InboxReminderItem`; no reminder load branch.
- **Load:** In `LoadInboxAsync`, always call `_apiClient.GetInboxAsync(CurrentStoreId, SelectedFilter, search)`. Map response to `InboxThreadItem` with:
  - **DisplayName:** From `customer.first_name` / `customer.last_name` → “Last, First” (or first/last only or “Unknown”).
  - **Time:** Format `last_message_at`: today → “h:mm tt”; this week → “ddd h:mm tt”; older → “MM/dd h:mm tt” (local time).
  - Keep ThreadId, CustomerId, CustomerPhone, UnreadCount, LastMessageBody, Status.
- **Selection:** Keep `SelectedThread`; on change call `OpenThread(item)` → navigate to `ThreadViewModel`. **ThreadView** already uses a two-column layout: left = messages + compose, **right = Context Panel (CustomerPanelView)** showing customer name, phone, active tickets, items, notes, payment history, etc.

**File:** `SmsOpsHQ.Desktop/Views/InboxView.xaml`

- **Filter pills:** **All**, **Open**, **Unread**, **Closed** (all four; no Reminders).
- **List:** `ItemsSource="{Binding Threads}"`, `SelectedItem="{Binding SelectedThread}"`.
- **Item template:** Google Voice–style: avatar (circle, initial from DisplayName or Phone), row 1: DisplayName (bold if UnreadCount > 0) + time right-aligned, row 2: Phone + unread badge. Optional message preview line.
- **Count:** e.g. “X conversations”.

### 4.3 Reminders Screen — New

**File:** `SmsOpsHQ.Desktop/ViewModels/RemindersViewModel.cs` (new)

- **List:** `ObservableCollection<ReminderListItem> Reminders`.
- **Item type:** `ReminderListItem` with: ReminderId, Phone, CustomerName, Message, SentAt (formatted), ReminderType, TransNo, DueDate, and display helpers (AvatarLetter, TimeText).
- **Load:** `LoadRemindersCommand` → `_apiClient.GetSentRemindersAsync(limit)` → map each to `ReminderListItem`, fill `Reminders`.
- **Selection:** `SelectedReminder`; on change → navigate to **ReminderDetailViewModel** with the selected item (or its key data). Optional “Back” command to return to Reminders list.
- **Count:** e.g. “X reminders”.

**File:** `SmsOpsHQ.Desktop/Views/RemindersView.xaml` (new)

- **Header:** Title “Reminders”, optional search (client-side filter by name/phone) later.
- **List:** `ItemsSource="{Binding Reminders}"`, `SelectedItem="{Binding SelectedReminder}"`.
- **Item template:** Same Google Voice–style: avatar, row 1: CustomerName + SentAt, row 2: Phone + e.g. “Ticket #TransNo” or ReminderType. No unread badge.
- **Loading / error:** Same pattern as Inbox (overlay, error bar).

### 4.4 Reminder Detail (When User Clicks a Reminder Row)

**File:** `SmsOpsHQ.Desktop/ViewModels/ReminderDetailViewModel.cs` (new)

- Holds selected reminder (ReminderId, Phone, CustomerName, Message, SentAt, TransNo, DueDate, ReminderType).
- Resolve customer for **Context Panel:** Call `GetCustomerByPhoneAsync(phone)`; if response includes a local `CustomerId`, create **CustomerPanelViewModel** with that id and load via `GetCustomerContextAsync`. If only external data, feed that into the same panel or a thin adapter so the **right panel shows the same Context Panel content** (name, phone, active tickets, items, notes, payment history).
- Expose **CustomerPanel** (ViewModel) for the right panel so ReminderDetailView can show the same **Context Panel** as ThreadView.
- **Back command:** Navigate back to `RemindersViewModel`.

**File:** `SmsOpsHQ.Desktop/Views/ReminderDetailView.xaml` (new)

- **Layout (same idea as ThreadView):** Two columns. **Left:** Reminder message card (and Back button). **Right:** **Context Panel (Customer Info)** — reuse **CustomerPanelView** bound to `CustomerPanel`. Content must match: Customer name, Phone, Active tickets, Items, Notes, Payment history, etc.

### 4.5 App Registration

**File:** `SmsOpsHQ.Desktop/App.xaml`

- Add `DataTemplate` for `RemindersViewModel` → `RemindersView`.
- Add `DataTemplate` for `ReminderDetailViewModel` → `ReminderDetailView`.

### 4.6 ApiClient

**File:** `SmsOpsHQ.Desktop/Services/ApiClient.cs`

- `GetSentRemindersAsync(int limit = 200)` already exists; use it from RemindersViewModel.
- `GetCustomerByPhoneAsync(phone)` already exists; use it from ReminderDetailViewModel.

---

## 5. Performance Checklist

- **Inbox:** One API call; one query with `Include(Customer)`. No N+1.
- **Reminders list:** One API call to `GET /api/reminders/sent`. Single query on backend.
- **Reminder detail:** One call to `GetCustomerByPhone` when opening detail.
- **Desktop:** Search on Enter or debounced; list virtualization by default (ListBox).

---

## 6. File and Task Summary

| Layer   | File / Component           | Task |
|---------|----------------------------|------|
| Desktop | `MainWindow.xaml`          | Add Reminders nav button below Inbox (icon + label “Reminders”, CommandParameter="Reminders", active state when SelectedNavItem=Reminders). |
| Desktop | `MainViewModel.cs`         | Add `case "Reminders": _navigation.NavigateTo<RemindersViewModel>(); break;` |
| Desktop | `InboxViewModel.cs`        | Keep Threads only. Add DisplayName (“Last, First”) and time formatting. Filters: all, open, unread, **closed**. No reminder logic. |
| Desktop | `InboxView.xaml`           | Keep filter pills: All, Open, Unread, **Closed**. Google Voice–style item template (avatar, DisplayName, time, phone, unread badge). |
| Desktop | `RemindersViewModel.cs` (new) | Load GET /api/reminders/sent, map to ReminderListItem, expose Reminders and SelectedReminder; on selection navigate to ReminderDetailViewModel. |
| Desktop | `RemindersView.xaml` (new) | Header “Reminders”, list bound to Reminders, Google Voice–style row template. |
| Desktop | `ReminderDetailViewModel.cs` (new) | Hold reminder data; call GetCustomerByPhone; expose reminder + customer context; Back → RemindersViewModel. |
| Desktop | `ReminderDetailView.xaml` (new) | Reminder message card + customer panel (reuse or adapt CustomerPanelView). |
| Desktop | `App.xaml`                 | Register DataTemplates: RemindersViewModel → RemindersView, ReminderDetailViewModel → ReminderDetailView. |
| Backend | (optional)                | Store filter for reminders API; index on SmsReminders(Status, CreatedAt DESC). |

---

## 7. Implementation Order

1. **Sidebar + navigation**
   - Add Reminders button in MainWindow.xaml (below Inbox).
   - Add Reminders case in MainViewModel.Navigate.

2. **Inbox: keep Closed; improve UI**
   - InboxViewModel: ensure filter closed is used; add DisplayName and time formatting for InboxThreadItem.
   - InboxView: confirm four pills (All, Open, Unread, Closed); update item template to Google Voice style (avatar, two lines, badge).

3. **Reminders list screen**
   - Create ReminderListItem (or similar) and RemindersViewModel (load sent reminders, selection).
   - Create RemindersView (header, list, item template).
   - Register RemindersViewModel → RemindersView in App.xaml.

4. **Reminder detail**
   - Create ReminderDetailViewModel (reminder + GetCustomerByPhone, Back command).
   - Create ReminderDetailView (reminder card + customer panel).
   - Register in App.xaml. From RemindersViewModel, on SelectedReminder change navigate to ReminderDetailViewModel.

5. **Polish**
   - Count labels (“X conversations” / “X reminders”), loading and error states, optional search on Reminders list later.

---

## 8. Summary

- **Inbox** = conversations only. Tabs: **All**, **Open**, **Unread**, **Closed**. No Reminders in Inbox. Clicking a thread → Thread view with **Context Panel** on the right (customer name, phone, active tickets, items, notes, payment history, etc.).
- **Reminders** = separate sidebar item below Inbox. Opens Reminders screen (list of sent reminders). Clicking a reminder → Reminder detail with **Context Panel** on the right (same content: customer name, phone, active tickets, items, notes, payment history, etc.). Reuse **CustomerPanelView** so both flows show the same right-panel customer info.
- Inbox and Reminders each have a single, clear purpose; both show the **Context Panel** when a customer is selected.
