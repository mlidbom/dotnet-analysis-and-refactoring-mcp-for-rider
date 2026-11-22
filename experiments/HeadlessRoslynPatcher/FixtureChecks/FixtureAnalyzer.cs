using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FixtureAnalyzer : DiagnosticAnalyzer
{
   static readonly DiagnosticDescriptor Rule = new("ROYDER301", "Probe for Roslyn analyzer output",
      "Round three analyzer reached {0}", "Probe", DiagnosticSeverity.Warning, isEnabledByDefault: true);
   public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

   public override void Initialize(AnalysisContext context)
   {
      context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
      context.EnableConcurrentExecution();
      context.RegisterSyntaxNodeAction(it =>
      {
         var method = (MethodDeclarationSyntax)it.Node;
         if (method.Identifier.ValueText == "RoundThreeAnalyzerTarget")
            it.ReportDiagnostic(Diagnostic.Create(Rule, method.Identifier.GetLocation(), method.Identifier.ValueText));
      }, SyntaxKind.MethodDeclaration);
   }
}
