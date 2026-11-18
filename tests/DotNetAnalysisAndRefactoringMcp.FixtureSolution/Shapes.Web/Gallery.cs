namespace Shapes.Web;

/// <summary>The exhibits a web page shows.</summary>
public static class Gallery
{
   /// <summary>An exhibit of <paramref name="shape"/>.</summary>
   public static Exhibit Of(IShape shape) => new(shape);
}
