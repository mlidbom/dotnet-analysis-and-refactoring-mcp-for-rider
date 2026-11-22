using Microsoft.CodeAnalysis;

[Generator]
public sealed class FixtureGenerator : IIncrementalGenerator
{
   public void Initialize(IncrementalGeneratorInitializationContext context) =>
      context.RegisterPostInitializationOutput(it => it.AddSource("RoundThreeGenerated.g.cs",
         "namespace Shapes; public static class RoundThreeGenerated { public static int Twice(int value) => value * 2; }"));
}
