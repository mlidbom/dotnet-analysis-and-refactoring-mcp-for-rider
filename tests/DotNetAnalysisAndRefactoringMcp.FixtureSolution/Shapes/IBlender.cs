namespace Shapes;

/// <summary>Blends two colors: an interface member with overloads, each implemented by <see cref="EvenBlender"/>.</summary>
public interface IBlender
{
  string Blend(string first, string second);

  string Blend(string first, string second, double share);
}
