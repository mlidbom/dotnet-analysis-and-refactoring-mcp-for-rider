#nullable enable
using System.IO;

namespace DotNetAnalysisAndRefactoringMcp.Serving.Routing;

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
      GitWorkTree = WorkTreeOf(folder);
   }

   public static string? WorkTreeOf(string? directory)
   {
      for(var folder = directory; folder != null; folder = Path.GetDirectoryName(folder))
         if(IsWorkTreeRoot(folder)) return folder;
      return null;
   }

   /// <summary>Whether <paramref name="folder"/> is the root of a Git work tree: it holds a <c>.git</c> folder, or a linked work tree's <c>.git</c> file.</summary>
   public static bool IsWorkTreeRoot(string folder)
   {
      var marker = Path.Combine(folder, ".git");
      return Directory.Exists(marker) || File.Exists(marker);
   }

   public string DescribeWorkTree() => GitWorkTree ?? $"{Absolute} (outside a Git work tree)";
}
