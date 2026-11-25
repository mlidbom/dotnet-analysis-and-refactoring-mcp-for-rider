#nullable enable
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

   /// <summary>Its line in an answer, under its file: <c>66:63 warning ConvertTypeCheckPatternToNullCheck: Use not null pattern …</c>.</summary>
   public string Text => $"{Line}:{Column} {Severities.Name(Severity)} {InspectionId}: {Message}{(HasQuickFix ? "  (quick-fix)" : "")}";
}
