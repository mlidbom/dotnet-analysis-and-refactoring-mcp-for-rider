namespace Shapes;

public sealed partial class Palette
{
  public string Mix(string first, string second) => new Mixer(first, second).Result;

  sealed class Mixer(string first, string second)
  {
    public string Result => $"{first}+{second}";
  }
}
