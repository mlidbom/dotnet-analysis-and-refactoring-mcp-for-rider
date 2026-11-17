#nullable enable
using JetBrains.ReSharper.Psi;
using Newtonsoft.Json;

namespace DotNetAnalysisAndRefactoringMcp.Tools.Json;

/// <summary>
///   What a C# 14 extension block extends, in the <see cref="JsonFormat">JSON form</see>: <c>extension(Circle&lt;T&gt; circle)</c>
///   is <c>{"name": "circle", "type": "Circle&lt;T&gt;", "typeName": "Circle"}</c>.
/// </summary>
public sealed class ReceiverJson
{
   /// <summary>The receiver parameter's name; left out for a block of static members only, which has none.</summary>
   [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)] public string? Name { get; private set; }

   /// <summary>The receiver's type as C# writes it.</summary>
   [JsonProperty("type")] public string Type { get; private set; } = "";

   /// <summary>The bare identifier of the receiver's type, such as <c>Circle</c> for <c>Circle&lt;T&gt;</c>.</summary>
   [JsonProperty("typeName")] public string TypeName { get; private set; } = "";

   public static ReceiverJson Of(ITypeExtension block)
   {
      var type = block.ReceiverType.GetPresentableName(block.PresentationLanguage);
      return new ReceiverJson
             {
                Name = block.ReceiverParameter is { } parameter && !block.IsLogicallyStatic ? parameter.ShortName : null,
                Type = type,
                TypeName = block.ReceiverType is IDeclaredType declared && declared.GetTypeElement() is { } element
                              ? element.ShortName
                              : type
             };
   }
}
