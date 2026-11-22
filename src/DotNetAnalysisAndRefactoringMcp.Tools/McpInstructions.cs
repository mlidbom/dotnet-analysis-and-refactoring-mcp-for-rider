namespace DotNetAnalysisAndRefactoringMcp;

/// <summary>
/// The usage guide every client receives in the <c>initialize</c> response, so agents know the basics without
/// a skill. Every line here is a claim about the code; change both together. In the tools build, so a change to it
/// needs no Rider restart; a client gets it when it connects.
/// </summary>
/// <remarks>
/// The first line says what the server is for, in under 250 characters: Codex shows a model only that much of a
/// server's instructions until it discovers the server's tools (Codex 0.171.0, found by gpt-collaborators,
/// 2026-11-22).
/// </remarks>
public static class McpInstructions
{
   public const string Text =
      "ReSharper code intelligence for the C# and other .NET code of the solutions open in Rider: usages, definitions, "
    + "symbol info and source, hierarchies, errors and inspections, and refactorings such as rename, move to namespace "
    + "and fix usings.\n"
    + "- File paths may be absolute, or relative to the repository root or the solution's folder.\n"
    + "- 'solutionName' is only needed when several solutions are open and no filePath picks one.\n"
    + "- Symbols may be named 'Type', 'Type.Member' or fully qualified; an ambiguous name returns its candidates. "
    + "Types compiled into referenced DLLs resolve too, and get_symbol_source decompiles them.\n"
    + "- inspect_code runs the inspections over files, folders, projects or the whole solution, with solution-wide "
    + "ones such as UnusedMember.Global when Rider's solution-wide analysis is complete: no jb inspectcode needed.\n"
    + "- get_file_errors is the fast check of syntax and unresolved references; get_diagnostics runs every "
    + "inspection on one file, the compiler's type errors included.\n"
    + "- get_file_errors and inspect_code take changedSince: '<git ref>' (or changed: true) to check every source file "
    + "changed since then, and untracked ones, in one call.\n"
    + "- find_usages prints rg-style 'path:line: code' lines; mode 'files' lists only the files.\n"
    + "- Errors the build doesn't have, such as an unresolved InitializeComponent in a new view, mean Rider's model "
    + "of the project is stale: reload_projects with the file fixes it. Run no edit tool on the file until then: a "
    + "fix on a stale model once removed a using the build needed.\n"
    + "- Files edited on disk are reloaded before each call. Edit tools (fix_usings, format_file, rename_symbol, "
    + "generate_members, apply_quick_fix, apply_suggestions) refuse while Rider's copy of a file differs "
    + "from disk.\n"
    + "- GET /health on this port reports open solutions, whether indexing is still running, and pending disk "
    + "changes.";
}
