using DotNetAnalysisAndRefactoringMcp.Specifications;
using Xunit;
using Xunit.Sdk;
using Xunit.v3;

// Every specification calls the tools in the one sandbox Rider, set up once for the run.
[assembly: AssemblyFixture(typeof(TheSandboxWithTheFixtureSolution))]

// One call at a time, until the plugin's handling of concurrent calls is itself specified.
[assembly: Parallelization(Mode = ParallelMode.None)]
