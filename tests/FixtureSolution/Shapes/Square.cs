namespace Shapes;

/// <summary>A square of a given side.</summary>
public sealed class Square(double side) : IShape
{
  public double Area() => side * side;
}
