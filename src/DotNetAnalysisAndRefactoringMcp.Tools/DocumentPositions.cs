#nullable enable
using JetBrains.DocumentModel;
using JetBrains.Util;
using JetBrains.Util.dataStructures.TypedIntrinsics;

namespace DotNetAnalysisAndRefactoringMcp;

/// <summary>A tool call's 1-based line and column, turned into a document offset once they're checked against it.</summary>
/// <remarks>
///   Until 2026-11-15 each tool caught whatever <c>GetOffsetByCoords</c> threw for a position outside the file. A
///   check says what's out of range, and needs no catch.
/// </remarks>
public static class DocumentPositions
{
   /// <summary>The offset of <paramref name="line"/> and <paramref name="column"/>, or why the file has none.</summary>
   /// <remarks>The column may be one past a line's last character: the end of the line.</remarks>
   public static (int Offset, string? Error) OffsetOf(IDocument document, int line, int column)
   {
      var lines = (int)document.GetLineCount();
      if(line < 1 || line > lines)
         return (0, $"Line {line} is outside the file, which has {lines} lines.");

      var docLine = (Int32<DocLine>)(line - 1);
      var start = document.GetLineStartOffset(docLine);
      var end = document.GetLineEndOffsetNoLineBreak(docLine);
      if(column < 1 || column > end - start + 1)
         return (0, $"Column {column} is outside line {line}, which has {end - start} characters: {document.GetText(new TextRange(start, end)).Trim()}");

      return (document.GetOffsetByCoords(new DocumentCoords(docLine, (Int32<DocColumn>)(column - 1))), null);
   }
}
