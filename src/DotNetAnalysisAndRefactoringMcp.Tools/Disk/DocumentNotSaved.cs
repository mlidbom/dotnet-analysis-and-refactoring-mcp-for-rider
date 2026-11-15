namespace DotNetAnalysisAndRefactoringMcp.Disk;

/// <summary>
///   A document an edit changed whose file still didn't hold its text when the call stopped waiting for Rider to save
///   it: the file, and how it differed from Rider's copy, for the answer's note.
/// </summary>
/// <remarks>
///   The note said only that Rider hadn't saved the file, and one such wait, in a specification run on 2026-11-15, didn't
///   recur by hand: how the file differs is what tells why the next time.
/// </remarks>
sealed class DocumentNotSaved(string file, string howTheFileDiffers)
{
   public string File { get; } = file;

   /// <summary>The first line in which the file differs from Rider's copy, or why it couldn't be read.</summary>
   public string HowTheFileDiffers { get; } = howTheFileDiffers;
}
