#nullable enable
using System;
using JetBrains.DocumentModel;
using JetBrains.ReSharper.Daemon.SolutionAnalysis;
using JetBrains.ReSharper.Feature.Services.Daemon;
using JetBrains.ReSharper.Psi;

namespace DotNetAnalysisAndRefactoringMcp;

/// <summary>
///   What an inspection found in a file, as <c>inspect_code</c> and <c>get_diagnostics</c> list it: where, how severe,
///   which inspection, and its message. Plain values, taken while ReSharper holds its read lock: nothing of the
///   engine's is kept.
/// </summary>
public sealed class InspectionIssue
{
   /// <summary>The file's full path.</summary>
   public string File { get; }

   /// <summary>1-based.</summary>
   public int Line { get; }

   /// <summary>1-based.</summary>
   public int Column { get; }

   public Severity Severity { get; }

   /// <summary>The id its severity is configured by, such as <c>MemberCanBeInternal</c>.</summary>
   public string InspectionId { get; }

   public string Message { get; }

   /// <summary>Whether a quick-fix is known to be available for it: <c>get_diagnostics</c> asks, <c>inspect_code</c> doesn't.</summary>
   public bool HasQuickFix { get; }

   public InspectionIssue(string file, int line, int column, Severity severity, string inspectionId, string message, bool hasQuickFix)
   {
      File = file;
      Line = line;
      Column = column;
      Severity = severity;
      InspectionId = inspectionId;
      Message = message;
      HasQuickFix = hasQuickFix;
   }

   /// <summary>An issue Inspect Code's engine found; nothing for one no longer in the file.</summary>
   public static InspectionIssue? Of(IPsiSourceFile file, IssuePointer issue)
   {
      if(!issue.IsValid) return null;
      var (line, column) = PsiHelpers.GetLineColumn(new DocumentOffset(file.Document, issue.NavigationOffset));
      return new InspectionIssue(file.GetLocation().FullPath,
                                 line,
                                 column,
                                 issue.GetSeverity(),
                                 issue.IssueType.ConfigurableSeverityId ?? issue.IssueType.GroupId,
                                 issue.Message,
                                 hasQuickFix: false);
   }

   /// <summary>
   ///   What ReSharper is known to get wrong about it, said beside it in an answer, so that an agent doesn't "fix" code
   ///   that is right; nothing when nothing is known.
   /// </summary>
   /// <remarks>
   ///   ReSharper 2026.3 resolves no C# 14 cref to an extension block's member, <c>extension(Rope).Inches</c>, which
   ///   the compiler resolves (from agentharness-facades, 2026-11-26). It reports one as <c>InvalidXmlDocComment</c>,
   ///   or in a project that generates its documentation file, as the compiler's <c>CS1574</c>.
   /// </remarks>
   public string? KnownMistake => IsAnUnresolvedCrefToAnExtensionBlocksMember
                                     ? "ReSharper 2026.3 resolves no cref to an extension block's member, such as extension(T).Member: only a build that generates the documentation file checks this one"
                                     : null;

   bool IsAnUnresolvedCrefToAnExtensionBlocksMember =>
      (InspectionId == "InvalidXmlDocComment" || InspectionId.StartsWith("CSharpWarnings::CS1574", StringComparison.Ordinal))
      && Message == "Cannot resolve symbol 'extension'";

   /// <summary>Its line in an answer, under its file: <c>66:63 warning ConvertTypeCheckPatternToNullCheck: Use not null pattern …</c>.</summary>
   public string Text => $"{Line}:{Column} {Severities.Name(Severity)} {InspectionId}: {Message}{(HasQuickFix ? "  (quick-fix)" : "")}" +
                         (KnownMistake is { } mistake ? $"  ({mistake})" : "");
}
