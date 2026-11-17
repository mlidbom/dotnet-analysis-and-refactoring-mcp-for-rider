namespace Shapes.Drawing.Renaming;

/// <summary>A piece of a line, which leaves <see cref="IMeasured.Length"/> to the kind of segment.</summary>
public abstract class Segment : IMeasured
{
  public abstract double Length();
}
