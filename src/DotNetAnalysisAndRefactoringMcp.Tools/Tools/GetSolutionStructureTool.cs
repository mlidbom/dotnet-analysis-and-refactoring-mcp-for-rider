using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using JetBrains.ProjectModel;
using JetBrains.ProjectModel.Impl;
using Newtonsoft.Json.Linq;

namespace DotNetAnalysisAndRefactoringMcp.Tools;

public class GetSolutionStructureTool : IMcpTool
{
   readonly ISolution _solution;

   public GetSolutionStructureTool(ISolution solution) => _solution = solution;

   public string Name => "get_solution_structure";
   public ToolAnnotations Annotations => ToolAnnotations.ReadOnly;

   public string Description =>
      "Get the solution structure: all projects with their paths, target frameworks, " +
      "and project-to-project references. Useful for understanding the architecture and dependency graph.";

   public object InputSchema => new
                                {
                                   type = "object",
                                   properties = new
                                                {
                                                   includeFiles = new { type = "boolean", description = "If true, include file lists for each project. Default: false (can be verbose)." }
                                                },
                                   required = Array.Empty<string>()
                                };

   public object Execute(JObject arguments)
   {
      var includeFiles = arguments["includeFiles"]?.Value<bool>() ?? false;

      var solutionPath = _solution.SolutionFilePath.FullPath;
      var root = SolutionRoot.Of(_solution);
      var sb = new StringBuilder();

      var projects = _solution.GetAllProjects()
                              .Where(p => p.ProjectFileLocation != null && !p.ProjectFileLocation.IsEmpty)
                              .ToList();

      sb.Append(solutionPath).Append(" — ").Append(projects.Count).Append(" projects").Append('\n');

      foreach(var project in projects)
      {
         sb.Append('\n');
         sb.Append("[").Append(project.Name).Append("] ").Append(root.Relative(project.ProjectFileLocation.FullPath)).Append('\n');

         // Target frameworks
         var tfms = project.TargetFrameworkIds;
         if(tfms != null && tfms.Any())
            sb.Append("  frameworks: ").Append(string.Join(", ", tfms.Select(t => t.ToString()))).Append('\n');

         // Project references
         var projectRefs = new HashSet<string>();
         foreach(var tfm in project.TargetFrameworkIds ?? Enumerable.Empty<JetBrains.Util.Dotnet.TargetFrameworkIds.TargetFrameworkId>())
         {
            foreach(var reference in project.GetProjectReferences(tfm))
            {
               var refName = ProjectReferenceExtension.GetReferencedName(reference);
               if(!string.IsNullOrEmpty(refName))
                  projectRefs.Add(refName);
            }
         }

         if(projectRefs.Count > 0)
            sb.Append("  references: ").Append(string.Join(", ", projectRefs.OrderBy(r => r))).Append('\n');

         // File count
         var fileCount = project.GetAllProjectFiles().Count();
         sb.Append("  files: ").Append(fileCount.ToString()).Append('\n');

         // File list (optional)
         if(includeFiles)
         {
            foreach(var file in project.GetAllProjectFiles().OrderBy(f => f.Location.FullPath))
            {
               sb.Append("    ").Append(root.Relative(file.Location.FullPath)).Append('\n');
            }
         }
      }

      return sb.ToString().TrimEnd();
   }
}
