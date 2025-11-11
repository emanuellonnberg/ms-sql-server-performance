using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SqlDiagnostics.Core.Models;

/// <summary>
/// Represents structural findings discovered during DataSet diagnostics.
/// </summary>
public sealed class DataSetStructureAnalysis
{
    private readonly List<DataTableAnalysis> _tables = new();

    /// <summary>
    /// Gets the collection of per-table analysis results.
    /// </summary>
    public IReadOnlyList<DataTableAnalysis> Tables => new ReadOnlyCollection<DataTableAnalysis>(_tables);

    /// <summary>
    /// Gets a value indicating whether any table issues were detected.
    /// </summary>
    public bool HasIssues => _tables.Any(t => t.Issues.Count > 0);

    /// <summary>
    /// Adds a new table analysis entry.
    /// </summary>
    /// <param name="analysis">Table analysis to add.</param>
    public void AddTable(DataTableAnalysis analysis)
    {
        if (analysis is null)
        {
            throw new ArgumentNullException(nameof(analysis));
        }

        _tables.Add(analysis);
    }
}

/// <summary>
/// Structural diagnostics collected for a single <see cref="System.Data.DataTable"/>.
/// </summary>
public sealed class DataTableAnalysis
{
    private readonly List<string> _issues = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="DataTableAnalysis"/> class.
    /// </summary>
    /// <param name="tableName">Name of the table being analyzed.</param>
    public DataTableAnalysis(string tableName)
    {
        TableName = string.IsNullOrWhiteSpace(tableName) ? "<unnamed>" : tableName;
    }

    /// <summary>
    /// Gets the table name.
    /// </summary>
    public string TableName { get; }

    /// <summary>
    /// Gets or sets the number of rows in the table.
    /// </summary>
    public int RowCount { get; set; }

    /// <summary>
    /// Gets or sets the number of columns in the table.
    /// </summary>
    public int ColumnCount { get; set; }

    /// <summary>
    /// Gets or sets the number of DataRelations associated with the table.
    /// </summary>
    public int RelationCount { get; set; }

    /// <summary>
    /// Gets or sets the number of columns participating in the primary key.
    /// </summary>
    public int PrimaryKeyColumnCount { get; set; }

    /// <summary>
    /// Gets the collection of diagnostic issues identified for the table.
    /// </summary>
    public IReadOnlyList<string> Issues => _issues;

    /// <summary>
    /// Adds an issue affecting the table.
    /// </summary>
    /// <param name="issue">Issue to register.</param>
    public void AddIssue(string issue)
    {
        if (string.IsNullOrWhiteSpace(issue))
        {
            return;
        }

        _issues.Add(issue);
    }
}
