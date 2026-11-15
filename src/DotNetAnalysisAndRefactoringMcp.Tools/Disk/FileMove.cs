namespace DotNetAnalysisAndRefactoringMcp.Disk;

/// <summary>A file an edit moves on disk, once Rider has saved the edit: from where, to where.</summary>
public sealed class FileMove(string from, string to)
{
   public string From { get; } = from;

   public string To { get; } = to;
}
