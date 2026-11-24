#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
   public static DeclarationSource? Of(IDeclaredElement element, IDeclaration declaration)
   {
      var range = TreeNodeExtensions.GetDocumentRange(declaration);
      if(!range.IsValid()) return null;

      var document = range.Document;
      var first = (int)range.StartOffset.ToDocumentCoords().Line;
      var last = (int)range.EndOffset.ToDocumentCoords().Line;
      var lines = Enumerable.Range(first, last - first + 1).Select(it => LineText(document, it)).ToList();
      return new DeclarationSource(PsiHelpers.GetQualifiedName(element), declaration.GetSourceFile()?.GetLocation().FullPath ?? "", first + 1, lines);
   }

   /// <summary>
   ///   The heading and the numbered lines. Past <paramref name="maxCharacters"/> of source, the lines stop before the
   ///   line that would exceed it, and a last line says where to read on.
   /// </summary>
   public string Text(int maxCharacters)
   {
      var text = new StringBuilder($"{_qualifiedName} — {(_file.Length > 0 ? _file : "[no file]")}:{_firstLine}-{LastLine}");
      var numberWidth = LastLine.ToString().Length;
      var characters = 0;
      for(var index = 0; index < _lines.Count; index++)
      {
         characters += _lines[index].Length + 1;
         if(characters > maxCharacters)
            return text.Append($"\n… cut at {maxCharacters} characters: the declaration goes on from line {_firstLine + index} to {LastLine}; read the file for the rest.").ToString();

         text.Append('\n').Append((_firstLine + index).ToString().PadLeft(numberWidth)).Append('\t').Append(_lines[index]);
      }

      return text.ToString();
   }

   static string LineText(IDocument document, int zeroBasedLine)
   {
      var line = (Int32<DocLine>)zeroBasedLine;
      return document.GetText(new TextRange(document.GetLineStartOffset(line), document.GetLineEndOffsetNoLineBreak(line)));
   }
}
