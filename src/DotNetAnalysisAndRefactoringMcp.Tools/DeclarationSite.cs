#nullable enable
using System.Linq;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.Tree;

namespace DotNetAnalysisAndRefactoringMcp;

/// <summary>
///   Where a declaration is, as an editor's go-to-declaration puts the caret: its file, and the line and column of
///   its name.
/// </summary>
/// <remarks>
///   Not the start of the declaration's own range, which includes its doc comment and attributes: until 2026-11-14
///   the tools gave the first line of a type's doc comment as the line it is declared on.
/// </remarks>
public sealed class DeclarationSite
{
   /// <summary>The file's full path; empty for a declaration whose file has none.</summary>
   public string File { get; }

   /// <summary>1-based.</summary>
   public int Line { get; }

   /// <summary>1-based.</summary>
   public int Column { get; }

   DeclarationSite(string file, int line, int column)
   {
      File = file;
      Line = line;
      Column = column;
   }

   /// <summary>Nothing for a declaration without a source file or a range in it.</summary>
   public static DeclarationSite? Of(IDeclaration declaration)
   {
      var sourceFile = declaration.GetSourceFile();
      if(sourceFile == null)
         return null;

      var range = declaration.GetNameDocumentRange();
      if(!range.IsValid())
         range = TreeNodeExtensions.GetDocumentRange(declaration);
      if(!range.IsValid())
         return null;

      var (line, column) = PsiHelpers.GetLineColumn(range.StartOffset);
      return new DeclarationSite(sourceFile.GetLocation().FullPath, line, column);
   }

   /// <summary>
   ///   The site of the element's <see cref="MainDeclaration"/> in a file with a path, or else of its first other one
   ///   there; nothing for compiled elements.
   /// </summary>
   public static DeclarationSite? OfMainDeclarationOf(IDeclaredElement element) =>
      MainDeclaration.InOrder(element).Select(Of).FirstOrDefault(it => it is { File.Length: > 0 });

   /// <summary>
   ///   The site of the element's <see cref="MainDeclaration"/>, or for a member with no declaration of its own, where
   ///   it is written: a positional record's property at the parameter of the same name in its type's header, anything
   ///   else, such as an implicit constructor, at its type's name. Nothing for compiled elements.
   /// </summary>
   public static DeclarationSite? WhereWritten(IDeclaredElement element) =>
      OfMainDeclarationOf(element) ?? WhereAMemberWithoutADeclarationIsWritten(element);

   static DeclarationSite? WhereAMemberWithoutADeclarationIsWritten(IDeclaredElement element)
   {
      if(element is not ITypeMember member || element is ITypeElement || member.ContainingType is not { } type)
         return null;

      return type.Constructors
                 .SelectMany(it => it.Parameters)
                 .Where(it => it.ShortName == member.ShortName)
                 .Select(OfMainDeclarationOf)
                 .FirstOrDefault(it => it != null)
          ?? OfMainDeclarationOf(type);
   }
}
