using Xunit;

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Serving.WithAPeer;

/// <summary>The specifications that need <see cref="ThePeerRider"/>: it runs while they do, and only then.</summary>
[CollectionDefinition(nameof(WithThePeerRider))]
public sealed class WithThePeerRider : ICollectionFixture<ThePeerRider>;
