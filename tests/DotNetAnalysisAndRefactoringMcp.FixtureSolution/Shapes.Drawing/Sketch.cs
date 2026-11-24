namespace Shapes.Drawing;

/// <summary>
///   A method that calls in every way C# has, for the outgoing call tree: a constructor, an operator, a property, an
///   indexer, an interface's member, a virtual member, a local function that calls itself, and a method passed as a
///   delegate; and a nameof, a cref and an auto-property, which call nothing.
/// </summary>
public class Sketch
{
  readonly IShape[] _shapes;

  public Sketch(params IShape[] shapes) => _shapes = Ordered(shapes);

  public string Title { get; init; } = "sketch";

  /// <summary>Draws it. This comment names <see cref="Palette.Pick{TColor}"/>, which it doesn't call.</summary>
  public string Draw()
  {
    var copy = new Sketch(_shapes);
    var outline = new Outline() + new Outline();
    var areas = _shapes.Select(AreaOf).ToList();
    return $"{nameof(Measuring.TryMeasure)} {Title} {copy.Title} {Caption} {this[0]} {areas.Count} {Nesting(0)} {Describe()} {outline[0]}";

    int Nesting(int from) => from >= _shapes.Length ? 0 : 1 + Nesting(from + 1);
  }

  /// <summary>Mixes as <see cref="Caption"/> does: a call tree from <see cref="Draw"/> meets the same calls twice at its deepest level.</summary>
  public virtual string Describe() => new Palette().Mix("grey", Title);

  public string Caption => new Palette().Mix("red", "blue");

  public double this[int index] => _shapes[index].Area();

  static double AreaOf(IShape shape) => shape.Area();

  static IShape[] Ordered(IShape[] shapes) => shapes;
}
