# SQL Server Diagnostic Queries Reference

## Overview

SQL queries for collecting diagnostic information from SQL Server using Dynamic Management Views (DMVs).

## Required Permissions

```sql
GRANT VIEW SERVER STATE TO [DiagnosticUser];
GRANT VIEW DATABASE STATE TO [DiagnosticUser];
GRANT CONNECT SQL TO [DiagnosticUser];
```

---

## Connection Diagnostics

### Current Connection Information

```sql
SELECT 
    c.session_id,
    c.connect_time,
    c.net_transport,
    c.protocol_type,
    c.encrypt_option,
    c.auth_scheme,
    c.num_reads,
    c.num_writes,
    c.last_read,
    c.last_write,
    c.net_packet_size,
    c.client_net_address,
    c.client_tcp_port,
    s.host_name,
    s.program_name,
    s.login_time,
    s.last_request_start_time
FROM sys.dm_exec_connections c
INNER JOIN sys.dm_exec_sessions s ON c.session_id = s.session_id
WHERE c.session_id = @@SPID;
```

### Connection Counts by Application

```sql
SELECT 
    program_name,
    COUNT(*) AS connection_count,
    SUM(CASE WHEN status = 'sleeping' THEN 1 ELSE 0 END) AS idle_connections,
    SUM(CASE WHEN status = 'running' THEN 1 ELSE 0 END) AS active_connections
FROM sys.dm_exec_sessions
WHERE is_user_process = 1
GROUP BY program_name
ORDER BY connection_count DESC;
```

---

## Wait Statistics

### Session Wait Statistics

```sql
SELECT 
    wait_type,
    waiting_tasks_count,
    wait_time_ms,
    max_wait_time_ms,
    signal_wait_time_ms,
    wait_time_ms - signal_wait_time_ms AS resource_wait_time_ms
FROM sys.dm_exec_session_wait_stats
WHERE session_id = @@SPID
    AND wait_time_ms > 0
ORDER BY wait_time_ms DESC;
```

### Server-Wide Wait Statistics

```sql
WITH Waits AS (
    SELECT 
        wait_type,
        wait_time_ms / 1000.0 AS wait_time_s,
        (wait_time_ms - signal_wait_time_ms) / 1000.0 AS resource_wait_time_s,
        signal_wait_time_ms / 1000.0 AS signal_wait_time_s,
        waiting_tasks_count
    FROM sys.dm_os_wait_stats
    WHERE wait_type NOT IN (
        'BROKER_EVENTHANDLER', 'BROKER_RECEIVE_WAITFOR',
        'CHECKPOINT_QUEUE', 'SLEEP_TASK', 'WAITFOR'
    )
    AND wait_time_ms > 0
)
SELECT TOP 20
    wait_type,
    CAST(wait_time_s AS DECIMAL(12, 2)) AS wait_time_s,
    CAST(resource_wait_time_s AS DECIMAL(12, 2)) AS resource_wait_time_s,
    waiting_tasks_count,
    CAST(100.0 * wait_time_s / SUM(wait_time_s) OVER() AS DECIMAL(5, 2)) AS pct_total_waits
FROM Waits
ORDER BY wait_time_s DESC;
```

---

## Query Performance

### Currently Running Queries

```sql
SELECT 
    r.session_id,
    r.start_time,
    DATEDIFF(ms, r.start_time, GETDATE()) AS elapsed_ms,
    r.status,
    r.command,
    r.wait_type,
    r.cpu_time,
    r.total_elapsed_time,
    r.reads,
    r.writes,
    r.logical_reads,
    r.row_count,
    r.blocking_session_id,
    DB_NAME(r.database_id) AS database_name,
    SUBSTRING(qt.text, (r.statement_start_offset/2)+1,
        ((CASE r.statement_end_offset
            WHEN -1 THEN DATALENGTH(qt.text)
            ELSE r.statement_end_offset
        END - r.statement_start_offset)/2) + 1) AS query_text
FROM sys.dm_exec_requests r
CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) qt
WHERE r.session_id != @@SPID
    AND r.session_id > 50
ORDER BY r.total_elapsed_time DESC;
```

### Blocking Chains

```sql
WITH BlockingChain AS (
    SELECT 
        session_id,
        blocking_session_id,
        CAST(session_id AS VARCHAR(MAX)) AS chain,
        0 AS level
    FROM sys.dm_exec_requests
    WHERE blocking_session_id != 0
    
    UNION ALL
    
    SELECT 
        r.session_id,
        r.blocking_session_id,
        CAST(bc.chain + ' -> ' + CAST(r.session_id AS VARCHAR(MAX)) AS VARCHAR(MAX)),
        bc.level + 1
    FROM sys.dm_exec_requests r
    INNER JOIN BlockingChain bc ON r.blocking_session_id = bc.session_id
    WHERE bc.level < 10
)
SELECT DISTINCT
    bc.level,
    bc.session_id,
    bc.blocking_session_id,
    bc.chain AS blocking_chain,
    r.wait_type,
    r.wait_time,
    DB_NAME(r.database_id) AS database_name,
    t.text AS query_text
FROM BlockingChain bc
INNER JOIN sys.dm_exec_requests r ON bc.session_id = r.session_id
CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) t
ORDER BY bc.level, bc.session_id;
```

### Expensive Queries from Cache

```sql
SELECT TOP 20
    qs.execution_count,
    qs.total_worker_time / 1000 AS total_cpu_time_ms,
    qs.total_worker_time / qs.execution_count / 1000 AS avg_cpu_time_ms,
    qs.total_elapsed_time / 1000 AS total_elapsed_time_ms,
    qs.total_logical_reads,
    qs.total_logical_reads / qs.execution_count AS avg_logical_reads,
    qs.creation_time,
    qs.last_execution_time,
    SUBSTRING(qt.text, (qs.statement_start_offset/2)+1,
        ((CASE qs.statement_end_offset
            WHEN -1 THEN DATALENGTH(qt.text)
            ELSE qs.statement_end_offset
        END - qs.statement_start_offset)/2) + 1) AS query_text
FROM sys.dm_exec_query_stats qs
CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) qt
ORDER BY qs.total_worker_time DESC;
```

---

## Server Resource Metrics

### CPU Usage

```sql
DECLARE @ts_now BIGINT = (SELECT cpu_ticks/(cpu_ticks/ms_ticks) FROM sys.dm_os_sys_info);

SELECT TOP 60
    DATEADD(ms, -1 * (@ts_now - [timestamp]), GETDATE()) AS event_time,
    SQLProcessUtilization AS sql_cpu_usage_pct,
    100 - SystemIdle - SQLProcessUtilization AS other_cpu_usage_pct,
    SystemIdle AS idle_cpu_pct
FROM (
    SELECT 
        record.value('(./Record/SchedulerMonitorEvent/SystemHealth/SystemIdle)[1]', 'int') AS SystemIdle,
        record.value('(./Record/SchedulerMonitorEvent/SystemHealth/ProcessUtilization)[1]', 'int') AS SQLProcessUtilization,
        timestamp
    FROM (
        SELECT timestamp, CONVERT(XML, record) AS record
        FROM sys.dm_os_ring_buffers
        WHERE ring_buffer_type = N'RING_BUFFER_SCHEDULER_MONITOR'
            AND record LIKE '%<SystemHealth>%'
    ) AS x
) AS y
ORDER BY event_time DESC;
```

### Memory Usage

```sql
-- Memory clerks
SELECT 
    type AS memory_clerk_type,
    SUM(pages_kb) / 1024 AS memory_used_mb
FROM sys.dm_os_memory_clerks
GROUP BY type
HAVING SUM(pages_kb) / 1024 > 10
ORDER BY memory_used_mb DESC;

-- System memory
SELECT 
    (total_physical_memory_kb / 1024) AS total_physical_memory_mb,
    (available_physical_memory_kb / 1024) AS available_physical_memory_mb,
    (committed_kb / 1024) AS sql_committed_mb,
    (committed_target_kb / 1024) AS sql_committed_target_mb
FROM sys.dm_os_sys_memory;

-- Page Life Expectancy
SELECT 
    counter_name,
    cntr_value AS page_life_expectancy_seconds
FROM sys.dm_os_performance_counters
WHERE object_name LIKE '%Buffer Manager%'
    AND counter_name = 'Page life expectancy';
```

### Disk I/O Statistics

```sql
SELECT 
    DB_NAME(vfs.database_id) AS database_name,
    mf.name AS file_name,
    mf.physical_name,
    mf.type_desc AS file_type,
    vfs.num_of_reads,
    vfs.num_of_writes,
    vfs.num_of_bytes_read / 1024 / 1024 AS mb_read,
    vfs.num_of_bytes_written / 1024 / 1024 AS mb_written,
    vfs.io_stall_read_ms,
    vfs.io_stall_write_ms,
    CASE WHEN vfs.num_of_reads = 0 THEN 0
        ELSE vfs.io_stall_read_ms / vfs.num_of_reads
    END AS avg_read_latency_ms,
    CASE WHEN vfs.num_of_writes = 0 THEN 0
        ELSE vfs.io_stall_write_ms / vfs.num_of_writes
    END AS avg_write_latency_ms
FROM sys.dm_io_virtual_file_stats(NULL, NULL) vfs
INNER JOIN sys.master_files mf 
    ON vfs.database_id = mf.database_id 
    AND vfs.file_id = mf.file_id
ORDER BY vfs.io_stall_read_ms + vfs.io_stall_write_ms DESC;
```

---

## Database Diagnostics

### Transaction Log Information

```sql
SELECT 
    DB_NAME(database_id) AS database_name,
    total_log_size_in_bytes / 1024 / 1024 AS total_log_size_mb,
    used_log_space_in_bytes / 1024 / 1024 AS used_log_space_mb,
    used_log_space_in_percent,
    log_space_in_bytes_since_last_backup / 1024 / 1024 AS log_since_backup_mb
FROM sys.dm_db_log_space_usage;
```

### Index Fragmentation

```sql
SELECT 
    OBJECT_SCHEMA_NAME(ips.object_id) AS schema_name,
    OBJECT_NAME(ips.object_id) AS table_name,
    i.name AS index_name,
    ips.index_type_desc,
    ips.avg_fragmentation_in_percent,
    ips.page_count,
    ips.record_count
FROM sys.dm_db_index_physical_stats(DB_ID(), NULL, NULL, NULL, 'LIMITED') ips
INNER JOIN sys.indexes i 
    ON ips.object_id = i.object_id 
    AND ips.index_id = i.index_id
WHERE ips.avg_fragmentation_in_percent > 10
    AND ips.page_count > 1000
ORDER BY ips.avg_fragmentation_in_percent DESC;
```

### Missing Indexes

```sql
SELECT TOP 20
    migs.avg_total_user_cost * migs.avg_user_impact * (migs.user_seeks + migs.user_scans) AS index_advantage,
    migs.last_user_seek,
    mid.statement AS table_name,
    mid.equality_columns,
    mid.inequality_columns,
    mid.included_columns,
    migs.user_seeks
FROM sys.dm_db_missing_index_groups mig
INNER JOIN sys.dm_db_missing_index_group_stats migs 
    ON migs.group_handle = mig.index_group_handle
INNER JOIN sys.dm_db_missing_index_details mid 
    ON mig.index_handle = mid.index_handle
WHERE migs.avg_total_user_cost * migs.avg_user_impact * (migs.user_seeks + migs.user_scans) > 10000
ORDER BY index_advantage DESC;
```

---

## Configuration

### Server Configuration

```sql
SELECT 
    name,
    value AS current_value,
    value_in_use,
    minimum,
    maximum,
    is_dynamic,
    is_advanced,
    description
FROM sys.configurations
ORDER BY name;
```

### Database Options

```sql
SELECT 
    name AS database_name,
    compatibility_level,
    recovery_model_desc,
    state_desc,
    is_auto_close_on,
    is_auto_shrink_on,
    is_auto_create_stats_on,
    is_auto_update_stats_on
FROM sys.databases
WHERE database_id > 4
ORDER BY name;
```

---

## SQL Server Version

```sql
SELECT 
    SERVERPROPERTY('ProductVersion') AS version,
    SERVERPROPERTY('ProductLevel') AS service_pack,
    SERVERPROPERTY('Edition') AS edition,
    SERVERPROPERTY('EngineEdition') AS engine_edition,
    CASE SERVERPROPERTY('EngineEdition')
        WHEN 1 THEN 'Personal/Desktop'
        WHEN 2 THEN 'Standard'
        WHEN 3 THEN 'Enterprise'
        WHEN 4 THEN 'Express'
        WHEN 5 THEN 'Azure SQL Database'
        WHEN 6 THEN 'Azure SQL Data Warehouse'
        ELSE 'Unknown'
    END AS edition_desc;
```

---

## Quick Reference: Common Thresholds

| Metric | Warning | Critical |
|--------|---------|----------|
| Page Life Expectancy | < 300s | < 100s |
| CPU Usage | > 80% | > 90% |
| Blocking Duration | > 30s | > 60s |
| I/O Latency | > 15ms | > 25ms |
| Connection Time | > 100ms | > 500ms |

---

## Usage Notes

1. **Performance Impact**: Some queries can be resource-intensive
2. **Permissions**: Require VIEW SERVER STATE
3. **Version Compatibility**: SQL Server 2012+
4. **Sampling**: Use periodic sampling rather than continuous queries
