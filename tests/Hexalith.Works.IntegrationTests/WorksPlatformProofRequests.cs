using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using Hexalith.EventStore.ServiceDefaults.Authentication;

using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Explicit short-lived synthetic credentials for requests through real target sidecars.</summary>
internal static class WorksPlatformProofRequests
{
    /// <summary>Signs an exact workload audience/operation and optional resource bindings for a disposable fixture.</summary>
    internal static string Assertion(string caller, string audience, string[] operations, IReadOnlyDictionary<string, object>? bindings = null)
    {
        DateTime now = DateTime.UtcNow;
        Dictionary<string, object> claims = bindings is null ? [] : new(bindings);
        claims["azp"] = caller;
        claims[EventStoreWorkloadAuthenticationDefaults.OperationClaimType] = operations;
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "hexalith-dev", Audience = audience, IssuedAt = now, NotBefore = now.AddSeconds(-1), Expires = now.AddMinutes(2),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes("DevOnlySigningKey-AtLeast32Chars!")), SecurityAlgorithms.HmacSha256),
            Claims = claims,
        });
    }

    /// <summary>Uses the caller's actual Dapr sidecar; receiver app-channel tokens remain independently enforced.</summary>
    internal static async Task<HttpResponseMessage> PostAsync(HttpClient callerSidecar, string receiver, string route,
        object body, string assertion, CancellationToken cancellationToken, IReadOnlyDictionary<string, string>? probeHeaders = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            "v1.0/invoke/" + Uri.EscapeDataString(receiver) + "/method/" + route.TrimStart('/'))
        {
            Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(body, JsonSerializerOptions.Web)),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        request.Headers.TryAddWithoutValidation(EventStoreWorkloadAuthenticationDefaults.AssertionHeaderName, assertion);
        if (probeHeaders is not null)
            foreach ((string name, string value) in probeHeaders) request.Headers.TryAddWithoutValidation(name, value);
        return await callerSidecar.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
