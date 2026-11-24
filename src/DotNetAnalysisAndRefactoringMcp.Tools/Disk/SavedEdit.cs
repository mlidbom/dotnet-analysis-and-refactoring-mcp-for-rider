using System.Collections.Generic;

namespace DotNetAnalysisAndRefactoringMcp.Disk;

/// <summary>
///   What became of the files an edit changed once the call saved them: those Rider hadn't saved in time, and those
///   that, saved, don't hold the edit's result.
/// </summary>
sealed class SavedEdit(List<DocumentNotSaved> notSaved, List<FileNotTheEditsResult> notTheEditsResult)
{
   public static SavedEdit Nothing => new([], []);

   public List<DocumentNotSaved> NotSaved { get; } = notSaved;
   public List<FileNotTheEditsResult> NotTheEditsResult { get; } = notTheEditsResult;
}
