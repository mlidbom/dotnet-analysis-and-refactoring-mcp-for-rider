namespace Shapes;

public sealed partial class Palette
{
  public string Mix(string first, string second) => Mixer.Of(first, second).Result;

  public string Mix(string first, string second, string third) => Mix(Mix(first, second), third);

  sealed class Mixer(string first, string second)
  {
    public string Result => $"{first}+{second}";

    /// <summary>A call in the two-color overload, for a call hierarchy from the three-color one to reach past it.</summary>
    public static Mixer Of(string first, string second) => new(first, second);
  }
}
