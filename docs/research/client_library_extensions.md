# Client Libraries & SDK Extensions

## Overview
Beyond DMVs and external tools, several programming libraries and runtime features can enrich our diagnostics or streamline data collection. Understanding their capabilities helps us decide whether to embed them, offer plug-ins, or provide optional integrations.

## Microsoft.Data.SqlClient Enhancements
- **Connection Resiliency**: built-in retry logic, connection pooling metrics (`SqlConnection` statistics) already leveraged by our Query diagnostics.  
- **Telemetry Hooks**: `SqlClient` exposes event counters (connection opens, commands) via EventSource in .NET; we can capture these during CLI runs to show client-side perspective.  
- **Always Encrypted / Column Encryption Keys**: we must ensure diagnostic queries respect encryption requirements; library APIs help detect enabled features.

## PowerShell & dbatools
- **dbatools**: community module with 500+ commands (`Test-DbaLastBackup`, `Get-DbaDbSpace`, `Measure-DbaDbVirtualLogFile`).  
- **Usage Patterns**: administrators rely on PowerShell scripts for automation; offering a bridge (generate PowerShell scaffolding or call scripts from CLI) could accelerate adoption.  
- **Packaging**: dbatools uses PowerShell Gallery; cross-platform support requires PowerShell 7.

## Python & Go Ecosystem
- **pyodbc / sqlalchemy**: Python-based collectors widely used in data teams; we could expose our logic as a Python package or script for custom pipelines.  
- **Go Libraries**: `github.com/denisenkom/go-mssqldb` (driver powering Prometheus exporters) supports TLS, Azure AD auth, and connection pooling—useful if we ever embed a lightweight agent in Go.  
- **Interoperability**: providing schema and output formats enables customers to extend diagnostics in their preferred language.

## OpenTelemetry & Event Streaming
- **OTel Metrics & Traces**: by emitting OpenTelemetry metrics, we can integrate with any back-end supporting OTLP (Grafana, New Relic, Splunk).  
- **Event Hubs / Kafka**: stream periodic snapshots for centralized analytics. Requires schema stability (Avro/JSON) and security guidelines.

## Packaging Considerations
- **Optional Dependencies**: bundling PowerShell or Python runtime inflates package size—consider plugin architecture where users opt in.  
- **Authentication**: library choice impacts available auth methods (Kerberos, Azure AD Integrated, Managed Identity). Document support matrix.  
- **Licensing**: ensure third-party libraries’ licenses (MIT, Apache) align with our distribution.

## Recommended Next Steps
1. **Auth Matrix**: document which libraries support integrated auth scenarios (e.g., Linux Kerberos with `SqlClient`, Azure AD with ODBC).  
2. **Plugin Architecture**: design extension points so we can add PowerShell/dbatools or Python collectors without bloating the core CLI.  
3. **OpenTelemetry Spike**: prototype exporting wait stats and resource metrics as OTLP to validate compatibility.  
4. **Community Outreach**: engage dbatools maintainers about reciprocal usage; explore bundling optional script packs.  
5. **Sample Integrations**: publish example scripts (PowerShell, Python) that consume our JSON output and push to other systems, showcasing extensibility.
