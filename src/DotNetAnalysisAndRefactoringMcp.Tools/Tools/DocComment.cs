#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
   static readonly DocComment None = new(null, null);

   /// <summary>The summary's text; nothing when the comment has none.</summary>
   readonly string? _summary;

   /// <summary>The remarks' text; nothing when the comment has none.</summary>
   readonly string? _remarks;

   DocComment(string? summary, string? remarks)
   {
      _summary = summary;
      _remarks = remarks;
   }

   /// <summary>The line the tools write for the summary, <c>doc: …</c>; nothing when there is no summary.</summary>
   public string? SummaryLine => string.IsNullOrEmpty(_summary) ? null : $"doc: {_summary}";

   /// <summary>The line the tools write for the remarks, <c>remarks: …</c>; nothing when there are none.</summary>
   string? RemarksLine => string.IsNullOrEmpty(_remarks) ? null : $"remarks: {_remarks}";

   bool IsEmpty => string.IsNullOrEmpty(_summary) && string.IsNullOrEmpty(_remarks);

   /// <summary>Its <see cref="SummaryLine"/> and its <see cref="RemarksLine"/>, those it has, each as a line of <paramref name="text"/>.</summary>
   public void AppendTo(StringBuilder text)
   {
      if(SummaryLine is {} summary)
         text.AppendLine(summary);

      if(RemarksLine is {} remarks)
         text.AppendLine(remarks);
   }

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
      var fromTheXml = FromTheXml(XmlOf(element, expandInheritdoc));
      return fromTheXml.IsEmpty ? FromTheSourceOf(element) : fromTheXml;
   }

   /// <summary>The XML ReSharper gives for <paramref name="element"/>'s doc comment; nothing when it has none, or fails.</summary>
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

   /// <summary>The summary and the remarks in <paramref name="xml"/>, the XML ReSharper gives for a doc comment.</summary>
   static DocComment FromTheXml(XmlNode? xml) =>
      xml == null
         ? None
         : new DocComment(xml.SelectSingleNode("//summary") is {} summary ? DocCommentText.Of(summary) : null,
                          xml.SelectSingleNode("//remarks") is {} remarks ? DocCommentText.Of(remarks) : null);

   /// <summary>
   ///   The doc comment above <paramref name="element"/>'s declaration, read from its file: for when ReSharper gives no
   ///   XML for it, as when <see cref="IDeclaredElement.GetXMLDoc"/> fails, or when the comment isn't well-formed XML.
   /// </summary>
   static DocComment FromTheSourceOf(IDeclaredElement element)
   {
      var lines = MainDeclaration.Of(element) is {} declaration && DeclarationSite.Of(declaration) is {} site
                     ? DocCommentLinesAbove(site)
                     : [];
      return lines.Count == 0 ? None : FromTheLines(lines);
   }

   /// <summary>
   ///   The lines of the doc comment above <paramref name="site"/>, each without its <c>///</c>: up from the line of the
   ///   declaration's name, over its attributes and blank lines. None when its file isn't on disk, or ends before that line.
   /// </summary>
   static IReadOnlyList<string> DocCommentLinesAbove(DeclarationSite site)
   {
      if(!File.Exists(site.File))
         return [];

      var fileLines = File.ReadAllLines(site.File);
      if(site.Line - 1 > fileLines.Length)
         return [];

      var docLines = new List<string>();
      for(var lineIndex = site.Line - 2; lineIndex >= 0; lineIndex--)
      {
         var trimmed = fileLines[lineIndex].TrimStart();
         if(trimmed.StartsWith("///"))
            docLines.Insert(0, trimmed.Substring(3));
         else if(!IsBlankOrAnAttributeBelowTheComment(trimmed))
            break;
      }

      return docLines;

      bool IsBlankOrAnAttributeBelowTheComment(string line) => string.IsNullOrWhiteSpace(line) || (docLines.Count == 0 && line.StartsWith("["));
   }

   /// <summary>
   ///   The summary and the remarks of the doc comment written as <paramref name="lines"/>; for one that isn't well-formed
   ///   XML, such as one with a bare <c>&lt;</c>, its text as written, saying so, as its summary.
   /// </summary>
   static DocComment FromTheLines(IReadOnlyList<string> lines)
   {
      try
      {
         var comment = XElement.Parse("<root>" + string.Join(Environment.NewLine, lines) + "</root>");
         return new DocComment(comment.Element("summary") is {} summary ? DocCommentText.Of(summary) : null,
                               comment.Element("remarks") is {} remarks ? DocCommentText.Of(remarks) : null);
      }
      catch(XmlException)
      {
         return new DocComment(string.Join(" ", lines).Trim() + " [the doc comment isn't well-formed XML]", null);
      }
   }
}
