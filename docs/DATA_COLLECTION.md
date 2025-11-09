# SQL Diagnostics Toolkit - Data Collection Matrix

This document outlines the telemetry we can gather to investigate connection drops, high latency, and throughput issues when working with Microsoft SQL Server or SQL Server Express. Everything listed below is achievable with client-side instrumentation, lightweight server DMV queries, or OS-level probes that do not require privileged packet capture tooling.

## 1. Connection Lifecycle Metrics

- **Connection latency** (per attempt)  
  Captured by timing `SqlConnection.OpenAsync`. Enables percentile/average analysis to uncover spikes.
- **Success/failure rate**  
  Number of successful opens versus transient or fatal failures. Captures SQL error numbers and severity.
- **Failure signatures**  
  Store `SqlException.Number`, `Class`, `State`, and `Server` to correlate with SQL logs.
- **TLS handshake timing** *(future work)*  
  Available from `SqlClient` diagnostic events or `EventSource` traces.
- **Authentication provider** *(future work)*  
  Useful when Kerberos/NTLM negotiation drives latency.

## 2. Network Reachability

- **ICMP latency and jitter**  
  Uses `Ping.SendPingAsync` for round-trip measurements and variance between samples.
- **Packet loss approximation**  
  Compare sent/received ping counts; supplement with TCP connect attempts in future iterations.
- **DNS resolution latency** *(planned)*  
  `Dns.GetHostEntryAsync` timing helps diagnose slow name resolution.
- **TCP port accessibility** *(planned)*  
  Attempt `TcpClient.ConnectAsync` to ensure `1433` (or custom port) is reachable through firewalls/NAT.
- **Traceroute snapshot** *(optional)*  
  Requires raw socket privileges; consider external tooling integration.

## 3. Query Execution Statistics

- **Client statistics** (`SqlConnection.RetrieveStatistics`)  
  Provides network/server time breakdown, roundtrips, bytes sent/received, row counts.
- **Wait types and blocking** *(planned)*  
  Query DMVs: `sys.dm_exec_requests`, `sys.dm_os_waiting_tasks`.
- **Execution plans** *(planned)*  
  `SET STATISTICS XML ON` or `sys.dm_exec_query_plan` for problematic statements.
- **Batch compilation time** *(future)*  
  DMV `sys.dm_exec_cached_plans` to review plan cache churn.

## 4. Server Health Snapshots

- **CPU utilization split**  
  From `sys.dm_os_ring_buffers` (process vs SQL Server CPU).
- **Memory availability**  
  `sys.dm_os_sys_memory` for physical memory pressure metrics.
- **Page life expectancy**  
  Highlight buffer cache churn and memory pressure.
- **IO stall time**  
  `sys.dm_io_virtual_file_stats` summarizing cumulative stall per database file.
- **TempDB contention indicators** *(planned)*  
  DMVs for wait stats (`PAGELATCH_*`) and allocation contention.
- **Configuration drift** *(planned)*  
  Compare `sys.configurations` against best-practice baselines.

## 5. Transport & Packet-Level Insight

While the toolkit focuses on Managed .NET APIs, more granular packet analysis can be added later by:

- **Enabling `SqlClient` diagnostics listener** to capture TDS packet sizes and retries.
- **Capturing ETW events** (`Microsoft.Data.SqlClient.EventSource`) for network errors and timeouts.
- **Integrating with `tcpdump`/`netsh trace`** for on-demand packet captures when permissions allow.

## 6. Environmental Context

- **Client machine metadata** (OS version, machine name, IP address) for cross-reference.
- **Connection string fingerprint** (sanitized) to highlight use of named instances, multi-subnet failover, etc.
- **Time correlation**  
  Timestamps on every sample allow cross-plotting with server-side logs (SQL error log, Windows Event Viewer).

## Permissions

- Basic connection/network metrics only require `CONNECT` permission.
- Server DMVs require `VIEW SERVER STATE`; database-scoped metrics may need `VIEW DATABASE STATE`.
- No elevated OS privileges are necessary unless packet capture is added in future releases.

## Next Steps

1. Expand collectors to add DNS/TCP probes and DMV-based wait statistics.
2. Implement structured logging for each diagnostic pass.
3. Add persistence (JSON/SQLite) for historical comparison.
4. Surface metrics in multiple formats (JSON/Markdown/HTML) for easier sharing with DBAs and network teams.
