# SmsOps HQ – .NET Rewrite

ASP.NET Core 8 + WPF + SQLite rewrite of the SMS Operations HQ system (see [DOTNET_HANDOVER](../../DOTNET_HANDOVER) and [memory/design/001_smops_hq_dotnet_rewrite.md](../xpawndata/memory/design/001_smops_hq_dotnet_rewrite.md)).

## Prerequisites

See [PREREQUISITES.md](PREREQUISITES.md). You need **.NET 8 SDK** and **Visual Studio 2022** (or VS Code + C#) with WPF workload.

## Solution structure (Milestone 1)

| Project | Description |
|--------|-------------|
| **SmsOpsHQ.Core** | Domain entities, DTOs, repository/service interfaces (no EF) |
| **SmsOpsHQ.Infrastructure** | EF Core, SQLite, repository implementations |
| **SmsOpsHQ.Api** | ASP.NET Core Web API |
| **SmsOpsHQ.Desktop** | WPF client |
| **SmsOpsHQ.Tests** | xUnit tests |

## Build and test

From the `SmsOpsHQ` directory (where `SmsOpsHQ.sln` is):

```powershell
dotnet build SmsOpsHQ.sln
dotnet test SmsOpsHQ.sln
```

Or run the verification script:

```powershell
.\scripts\build.ps1
```

## Run API (after Milestone 8+)

```powershell
dotnet run --project SmsOpsHQ.Api
```

## Run Desktop (after Milestone 11+)

```powershell
dotnet run --project SmsOpsHQ.Desktop
```

## Implementation progress

Tracked in [memory/milestones/001_smops_hq_dotnet_rewrite_milestones.md](../xpawndata/memory/milestones/001_smops_hq_dotnet_rewrite_milestones.md).
