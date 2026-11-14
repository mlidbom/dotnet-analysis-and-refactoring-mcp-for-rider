namespace Shapes;

/// <summary>A circle of a given radius.</summary>
public sealed class Circle(double radius) : IShape
{
  public double Area() => Math.PI * radius * radius;
}
