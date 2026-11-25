#nullable enable
namespace DotNetAnalysisAndRefactoringMcp.Tools;

/// <summary>
///   A file that a rename renamed, as Rider's Rename renames a type's file named after it: its full path before, and
///   after.
/// </summary>
sealed class RenamedFile(string from, string to)
{
   public string From { get; } = from;
   public string To { get; } = to;
}
