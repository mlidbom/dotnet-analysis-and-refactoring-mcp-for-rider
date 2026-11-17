namespace Shapes;

/// <summary>Where a shape lies, given as tuples: a value tuple and an ITuple, which deconstruct without a Deconstruct method.</summary>
public static class Bounds
{
  public static double Width((double Left, double Right) horizontal)
  {
    var (left, right) = horizontal;
    return right - left;
  }

  public static double Height(System.Runtime.CompilerServices.ITuple vertical) =>
    vertical is (double bottom, double top) ? top - bottom : 0;
}
