# SQL Diagnostics Toolkit — Quick Start

Get from clone to actionable SQL Server diagnostics in about 10 minutes. This guide assumes you have access to a SQL Server instance (on-premises, Azure SQL, or LocalDB) and a connection string with the appropriate permissions.

---

## 1. Prerequisites

- .NET SDK 8.0 or later
- A SQL Server instance reachable from your machine
- (Optional) PowerShell or bash terminal for running the CLI

Set the connection string you want to diagnose:

```bash
export SQLDIAG_CONNECTION_STRING="Server=tcp:demo.database.windows.net;Database=AdventureWorks;User Id=demo;Password=SuperSecret!"
```

---

## 2. Restore, build, and test

```bash
git clone https://github.com/<your-org>/SqlDiagnostics.git
cd SqlDiagnostics
dotnet restore
dotnet build
dotnet test
```

All solution artifacts (library, CLI, samples, tests) are part of the single `SqlDiagnostics.sln` file.

---

## 3. Run your first diagnostic (CLI)

```bash
dotnet run --project src/SqlDiagnostics.Cli -- quick --connection "$SQLDIAG_CONNECTION_STRING"
```

You will receive a detailed text report covering connection success rate, latency, network reachability, and highlighted recommendations. Add `--format json|html|markdown` to export the same report to a file (use `--output <path>` to set the destination).

Other useful CLI workflows:

- `dotnet run --project src/SqlDiagnostics.Cli -- triage` – 30-second quick triage with live progress  
- `dotnet run --project src/SqlDiagnostics.Cli -- baseline capture --name production` – capture a baseline (defaults to 5 samples)  
- `dotnet run --project src/SqlDiagnostics.Cli -- baseline compare --name production` – compare the current state versus a baseline  
- `dotnet run --project src/SqlDiagnostics.Cli -- dataset-test --query "SELECT TOP 100 * FROM sys.objects"` – instrument a `SqlDataAdapter` load  
- `dotnet run --project src/SqlDiagnostics.Cli -- package --days 3` – zip the structured logs for support escalation

For a deeper sweep that includes server, database, and query insights:

```bash
dotnet run --project src/SqlDiagnostics.Cli -- comprehensive --connection "$SQLDIAG_CONNECTION_STRING"
```

---

## 4. Embed diagnostics in your own code

Reference the `SqlDiagnostics.Core` project or the published NuGet package and lean on the integration helpers for common workflows.

```csharp
using SqlDiagnostics.Core.Baseline;
using SqlDiagnostics.Core.Integration;

using var logger = SqlDiagnosticsIntegration.CreateDefaultLogger(enableConsole: true);
using var client = SqlDiagnosticsIntegration.CreateClient(logger);

var report = await client.RunFullDiagnosticsAsync(connectionString);
Console.WriteLine($"Health Score: {report.GetHealthScore()}/100");

var triage = await SqlDiagnosticsIntegration.RunQuickTriageAsync(connectionString);
Console.WriteLine($"Triage: {triage.Diagnosis.Category} - {triage.Diagnosis.Summary}");

// Capture a baseline for future comparisons
await SqlDiagnosticsIntegration.CaptureBaselineAsync(
    connectionString,
    baselineName: "production",
    options: new BaselineOptions { SampleCount = 5, SampleInterval = TimeSpan.FromSeconds(2) });
```

Each preset still returns the rich `DiagnosticReport` so you can export, compare with baselines, or feed the results into alerting.

---

## 5. Stream live telemetry (optional)

If you need continuous monitoring, subscribe to `SqlDiagnosticsClient.MonitorContinuously`. The observable pushes `DiagnosticSnapshot` instances at the interval you choose.

```csharp
using var subscription = client
    .MonitorContinuously(
        connectionString,
        interval: TimeSpan.FromSeconds(30),
        options: QuickStartScenarios.ConnectionHealth())
    .Subscribe(snapshot =>
    {
        Console.WriteLine($"[{snapshot.GeneratedAtUtc:O}] Success Rate: {snapshot.Report.Connection?.SuccessRate:P1}");
    });
```

Cancel the subscription to stop the loop.

---

## 6. Troubleshooting

- **Authentication failures**: double-check your connection string or try using `Authentication=Active Directory Default;` when connecting to Azure SQL with managed identity.
- **Firewall or port blocked (1433)**: enable outbound traffic or use the `Network -> Port Connectivity` section of the report to confirm where the block occurs.
- **DNS name cannot be resolved**: verify the host (e.g., `demo.database.windows.net`) and ensure corporate DNS overrides are not interfering.
- **Timeouts**: increase the `Timeout` on the connection string or pass a longer timeout via `QuickStartScenarios.ConnectionHealth(TimeSpan.FromSeconds(60))`.

---

## 7. Next steps

- Review `docs/` for deeper architectural notes and baseline comparison strategies.
- Explore the WPF dashboard under `src/SqlDiagnostics.UI.Wpf` (Windows only) for a realtime view, including the new **Connection Quality…** dialog.
- Host the reusable connection quality dialog in your own WPF application by referencing `SqlDiagnostics.UI.Dialogs` and calling `ConnectionQualityDialogLauncher.Show`.
- Extend diagnostics by adding custom recommendation rules via `SqlDiagnosticsClient.RegisterRecommendationRule`.
