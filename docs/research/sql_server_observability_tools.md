# SQL Server Observability & Metrics Exporters

## Overview
When teams need continuous telemetry, they rely on exporters and agent-based monitoring that stream data into Grafana, Prometheus, or logging systems. Understanding these ecosystems helps us align formats, reuse proven queries, or provide interoperability.

## Prometheus Exporters
- **sqlserver_exporter (Wrouesnel)**  
  - Popular open-source exporter written in Go; scrapes wait stats, performance counters, database size, and availability replicas.  
  - Uses a configuration-driven query catalog with templating for multi-instance targets.  
  - **Reuse Potential**: replicate high-value queries (wait stats delta, file stats) and optionally emit Prometheus-compatible metrics from our CLI or service.
- **sql_exporter (free/sql-exporter)**  
  - Generic SQL exporter supporting multiple drivers (MSSQL, MySQL, Postgres).  
  - Separates collectors, metrics, and labels for flexible dashboards.  
  - **Opportunity**: adopt its YAML schema for users who already manage exporters, or generate configuration files automatically from our tooling.

## Telegraf (InfluxData) Plugin
- **MSSQL Input Plugin**: polls DMVs and counters, writing to InfluxDB, Prometheus remote write, or other sinks.  
- Provides baseline queries for CPU, memory, waits, I/O, file size, log usage, and AG health.  
- **Integration Idea**: allow exporting our snapshot as Telegraf line protocol so customers can import into an existing TICK stack.

## SQLWATCH
- **What It Is**: open-source SQL Server monitoring framework storing DMV snapshots into its own database, with Grafana dashboards.  
- **Relevance**: rich schema with staging tables for waits, IO, jobs, blocking, and Alerting.  
- **Potential Collaboration**: import SQLWATCH data for historical analysis or emit our results into SQLWATCH tables when present.

## Azure Monitor & AWS CloudWatch Integrations
- Managed services provide first-party dashboards for PaaS offerings.  
- Azure Monitor Metric names (cpu_percent, xtp_storage_percent, etc.) could map to our collected metrics, letting customers compare baseline vs. point-in-time.  
- AWS RDS SQL Server exposes CloudWatch metrics; we can document how our checks complement them (e.g., granular waits).

## Observability-friendly Output
- **Data Formats**: JSON, NDJSON, Prometheus text, OpenTelemetry Metrics (OTLP), and CSV are in demand for downstream ingestion.  
- **Transport**: push vs. pull; we can expose HTTP endpoints for Prometheus scraping or produce files for later upload.  
- **Metadata**: include labels like instance name, environment, database, and run identifier to correlate with dashboards.

## Considerations
- Exporters expect continuous sampling; our tool is snapshot-based. We can add “loop mode” or integration hooks to run at intervals.  
- Security & credentials: storing connection strings for exporters requires rotation and encryption guidance.  
- Resource impact: copying heavy queries (e.g., top waits) might add load if run every 15 seconds—need throttling guidance.

## Recommended Next Steps
1. **Prometheus Prototype**: add optional `--prometheus-output` flag emitting `sqldiag_*` metrics (with help text referencing official exporters).  
2. **Query Crosswalk**: compare our DMV set with Prometheus/Telegraf defaults to identify gaps.  
3. **Format Strategy**: design a serialization layer that supports JSON + Prometheus text to future-proof OTel integration.  
4. **Community Outreach**: engage maintainers of popular exporters to share improvements or reuse queries (respecting licenses).  
5. **Benchmarking**: stress-test our collectors with exporter-grade frequency to confirm minimal impact on production workloads.
