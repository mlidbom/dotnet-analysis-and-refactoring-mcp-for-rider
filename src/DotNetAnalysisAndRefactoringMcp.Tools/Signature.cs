using System.Linq;
using System.Text;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.CSharp;

namespace DotNetAnalysisAndRefactoringMcp;

/// <summary>
///   A declared element's signature as the tools write it, on one line: <c>property Id : int</c>,
///   <c>static method DoStuff(x:int, y:string) : void</c>, <c>class MyClass</c>.
/// </summary>
public static class Signature
{
   /// <summary><paramref name="element"/>'s signature.</summary>
   public static string Of(IDeclaredElement element)
   {
      var lang = element.PresentationLanguage ?? CSharpLanguage.Instance;
      if(element is ITypeExtension block)
         return FormatExtensionBlock(block, lang);

      var sb = new StringBuilder();

      // Modifiers. For a type, ReSharper's IsStatic is true whatever the type is, and a static class is abstract
      // and sealed, as in IL: so a type's modifier comes from those two. An interface's and a struct's are implied.
      if(element is IClass type)
      {
         if(type.IsAbstract && type.IsSealed) sb.Append("static ");
         else if(type.IsAbstract) sb.Append("abstract ");
         else if(type.IsSealed) sb.Append("sealed ");
      } else if(element is not ITypeElement && element is IModifiersOwner mod)
      {
         if(mod.IsStatic) sb.Append("static ");
         if(mod.IsAbstract) sb.Append("abstract ");
         if(element is IMethod m)
         {
            if(m.IsVirtual) sb.Append("virtual ");
            if(m.IsOverride) sb.Append("override ");
         }
      }

      // Kind + Name
      sb.Append(element.GetElementType().PresentableName);
      sb.Append(' ');
      sb.Append(element.ShortName);

      // Parameters (methods/local functions always get parens; others only if they have params, e.g. indexers)
      if(element is IMethod || element is JetBrains.ReSharper.Psi.CSharp.DeclaredElements.ILocalFunction)
         AppendParams(sb, (IParametersOwner)element, lang);
      else if(element is IParametersOwner po && po.Parameters.Count > 0)
         AppendParams(sb, po, lang);

      // Type: return type for methods/local functions, declared type for fields/properties
      if(element is IMethod method)
      {
         sb.Append(" : ");
         sb.Append(method.ReturnType.GetPresentableName(lang));
      } else if(element is JetBrains.ReSharper.Psi.CSharp.DeclaredElements.ILocalFunction localFunc)
      {
         sb.Append(" : ");
         sb.Append(localFunc.ReturnType.GetPresentableName(lang));
      } else if(element is ITypeOwner typeOwner)
      {
         sb.Append(" : ");
         sb.Append(typeOwner.Type.GetPresentableName(lang));
      }

      return sb.ToString();
   }

   /// <summary>
   ///   A C# 14 extension block as C# declares it, with its receiver as the signatures write a parameter:
   ///   <c>extension&lt;T&gt;(source:IEnumerable&lt;T&gt;)</c>, or <c>extension(Circle)</c> for a block of static members
   ///   only. ReSharper models the block as a class named <c>$extension</c>.
   /// </summary>
   static string FormatExtensionBlock(ITypeExtension block, PsiLanguageType lang)
   {
      var receiverType = block.ReceiverType.GetPresentableName(lang);
      var receiver = block.ReceiverParameter is { } parameter && !block.IsLogicallyStatic
                        ? $"{parameter.ShortName}:{receiverType}"
                        : receiverType;
      var typeParameters = block.TypeParametersCount == 0
                              ? ""
                              : $"<{string.Join(", ", block.TypeParameters.Select(it => it.ShortName))}>";
      return $"extension{typeParameters}({receiver})";
   }

   static void AppendParams(StringBuilder sb, IParametersOwner owner, PsiLanguageType lang)
   {
      sb.Append('(');
      var first = true;
      foreach(var p in owner.Parameters)
      {
         if(!first) sb.Append(", ");
         sb.Append(p.ShortName);
         sb.Append(':');
         sb.Append(p.Type.GetPresentableName(lang));
         first = false;
      }

      sb.Append(')');
   }
}
