namespace Shapes;

/// <summary>The colors a drawing may use, declared in two parts: this one, named after it, and its mixing.</summary>
public sealed partial class Palette
{
  public string[] Colors { get; } = ["red", "green", "blue"];

  /// <summary>A generic method with a doc comment, so its type parameter's line differs from its declaration's.</summary>
  public TColor Pick<TColor>(TColor fallback) => fallback;

  public int Count => Colors.Length;
}
