using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Refactoring;

/// <summary>Rider's Fix in folder, found in one file and applied to every file in its folder (2026-11-15).</summary>
/// <remarks>
///   It changes files, so it fixes them once, for every fact: xUnit makes an instance per fact. Rider saves the changed
///   documents a while after the fix, so the other file is read once it's saved.
/// </remarks>
public class When_fixing_an_inspection_folder_wide(TheSandboxWithTheFixtureSolution sandbox)
{
   static readonly TimeSpan HowLongSavingMayTake = TimeSpan.FromSeconds(30);
   static readonly object Gate = new();
   static (string Answer, string OtherFile)? _observed;

   readonly (string Answer, string OtherFile) _the = Once(sandbox);

   [XF] public void it_says_it_applied_the_fix_in_the_folder() => _the.Answer.Must().Contain("each in the file's folder");

   [XF] public void it_says_how_many_files_it_changed() => _the.Answer.Must().Contain("changed 2 files");

   [XF] public void it_fixes_the_other_file_in_the_folder_too()=> _the.OtherFile.Must().Contain("readonly double");

   static (string Answer, string OtherFile) Once(TheSandboxWithTheFixtureSolution sandbox)
   {
      lock(Gate)
         return _observed ??= Observe(sandbox);
   }

   static (string Answer, string OtherFile) Observe(TheSandboxWithTheFixtureSolution sandbox)
   {
      var answer = sandbox.McpServer.CallTool("apply_suggestions",
                                              new
                                              {
                                                 filePath = sandbox.PathOf("Shapes.Drawing/Captures/Ellipse.cs"),
                                                 inspectionIds = "PrimaryConstructorParameterCaptureDisallowed",
                                                 scope = "folder"
                                              });
      var other = sandbox.PathOf("Shapes.Drawing/Captures/Rectangle.cs");
      var deadline = DateTime.UtcNow + HowLongSavingMayTake;
      while(!File.ReadAllText(other).Contains("readonly double") && DateTime.UtcNow < deadline)
         Thread.Sleep(250);
      return (answer, File.ReadAllText(other));
   }
}
