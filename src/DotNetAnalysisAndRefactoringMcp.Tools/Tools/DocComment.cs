#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using JetBrains.ReSharper.Psi;

namespace DotNetAnalysisAndRefactoringMcp.Tools;

/// <summary>
///   A declared element's doc comment as a reader sees it in the IDE: the text of its summary and of its remarks, each
///   on one line, as <see cref="DocCommentText"/> writes them, without the <c>///</c> and the tags.
/// </summary>
sealed class DocComment
{
   DocComment(string? summary, string? remarks)
   {
      Summary = summary;
      Remarks = remarks;
   }

   /// <summary>The summary's text; nothing when the comment has none.</summary>
   public string? Summary { get; }

   /// <summary>The remarks' text; nothing when the comment has none.</summary>
   public string? Remarks { get; }

   /// <summary>The line the tools write for the summary, <c>doc: …</c>; nothing when there is no summary.</summary>
   public string? SummaryLine => string.IsNullOrEmpty(Summary) ? null : $"doc: {Summary}";

   /// <summary>The line the tools write for the remarks, <c>remarks: …</c>; nothing when there are none.</summary>
   public string? RemarksLine => string.IsNullOrEmpty(Remarks) ? null : $"remarks: {Remarks}";

   bool IsEmpty => string.IsNullOrEmpty(Summary) && string.IsNullOrEmpty(Remarks);

   /// <summary>
   ///   <paramref name="element"/>'s doc comment, an <c>&lt;inheritdoc/&gt;</c> replaced by what it inherits. A compiled
   ///   element's comes from its assembly's XML documentation file.
   /// </summary>
   public static DocComment Of(IDeclaredElement element) => Read(element, expandInheritdoc: true);

   /// <summary>
   ///   Only what is written on <paramref name="element"/> itself: an <c>&lt;inheritdoc/&gt;</c> gives neither summary nor
   ///   remarks. A list of implementations shows each one's own, not the summary they all inherit, over and over.
   /// </summary>
   public static DocComment WrittenOn(IDeclaredElement element) => Read(element, expandInheritdoc: false);

   static DocComment Read(IDeclaredElement element, bool expandInheritdoc)
   {
      var xml = XmlOf(element, expandInheritdoc);
      var fromTheXml = new DocComment(xml?.SelectSingleNode("//summary") is {} summary ? DocCommentText.Of(summary) : null,
                                      xml?.SelectSingleNode("//remarks") is {} remarks ? DocCommentText.Of(remarks) : null);
      return fromTheXml.IsEmpty ? FromTheSourceOf(element) : fromTheXml;
   }

   /// <remarks>The NRE is ReSharper's, observed for some static methods: their doc comment is then read from the source.</remarks>
   static XmlNode? XmlOf(IDeclaredElement element, bool expandInheritdoc)
   {
      try
      {
         return element.GetXMLDoc(expandInheritdoc);
      }
      catch(NullReferenceException)
      {
         return null;
      }
   }

   /// <summary>
   ///   The doc comment above <paramref name="element"/>'s declaration, read from its file: for when ReSharper gives no
   ///   XML for it, as when <see cref="IDeclaredElement.GetXMLDoc"/> fails, or when the comment isn't well-formed XML.
   /// </summary>
   static DocComment FromTheSourceOf(IDeclaredElement element)
   {
      var none = new DocComment(null, null);
      var declaration = MainDeclaration.Of(element);
      if(declaration == null)
         return none;

      var site = DeclarationSite.Of(declaration);
      var filePath = site?.File;
      if(string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
         return none;

      // Up from the name's line, over attributes, to the doc comment.
      var declarationLine = site!.Line;
      if(declarationLine <= 1)
         return none;

      var lines = File.ReadAllLines(filePath);
      if(declarationLine - 1 > lines.Length)
         return none;

      var docLines = new List<string>();
      for(var lineIndex = declarationLine - 2; lineIndex >= 0; lineIndex--)
      {
         var trimmed = lines[lineIndex].TrimStart();
         if(trimmed.StartsWith("///"))
         {
            docLines.Insert(0, trimmed.Substring(3));
            continue;
         }

         if(string.IsNullOrWhiteSpace(trimmed) || (docLines.Count == 0 && trimmed.StartsWith("[")))
            continue;

         break;
      }

      if(docLines.Count == 0)
         return none;

      try
      {
         var root = XDocument.Parse("<root>" + string.Join(Environment.NewLine, docLines) + "</root>").Root;
         return root == null
                   ? none
                   : new DocComment(root.Element("summary") is {} summary ? DocCommentText.Of(summary) : null,
                                    root.Element("remarks") is {} remarks ? DocCommentText.Of(remarks) : null);
      }
      catch(XmlException)
      {
         // A doc comment that isn't well-formed XML, such as one with a bare '<': its text as written, and saying so.
         return new DocComment(string.Join(" ", docLines).Trim() + " [the doc comment isn't well-formed XML]", null);
      }
   }
}
