using Shouldly;

namespace Hexalith.Works.IntegrationTests;

public sealed class ScaffoldIntegrationTests
{
    [Fact]
    public void IntegrationTestProject_ShouldBeConfigured()
    {
        typeof(ScaffoldIntegrationTests).Assembly.GetName().Name.ShouldBe("Hexalith.Works.IntegrationTests");
    }
}
