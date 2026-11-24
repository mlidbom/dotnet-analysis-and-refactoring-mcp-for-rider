namespace Shapes.Drawing;

/// <summary>Sketches made as the book is: a call in a field's initializer, for the incoming call tree.</summary>
public sealed class SketchBook
{
  readonly Sketch _first = new Sketch(new Circle(1));

  public string Cover() => _first.Draw();
}
