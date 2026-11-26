using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Inspections;

/// <summary>
///   Rope.cs's doc comment refers to <c>extension(Rope).Inches</c>, a member of an extension block, as C# 14 writes it.
///   The compiler resolves it, ReSharper 2026.3 doesn't: until 2026-11-26 the tools gave its warning as one more cref
///   to fix, and agents "fixed" correct comments (from agentharness-facades).
/// </summary>
public class When_checking_a_cref_to_an_extension_blocks_member(TheSandboxWithTheFixtureSolution sandbox)
{
   const string TheKnownMistake = "ReSharper 2026.3 resolves no cref to an extension block's member";

   readonly string _diagnostics = sandbox.McpServer.CallTool("get_diagnostics", new { filePath = sandbox.PathOf("Shapes/Rope.cs") });
   readonly string _inspection = sandbox.McpServer.CallTool("inspect_code", new { filePath = "Shapes/Rope.cs" });

   /// <summary>When a ReSharper resolves it, this fails: then <c>InspectionIssue.KnownMistake</c> says it no more.</summary>
   [XF] public void ReSharper_still_cannot_resolve_it() => _diagnostics.Must().Contain("Cannot resolve symbol 'extension'");

   [XF] public void the_diagnostics_say_that_ReSharper_gets_it_wrong() => _diagnostics.Must().Contain(TheKnownMistake);

   [XF] public void and_so_does_the_inspection() => _inspection.Must().Contain(TheKnownMistake);
}
