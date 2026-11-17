namespace Shapes.Drawing.Captures;

/// <summary>Captures its primary constructor's parameters, which the specifications fix folder-wide.</summary>
public sealed class Rectangle(double width, double height)
{
  public double Area() => width * height;
}
