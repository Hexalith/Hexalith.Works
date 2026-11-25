using System.Security.Cryptography;

using Hexalith.EventStore.Client.Gateway;
using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.DomainService;
using Hexalith.EventStore.ServiceDefaults.Authentication;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

using NSubstitute;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

public sealed class WorksSdkProofWorkloadAssertionIssuerTests
{
    [Fact]
    public async Task ProofHostingIssuesBoundStreamReadAssertionsForTheGatewayAudience()
    {
        using RSA rsa = RSA.Create(2048);
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection().AddLogging().AddSingleton<IConfiguration>(configuration)
            .AddSingleton(Substitute.For<IReadModelStore>());
        var context = new WebHostBuilderContext { Configuration = configuration, HostingEnvironment = Substitute.For<IWebHostEnvironment>() };
        WorksSdkProofHostingStartup.ConfigureProofServices(context, services, "works", Convert.ToBase64String(rsa.ExportPkcs8PrivateKey()));
        using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        IWorkloadAssertionIssuer issuer = provider.GetRequiredService<IWorkloadAssertionIssuer>();
        issuer.CanBindResources.ShouldBeTrue();
        string? assertion = await issuer.IssueAsync(new WorkloadAssertionRequest(
            EventStoreTrustedEffectSubmissionExtensions.DefaultGatewayAudience,
            EventStoreGatewayWorkloadOperations.StreamRead,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [EventStoreWorkloadAuthenticationDefaults.TenantBindingClaimType] = "sdk415-tenant",
                [EventStoreWorkloadAuthenticationDefaults.DomainBindingClaimType] = "work",
            }), TestContext.Current.CancellationToken).ConfigureAwait(true);
        assertion.ShouldNotBeNullOrWhiteSpace();
        var token = new JsonWebToken(assertion);
        token.Issuer.ShouldBe(WorksSdkProofDelegationProvider.Issuer);
        token.Audiences.ShouldBe([EventStoreTrustedEffectSubmissionExtensions.DefaultGatewayAudience]);
        token.GetClaim(EventStoreWorkloadAuthenticationDefaults.CallerClaimType).Value.ShouldBe("works");
        token.GetClaim(EventStoreWorkloadAuthenticationDefaults.OperationClaimType).Value.ShouldBe(EventStoreGatewayWorkloadOperations.StreamRead);
        token.GetClaim(EventStoreWorkloadAuthenticationDefaults.TenantBindingClaimType).Value.ShouldBe("sdk415-tenant");
        token.GetClaim(EventStoreWorkloadAuthenticationDefaults.DomainBindingClaimType).Value.ShouldBe("work");
    }

    [Fact]
    public void BoundPlatformProofKeySignsHttpAssertionsWithTheProofIssuer()
    {
        using RSA rsa = RSA.Create(2048);
        WorksAppHostSmokeHarness.BindPlatformCommandSigningKey(Convert.ToBase64String(rsa.ExportPkcs8PrivateKey()));
        try
        {
            var token = new JsonWebToken(WorksPlatformProofRequests.Assertion(
                "eventstore", "works", [EventStoreWorkloadOperations.DomainServiceProject]));
            token.Issuer.ShouldBe(WorksSdkProofDelegationProvider.Issuer);
            token.Audiences.ShouldBe(["works"]);
            token.GetClaim(EventStoreWorkloadAuthenticationDefaults.CallerClaimType).Value.ShouldBe("eventstore");
        }
        finally
        {
            WorksAppHostSmokeHarness.BindPlatformCommandSigningKey(string.Empty);
        }
    }
}
