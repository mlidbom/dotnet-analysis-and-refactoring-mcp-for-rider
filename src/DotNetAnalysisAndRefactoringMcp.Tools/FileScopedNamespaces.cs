#nullable enable
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using JetBrains.ReSharper.Psi.CSharp.Tree;
using JetBrains.ReSharper.Psi.CSharp.Util;

namespace DotNetAnalysisAndRefactoringMcp;

/// <summary>
///   ReSharper's conversion of a block-scoped namespace declaration to a file-scoped one, across Rider versions: the one
///   in <see cref="CSharpNamespaceUtil"/>, found by name and called with the defaults of whatever parameters it has.
/// </summary>
/// <remarks>
///   Rider 2026 gave <see cref="CSharpNamespaceUtil.ConvertToFileScoped"/> an optional progress indicator, which the
///   2025.3 SDK's has not, so a call compiled against the SDK isn't found in Rider 2026: a
///   <c>MissingMethodException</c>, which failed every move (2026-11-15).
/// </remarks>
static class FileScopedNamespaces
{
   static readonly MethodInfo Convert = typeof(CSharpNamespaceUtil).GetMethods().Single(it => it.Name == nameof(CSharpNamespaceUtil.ConvertToFileScoped));

   /// <summary>Makes <paramref name="declaration"/> file-scoped, with the types it holds.</summary>
   public static void ConvertToFileScoped(ICSharpNamespaceDeclaration declaration)
   {
      try
      {
         Convert.Invoke(null, [declaration, .. Convert.GetParameters().Skip(1).Select(it => it.DefaultValue)]);
      }
      catch(TargetInvocationException exception) when(exception.InnerException != null)
      {
         ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
         throw;
      }
   }
}
