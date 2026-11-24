namespace Shapes;

/// <summary>Blends two colors half and half.</summary>
public sealed class EvenBlender : IBlender
{
  public string Blend(string first, string second) => Blend(first, second, 0.5);

  public string Blend(string first, string second, double share) => $"{first}*{1 - share}+{second}*{share}";
}
