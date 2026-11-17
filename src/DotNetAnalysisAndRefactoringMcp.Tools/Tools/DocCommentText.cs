using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace DotNetAnalysisAndRefactoringMcp.Tools;

/// <summary>
///   The text of a doc comment's part, such as its <c>summary</c>, as a reader sees it in the IDE: a
///   <c>&lt;see cref&gt;</c> as the name it links to, a <c>&lt;paramref&gt;</c> as the parameter's name, on one line.
/// </summary>
/// <remarks>
///   The XML's own text leaves out every element without content, so "The sum of the areas of
///   <c>&lt;paramref name="shapes"/&gt;</c>." read "The sum of the areas of ." (until 2026-11-17), and a doc comment
///   that links every name, as the code standards ask, lost most of what it said.
/// </remarks>
static class DocCommentText
{
   static readonly Regex Whitespace = new(@"\s+");
   static readonly Regex GenericArity = new(@"`\d+");

   /// <summary>The text of <paramref name="part"/>, an element of the XML ReSharper gives for a declared element's doc comment.</summary>
   public static string Of(XmlNode part) => Of(XElement.Load(new XmlNodeReader(part)));

   /// <summary>The text of <paramref name="part"/>, such as a doc comment's <c>summary</c> element.</summary>
   public static string Of(XElement part) => Whitespace.Replace(TextIn(part), " ").Trim();

   static string TextIn(XElement element) => string.Concat(element.Nodes().Select(TextOf));

   static string TextOf(XNode node) => node switch
   {
      XText text                                                 => text.Value,
      XElement { Name.LocalName: "see" or "seealso", IsEmpty: true } link => LinkedNameIn(link),
      XElement { Name.LocalName: "paramref" or "typeparamref" } reference => reference.Attribute("name")?.Value ?? "",
      XElement { Name.LocalName: "br" or "para" } element        => " " + TextIn(element) + " ",
      XElement element                                           => TextIn(element),
      _                                                          => ""
   };

   /// <summary>What an empty <c>&lt;see&gt;</c> shows: the name its <c>cref</c> links to, its <c>langword</c>, or its <c>href</c>.</summary>
   static string LinkedNameIn(XElement link) =>
      link.Attribute("cref")?.Value is { } cref
         ? NameIn(cref)
         : link.Attribute("langword")?.Value ?? link.Attribute("href")?.Value ?? "";

   /// <summary>
   ///   A cref as the IDE shows it: as written in the source, such as <c>IShape.Area</c>, or from a documentation id, such
   ///   as <c>T:Shapes.Circle</c>, the type's name, and <c>M:Shapes.Circle.Area(System.Double)</c>, the member's with its
   ///   type's.
   /// </summary>
   static string NameIn(string cref)
   {
      if(cref.Length < 2 || cref[1] != ':')
         return cref;

      var name = GenericArity.Replace(cref.Substring(2).Split('(')[0], "");
      var segments = name.Split('.');
      return cref[0] == 'T' || segments.Length < 2
                ? segments[segments.Length - 1]
                : $"{segments[segments.Length - 2]}.{segments[segments.Length - 1]}";
   }
}
