using Xunit;

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Serving.WithASecondSolutionOfTheWorkTree;

/// <summary>The specifications that need <see cref="TheOutlinesSolutionInThePeer"/>: it runs while they do, and only then.</summary>
[CollectionDefinition(nameof(WithTheOutlinesSolutionInThePeer))]
public sealed class WithTheOutlinesSolutionInThePeer : ICollectionFixture<TheOutlinesSolutionInThePeer>;
