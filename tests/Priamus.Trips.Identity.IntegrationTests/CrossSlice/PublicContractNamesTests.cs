using Priamus.Trips.Identity.Features.CreateUser;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Identity.IntegrationTests.CrossSlice;

/// <summary>
/// OpenAPI keys schemas by simple type name, so two public request/response types with the same
/// name in different slices collapse into one schema and the document lies about one of them.
/// This closes the class for every slice, present and future.
/// </summary>
public sealed class PublicContractNamesTests
{
    [Fact]
    public void PublicFeatureTypes_NoTwoShareASimpleName()
    {
        // Arrange
        var featureTypes = typeof(CreateUserRequest).Assembly.GetExportedTypes()
            .Where(t => t.Namespace?.StartsWith("Priamus.Trips.Identity.Features", StringComparison.Ordinal) == true)
            .ToList();

        // Act
        var duplicates = featureTypes
            .GroupBy(t => t.Name)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(t => t.FullName))}")
            .ToList();

        // Assert
        featureTypes.ShouldNotBeEmpty();
        duplicates.ShouldBeEmpty();
    }
}
