namespace Shapes.Drawing.Renaming;

/// <summary>Has a method of the same name as <see cref="IMeasured"/>'s, unrelated to it.</summary>
public sealed class Rope(double length)
{
  public double Length() => length;
}
