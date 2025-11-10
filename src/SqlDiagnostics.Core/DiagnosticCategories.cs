using System;

namespace SqlDiagnostics.Core;

/// <summary>
/// Flags enumeration that specifies which diagnostic modules should run.
/// </summary>
[Flags]
public enum DiagnosticCategories
{
    None = 0,
    Connection = 1 << 0,
    Network = 1 << 1,
    Query = 1 << 2,
    Server = 1 << 3,
    Database = 1 << 4,
    All = Connection | Network | Query | Server | Database
}
