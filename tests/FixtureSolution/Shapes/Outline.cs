namespace Shapes;

/// <summary>A shape's outline: a member of every kind, and types nested in it, for the JSON form's kinds.</summary>
public sealed class Outline : IDisposable
{
  public const int MostPoints = 64;

  public event Action? Changed;

  public double this[int index] => index;

  public static Outline operator +(Outline left, Outline right) => left;

  void IDisposable.Dispose() => Changed?.Invoke();

  /// <summary>Which way the outline is drawn.</summary>
  public enum Direction { Clockwise, Anticlockwise }

  /// <summary>One point of the outline.</summary>
  public readonly record struct Point(double X, double Y);

  /// <summary>What is told when the outline is drawn again.</summary>
  public delegate void Redrawn(Outline outline);

  /// <summary>One corner of the outline.</summary>
  public sealed class Corner
  {
    public double Angle => 90;
  }
}
