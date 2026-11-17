namespace Shapes;

public static class ShapeExtensions
{
  /// <summary>The sum of the areas of <paramref name="shapes"/>.</summary>
  public static double TotalArea(this IEnumerable<IShape> shapes) => shapes.Sum(it => it.Area());

  /// <summary>An area scaled by <paramref name="factor"/> in both directions.</summary>
  public static double Scaled(double area, double factor) => area * factor * factor;

  extension(Circle circle)
  {
    /// <summary>The width of <paramref name="circle"/>: twice its radius.</summary>
    public double Diameter => 2 * Math.Sqrt(circle.Area() / Math.PI);

    /// <summary>A circle whose radius is <paramref name="by"/> longer.</summary>
    public Circle Grown(double by) => new(circle.Diameter / 2 + by);
  }
}
