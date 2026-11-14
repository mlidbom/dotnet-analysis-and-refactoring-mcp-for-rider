namespace Shapes.Drawing.Moving;

/// <summary>Calls an extension method of <see cref="Lengths"/> only as one, which a move gives no using of its own.</summary>
public static class Ruler
{
  public static double TwiceTheLength(double length) => length.Doubled();
}
