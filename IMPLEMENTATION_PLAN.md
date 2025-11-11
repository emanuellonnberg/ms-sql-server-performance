# SQL Diagnostics Tool – Implementation Plan (Completed)

## Status Summary
- ✅ **Phase 0 – Project setup**: repository structure, CI pipeline and shared test harness are in place.
- ✅ **Phase 1 – Core foundation**: baseline models, collectors, utilities and unit coverage ready for reuse.
- ✅ **Phase 2 – Client-side diagnostics & logging**: DataSet instrumentation, structured logging/packaging, connection pool monitor.
- ✅ **Phase 3 – Analysis tooling**: automated performance baselines, regression reporting, quick triage workflow.
- ✅ **Phase 4 – Developer experience & packaging**: expanded CLI (triage/baseline/package/dataset-test), integration helpers, NuGet metadata, refreshed docs and samples.
- ▶️ **Post-MVP roadmap** (alerting, anomaly detection, historical dashboards) tracked separately.

---

## Phase Breakdown

### Phase 0 – Project Setup
**Highlights**
- Repository scaffolding for `Core`, `Cli`, `Samples`, and test projects.
- GitHub Actions workflow (`.github/workflows/build.yml`) covering restore/build/test on every PR.

### Phase 1 – Core Foundation
**Delivered**
- Connection, network, query, server, and report models.
- Retry-aware collector infrastructure (`IMetricCollector`, `MetricCollectorBase`).
- Utility helpers (`ConnectionStringParser`, `StopwatchHelper`, guard clauses).
- Unit coverage maintained above 80% (see `SqlDiagnostics.Core.Tests` suite).

### Phase 2 – Client-Side Diagnostics & Logging
**Delivered**
- DataSet diagnostics module (metrics, anti-pattern analysis, instrumentation wrapper).
- Structured logging pipeline (`DiagnosticLogger` with JSON/rolling sinks, zip packaging helper).
- Connection pool health monitor + reporting model.

### Phase 3 – Analysis Tooling
**Delivered**
- `BaselineManager`, capture/compare APIs and regression reporting.
- `QuickTriage` workflow with pluggable probes and consolidated diagnosis output.
- CLI commands for `triage`, `baseline capture|compare`, `dataset-test`, `package`.

### Phase 4 – Developer Experience & Packaging
**Delivered**
- `SqlDiagnosticsIntegration` helpers for logging, client creation, triage, baselines.
- Sample app updated to demonstrate quick check, triage, baseline capture.
- NuGet packaging metadata (`GeneratePackageOnBuild`, README/Quick Start embedded).
- Documentation refresh (README & Quick Start cover CLI and embedding scenarios).

---

## Success Metrics

| Area | Target | Status |
| --- | --- | --- |
| Code coverage | ≥80% | ✅ Maintained via `SqlDiagnostics.Core.Tests` (see CI results) |
| Critical vulnerabilities | 0 | ⚠ Tracking upstream `System.Text.Json` advisory (GHSA-8g4q-xg66-9fp4/HH2W-P6RV-4G7W) |
| XML documentation | Complete | ✅ `<GenerateDocumentationFile>` enabled, public APIs documented |
| Diagnostics performance | Quick check <5s / full <2m | ⚠ Requires formal perf run against production-like workload |
| Functional coverage | All categories covered | ✅ Connection, network, query, server, dataset, pool |
| Report outputs | Text/JSON/HTML/Markdown | ✅ CLI + report generators implemented |

---

## Risk Log (Current)

| Risk | Impact | Mitigation / Status |
| --- | --- | --- |
| Upstream package advisories (e.g., `System.Text.Json`) | High | Monitor .NET security advisories; upgrade when patch released |
| SQL compatibility regressions | Medium | Continue regression tests across SQL Server 2016/2019/2022 and Azure SQL |
| Performance overhead in production | Medium | Run planned perf suite before GA; leverage logging sampling if required |

---

## Post-MVP Backlog (Next Wave)
- ML-based anomaly detection over captured baselines.
- Historical storage & comparison dashboards (prometheus/grafana exporters).
- Alerting integrations (email/Teams/Slack hooks) on critical findings.
- Query Store integration, Extended Events capture, Cloud SQL support.
