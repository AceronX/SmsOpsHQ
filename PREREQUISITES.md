# SmsOps HQ – .NET Rewrite Prerequisites

Install and verify the following **before** creating the ASP.NET Core + WPF + SQLite project.

---

## 1. .NET 8 SDK (required)

- **What:** Runtime and SDK for building .NET 8 apps (API + WPF).
- **Download:** https://dotnet.microsoft.com/download/dotnet/8.0  
  - Choose **SDK 8.0.x** (not only Runtime).
- **Verify:** Open a new Command Prompt or PowerShell and run:
  ```powershell
  dotnet --version
  ```
  You should see `8.0.xxx`.

---

## 2. Visual Studio 2022 (recommended for WPF)

- **What:** IDE with .NET 8, WPF designer, and debugging. WPF projects are easiest to build and run from Visual Studio on Windows.
- **Download:** https://visualstudio.microsoft.com/vs/  
  - **Edition:** Community (free) is enough.
- **Workloads to install (during setup):**
  - **.NET desktop development** (includes WPF and Windows desktop)
  - **ASP.NET and web development** (for the API project)
- **Verify:** Start Visual Studio 2022 → **Help** → **About**. Confirm “.NET 8” and “Windows Forms and WPF” (or similar) are listed.

**Alternative (no Visual Studio):** You can use **VS Code** + **C# Dev Kit** extension and the `dotnet` CLI only. WPF will work from the command line, but you won’t have the visual designer. Prefer Visual Studio if you want the full WPF experience.

---

## 3. SQLite (no separate install for development)

- **What:** The app will use SQLite via **Entity Framework Core SQLite**. The SQLite library is included in the NuGet package.
- **Action:** Nothing to install. We will add the NuGet package when creating the API project.

*(Optional: If you want to inspect the database file with a GUI, you can install [DB Browser for SQLite](https://sqlitebrowser.org/) or use the SQLite extension in VS Code.)*

---

## 4. Git (optional but recommended)

- **What:** Version control for the new solution.
- **Download:** https://git-scm.com/download/win  
- **Verify:** In a new terminal:
  ```powershell
  git --version
  ```

---

## 5. Checklist before you start the project

| Item                    | How to check                          |
|-------------------------|----------------------------------------|
| .NET 8 SDK installed    | `dotnet --version` shows 8.0.x         |
| Visual Studio 2022      | Opens; .NET desktop + ASP.NET workloads |
| (Optional) Git          | `git --version` works                  |

---

## 6. Optional (for later when you integrate)

- **Twilio account** – Same as current Productionssms (SID, Auth Token, phone number).
- **XPD database access** – If you will integrate with the existing Access/XPD system (path, MDW, credentials). Not needed for the first phase of the new project.

---

## 7. Folder and next step

- **Project folder:** `c:\Users\Testing\Videos\xpawn\SmsOpsHQ`
- **Next:** After all items above are done, you can create the solution (API + WPF + SQLite) in this folder using the step-by-step project-creation guide.

---

**Summary:** Install **.NET 8 SDK** and **Visual Studio 2022** with **.NET desktop** and **ASP.NET and web development** workloads. No separate SQLite install is required. Then you’re ready to create the project.
