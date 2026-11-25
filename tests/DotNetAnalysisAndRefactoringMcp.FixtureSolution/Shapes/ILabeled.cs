namespace Shapes;

/// <summary>Has a label: a documented interface member, which <see cref="Badge"/> implements under an inheritdoc and <see cref="Sticker"/> under a summary of its own.</summary>
public interface ILabeled
{
  /// <summary>The text to show for it.</summary>
  string Label();
}
