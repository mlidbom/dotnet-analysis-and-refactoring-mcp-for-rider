#nullable enable
using System.IO;

namespace DotNetAnalysisAndRefactoringMcp.Serving;

/// <summary>A file or folder named in a call, resolved to an absolute path before choosing an open solution.</summary>
sealed class RequestedPath
{
   public string Absolute { get; }

   /// <summary>The nearest enclosing Git work tree, including a linked work tree whose .git is a file.</summary>
   public string? GitWorkTree { get; }

   /// <summary>Zero for an existing path. For a new relative path, its existing folders help identify the intended solution.</summary>
   public int MissingComponents { get; }

   public RequestedPath(string absolute)
   {
      Absolute = Path.GetFullPath(absolute);
      string? existing = Absolute;
      while(existing != null && !Directory.Exists(existing) && !File.Exists(existing))
      {
         MissingComponents++;
         existing = Path.GetDirectoryName(existing);
      }

      var folder = File.Exists(existing) ? Path.GetDirectoryName(existing) : existing;
      var root = SolutionRoot.Of(folder).Workspace;
      if(root == null) return;
      var marker = Path.Combine(root, ".git");
      if(Directory.Exists(marker) || File.Exists(marker)) GitWorkTree = root;
   }

   public string DescribeWorkTree() => GitWorkTree ?? $"{Absolute} (outside a Git work tree)";
}
