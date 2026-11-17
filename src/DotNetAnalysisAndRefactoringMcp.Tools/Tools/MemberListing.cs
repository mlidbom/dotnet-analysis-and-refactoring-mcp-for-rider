using System.Collections.Generic;
using System.Linq;
using System.Text;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.CSharp.DeclaredElements;

namespace DotNetAnalysisAndRefactoringMcp.Tools;

/// <summary>
///   A type's members as <see cref="GetSymbolInfoTool"/> lists them with <c>includeMembers</c>: what the source
///   declares, each with its access, and not what the compiler generates from it, such as a property's accessors or a
///   record's equality members.
/// </summary>
/// <remarks>
///   A C# 14 extension block is listed as C# declares it, such as <c>extension(circle:Circle)</c>, with its members
///   under it. ReSharper models the block as a nested <see cref="ITypeExtension"/>, and gives the static class the
///   static method each of the block's members compiles to, an <see cref="ICSharpExtensionSecondaryMember"/>, such as
///   <c>get_Diameter(circle:Circle)</c> for a property <c>Diameter</c>: those are left out. A compiled type's blocks
///   are modeled otherwise, and not listed so yet (dev_docs/backlog.md).
/// </remarks>
class MemberListing(ITypeElement type)
{
   readonly HashSet<string> _propertyNames = [.. type.GetMembers().OfType<IProperty>().Select(it => it.ShortName)];
   readonly HashSet<string> _eventNames = [.. type.GetMembers().OfType<IEvent>().Select(it => it.ShortName)];

   /// <summary>One line for each member, each starting with <paramref name="indent"/>, and an extension block's members indented under it.</summary>
   public void AppendTo(StringBuilder text, string indent)
   {
      foreach(var member in type.GetMembers())
      {
         if(member is ITypeExtension block)
         {
            text.Append(indent).AppendLine(PsiHelpers.FormatSignature(block));
            new MemberListing(block).AppendTo(text, indent + "  ");
         } else if(IsDeclared(member))
            text.Append(indent).Append(AccessOf(member)).AppendLine(PsiHelpers.FormatSignature(member));
      }
   }

   bool IsDeclared(ITypeMember member) =>
      member is not ICSharpExtensionSecondaryMember && !IsAccessor(member) && !IsCompilerGenerated(member);

   /// <summary>A property's get_ or set_ method, or an event's add_ or remove_ method: the compiler's, under the name of what C# declares.</summary>
   bool IsAccessor(ITypeMember member)
   {
      if(member is not IMethod) return false;
      var name = member.ShortName;
      return (name.StartsWith("get_") || name.StartsWith("set_")) && _propertyNames.Contains(name.Substring(4))
          || (name.StartsWith("add_") || name.StartsWith("remove_")) && _eventNames.Contains(name.Substring(name.IndexOf('_') + 1));
   }

   /// <summary>
   ///   "private " and the like, as C# writes it, before a member in the listing: a compiled type's listing holds its
   ///   private members too, and nothing else tells them from the public ones.
   /// </summary>
   static string AccessOf(ITypeMember member) => member.GetAccessRights() switch
   {
      AccessRights.PUBLIC                 => "public ",
      AccessRights.PRIVATE                => "private ",
      AccessRights.INTERNAL               => "internal ",
      AccessRights.PROTECTED              => "protected ",
      AccessRights.PROTECTED_OR_INTERNAL  => "protected internal ",
      AccessRights.PROTECTED_AND_INTERNAL => "private protected ",
      _                                   => ""
   };

   /// <summary>
   ///   Members the compiler generates, typical of records: Equals, GetHashCode, ToString, PrintMembers, Deconstruct,
   ///   op_Equality, op_Inequality, EqualityContract, and clone methods.
   /// </summary>
   static bool IsCompilerGenerated(ITypeMember member)
   {
      var name = member.ShortName;

      // Skip any $-prefixed members (compiler internals)
      if(name.StartsWith("$") || name.StartsWith("<"))
         return member.GetDeclarations().Count == 0;

      // Equality operators are always compiler-generated on records
      // (these may be IOperator, not IMethod, so check before the IMethod block)
      if(name == "op_Equality" || name == "op_Inequality")
         return true;

      // Parameterless constructor on a record struct (always compiler-generated)
      if(name == ".ctor" && member is IParametersOwner ctor && ctor.Parameters.Count == 0)
         return true;

      if(member is IMethod)
      {
         switch(name)
         {
            case "Equals":
            case "GetHashCode":
            case "ToString":
            case "PrintMembers":
            case "Deconstruct":
               return member.GetDeclarations().Count == 0;
         }
      }

      // EqualityContract property (record-generated)
      if(member is IProperty && name == "EqualityContract")
         return member.GetDeclarations().Count == 0;

      return false;
   }
}
