namespace Shapes;

/// <summary>A badge, whose label inherits its doc comment.</summary>
public sealed class Badge : ILabeled
{
  /// <inheritdoc/>
  public string Label() => "badge";
}
