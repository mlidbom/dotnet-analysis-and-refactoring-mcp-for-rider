namespace DotNetAnalysisAndRefactoringMcp.Specifications.Serving.WithASecondSolutionOfTheWorkTree;

/// <summary>
///   The peer sandbox Rider with a second solution of the first sandbox's own copy of the fixture solution open, beside
///   the fixture solution's file: <c>Outlines.slnx</c>, which lists <c>Shapes</c>, as the fixture solution does, and
///   <c>Outlines</c>, a project only it lists. Two open solutions then hold every file of the copy's work tree, equally
///   closely by where their files lie, and only the projects they list tell them apart.
/// </summary>
/// <remarks>Started for the specifications in the collection <see cref="WithTheOutlinesSolutionInThePeer"/>, and stopped after them.</remarks>
public sealed class TheOutlinesSolutionInThePeer : APeerRiderWithASolutionOpen
{
   public const string SolutionFile = "Outlines.slnx";

   /// <summary>A file of the project only <see cref="SolutionFile"/> lists, declaring the type <c>Outline</c>.</summary>
   public const string OutlinesFile = "Outlines/Outline.cs";

   public TheOutlinesSolutionInThePeer(TheSandboxWithTheFixtureSolution sandbox) : base(sandbox)
   {
      Directory.CreateDirectory(sandbox.PathOf("Outlines"));
      File.WriteAllText(sandbox.PathOf("Outlines/Outlines.csproj"),
                        "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n");
      File.WriteAllText(sandbox.PathOf(OutlinesFile), "namespace Outlines;\n\n/// <summary>A type of the project only Outlines.slnx lists.</summary>\npublic sealed class Outline;\n");
      File.WriteAllText(SolutionPath, "<Solution>\n  <Project Path=\"Shapes/Shapes.csproj\" />\n  <Project Path=\"Outlines/Outlines.csproj\" />\n</Solution>\n");
      Open(sandbox.PathOf(OutlinesFile));
   }

   public override string SolutionPath => Sandbox.PathOf(SolutionFile);

   /// <summary>Stops the peer, and removes the solution and its project from the copy, where the specifications after these would find them changed since <c>HEAD</c>.</summary>
   public override void Dispose()
   {
      base.Dispose();
      File.Delete(SolutionPath);
      Directory.Delete(Sandbox.PathOf("Outlines"), recursive: true);
   }
}
