# Built-In SQL Server Diagnostic Tooling

## Overview
SQL Server exposes a rich catalogue of dynamic management views (DMVs), catalog views, and diagnostic stored procedures that require no external dependencies. Leveraging these first-party assets keeps our footprint light, works on SQL Server Express, and avoids licensing friction.

## Key Artifacts
- **DMVs**  
  - `sys.dm_exec_requests`, `sys.dm_exec_sessions`, `sys.dm_exec_connections`: real-time session, request, and connection state.  
  - `sys.dm_os_wait_stats`, `sys.dm_os_latch_stats`: cumulative wait analysis for isolating resource bottlenecks.  
  - `sys.dm_db_index_usage_stats`, `sys.dm_db_missing_index_details`: table/index usage patterns and advisories.  
  - `sys.dm_os_performance_counters`: exposes the same counters as PerfMon; good for buffer/cache ratios and throughput.  
  - `sys.dm_db_resource_stats`, `sys.dm_db_task_space_usage`: per-database CPU/IO/memory snapshots (Azure SQL also supported).
- **System Stored Procedures**  
  - `sp_whoisactive` (Adam Machanic, MIT licensed): gold-standard ad hoc tool for session diagnostics, blocking, and execution plans.  
  - `sp_blitzFirst`/`sp_blitzCache` (Brent Ozar Unlimited, MIT): actionable health checks and cached-plan analysis.  
  - `sp_helpdb`, `sp_spaceused`: quick sizing metrics with minimal privileges.
- **Extended Events / Trace**  
  - Lightweight session definitions targeting wait_info, blocked_process_report, or query_store runtime data for long-running captures.

## Integration Opportunities
- **Collector Modules**: encapsulate DMV queries similar to our `ServerDiagnostics` to avoid duplication.  
- **Snapshot Strategies**: combine cumulative DMV snapshots with deltas over time (e.g., wait stats baseline) for more actionable reporting.  
- **User-configurable Profiles**: allow operators to toggle modules (e.g., “include blocking diagnostics” runs `sp_whoisactive` for N seconds).  
- **Plan for Permissions**: document minimal required rights (`VIEW SERVER STATE`, `ALTER TRACE` for some XEvent targets).

## Considerations / Risks
- DMVs reset on server restart and some counters are cumulative; we need baselining logic to avoid misleading spikes.  
- `sp_whoisactive` and `sp_blitzFirst` require deployment to the target instance; offer script deployment or embed definitions.  
- Extended Events requires write access to create sessions; fall back to DMVs when denied.  
- Need to manage compatibility differences (e.g., `sys.dm_db_log_stats` exists only in SQL Server 2016 SP1+).

## Recommended Next Steps
1. **Catalog Queries**: check DMVs we already hit vs. high-value gaps (blocking chains, missing indexes, query wait types).  
2. **Prototype Baselines**: instrument wait stat deltas between two snapshots inside a single run.  
3. **Script Pack**: ship optional folder containing vetted versions of `sp_whoisactive` / `sp_blitzFirst`; expose CLI flag to run them.  
4. **Permission Matrix**: create guidance table mapping collector sections to required roles (sysadmin vs. VIEW SERVER STATE).  
5. **Testing**: validate queries against SQL Server Express 2019/2022 and Azure SQL to confirm feature parity.
