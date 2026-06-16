using Shouldly;

namespace Hexalith.Works.UnitTests;

public sealed class ScaffoldTests
{
    [Fact]
    public void UnitTestProject_ShouldBeConfigured()
    {
        typeof(ScaffoldTests).Assembly.GetName().Name.ShouldBe("Hexalith.Works.UnitTests");
    }
}
