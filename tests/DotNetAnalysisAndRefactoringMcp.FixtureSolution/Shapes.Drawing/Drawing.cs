using Shapes;

namespace Shapes.Drawing;

/// <summary>Shapes drawn together.</summary>
public sealed class Drawing
{
  readonly List<IShape> _shapes = [new Circle(1), new Square(2)];

  public double TotalArea() => _shapes.TotalArea();

  public double AreaOfTheFirst() => _shapes[0].Area();

  public double AreaOfTheLast => _shapes[^1].Area();

  public double ScaledAreaOfTheFirst() => ShapeExtensions.Scaled(AreaOfTheFirst(), 2);
}
