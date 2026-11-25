#nullable enable
using System.Collections.Generic;
using System.Linq;
using JetBrains.DocumentModel;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.Tree;
using JetBrains.Util;
using JetBrains.Util.dataStructures.TypedIntrinsics;

namespace DotNetAnalysisAndRefactoringMcp;

/// <summary>
///   The source of one declaration as a file read shows it: its whole lines, each after its 1-based line number, under
///   a heading that names the declared element and where it is, <c>Shapes.Palette.Pick — C:\…\Palette.cs:8-9</c>.
/// </summary>
/// <remarks>
///   Whole lines, the first one's indentation included, so that an agent can quote them to an edit as they are. Until
///   2026-11-24 get_symbol_source answered with the declaration's text in a JSON string, every newline and quote
///   escaped, without line numbers, and its first line cut at the declaration's first column.
/// </remarks>
public sealed class DeclarationSource
{
   readonly string _qualifiedName;
   readonly string _file;
   readonly int _firstLine;
   readonly IReadOnlyList<string> _lines;

   DeclarationSource(string qualifiedName, string file, int firstLine, IReadOnlyList<string> lines)
   {
      _qualifiedName = qualifiedName;
      _file = file;
      _firstLine = firstLine;
      _lines = lines;
   }

   int LastLine => _firstLine + _lines.Count - 1;

   /// <summary>
   ///   <paramref name="declaration"/>'s source, named as <paramref name="element"/>; nothing for a declaration with no
   ///   range in a document, such as a source-generated one.
   /// </summary>
   public static DeclarationSource? Of(IDeclaredElement element, IDeclaration declaration) => From(element, declaration, declaration);

   /// <summary>
   ///   <paramref name="declaration"/>'s source from the first line after its doc comment: its attributes, its signature
   ///   and its body, as <c>go_to_definition</c> shows them under the comment's summary.
   /// </summary>
   public static DeclarationSource? AfterTheDocCommentOf(IDeclaredElement element, IDeclaration declaration) =>
      From(element, declaration, FirstNodeAfterTheDocCommentOf(declaration));

   /// <summary>
   ///   <paramref name="declaration"/>'s source from the line <paramref name="firstNode"/>, one of its parts, starts on, to
   ///   its last line.
   /// </summary>
   static DeclarationSource? From(IDeclaredElement element, IDeclaration declaration, ITreeNode firstNode)
   {
      var range = TreeNodeExtensions.GetDocumentRange(declaration);
      if(!range.IsValid()) return null;

      var document = range.Document;
      var first = (int)TreeNodeExtensions.GetDocumentRange(firstNode).StartOffset.ToDocumentCoords().Line;
      var last = (int)range.EndOffset.ToDocumentCoords().Line;
      var lines = Enumerable.Range(first, last - first + 1).Select(it => LineText(document, it)).ToList();
      return new DeclarationSource(PsiHelpers.GetQualifiedName(element), declaration.GetSourceFile()?.GetLocation().FullPath ?? "", first + 1, lines);
   }

   /// <summary>The first of <paramref name="declaration"/>'s parts that isn't its doc comment, or the line break after it.</summary>
   static ITreeNode FirstNodeAfterTheDocCommentOf(IDeclaration declaration)
   {
      for(var child = declaration.FirstChild; child != null; child = child.NextSibling)
         if(child is not (IDocCommentBlock or IWhitespaceNode))
            return child;

      return declaration;
   }

   /// <summary>The <see cref="Heading"/>, then the <see cref="Lines"/>.</summary>
   public string Text(int maxCharacters) => $"{Heading}\n{Lines(maxCharacters)}";

   /// <summary>The declared element's name, and where its source is: <c>Shapes.Palette.Pick — C:\…\Palette.cs:8-9</c>.</summary>
   string Heading => $"{_qualifiedName} — {(_file.Length > 0 ? _file : "[no file]")}:{_firstLine}-{LastLine}";

   /// <summary>
   ///   The numbered lines. Past <paramref name="maxCharacters"/> of source, they stop before the line that would exceed
   ///   it, though never before the first, and a last line says where to read on.
   /// </summary>
   public string Lines(int maxCharacters)
   {
      var numberWidth = LastLine.ToString().Length;
      var lines = new List<string>();
      var characters = 0;
      for(var index = 0; index < _lines.Count; index++)
      {
         characters += _lines[index].Length + 1;
         if(characters > maxCharacters && index > 0)
         {
            lines.Add($"… cut at {maxCharacters} characters: the declaration goes on from line {_firstLine + index} to {LastLine}; read the file for the rest.");
            break;
         }

         lines.Add($"{(_firstLine + index).ToString().PadLeft(numberWidth)}\t{_lines[index]}");
      }

      return string.Join("\n", lines);
   }

   static string LineText(IDocument document, int zeroBasedLine)
   {
      var line = (Int32<DocLine>)zeroBasedLine;
      return document.GetText(new TextRange(document.GetLineStartOffset(line), document.GetLineEndOffsetNoLineBreak(line)));
   }
}
