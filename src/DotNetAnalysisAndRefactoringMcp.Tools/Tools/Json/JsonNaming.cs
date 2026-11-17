#nullable enable
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.CSharp.Impl;

namespace DotNetAnalysisAndRefactoringMcp.Tools.Json;

/// <summary>
///   How the <see cref="JsonFormat">JSON form</see> names a symbol: its kind, as one of a fixed set of words, the C#
///   keyword where there is one, and its bare C# identifier, as a terminal prints it and a reader indexes it.
/// </summary>
public static class JsonNaming
{
   /// <summary>Every <see cref="KindOf">kind</see>, for the tools' descriptions.</summary>
   public const string Kinds = "class, record, struct, record struct, interface, enum, delegate, extension, method, "
                             + "property, indexer, field, event, constructor, operator; and for what only a position "
                             + "reaches: local function, local variable, parameter, type parameter, namespace";

   /// <summary>
   ///   The kind of <paramref name="element"/>: a C# 14 extension block is an <c>extension</c>, an indexer is not a
   ///   property, and a constant or an enum member is a <c>field</c>.
   /// </summary>
   public static string KindOf(IDeclaredElement element)
   {
      if(element is ITypeExtension) return "extension";
      if(element is IProperty { Parameters.Count: > 0 }) return "indexer";

      var type = element.GetElementType();
      if(type == CLRDeclaredElementType.CLASS) return "class";
      if(type == CSharpDeclaredElementType.RECORD) return "record";
      if(type == CLRDeclaredElementType.STRUCT) return "struct";
      if(type == CSharpDeclaredElementType.RECORD_STRUCT) return "record struct";
      if(type == CLRDeclaredElementType.INTERFACE) return "interface";
      if(type == CLRDeclaredElementType.ENUM) return "enum";
      if(type == CLRDeclaredElementType.DELEGATE) return "delegate";
      if(type == CLRDeclaredElementType.METHOD) return "method";
      if(type == CLRDeclaredElementType.PROPERTY) return "property";
      if(type == CLRDeclaredElementType.FIELD || type == CLRDeclaredElementType.CONSTANT || type == CLRDeclaredElementType.ENUM_MEMBER) return "field";
      if(type == CLRDeclaredElementType.EVENT) return "event";
      if(type == CLRDeclaredElementType.CONSTRUCTOR) return "constructor";
      if(type == CLRDeclaredElementType.SIGN_OPERATOR || type == CLRDeclaredElementType.CONVERSION_OPERATOR) return "operator";
      if(type == CSharpDeclaredElementType.LOCAL_FUNCTION) return "local function";
      if(type == CLRDeclaredElementType.LOCAL_VARIABLE || type == CLRDeclaredElementType.LOCAL_CONSTANT) return "local variable";
      if(type == CLRDeclaredElementType.PARAMETER) return "parameter";
      if(type == CLRDeclaredElementType.TYPE_PARAMETER) return "type parameter";
      if(type == CLRDeclaredElementType.NAMESPACE) return "namespace";
      return type.PresentableName;
   }

   /// <summary>
   ///   The C# identifier of <paramref name="element"/>, without type parameters or arity: a constructor's is its
   ///   type's, an indexer's is <c>this</c>, where ReSharper has <c>.ctor</c> and <c>Item</c>. An operator, which C# gives
   ///   no identifier, has its CLR name, such as <c>op_Addition</c>.
   /// </summary>
   public static string NameOf(IDeclaredElement element) =>
      element switch
      {
         IConstructor constructor            => constructor.ContainingType?.ShortName ?? element.ShortName,
         IProperty { Parameters.Count: > 0 } => "this",
         _                                   => element.ShortName
      };

   /// <summary>
   ///   The name C# qualifies <paramref name="element"/> with: a type's namespace and containing types, a member's
   ///   type's, a C# 14 extension block's member's static class's.
   /// </summary>
   public static string QualifiedNameOf(IDeclaredElement element) =>
      element is ITypeMember member and not ITypeElement && PsiHelpers.QualifyingTypeOf(member) is { } type
         ? $"{PsiHelpers.GetQualifiedName(type)}.{NameOf(element)}"
         : PsiHelpers.GetQualifiedName(element);
}
