#nullable enable
using System;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.Resolve;
using JetBrains.ReSharper.Psi.Tree;

namespace DotNetAnalysisAndRefactoringMcp.CallTrees;

/// <summary>
///   Where a <see cref="Caller"/>'s code calls the member above it in an <see cref="IncomingCallTree"/>: the line, as
///   <c>find_usages</c> shows it, and how the call reaches the member.
/// </summary>
public sealed class CallSite
{
   /// <summary>The file's full path; empty for a file that has none.</summary>
   public string File { get; }

   /// <summary>For a call in a file a source generator wrote, which has no path, that file's name: <see cref="GeneratedFile"/>.</summary>
   public string? GeneratedFileName { get; }

   /// <summary>1-based: the line of the called member's name, not of the start of an expression it is the end of.</summary>
   public int Line { get; }

   /// <summary>1-based: the column of the called member's name, which tells two calls on one line apart.</summary>
   public int Column { get; }

   /// <summary>The line's text, trimmed.</summary>
   public string Code { get; }

   /// <summary>
   ///   The member the call names, when it isn't the one above but one that member overrides or implements, such as
   ///   <c>Shapes.IShape.Area</c> for <c>Shapes.Circle.Area</c>: the call runs the one above when the object is of its type.
   /// </summary>
   public IDeclaredElement? Through { get; }

   /// <summary>Whether the code passes the member as a delegate, <c>shapes.Select(AreaOf)</c>, rather than calling it.</summary>
   public bool AsDelegate { get; }

   CallSite(string file, string? generatedFileName, int line, int column, string code, IDeclaredElement? through, bool asDelegate)
   {
      File = file;
      GeneratedFileName = generatedFileName;
      Line = line;
      Column = column;
      Code = code;
      Through = through;
      AsDelegate = asDelegate;
   }

   /// <summary>
   ///   Its file as the text answers write it: <c>Shapes\Palette.cs</c>, the path relative to <paramref name="root"/>, or
   ///   the <see cref="GeneratedFileName"/>.
   /// </summary>
   public string FileAsWritten(SolutionRoot root) => GeneratedFileName ?? root.Relative(File);

   /// <summary>Whether it is in the file of <paramref name="declaration"/>: never when that is nothing.</summary>
   public bool IsInTheFileOf(DeclarationSite? declaration) =>
      string.Equals(File, declaration?.File, StringComparison.OrdinalIgnoreCase) && GeneratedFileName == declaration?.GeneratedFileName;

   /// <summary>
   ///   The site of <paramref name="call"/>, made through <paramref name="reference"/>, which names
   ///   <paramref name="through"/> when it isn't the member above; nothing for one with no place in a file.
   /// </summary>
   public static CallSite? Of(IReference reference, Call call, IDeclaredElement? through)
   {
      var node = reference.GetTreeNode();
      if(node.GetSourceFile() is not { } sourceFile || node.GetContainingFile() is not { } file)
         return null;

      var range = file.GetDocumentRange(reference.GetTreeTextRange());
      if(!range.IsValid())
         return null;

      var (line, column) = PsiHelpers.GetLineColumn(range.StartOffset);
      return new CallSite(sourceFile.GetLocation().FullPath, GeneratedFile.NameOf(sourceFile), line, column,
                          PsiHelpers.LineText(range.Document, line), through, call.Reached == HowReached.AsDelegate);
   }
}
