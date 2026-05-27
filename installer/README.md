# SmsOps HQ — Windows installer

## What you get

| Artifact | Description |
|----------|-------------|
| `SmsOpsHQ-Setup-x.x.x-x64.exe` | Standard Windows installer (Inno Setup) |
| `SmsOpsHQ-Desktop-StoreTest.zip` | Portable copy (no installer) |
| `Store\` folder | Same files as inside the installer |

The installer places:

- `SmsOpsHQ.Desktop.exe` — main app
- `api\SmsOpsHQ.Api.exe` — local API (starts hidden when configured)
- Default install: `C:\Program Files\SmsOps HQ\`

User-specific settings (Twilio credentials, XBlue, etc.) stay under `%AppData%\SmsOpsHQ\` and are **not** removed on upgrade unless you delete them manually.

## Build the setup file (on your dev PC)

1. Install **.NET 8 SDK**
2. Install **Inno Setup 6** from https://jrsoftware.org/isdl.php (use default options so `ISCC.exe` is on disk)
3. From the **repo root**:

```powershell
.\build-setup.ps1
```

If PowerShell says *running scripts is disabled*, use either:

```cmd
build-setup.cmd
```

```powershell
powershell -ExecutionPolicy Bypass -File .\build-setup.ps1
```

Optional:

```powershell
.\build-setup.ps1 -AppVersion "1.0.1"
.\build-setup.ps1 -SkipPublish   # only re-run Inno if publish folder already exists
```

Output installer:

`SmsOpsHQ.Desktop\bin\Publish\SmsOpsHQ-Setup-1.0.0-x64.exe`

## Deploy to a store PC

1. Copy `SmsOpsHQ-Setup-*.exe` to the target machine (USB, RDP, etc.)
2. Run as Administrator (installer requests admin for `Program Files`)
3. After install, open **SmsOps HQ** from the Start menu
4. First-run configuration:
   - **Settings → Twilio** — Account SID and Auth Token (used for **outbound** SMS only)
   - **Settings → Hub** (or edit `%AppData%\SmsOpsHQ\hub_config.json`) — `Hub:Enabled = true` plus the `Url`, `StoreKey`, and `DeploymentId` issued by the HQ admin
   - **Settings → Database** — XPD paths if you sync pawn data
   - Change the default `admin` / `password` login

No separate .NET install is required on the store PC (self-contained publish).

> **No public URL / no ngrok required on the store PC.** Inbound SMS in production is routed through **SmsOpsHQ.Hub** (the master console). The store API only needs outbound HTTPS to Twilio (for sending) and to the Hub (for SignalR + heartbeats). See the central architecture in [`../../docs/CENTRAL_TWILIO_WEBHOOK_REQUIREMENTS.md`](../../docs/CENTRAL_TWILIO_WEBHOOK_REQUIREMENTS.md) and the per-store cutover steps in [`../../docs/TWILIO_CUTOVER.md`](../../docs/TWILIO_CUTOVER.md).

## Portable deploy (no installer)

Use the ZIP from `publish-store.ps1` or extract `bin\Publish\Store\` to e.g. `C:\SmsOpsHQ\` and run `SmsOpsHQ.Desktop.exe`.

## Troubleshooting

| Issue | What to check |
|-------|----------------|
| Outbound SMS not delivered | Settings → Twilio banner must show **LIVE**, not MOCK; confirm Account SID + Auth Token are correct |
| Inbound SMS not arriving | Confirm the Hub is reachable from this PC (check `Hub:Url`), and that this store's number(s) appear on its **Store detail** page on the Hub (heartbeat populates the routing table). If the Hub is down, inbound SMS will not be delivered to any store. |
| Store appears **offline** on the Hub | Hub URL/key wrong, Hub down, or no internet on the store PC. The desktop client still works offline; messages will catch up when the Hub returns (queued by Twilio + replayed by the Hub on reconnect). |
| API won't start | `api\logs\smsops-*.log` under install folder |
| Port 5000 in use | Another API instance or old Python API on same port |
| Build: Inno not found | Install Inno Setup 6, reopen PowerShell, run `build-setup.ps1` again |
