namespace DotNetAnalysisAndRefactoringMcp.DevelopmentEnvironment;

/// <summary>
///   The root of the repository these tools were built from: the folder above their build output that holds the
///   frontend's <c>rider-plugin</c> folder and a solution file.
/// </summary>
public static class RepositoryRoot
{
  public static string Folder { get; } = FindAbove(AppContext.BaseDirectory);

  /// <summary>
  ///   The solution file's name without its extension, which starts the title of a Rider window that has it open.
  /// </summary>
  public static string SolutionName { get; } = Path.GetFileNameWithoutExtension(Directory.GetFiles(Folder, "*.sln").Single());

  static string FindAbove(string folder) =>
    Directory.Exists(Path.Combine(folder, "rider-plugin")) && Directory.GetFiles(folder, "*.sln").Length == 1
      ? folder
      : FindAbove(Directory.GetParent(folder)?.FullName
                  ?? throw new InvalidOperationException($"No repository root above {AppContext.BaseDirectory}: the tools run from their build output inside the repository."));
}
