namespace Shapes;

/// <summary>A sticker, whose label has a doc comment of its own.</summary>
public sealed class Sticker : ILabeled
{
  /// <summary>What is printed on the sticker.</summary>
  public string Label() => "sticker";
}
