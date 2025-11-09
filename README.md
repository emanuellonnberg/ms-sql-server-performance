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

1. Install .NET 8 SDK.
2. Restore the solution dependencies:
   ```
   dotnet restore
   ```
3. Build and test:
   ```
   dotnet build
   dotnet test
   ```
4. Run a quick diagnostic from the CLI (requires a valid connection string):
   ```
   dotnet run --project src/SqlDiagnostics.Cli -- quick --connection "Server=...;Database=...;User Id=...;Password=...;"
   ```

You can also reference `SqlDiagnostics.Core` directly from other projects to execute diagnostics programmatically.

## Diagnostics Coverage (Initial Pass)

- Connection reliability testing (latency, success rate, transient error capture)
- Network reachability and jitter estimation using ICMP
- Query execution instrumentation leveraging SQL Client statistics
- Server-level snapshots (CPU, memory, PLE, IO stall) via DMVs
- Recommendation scaffolding for actionable remediation hints

Further work will evolve these building blocks into richer reports, monitoring workflows, and historical baselining as described in `IMPLEMENTATION_PLAN.md`.