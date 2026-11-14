#nullable enable
using JetBrains.ReSharper.Feature.Services.Daemon;

namespace DotNetAnalysisAndRefactoringMcp
{
  /// <summary>Inspection severities as the tools take and print them: "error", "warning", "suggestion", "hint", "info".</summary>
  public static class Severities
  {
    /// <summary>Nothing for an empty or unknown name.</summary>
    public static Severity? Parse(string? name) =>
      name?.Trim().ToLowerInvariant() switch
      {
        "error" => Severity.ERROR,
        "warning" => Severity.WARNING,
        "suggestion" => Severity.SUGGESTION,
        "hint" => Severity.HINT,
        "info" => Severity.INFO,
        _ => null
      };

    public static string Name(Severity severity) =>
      severity switch
      {
        Severity.ERROR => "error",
        Severity.WARNING => "warning",
        Severity.SUGGESTION => "suggestion",
        Severity.HINT => "hint",
        Severity.INFO => "info",
        Severity.DO_NOT_SHOW => "none",
        _ => "unknown"
      };

    /// <summary>
    ///   Whether <paramref name="severity"/> is <paramref name="minimum"/> or more important. The enum is ordered by
    ///   importance (DO_NOT_SHOW &lt; INFO &lt; HINT &lt; SUGGESTION &lt; WARNING &lt; ERROR).
    /// </summary>
    public static bool AtLeast(Severity severity, Severity minimum) => (int)severity >= (int)minimum;
  }
}
