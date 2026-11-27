namespace DotNetAnalysisAndRefactoringMcp.Specifications.Serving.WithASecondSolutionOfTheWorkTree;

/// <summary>
///   A <c>.royder.json</c> at the root of the first sandbox's copy of the fixture solution, naming the solution that the
///   copy's files go to, as a repository names its own: written for one specification, and removed after it.
/// </summary>
sealed class TheRepositorysSolution : IDisposable
{
   readonly string _file;

   public TheRepositorysSolution(TheSandboxWithTheFixtureSolution sandbox, string solution)
   {
      _file = sandbox.PathOf(".royder.json");
      File.WriteAllText(_file, $"{{\"solution\": \"{solution}\"}}\n");
   }

   public void Dispose() => File.Delete(_file);
}
