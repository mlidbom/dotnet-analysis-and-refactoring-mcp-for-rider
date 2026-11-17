namespace Shapes.Drawing.Renaming;

/// <summary>A positional record, whose properties its parameters declare.</summary>
public sealed record Measurement(string Unit, double Value)
{
  public string Shown() => $"{Value} {Unit}";
}
