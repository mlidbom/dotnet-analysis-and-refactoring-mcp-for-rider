namespace Shapes.Drawing.Renaming;

/// <summary>Reads a property of the positional record <see cref="Measurement"/> from another file.</summary>
public static class Survey
{
  public static double Largest(IEnumerable<Measurement> measurements) => measurements.Max(it => it.Value);
}
