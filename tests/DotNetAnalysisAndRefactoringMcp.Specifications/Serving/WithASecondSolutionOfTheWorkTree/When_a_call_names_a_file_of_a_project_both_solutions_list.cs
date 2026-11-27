using Compze.Must;
using Compze.xUnitBDD;
using Xunit;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Serving.WithASecondSolutionOfTheWorkTree;

/// <summary>find_usages of <c>Shapes.Circle</c>, at its declaration: the fixture solution lists Shapes.Drawing, which uses it, and Outlines.slnx doesn't.</summary>
[Collection(nameof(WithTheOutlinesSolutionInThePeer))]
public class When_a_call_names_a_file_of_a_project_both_solutions_list(TheSandboxWithTheFixtureSolution sandbox)
{
   protected readonly TheSandboxWithTheFixtureSolution Sandbox = sandbox;

   protected string UsagesOfCircle() => Sandbox.McpServer.CallTool("find_usages", CirclesDeclaration);

   protected object CirclesDeclaration => new { filePath = Sandbox.PathOf("Shapes/Circle.cs"), line = 4, column = 21 };

   public class and_the_repository_names_no_solution : When_a_call_names_a_file_of_a_project_both_solutions_list
   {
      readonly string _answer;

      public and_the_repository_names_no_solution(TheSandboxWithTheFixtureSolution sandbox) : base(sandbox) => _answer = UsagesOfCircle();

      [XF] public void this_Riders_solution_answers_it_as_the_one_that_lists_more_projects() => _answer.Must().Contain("Shapes.Drawing");
   }

   public class and_the_repository_names_the_peers_solution : When_a_call_names_a_file_of_a_project_both_solutions_list
   {
      readonly string _answer;

      public and_the_repository_names_the_peers_solution(TheSandboxWithTheFixtureSolution sandbox) : base(sandbox)
      {
         using var named = new TheRepositorysSolution(sandbox, TheOutlinesSolutionInThePeer.SolutionFile);
         _answer = UsagesOfCircle();
      }

      [XF] public void the_peers_solution_answers_it_finding_the_usages_in_Shapes() => _answer.Must().Contain("ShapeExtensions.cs");

      [XF] public void the_peers_solution_answers_it_finding_none_in_Shapes_Drawing_which_it_does_not_list() =>
         _answer.Must().Satisfy(it => !it.Contains("Shapes.Drawing"));
   }

   public class and_the_repository_names_a_solution_that_does_not_exist : When_a_call_names_a_file_of_a_project_both_solutions_list
   {
      readonly string _refusal;

      public and_the_repository_names_a_solution_that_does_not_exist(TheSandboxWithTheFixtureSolution sandbox) : base(sandbox)
      {
         using var named = new TheRepositorysSolution(sandbox, "Missing.slnx");
         _refusal = sandbox.McpServer.Refusal("find_usages", CirclesDeclaration);
      }

      [XF] public void the_call_is_refused_naming_the_file_that_names_it() => _refusal.Must().Contain(Sandbox.PathOf(".royder.json"));

      [XF] public void the_call_is_refused_naming_the_solution_it_names() => _refusal.Must().Contain("'Missing.slnx'");
   }
}
