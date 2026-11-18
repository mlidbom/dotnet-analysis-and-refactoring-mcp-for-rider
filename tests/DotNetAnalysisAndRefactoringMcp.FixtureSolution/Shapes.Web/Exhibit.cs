namespace Shapes.Web;

/// <summary>A shape shown on a web page.</summary>
public sealed class Exhibit(IShape shape)
{
   /// <summary>The shape shown.</summary>
   public IShape Shape { get; } = shape;
}
