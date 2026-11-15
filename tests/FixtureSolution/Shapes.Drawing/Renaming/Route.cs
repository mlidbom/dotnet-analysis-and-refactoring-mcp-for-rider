namespace Shapes.Drawing.Renaming;

/// <summary>A route of parts, whose length is the sum of each part's <see cref="IMeasured.Length"/>.</summary>
public static class Route
{
  public static double TotalLength(IEnumerable<IMeasured> parts) => parts.Sum(it => it.Length());
}
