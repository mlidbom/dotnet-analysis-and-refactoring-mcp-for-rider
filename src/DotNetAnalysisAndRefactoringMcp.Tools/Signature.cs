#nullable enable
using System.Linq;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.CSharp;
using JetBrains.ReSharper.Psi.CSharp.DeclaredElements;

namespace DotNetAnalysisAndRefactoringMcp;

/// <summary>
///   A declared element's signature as the tools write it, on one line: <c>property Id : int</c>,
///   <c>static method TryParse(text:string, out value:int) : bool</c>, <c>class MyClass</c>.
/// </summary>
/// <remarks>
///   A parameter is written <c>name:type</c>, after its modifiers, <c>this</c>, <c>params</c>, <c>ref</c>, <c>out</c>,
///   <c>in</c> or <c>ref readonly</c>, and before its default value, <c>scale:int = 2</c>: what an agent needs to write a
///   call to one overload of several. Until 2026-11-24 it had neither, and a compiled member's types were written as
///   the CLR names them, <c>Int32</c>, with an array's left empty: <c>Insert(index:Int32, value:)</c> for
///   <c>StringBuilder.Insert(int, char[])</c>.
/// </remarks>
public static class Signature
{
   /// <summary><paramref name="element"/>'s signature.</summary>
   public static string Of(IDeclaredElement element)
   {
      var language = LanguageOf(element);
      return element is ITypeExtension block
                ? OfExtensionBlock(block, language)
                : $"{KindOf(element)} {element.ShortName}{ParameterListOf(element, language)}{TypeOf(element, language)}";
   }

   /// <summary>What a signature writes before the name: <c>sealed class</c>, <c>static method</c>, <c>interface</c>.</summary>
   public static string KindOf(IDeclaredElement element) => $"{ModifiersOf(element)}{element.GetElementType().PresentableName}";

   /// <summary>
   ///   The language whose names the signature uses: C# for a compiled element, whose own presentation language writes
   ///   <c>Int32</c> and leaves an array's type empty; else the element's own, such as VB's for VB source.
   /// </summary>
   static PsiLanguageType LanguageOf(IDeclaredElement element) =>
      element is ICompiledElement ? CSharpLanguage.Instance : element.PresentationLanguage ?? CSharpLanguage.Instance;

   static string ModifiersOf(IDeclaredElement element) => element switch
   {
      IClass type                                       => ModifiersOfAClass(type),
      ITypeElement                                      => "",
      IMethod method                                    => ModifiersOfAMember(method) + (method.IsVirtual ? "virtual " : "") + (method.IsOverride ? "override " : ""),
      IModifiersOwner member                            => ModifiersOfAMember(member),
      _                                                 => ""
   };

   /// <summary>
   ///   From <c>IsAbstract</c> and <c>IsSealed</c>: ReSharper's <c>IsStatic</c> is true of every type, and a static class
   ///   is abstract and sealed, as in IL. An interface's and a struct's modifiers are implied, so they get none.
   /// </summary>
   static string ModifiersOfAClass(IClass type) =>
      type is { IsAbstract: true, IsSealed: true } ? "static "
      : type.IsAbstract                             ? "abstract "
      : type.IsSealed                               ? "sealed "
                                                    : "";

   static string ModifiersOfAMember(IModifiersOwner member) => (member.IsStatic ? "static " : "") + (member.IsAbstract ? "abstract " : "");

   /// <summary>
   ///   <paramref name="owner"/>'s parameters as a signature writes them, without the parentheses around them:
   ///   <c>first:string, second:string</c>, as a call tree names one overload of several, <c>Shapes.Palette.Mix(first:string, second:string)</c>.
   /// </summary>
   public static string ParametersOf(IParametersOwner owner) => Parameters(owner, LanguageOf(owner));

   /// <summary>A method's and a local function's parameter list, empty or not; another element's, such as an indexer's, only when it has parameters.</summary>
   static string ParameterListOf(IDeclaredElement element, PsiLanguageType language) => element switch
   {
      IMethod or ILocalFunction                                   => ParameterList((IParametersOwner)element, language),
      IParametersOwner { Parameters.Count: > 0 } parametersOwner  => ParameterList(parametersOwner, language),
      _                                                           => ""
   };

   static string ParameterList(IParametersOwner owner, PsiLanguageType language) => $"({Parameters(owner, language)})";

   static string Parameters(IParametersOwner owner, PsiLanguageType language)
   {
      var isAnExtensionMethod = owner is IMethod { IsExtensionMethod: true };
      return string.Join(", ", owner.Parameters.Select((it, index) => ParameterOf(it, isTheReceiver: isAnExtensionMethod && index == 0, language)));
   }

   static string ParameterOf(IParameter parameter, bool isTheReceiver, PsiLanguageType language)
   {
      var text = $"{(isTheReceiver ? "this " : "")}{(parameter.IsParams ? "params " : "")}{ModifierOf(parameter.Kind)}{parameter.ShortName}:{parameter.Type.GetPresentableName(language)}";
      return DefaultValueOf(parameter, language) is {} value ? $"{text} = {value}" : text;
   }

   static string ModifierOf(ParameterKind kind) => kind switch
   {
      ParameterKind.REFERENCE          => "ref ",
      ParameterKind.OUTPUT             => "out ",
      ParameterKind.INPUT              => "in ",
      ParameterKind.READONLY_REFERENCE => "ref readonly ",
      _                                => ""
   };

   /// <summary>An optional parameter's default value as C# writes it, such as <c>2</c>, <c>"red"</c> or <c>null</c>; <c>default</c> for one that is no constant, such as a struct's.</summary>
   static string? DefaultValueOf(IParameter parameter, PsiLanguageType language)
   {
      if(!parameter.IsOptional) return null;
      var value = parameter.GetDefaultValue();
      if(value.IsBadValue) return null;
      return value.IsConstant ? value.ConstantValue.GetPresentation(language, TypePresentationStyle.Default).Text : "default";
   }

   /// <summary>A method's return type, or a field's or a property's type, after a colon; nothing for an element without one, such as a type.</summary>
   static string TypeOf(IDeclaredElement element, PsiLanguageType language) => element switch
   {
      IMethod method                 => $" : {method.ReturnType.GetPresentableName(language)}",
      ILocalFunction localFunction   => $" : {localFunction.ReturnType.GetPresentableName(language)}",
      ITypeOwner typeOwner           => $" : {typeOwner.Type.GetPresentableName(language)}",
      _                              => ""
   };

   /// <summary>
   ///   A C# 14 extension block as C# declares it, with its receiver as the signatures write a parameter:
   ///   <c>extension&lt;T&gt;(source:IEnumerable&lt;T&gt;)</c>, or <c>extension(Circle)</c> for a block of static members
   ///   only. ReSharper models the block as a class named <c>$extension</c>.
   /// </summary>
   static string OfExtensionBlock(ITypeExtension block, PsiLanguageType language)
   {
      var receiverType = block.ReceiverType.GetPresentableName(language);
      var receiver = block.ReceiverParameter is { } parameter && !block.IsLogicallyStatic
                        ? $"{parameter.ShortName}:{receiverType}"
                        : receiverType;
      var typeParameters = block.TypeParametersCount == 0
                              ? ""
                              : $"<{string.Join(", ", block.TypeParameters.Select(it => it.ShortName))}>";
      return $"extension{typeParameters}({receiver})";
   }
}
