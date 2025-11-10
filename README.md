# SQL Diagnostics Toolkit

Cross-platform diagnostics toolkit for Microsoft SQL Server connectivity and performance investigations. The project ships as both a reusable .NET library (`SqlDiagnostics.Core`) and a command-line utility (`sqldiag`) so that you can embed it in existing applications or run ad-hoc checks from an operations workstation.

## Repository Layout

```
SqlDiagnostics.sln
├── src/
│   ├── SqlDiagnostics.Core/           # NuGet-ready diagnostics library
│   ├── SqlDiagnostics.Cli/            # Standalone CLI entry point
│   └── SqlDiagnostics.Samples/        # Minimal sample showcasing library usage
├── tests/
│   ├── SqlDiagnostics.Core.Tests/     # Unit test suite
│   └── SqlDiagnostics.Integration.Tests/ # Placeholder for live SQL integration scenarios
└── docs/                              # Design and architecture notes
```

## Getting Started

1. Install the .NET 8 SDK and ensure a SQL Server instance is reachable.
2. Clone and bootstrap the repository:
   ```
   git clone https://github.com/<your-org>/SqlDiagnostics.git
   cd SqlDiagnostics
   dotnet restore
   dotnet build
   dotnet test
   ```
3. Run a quick health check from the CLI (set `SQLDIAG_CONNECTION_STRING` or pass `--connection` explicitly):
   ```
   dotnet run --project src/SqlDiagnostics.Cli -- quick --connection "$SQLDIAG_CONNECTION_STRING"
   ```
4. Need a deeper report? Swap `quick` for `comprehensive` to include server, database, and query instrumentation.

The companion guide in `QUICK_START.md` walks through the end-to-end workflow, including exports, troubleshooting, and monitoring scenarios.

To embed diagnostics inside your own application, reference `SqlDiagnostics.Core` and use the new quick-start presets:

```csharp
using SqlDiagnostics.Core;
using SqlDiagnostics.Core.Client;
using SqlDiagnostics.Core.Models;

await using var client = new SqlDiagnosticsClient();

DiagnosticReport report = await client.RunFullDiagnosticsAsync(
    connectionString,
    QuickStartScenarios.ConnectionHealth(),
    cancellationToken);

Console.WriteLine($"Health Score: {report.GetHealthScore()}/100");
```

## Diagnostics Coverage (Initial Pass)

- Connection reliability testing (latency, success rate, transient error capture)
- Network reachability and jitter estimation using ICMP
- Query execution instrumentation leveraging SQL Client statistics
- Server-level snapshots (CPU, memory, PLE, IO stall) via DMVs
- Recommendation scaffolding for actionable remediation hints
- Event-driven monitoring loop (`SqlDiagnostics.Monitoring.DiagnosticMonitor`) that surfaces periodic snapshots for UI/automation scenarios
- Windows desktop dashboard prototype (`src/SqlDiagnostics.UI.Wpf`) that streams live metrics via WPF (build/run on Windows)

Further work will evolve these building blocks into richer reports, monitoring workflows, and historical baselining as described in `IMPLEMENTATION_PLAN.md`.

## Realtime WPF Dashboard (Preview)

The WPF preview lives in `src/SqlDiagnostics.UI.Wpf`. Open the `.csproj` (or the generated solution you prefer) on a Windows machine with the Windows Desktop SDK installed. The dashboard uses `DiagnosticMonitor` to:

- Accept a connection string and start/stop periodic `RunQuickCheckAsync` probes
- Display key connection/network metrics and last updated timestamps in realtime
- Surface high-priority recommendations as they are emitted

> **Note**: WPF tooling is Windows-only. The UI project is intentionally excluded from the cross-platform `SqlDiagnostics.sln` so CI builds on Linux/macOS stay green. Build it locally on Windows with `dotnet build src/SqlDiagnostics.UI.Wpf/SqlDiagnostics.UI.Wpf.csproj`.