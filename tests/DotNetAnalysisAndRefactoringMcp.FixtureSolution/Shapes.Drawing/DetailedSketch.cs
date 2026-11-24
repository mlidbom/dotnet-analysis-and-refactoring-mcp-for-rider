namespace Shapes.Drawing;

/// <summary>A sketch that describes itself in more words: an override, which calls the member it overrides.</summary>
public sealed class DetailedSketch() : Sketch(new Circle(1))
{
  public override string Describe() => base.Describe() + " in detail";
}
