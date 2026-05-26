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
   - **Settings → Twilio** — Account SID, Auth Token, Messaging Service SID (if required)
   - **Settings → Database** — XPD paths if you sync pawn data
   - Change the default `admin` / `password` login

No separate .NET install is required on the store PC (self-contained publish).

## Portable deploy (no installer)

Use the ZIP from `publish-store.ps1` or extract `bin\Publish\Store\` to e.g. `C:\SmsOpsHQ\` and run `SmsOpsHQ.Desktop.exe`.

## Troubleshooting

| Issue | What to check |
|-------|----------------|
| SMS not delivered | Settings → Twilio banner must show **LIVE**, not MOCK |
| API won't start | `api\logs\smsops-*.log` under install folder |
| Port 5000 in use | Another API instance or old Python API on same port |
| Build: Inno not found | Install Inno Setup 6, reopen PowerShell, run `build-setup.ps1` again |
