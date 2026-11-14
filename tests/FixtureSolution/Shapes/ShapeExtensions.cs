namespace Shapes;

public static class ShapeExtensions
{
  /// <summary>The sum of the areas of <paramref name="shapes"/>.</summary>
  public static double TotalArea(this IEnumerable<IShape> shapes) => shapes.Sum(it => it.Area());

  /// <summary>An area scaled by <paramref name="factor"/> in both directions.</summary>
  public static double Scaled(double area, double factor) => area * factor * factor;
}
