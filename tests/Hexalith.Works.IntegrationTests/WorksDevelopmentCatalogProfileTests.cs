using Aspire.Hosting;

using Hexalith.Platform.Composition;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>The canonical model refuses unknown retained business state before creating a runnable resource.</summary>
public sealed class WorksDevelopmentCatalogProfileTests
{
    [Fact]
    public void MissingOwnerProfileCannotGenerateKeysOrInitializeSharedDefaultState()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Hexalith.Works.slnx"))) root = root.Parent;
        string works = root.ShouldNotBeNull().FullName;
        string platform = Path.Combine(works, "references", "Hexalith.Platform");
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
        {
            Args = [], ProjectDirectory = platform, DisableDashboard = true,
        });
        builder.Environment.EnvironmentName = Environments.Development;
        builder.Configuration["Platform:Works:SourceRoot"] = works;
        builder.Configuration["Platform:Works:EventStoreSourceRoot"] = Path.Combine(works, "references", "Hexalith.EventStore");
        builder.Configuration["Platform:Works:DevelopmentCatalogProfile"] = null;
        int ownedLifetimes = builder.Services.Count(service => service.ServiceType == typeof(IHostedService));
        var refusal = Should.Throw<InvalidOperationException>(() => WorksPreviewModel.Compose(builder));
        refusal.Message.ShouldContain("Restore the private profile");
        refusal.Message.ShouldContain("cannot replace its digest keys");
        builder.Resources.ShouldBeEmpty();
        builder.Services.Count(service => service.ServiceType == typeof(IHostedService)).ShouldBe(ownedLifetimes);
    }
}
