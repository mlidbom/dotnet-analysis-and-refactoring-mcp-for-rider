using System.Diagnostics;
using System.IO;
using JetBrains.Application.Progress;
using JetBrains.DocumentModel;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.Caches;
using JetBrains.ReSharper.Psi.Search;
using JetBrains.ReSharper.Psi.Tree;
using JetBrains.Util;

namespace HeadlessHost;

/// <summary>Finds source references to one uniquely named member, reporting one hit per file and line as Royder does.</summary>
sealed class UsageSearch(ISolution solution)
{
   public bool Print(string symbol)
   {
      var timer = Stopwatch.StartNew();
      var services = solution.GetPsiServices();
      var member = ResolveMember(symbol);
      if(member == null) return false;
      using var context = CompilationContextCookie.GetOrCreate(member.GetSourceFiles().First().ResolveContext);
      var lines = new SortedSet<string>(StringComparer.Ordinal);
      services.Finder.FindReferences(member, SearchDomainFactory.Instance.CreateSearchDomain(solution, false),
         new FindResultConsumer(result =>
         {
            if(result is FindResultReference hit)
            {
               var node = hit.Reference.GetTreeNode();
               var file = node.GetSourceFile()!.GetLocation().FullPath;
               var line = node.GetDocumentRange().StartOffset.ToDocumentCoords().Line;
               lines.Add($"{Path.GetRelativePath(solution.SolutionDirectory.FullPath, file)}:{(int)line + 1}");
            }
            return FindExecution.Continue;
         }), NullProgressIndicator.Create());
      timer.Stop();
      foreach(var line in lines) Console.WriteLine(line);
      Console.WriteLine($"USAGES {symbol} count={lines.Count} elapsedMs={timer.ElapsedMilliseconds}");
      return true;
   }

   ITypeMember? ResolveMember(string symbol)
   {
      var parts = symbol.Split('.');
      if(parts.Length == 2)
      {
         var scope = solution.GetPsiServices().Symbols.GetSymbolScope(LibrarySymbolScope.NONE, caseSensitive: true);
         var members = scope.GetElementsByShortName(parts[0]).OfType<ITypeElement>()
                            .SelectMany(it => it.GetMembers()).Where(it => it.ShortName == parts[1]).ToArray();
         if(members.Length == 1) return members[0];
      }
      Console.Error.WriteLine($"Expected Type.Member identifying one source member. Received '{symbol}'.");
      return null;
   }
}
