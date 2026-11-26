namespace Shapes;

/// <summary>
///   A color sample, declared in two parts: this one, and the one the Shapes project's source generator writes, with
///   Shade.
/// </summary>
public sealed partial class Swatch
{
  public string Color { get; } = "red";

  /// <summary>Calls a generated member of this type and one of a wholly generated type.</summary>
  public string Describe() => $"{Color} at {Shade()} of {Ruler.Doubled(50)}";
}
