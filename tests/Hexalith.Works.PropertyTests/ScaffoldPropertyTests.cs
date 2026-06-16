using FsCheck;

using Shouldly;

namespace Hexalith.Works.PropertyTests;

public sealed class ScaffoldPropertyTests
{
    [Fact]
    public void CentralPackageManagement_ShouldLoadFsCheck()
    {
        typeof(Gen<int>).Assembly.GetName().Name.ShouldBe("FsCheck");
    }
}
