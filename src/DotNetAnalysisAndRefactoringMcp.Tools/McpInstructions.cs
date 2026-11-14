namespace DotNetAnalysisAndRefactoringMcp
{
    /// <summary>
    /// The usage guide every client receives in the <c>initialize</c> response, so agents know the basics without
    /// a skill. Every line here is a claim about the code; change both together. In the tools build, so a change to it
    /// needs no Rider restart; a client gets it when it connects.
    /// </summary>
    public static class McpInstructions
    {
        public const string Text =
            "ReSharper code intelligence for the solutions open in Rider.\n"
            + "- File paths may be absolute, or relative to the repository root or the solution's folder.\n"
            + "- 'solutionName' is only needed when several solutions are open and no filePath picks one.\n"
            + "- Symbols may be named 'Type', 'Type.Member' or fully qualified; an ambiguous name returns its candidates. "
            + "Types compiled into referenced DLLs resolve too, and get_symbol_source decompiles them.\n"
            + "- inspect_code runs the inspections over files, folders, projects or the whole solution, with solution-wide "
            + "ones such as UnusedMember.Global when Rider's solution-wide analysis is complete: no jb inspectcode needed.\n"
            + "- get_file_errors is the fast compile check. Pass changedSince: '<git ref>' (or changed: true) to check "
            + "every changed file in one call.\n"
            + "- find_usages prints rg-style 'path:line: code' lines; mode 'files' lists only the files.\n"
            + "- Errors the build doesn't have, such as an unresolved InitializeComponent in a new view, mean Rider's model "
            + "of the project is stale: reload_projects with the file fixes it.\n"
            + "- Files edited on disk are reloaded before each call. Edit tools (fix_usings, format_file, rename_symbol, "
            + "generate_members, apply_quick_fix, apply_suggestions) refuse while Rider's copy of a file differs "
            + "from disk.\n"
            + "- GET /health on this port reports open solutions, whether indexing is still running, and pending disk "
            + "changes.";
    }
}
