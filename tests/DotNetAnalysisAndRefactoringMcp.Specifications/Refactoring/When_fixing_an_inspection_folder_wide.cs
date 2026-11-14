using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Refactoring;

/// <summary>Rider's Fix in folder, found in one file and applied to every file in its folder (2026-11-15).</summary>
public class When_fixing_an_inspection_folder_wide
{
  readonly string _answer;
  readonly string _otherFile;

  public When_fixing_an_inspection_folder_wide(TheSandboxWithTheFixtureSolution sandbox)
  {
    _answer = sandbox.McpServer.CallTool("apply_suggestions", new
    {
      filePath = sandbox.PathOf("Shapes.Drawing/Captures/Ellipse.cs"),
      inspectionIds = "PrimaryConstructorParameterCaptureDisallowed",
      scope = "folder"
    });
    _otherFile = File.ReadAllText(sandbox.PathOf("Shapes.Drawing/Captures/Rectangle.cs"));
  }

  [XF] public void it_says_it_applied_the_fix_in_the_folder() => _answer.Must().Contain("each in the file's folder");

  [XF] public void it_fixes_the_other_file_in_the_folder_too() => _otherFile.Must().Contain("readonly double");
}
