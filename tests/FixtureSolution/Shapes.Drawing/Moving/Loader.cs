namespace Shapes.Drawing.Moving;

/// <summary>An interface that the specifications move to another namespace, and then the class in its file.</summary>
public interface ILoader
{
  Loaded Load();
}

/// <summary>What an <see cref="ILoader"/> loads.</summary>
public sealed class Loaded;
