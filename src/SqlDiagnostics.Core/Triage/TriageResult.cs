using System;
using System.Collections.Generic;

namespace SqlDiagnostics.Core.Triage;

/// <summary>
/// Aggregated output from <see cref="QuickTriage"/>.
/// </summary>
public sealed class TriageResult
{
    public DateTime StartedAtUtc { get; set; }
    public DateTime CompletedAtUtc { get; set; }
    public TimeSpan Duration { get; set; }

    public TestResult Network { get; set; } = TestResult.Pending("Network");
    public TestResult Connection { get; set; } = TestResult.Pending("Connection");
    public TestResult Query { get; set; } = TestResult.Pending("Query");
    public TestResult Server { get; set; } = TestResult.Pending("Server");
    public TestResult Blocking { get; set; } = TestResult.Pending("Blocking");

    public Diagnosis Diagnosis { get; set; } = Diagnosis.Unknown();
}

/// <summary>
/// Represents the outcome of an individual triage probe.
/// </summary>
public sealed class TestResult
{
    private readonly List<string> _issues = new();

    public TestResult(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Test name must be provided.", nameof(name));
        }

        Name = name;
    }

    public string Name { get; }
    public bool Success { get; set; }
    public TimeSpan Duration { get; set; }
    public string Details { get; set; } = string.Empty;
    public IReadOnlyList<string> Issues => _issues;

    internal static TestResult Pending(string name) => new(name)
    {
        Success = false,
        Details = "Not executed."
    };

    internal static TestResult Failure(string name, string details) => new(name)
    {
        Success = false,
        Details = details
    };

    public void AddIssue(string issue)
    {
        if (string.IsNullOrWhiteSpace(issue))
        {
            return;
        }

        _issues.Add(issue);
    }
}

/// <summary>
/// High-level diagnosis produced by the triage workflow.
/// </summary>
public sealed class Diagnosis
{
    public string Category { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public IList<string> Recommendations { get; } = new List<string>();

    internal static Diagnosis Unknown() => new()
    {
        Category = "Unknown",
        Summary = "Diagnosis has not been computed."
    };
}
