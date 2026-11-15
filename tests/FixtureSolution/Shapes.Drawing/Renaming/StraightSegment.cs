namespace Shapes.Drawing.Renaming;

/// <summary>A straight piece of a line, which overrides an implementation of an interface method.</summary>
public sealed class StraightSegment(double length) : Segment
{
  public override double Length() => length;
}
