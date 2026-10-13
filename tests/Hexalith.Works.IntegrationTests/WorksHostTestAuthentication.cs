using System.Text;

using Hexalith.EventStore.ServiceDefaults.Authentication;

using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Local credentials for integration tests that start the protected Works host.</summary>
internal static class WorksHostTestAuthentication
{
    private const string AppId = "works-integration-test";
    private const string Issuer = "works-integration-test-issuer";
    private const string ChannelToken = "works-integration-test-channel-token";
    private const string SigningKey = "works-integration-test-signing-key-with-32-bytes-minimum";

    internal static string[] ConfigurationArguments { get; } =
    [
        "--environment=Development",
        $"--{DaprAppChannelToken.ConfigurationKey}={ChannelToken}",
        $"--EventStore:DomainService:AppId={AppId}",
        $"--Authentication:JwtBearer:Issuer={Issuer}",
        "--Authentication:JwtBearer:Audience=hexalith-eventstore",
        $"--Authentication:JwtBearer:SigningKey={SigningKey}",
        $"--Authentication:JwtBearer:AllowedAlgorithms:0={SecurityAlgorithms.HmacSha256}",
        "--Authentication:JwtBearer:RequireHttpsMetadata=false",
    ];

    internal static void AddSidecarChannel(HttpClient client)
        => client.DefaultRequestHeaders.Add(DaprAppChannelToken.HeaderName, ChannelToken);

    internal static void AddProjectAssertion(HttpClient client)
    {
        DateTime now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = AppId,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(2),
            Claims = new Dictionary<string, object>
            {
                [EventStoreWorkloadAuthenticationDefaults.CallerClaimType] = "eventstore",
                [EventStoreWorkloadAuthenticationDefaults.OperationClaimType] = new[] { "domain-service:project" },
            },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)), SecurityAlgorithms.HmacSha256),
        };
        string assertion = new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(descriptor);
        client.DefaultRequestHeaders.Add(EventStoreWorkloadAuthenticationDefaults.AssertionHeaderName, assertion);
    }
}
