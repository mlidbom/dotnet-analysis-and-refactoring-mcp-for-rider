namespace Shapes;

/// <summary>A method with every form a parameter takes, for its signature: in, ref readonly, ref, out, default values and params.</summary>
public static class Measuring
{
  public static bool TryMeasure(string text, in double scale, ref readonly double origin, ref int measured, out double length,
                                string unit = "cm", double? limit = null, params string[] labels)
  {
    measured++;
    length = text.Length * scale - origin;
    return labels.Length > 0 || unit.Length > 0 || limit is null;
  }
}
