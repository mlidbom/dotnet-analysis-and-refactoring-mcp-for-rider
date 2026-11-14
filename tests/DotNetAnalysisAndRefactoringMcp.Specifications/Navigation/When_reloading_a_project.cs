using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Navigation;

/// <summary>Rider's Reload Project, for a project whose model in Rider is stale, waited for until it's done.</summary>
public class When_reloading_a_project(TheSandboxWithTheFixtureSolution sandbox)
{
  readonly string _byAFile = sandbox.McpServer.CallTool("reload_projects", new { filePath = "Shapes/Circle.cs" });
  readonly string _byProjectFileAndFolder = sandbox.McpServer.CallTool("reload_projects", new { filePaths = new[] { sandbox.PathOf("Shapes.Drawing/Shapes.Drawing.csproj"), sandbox.PathOf("Shapes") } });

  [XF] public void it_reloads_the_project_a_file_is_in() => _byAFile.Must().StartWith("Reloaded in");
  [XF] public void and_names_it() => _byAFile.Must().Contain("Shapes.csproj");

  [XF] public void it_reloads_a_project_by_its_project_file() => _byProjectFileAndFolder.Must().Contain("Shapes.Drawing.csproj");
  [XF] public void and_one_by_its_folder() => _byProjectFileAndFolder.Must().Contain(Path.Combine("Shapes", "Shapes.csproj"));
}
