namespace Shapes;

/// <summary>A rope, by its length.</summary>
public sealed record Rope(double Centimetres);

public static class RopeLengths
{
  /// <summary>How many centimetres an inch is, which <see cref="extension(Rope).Inches"/> divides by.</summary>
  public const double CentimetresPerInch = 2.54;

  extension(Rope rope)
  {
    /// <summary>The length of <paramref name="rope"/> in inches.</summary>
    public double Inches => rope.Centimetres / CentimetresPerInch;
  }
}
