namespace Shapes.Drawing;

/// <summary>Code that holds the inspection and refactoring cases the specifications look for.</summary>
public static class Exporting
{
  public static long ExportedLength()
  {
    using var stream = new MemoryStream();
    var neverRead = 42;
    return stream.Length;
  }
}
