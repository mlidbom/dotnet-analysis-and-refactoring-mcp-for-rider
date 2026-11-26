using DotNetAnalysisAndRefactoringMcp.DevelopmentEnvironment;
using DotNetAnalysisAndRefactoringMcp.DevelopmentEnvironment.Processes;

namespace DotNetAnalysisAndRefactoringMcp.Specifications;

/// <summary>
///   A fresh copy of the fixture solution (<c>tests/DotNetAnalysisAndRefactoringMcp.FixtureSolution</c>), which a
///   sandbox Rider opens: the specifications change their copy, never the original.
/// </summary>
static class FixtureSolutionCopy
{
   /// <summary>
   ///   Makes a copy in <paramref name="folder"/>, replacing the last run's, so that no run sees what an earlier one
   ///   changed. It is a git repository whose one commit is the copy: what a specification changes there is then changed
   ///   since <c>HEAD</c>, as the tools that take <c>changedSince</c> see it.
   /// </summary>
   public static void MakeIn(string folder)
   {
      if(Directory.Exists(folder)) DeleteWithItsRepository(folder);
      var original = Path.Combine(RepositoryRoot.Folder, "tests", TheSandboxWithTheFixtureSolution.SolutionName);
      Directory.CreateDirectory(folder);
      foreach(var subfolder in Directory.GetDirectories(original, "*", SearchOption.AllDirectories).Where(IsSource))
         Directory.CreateDirectory(Path.Combine(folder, Path.GetRelativePath(original, subfolder)));
      foreach(var file in Directory.GetFiles(original, "*", SearchOption.AllDirectories).Where(IsSource))
         File.Copy(file, Path.Combine(folder, Path.GetRelativePath(original, file)));
      PutTheSourceGeneratorIn(folder);
      CommitAsTheOnlyCommit(folder);

      bool IsSource(string path) =>
         !Path.GetRelativePath(original, path).Split(Path.DirectorySeparatorChar).Any(it => it is "bin" or "obj" or ".idea");
   }

   /// <summary>
   ///   <see cref="TheFixtureSourceGenerator"/>, built with the specifications, which reference it for this:
   ///   <c>Shapes.csproj</c> names it, where a copy has it.
   /// </summary>
   /// <remarks>
   ///   Not a project of the fixture solution's: Rider runs a source generator only once it is built, and a project of
   ///   its own would show in the tools' answers about the solution.
   /// </remarks>
   static void PutTheSourceGeneratorIn(string folder)
   {
      var generators = Directory.CreateDirectory(Path.Combine(folder, "SourceGenerators")).FullName;
      File.Copy(Path.Combine(AppContext.BaseDirectory, TheFixtureSourceGenerator.AssemblyFileName),
                Path.Combine(generators, TheFixtureSourceGenerator.AssemblyFileName));
   }

   static void CommitAsTheOnlyCommit(string folder)
   {
      string[] identity = ["-c", "user.name=Specifications", "-c", "user.email=specifications@localhost"];
      File.WriteAllLines(Path.Combine(folder, ".gitignore"), ["bin/", "obj/", ".idea/"]);
      ExternalCommand.Run("git", ["init", "--quiet"], folder);
      ExternalCommand.Run("git", ["add", "--all"], folder);
      ExternalCommand.Run("git", [.. identity, "commit", "--quiet", "--message", "The fixture solution as copied"], folder);
   }

   /// <summary>Git makes the files of its object store read-only, which <see cref="Directory.Delete(string, bool)"/> refuses to delete.</summary>
   static void DeleteWithItsRepository(string folder)
   {
      foreach(var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
         File.SetAttributes(file, FileAttributes.Normal);
      Directory.Delete(folder, recursive: true);
   }
}
