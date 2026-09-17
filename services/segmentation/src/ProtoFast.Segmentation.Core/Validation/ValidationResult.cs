namespace ProtoFast.Segmentation.Core.Validation;

/// <summary>
/// One check's verdict. A failure carries a precise, ID-bearing error report because that report
/// is fed straight back to the agent that produced the artifact as the repair prompt (plan §9.8)
/// — vague messages make bad repairs.
/// </summary>
public sealed record ValidationResult(string CheckId, bool Passed, IReadOnlyList<string> Errors)
{
    public static ValidationResult Pass(string checkId) => new(checkId, true, []);

    public static ValidationResult Fail(string checkId, params string[] errors) => new(checkId, false, errors);

    public static ValidationResult Fail(string checkId, IEnumerable<string> errors)
    {
        var list = errors.ToList();
        return new ValidationResult(checkId, list.Count == 0, list);
    }

    /// <summary>The repair prompt's body: what failed, on which ids, expected versus actual.</summary>
    public string ErrorReport => Passed
        ? string.Empty
        : $"Check '{CheckId}' failed:{Environment.NewLine}"
          + string.Join(Environment.NewLine, Errors.Take(MaxReportedErrors).Select(e => "- " + e))
          + (Errors.Count > MaxReportedErrors
              ? $"{Environment.NewLine}- …and {Errors.Count - MaxReportedErrors} more"
              : string.Empty);

    /// <summary>
    /// Enough for a model to see the pattern, few enough that the repair prompt stays small. A
    /// thousand-error report is a systematic mistake, and the first twenty show it just as well.
    /// </summary>
    private const int MaxReportedErrors = 20;
}

/// <summary>The outcome of running a set of checks; <see cref="Passed"/> is the gate.</summary>
public sealed record ValidationReport(IReadOnlyList<ValidationResult> Results)
{
    public bool Passed => Results.All(r => r.Passed);

    public IReadOnlyList<ValidationResult> Failures => [.. Results.Where(r => !r.Passed)];

    public string ErrorReport => string.Join(
        Environment.NewLine + Environment.NewLine,
        Failures.Select(f => f.ErrorReport));
}
