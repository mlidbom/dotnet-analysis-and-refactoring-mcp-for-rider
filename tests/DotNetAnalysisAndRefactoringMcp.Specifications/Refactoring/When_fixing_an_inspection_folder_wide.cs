using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Refactoring;

/// <summary>Rider's Fix in folder, found in one file and applied to every file in its folder (2026-11-15).</summary>
/// <remarks>
///   It changes files, so it fixes them once, for every fact: xUnit makes an instance per fact, and runs a class's
///   facts one at a time. The other file is read as the answer comes: an edit tool saves what it changed before it
///   answers, and until 2026-11-15 Rider saved it a while later.
/// </remarks>
public class When_fixing_an_inspection_folder_wide(TheSandboxWithTheFixtureSolution sandbox)
{
   static (string Answer, string OtherFile)? _observed;

   readonly (string Answer, string OtherFile) _the = _observed ??= Observe(sandbox);

   [XF] public void it_says_it_applied_the_fix_in_the_folder() => _the.Answer.Must().Contain("each in the file's folder");

   [XF] public void it_says_how_many_files_it_changed() => _the.Answer.Must().Contain("changed 2 files");

   [XF] public void it_fixes_the_other_file_in_the_folder_too() => _the.OtherFile.Must().Contain("readonly double");

   static (string Answer, string OtherFile) Observe(TheSandboxWithTheFixtureSolution sandbox)
   {
      var answer = sandbox.McpServer.CallTool("apply_suggestions",
                                              new
                                              {
                                                 filePath = sandbox.PathOf("Shapes.Drawing/Captures/Ellipse.cs"),
                                                 inspectionIds = "PrimaryConstructorParameterCaptureDisallowed",
                                                 scope = "folder"
                                              });
      return (answer, File.ReadAllText(sandbox.PathOf("Shapes.Drawing/Captures/Rectangle.cs")));
   }
}
