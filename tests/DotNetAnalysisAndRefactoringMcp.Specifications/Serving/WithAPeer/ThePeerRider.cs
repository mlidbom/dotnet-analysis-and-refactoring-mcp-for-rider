namespace DotNetAnalysisAndRefactoringMcp.Specifications.Serving.WithAPeer;

/// <summary>
///   The peer sandbox Rider with a fresh copy of the fixture solution of its own open: one with a type the first one's
///   copy doesn't have, <c>OnlyInThePeer</c>.
/// </summary>
/// <remarks>Started for the specifications in the collection <see cref="WithThePeerRider"/>, and stopped after them.</remarks>
public sealed class ThePeerRider : APeerRiderWithASolutionOpen
{
   readonly string _solutionFolder;

   public ThePeerRider(TheSandboxWithTheFixtureSolution sandbox) : base(sandbox)
   {
      _solutionFolder = Path.Combine(PeersSolutionsFolder,TheSandboxWithTheFixtureSolution.SolutionName);
      FixtureSolutionCopy.MakeIn(_solutionFolder);
      File.WriteAllText(PathOf("Shapes/OnlyInThePeer.cs"),
                        "namespace Shapes;\n\n/// <summary>A type only the peer's copy of the fixture solution has.</summary>\npublic sealed class OnlyInThePeer;\n");
      Open(PathOf("Shapes.Drawing/Drawing.cs"));
   }

   /// <summary>The peer's copy of the fixture solution.</summary>
   public override string SolutionPath => PathOf(TheSandboxWithTheFixtureSolution.SolutionFile);

   /// <summary>The full path of a file of the peer's copy of the fixture solution, given its path within the solution.</summary>
   public string PathOf(string pathInTheSolution) => Path.GetFullPath(Path.Combine(_solutionFolder, pathInTheSolution));
}
