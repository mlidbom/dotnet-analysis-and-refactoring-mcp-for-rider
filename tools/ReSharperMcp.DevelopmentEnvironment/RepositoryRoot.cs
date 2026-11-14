namespace ReSharperMcp.DevelopmentEnvironment;

/// <summary>The root of the repository these tools were built from: the folder that holds <c>ReSharperMcp.sln</c>.</summary>
public static class RepositoryRoot
{
  public static string Folder { get; } = FindAbove(AppContext.BaseDirectory);

  static string FindAbove(string folder) =>
    File.Exists(Path.Combine(folder, "ReSharperMcp.sln"))
      ? folder
      : FindAbove(Directory.GetParent(folder)?.FullName
                  ?? throw new InvalidOperationException($"No ReSharperMcp.sln above {AppContext.BaseDirectory}: the tools run from their build output inside the repository."));
}
