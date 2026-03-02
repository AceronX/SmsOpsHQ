# Customer Detail Panel – Workflow (Beginner-Friendly)

This document explains **how the right-hand “customer detail” panel gets its data and shows it**, from the moment you select a conversation to the moment the panel displays name, tickets, risk, and notes.

---

## 1. What You See as a User

- **Inbox**: list of conversations (left), thread messages (center), **customer details (right)**.
- When you **click a conversation**, the center shows messages and the **right panel** shows that conversation’s customer: name, address, phones, ticket counts, risk, notes, active/closed tickets.
- If the conversation’s phone number **does not match any customer** in the system, the right panel shows a blank state (“Not in XPD”) instead of wrong data.

The only way we know “which customer” a conversation belongs to is by **matching the conversation’s phone number** to customers. That matching uses the **Master Customer Phone Map** (see below).

---

## 2. High-Level Flow (5 Steps)

```
User clicks conversation
    → InboxViewModel opens ThreadViewModel and passes a callback to set the right panel
    → ThreadViewModel loads thread (GET /api/thread/{id}), gets CustomerPhone from response
    → ThreadViewModel creates CustomerPanelViewModel and calls LoadByPhoneAsync(CustomerPhone)
    → CustomerPanelViewModel calls API GET /api/customer/by-phone?phone=...
    → API resolves phone to CustomerKey(s), loads customer + tickets + quality, returns JSON
    → CustomerPanelViewModel fills its properties from the JSON; WPF binds the panel to those properties
```

So: **one conversation → one phone number → one API call by phone → one customer (or “not found”) → one panel update.**

---

## 3. Step-by-Step with Code

### Step 1: User selects a conversation (Inbox)

**File:** `SmsOpsHQ.Desktop/ViewModels/InboxViewModel.cs`

When the user clicks a row in the inbox list, the selected item is stored in `SelectedThread`. That triggers:

```csharp
partial void OnSelectedThreadChanged(InboxThreadItem? value)
{
    if (value is not null)
        OpenThread(value);
}
```

`OpenThread` creates a **ThreadViewModel** for that conversation and gives it a **callback** so the thread view can “set” the right-hand panel content:

```csharp
private void OpenThread(InboxThreadItem item)
{
    ThreadViewModel threadVm = new(
        _apiClient, _appState, _navigation, _signalRClient,
        item.ThreadId, item.CustomerName, _xblueService,
        setRightPanel: p => CustomerPanel = p,   // <-- this callback sets the right panel
        onCloseRequested: () => { CurrentThreadViewModel = null; CustomerPanel = null; });
    CurrentThreadViewModel = threadVm;
}
```

So: **InboxViewModel** owns the `CustomerPanel` property. Whatever we assign to `CustomerPanel` is what the right column shows (see InboxView.xaml below).

---

### Step 2: Thread loads; we get the conversation’s phone number

**File:** `SmsOpsHQ.Desktop/ViewModels/ThreadViewModel.cs`

When the thread view loads, it calls `LoadMessagesAsync()`. That method:

1. Calls **GET /api/thread/{threadId}** to load messages and thread info.
2. From the response it reads **customer** (name and, importantly, **phone**):

```csharp
if (result.TryGetProperty("thread", out JsonElement threadElem) &&
    threadElem.TryGetProperty("customer", out JsonElement custElem) && ...)
{
    CustomerName = custElem.TryGetProperty("name", ...) ? ... : "Unknown";
    CustomerPhone = custElem.TryGetProperty("phone", out JsonElement phoneElem)
        ? phoneElem.GetString() ?? "" : "";
    ...
}
```

3. If we have a phone and the panel is missing or for a different phone, we **create the customer panel and load it by phone**:

```csharp
if (!string.IsNullOrEmpty(CustomerPhone) &&
    (CustomerPanel is null || CustomerPanel.CustomerPhone != CustomerPhone))
{
    CustomerPanelViewModel panel = new(_apiClient, _xblueService);
    CustomerPanel = panel;
    _setRightPanel?.Invoke(panel);           // InboxViewModel.CustomerPanel = panel
    await panel.LoadByPhoneAsync(CustomerPhone);  // <-- this does the real work
}
```

So: **ThreadViewModel** gets `CustomerPhone` from the thread API, creates a **CustomerPanelViewModel**, assigns it to the right panel via the callback, then calls **LoadByPhoneAsync(CustomerPhone)**. Everything the panel shows comes from that one method.

---

### Step 3: Panel loads data by phone (Desktop → API)

**File:** `SmsOpsHQ.Desktop/ViewModels/CustomerPanelViewModel.cs`

`LoadByPhoneAsync(string phone)` is the main entry point for the panel:

```csharp
public async Task LoadByPhoneAsync(string phone)
{
    if (string.IsNullOrWhiteSpace(phone)) { Clear(); return; }
    ...
    JsonElement response = await _apiClient.GetCustomerByPhoneAsync(phone);
    bool found = response.TryGetProperty("found", out JsonElement foundElement) && foundElement.GetBoolean();

    if (!found)
    {
        ShowNotFound(phone);   // Panel shows "Not in XPD", empty stats, no tickets
        return;
    }

    IsCustomerFound = true;
    PopulateFromResponse(response);   // Maps JSON to all the panel properties
}
```

So the flow is:

- **GetCustomerByPhoneAsync(phone)** → HTTP **GET /api/customer/by-phone?phone=...**
- If **found == false**: we show the “not found” state and do not show partial/wrong data.
- If **found == true**: we call **PopulateFromResponse(response)** to fill every property the view binds to (name, address, stats, risk, notes, active/closed tickets).

All of that data is in the single JSON response from **GET /api/customer/by-phone**.

---

### Step 4: API resolves phone to customer and returns everything (Backend)

**File:** `SmsOpsHQ.Api/Controllers/CustomersController.cs`  
**Method:** `GetCustomerByPhone([FromQuery] string phone, ...)`

The API does the following in order:

1. **Normalize the phone** (so +1 555-123-4567 and 5551234567 match):

```csharp
string? normalizedPhone = PhoneUtils.ExtractLast10Digits(phone);
if (string.IsNullOrEmpty(normalizedPhone))
    return Ok(new { found = false, ... });
```

2. **Resolve phone → customer key(s)** using the **Master Customer Phone Map** (table `CustomerPhones`). One customer can have several numbers (ResPhone, BusPhone, numbers in Notes), so we get a list of keys:

```csharp
List<int> customerKeys = await _identityResolver.ResolveCustomerKeysAsync(phone, cancellationToken);
if (customerKeys.Count == 0)
    return Ok(new { found = false, ... });

int primaryCustomerKey = customerKeys[0];
```

3. **Load customer row** from `Customers` for the first key (name, address, phones, notes, etc.).
4. **Load tickets** for **all** resolved keys (so we see every ticket for that customer, no matter which number they used):

```csharp
List<Ticket> allTickets = await _ticketRepo.GetByCustomerKeysAsync(customerKeys, cancellationToken);
```

5. **Compute stats and quality** (active count, late count, risk level, payment history, etc.) and build ticket lists (active, CPU closed, PFX closed).
6. **Return one JSON object** with `found: true`, `customer`, `stats`, `quality`, `payment_history`, `active_tickets`, `cpu_tickets`, `pfx_tickets`, `ticket_notes`, `item_notes`, etc.

So: **phone in → normalize → lookup in CustomerPhones → CustomerKey(s) → load customer + tickets + quality → one response.** The desktop never “chooses” the customer; the API does by resolving the phone.

---

### Step 5: Where the “phone map” comes from (CustomerPhones table)

**File:** `SmsOpsHQ.Infrastructure/Services/IdentityResolver.cs`  
**Method:** `ResolveCustomerKeysAsync(string phoneE164, ...)`

The API uses this to go from **phone** to **CustomerKey**:

```csharp
string? normalized = PhoneUtils.ExtractLast10Digits(phoneE164);
...
List<int> keys = await _db.CustomerPhones
    .AsNoTracking()
    .Where(p => p.PhoneNormalized == normalized)
    .Select(p => p.CustomerKey)
    .Distinct()
    .OrderBy(k => k)
    .ToListAsync(cancellationToken);
return keys;
```

So the **Master Customer Phone Map** is the **CustomerPhones** table: each row is `(CustomerKey, PhoneNormalized, PhoneOriginal, PhoneType)`. It is **rebuilt during XPD sync** from:

- **Customers.ResPhone**
- **Customers.BusPhone**
- Every phone number **parsed from Customers.Notes**

**File:** `SmsOpsHQ.Infrastructure/Services/XpdSyncService.cs`  
**Method:** `RebuildPhoneIndexAsync` → for each customer, it inserts one row per phone (ResPhone, BusPhone, each number from Notes). So “look up by phone” means “look up in CustomerPhones by normalized phone, then use the returned CustomerKey(s).”

---

### Step 6: How the panel gets drawn (View binding)

**File:** `SmsOpsHQ.Desktop/Views/InboxView.xaml`

The right column is bound to **InboxViewModel.CustomerPanel**:

```xml
<ContentPresenter Content="{Binding CustomerPanel}"
                  Visibility="{Binding CustomerPanel, Converter={StaticResource NullToCollapsed}}"/>
```

- When **CustomerPanel** is **null**: the placeholder (“Customer details”) is shown.
- When **CustomerPanel** is a **CustomerPanelViewModel** instance: WPF uses the **DataTemplate** that maps `CustomerPanelViewModel` → `CustomerPanelView` (in `App.xaml`), so the right column shows **CustomerPanelView**.

**File:** `SmsOpsHQ.Desktop/Views/CustomerPanelView.xaml`

The view is just bindings to the ViewModel’s properties, for example:

- `Text="{Binding CustomerName}"`
- `Text="{Binding AddressLine}"`
- `Text="{Binding ActiveCount}"`
- `ItemsSource="{Binding ActiveTickets}"`
- etc.

So: **CustomerPanelViewModel** holds the data; **CustomerPanelView** displays it. No extra “load” happens in the view—everything was already loaded in **LoadByPhoneAsync** and **PopulateFromResponse**.

---

## 4. Data Flow Diagram (Simplified)

```
[Inbox list]
     │ user clicks conversation
     ▼
[InboxViewModel.OpenThread]
     │ creates ThreadViewModel, setRightPanel: p => CustomerPanel = p
     ▼
[ThreadViewModel.LoadMessagesAsync]
     │ GET /api/thread/{id}  →  CustomerPhone from thread.customer.phone
     │ new CustomerPanelViewModel(); _setRightPanel(panel); panel.LoadByPhoneAsync(CustomerPhone)
     ▼
[CustomerPanelViewModel.LoadByPhoneAsync(phone)]
     │ GET /api/customer/by-phone?phone=...
     ▼
[CustomersController.GetCustomerByPhone]
     │ PhoneUtils.ExtractLast10Digits(phone)
     │ IdentityResolver.ResolveCustomerKeysAsync(phone)  →  CustomerPhones table
     │ load customer row (primary key), load tickets (all keys), compute stats/quality
     │ return { found, customer, stats, quality, payment_history, active_tickets, ... }
     ▼
[CustomerPanelViewModel.PopulateFromResponse(response)]
     │ set CustomerName, AddressLine, ActiveCount, RiskLevel, ActiveTickets, ...
     ▼
[CustomerPanelView]
     │ bindings show CustomerName, AddressLine, ActiveCount, ActiveTickets, ...
     ▼
User sees the customer detail panel filled.
```

---

## 5. Summary for a Developer

| Step | Where | What happens |
|------|--------|----------------|
| 1 | InboxViewModel | User selects thread → OpenThread → create ThreadViewModel and pass `setRightPanel`. |
| 2 | ThreadViewModel.LoadMessagesAsync | GET thread → get CustomerPhone → create CustomerPanelViewModel, set it as CustomerPanel, call LoadByPhoneAsync(CustomerPhone). |
| 3 | CustomerPanelViewModel.LoadByPhoneAsync | GET /api/customer/by-phone?phone=... → if not found then ShowNotFound; else PopulateFromResponse. |
| 4 | CustomersController.GetCustomerByPhone | Normalize phone → resolve CustomerKeys from CustomerPhones → load customer + tickets for those keys → return one JSON. |
| 5 | IdentityResolver.ResolveCustomerKeysAsync | Query CustomerPhones by PhoneNormalized → return list of CustomerKey. |
| 6 | CustomerPhones table | Built by XpdSyncService from ResPhone, BusPhone, and phones in Notes. |
| 7 | CustomerPanelView | DataTemplate shows CustomerPanelView; XAML bindings display ViewModel properties. |

**Main idea:** The conversation only gives us a **phone number**. We use that phone to look up **CustomerKey(s)** in the **CustomerPhones** table (the “Master Phone Map”), then load **one customer + all their tickets and quality** from the API. The panel never “guesses” the customer—it always goes **phone → API by-phone → customer (or not found)**.

---

## 6. Design vs "All Nums + Data for All Nums + Thread + Panel"

**Requirement:** When you select one customer from the left list, (1) get all available numbers (ResPhone, BusPhone, phones from Notes), (2) check/get data for all those numbers, (3) display that data in the message thread section and the right customer detail panel.

**How the current design matches this:**

| Requirement | Current design | Match? |
|-------------|----------------|--------|
| **Get all available nums** | Backend: CustomerPhones index is built from ResPhone, BusPhone, and phones extracted from Notes. When we look up by any one of those numbers, we get the same customer. The by-phone API returns that customer including res_phone, bus_phone, and notes. The right panel now shows all of these: Res, Bus, and phones parsed from Notes in the phone line. | Yes. |
| **Check data for all that nums** | We do not make separate API calls per number. One phone resolves to CustomerKeys (all keys that share that phone), then we load customer and tickets for all those keys in one response. Customer and ticket data is unified. SMS threads are per-phone; we do not load or merge threads for multiple numbers. | Partially. |
| **Display on message thread section** | Inbox: center shows the SMS thread. Reminders: center shows reminder detail (sent reminder), not an SMS thread. | Inbox yes; Reminders center is reminder detail, not a thread. |
| **Display on right customer detail panel** | We call GET /api/customer/by-phone with the selected phone and fill the right panel. Reminders trigger this in OpenReminder so the right panel stays in sync. | Yes. |

**Summary:** One phone resolves the customer; the right panel shows all of that customer's data (ResPhone, BusPhone, phones from Notes). Data is unified by customer via the phone index; we do not "check" each number with separate calls.
