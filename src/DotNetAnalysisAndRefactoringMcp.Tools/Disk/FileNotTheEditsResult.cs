namespace DotNetAnalysisAndRefactoringMcp.Disk;

/// <summary>
///   A file an edit changed that, once Rider had saved it, held other text than the edit gave Rider's copy: Rider's
///   copies of the file disagreed as it edited, and the edit was applied to the other one, at the wrong places.
/// </summary>
/// <remarks>
///   <para>
///     Rider's backend computes an edit on its copy of a file and sends it to the frontend as changes at offsets, which
///     the frontend applies to its own copy and saves. When one of the two copies hasn't taken in a change on disk yet,
///     the changes land at the wrong places. On 2026-11-24 a rename in a file a script had rewritten seconds before
///     turned <c>KindOfUnansweredItem WaitingForYourGo</c> into <c>KinSuggestionsm WaitingForYourGo</c>, while the
///     private Rider was still opening the solution; <c>tests/live_edit_after_disk_change.py</c> reproduces it with a
///     rename that comes as the file is written.
///   </para>
///   <para>
///     Rider then takes the saved file in as a change on disk, so its copy matches the file and nothing looks unsaved:
///     only the text the edit gave Rider's copy tells.
///   </para>
/// </remarks>
sealed class FileNotTheEditsResult(string file, string howTheFileDiffers)
{
   public string File { get; } = file;

   /// <summary>The first line in which the file differs from the edit's result, or why it couldn't be read.</summary>
   public string HowTheFileDiffers { get; } = howTheFileDiffers;
}
