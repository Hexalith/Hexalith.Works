using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Hexalith.EventStore.Client.Processes;
using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.ServiceDefaults.Authentication;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

using NSubstitute;
using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Receiver evidence cannot be forged by mismatched requests, identities or final statuses.</summary>
public sealed class WorksPlatformDispositionProbeTests
{
    private const string Nonce = "0123456789abcdef0123456789abcdef";
    private static readonly ProcessSource s_source = new("tenant-probe", "work", "work-probe");

    /// <summary>Every supported case records the final response even after its request service scope has ended.</summary>
    [Theory]
    [InlineData("wrong-purpose")]
    [InlineData("wrong-tenant")]
    [InlineData("wrong-aggregate")]
    [InlineData("audit-outage")]
    public async Task BoundRefusalIsRecordedOnlyAfterResponseCompletion(string kind)
    {
        JsonObject observed = (await ObserveAsync(kind).ConfigureAwait(true)).ShouldNotBeNull();
        observed["nonce"]!.GetValue<string>().ShouldBe(Nonce);
        observed["kind"]!.GetValue<string>().ShouldBe(kind);
        observed["status"]!.GetValue<int>().ShouldBe(kind == "audit-outage" ? 500 : 403);
        observed["route"]!.GetValue<string>().ShouldBe(WorksPlatformDispositionProbe.Route);
        observed["tenant"]!.GetValue<string>().ShouldBe(s_source.Tenant);
        observed["domain"]!.GetValue<string>().ShouldBe(s_source.Domain);
        observed["aggregate"]!.GetValue<string>().ShouldBe(s_source.Aggregate);
        observed["workload"]!.GetValue<string>().ShouldBe("eventstore-admin");
        observed["count"]!.GetValue<int>().ShouldBe(1);
    }

    /// <summary>No caller-controlled header, source, identity or unrelated status can create receiver evidence.</summary>
    [Theory]
    [InlineData("method")]
    [InlineData("route")]
    [InlineData("nonce-missing")]
    [InlineData("nonce-short")]
    [InlineData("nonce-nonhex")]
    [InlineData("nonce-duplicate")]
    [InlineData("kind")]
    [InlineData("kind-duplicate")]
    [InlineData("length-missing")]
    [InlineData("length-too-large")]
    [InlineData("foreign-source")]
    [InlineData("anonymous")]
    [InlineData("extra-identity")]
    [InlineData("subject")]
    [InlineData("duplicate-subject")]
    [InlineData("workload")]
    [InlineData("actor")]
    [InlineData("domain")]
    [InlineData("duplicate-domain")]
    [InlineData("unbound-tenant")]
    [InlineData("unbound-aggregate")]
    [InlineData("authorized-purpose")]
    [InlineData("caller-transport-status")]
    [InlineData("success-status")]
    public async Task UnboundOrUnrelatedResponseCannotProduceEvidence(string scenario)
    {
        JsonObject? observed = await ObserveAsync("wrong-purpose", context =>
        {
            var identity = (ClaimsIdentity)context.User.Identity!;
            void Replace(string type, string value)
            {
                foreach (Claim claim in identity.FindAll(type).ToArray()) identity.RemoveClaim(claim);
                identity.AddClaim(new Claim(type, value));
            }
            switch (scenario)
            {
                case "method": context.Request.Method = "GET"; break;
                case "route": context.Request.Path = "/work/processes/other"; break;
                case "nonce-missing": context.Request.Headers.Remove(WorksPlatformDispositionProbe.NonceHeader); break;
                case "nonce-short": context.Request.Headers[WorksPlatformDispositionProbe.NonceHeader] = "0123"; break;
                case "nonce-nonhex": context.Request.Headers[WorksPlatformDispositionProbe.NonceHeader] = new string('z', 32); break;
                case "nonce-duplicate": context.Request.Headers.Append(WorksPlatformDispositionProbe.NonceHeader, Nonce); break;
                case "kind": context.Request.Headers[WorksPlatformDispositionProbe.KindHeader] = "success"; break;
                case "kind-duplicate": context.Request.Headers.Append(WorksPlatformDispositionProbe.KindHeader, "wrong-purpose"); break;
                case "length-missing": context.Request.ContentLength = null; break;
                case "length-too-large": context.Request.ContentLength = 4097; break;
                case "foreign-source": ReplaceBody(context, JsonSerializer.Serialize(new { source = s_source with { Tenant = "foreign" } }, JsonSerializerOptions.Web)); break;
                case "anonymous": context.User = new ClaimsPrincipal(new ClaimsIdentity(identity.Claims)); break;
                case "extra-identity": context.User.AddIdentity(new ClaimsIdentity()); break;
                case "subject": Replace(ClaimTypes.NameIdentifier, "workload:foreign"); break;
                case "duplicate-subject": identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "workload:eventstore-admin")); break;
                case "workload": Replace(EventStoreWorkloadAuthenticationDefaults.WorkloadClaimType, "foreign"); break;
                case "actor": Replace(EventStoreWorkloadAuthenticationDefaults.ActorBindingClaimType, "foreign"); break;
                case "domain": Replace(EventStoreWorkloadAuthenticationDefaults.DomainBindingClaimType, "foreign"); break;
                case "duplicate-domain": identity.AddClaim(new Claim(EventStoreWorkloadAuthenticationDefaults.DomainBindingClaimType, s_source.Domain)); break;
                case "unbound-tenant": Replace(EventStoreWorkloadAuthenticationDefaults.TenantBindingClaimType, "foreign"); break;
                case "unbound-aggregate": Replace("eventstore:bound-aggregate", "foreign"); break;
                case "authorized-purpose": Replace("eventstore:bound-purpose", "works.process-disposition"); break;
                case "caller-transport-status": context.Response.StatusCode = 500; break;
                case "success-status": context.Response.StatusCode = 204; break;
                default: throw new ArgumentException("Unknown case.", nameof(scenario));
            }
        }).ConfigureAwait(true);
        observed.ShouldBeNull();
    }

    /// <summary>Invalid probe bodies neither produce evidence nor consume bytes needed by the receiver.</summary>
    [Theory]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("17")]
    [InlineData("{\"source\":null}")]
    public async Task InvalidBodyIsIgnoredAndPreserved(string body)
    {
        (await ObserveAsync("wrong-purpose", context => ReplaceBody(context, body)).ConfigureAwait(true)).ShouldBeNull();
    }

    private static async Task<JsonObject?> ObserveAsync(string kind, Action<DefaultHttpContext>? mutate = null)
    {
        var store = new Story47InMemoryReadModelStore();
        using ServiceProvider provider = new ServiceCollection().AddSingleton<IReadModelStore>(store).BuildServiceProvider();
        var completions = new List<(Func<object, Task> Callback, object State)>();
        IHttpResponseFeature response = Substitute.For<IHttpResponseFeature>();
        response.Headers = new HeaderDictionary();
        response.StatusCode = kind == "audit-outage" ? 500 : 403;
        response.When(feature => feature.OnCompleted(Arg.Any<Func<object, Task>>(), Arg.Any<object>()))
            .Do(call => completions.Add((call.ArgAt<Func<object, Task>>(0), call.ArgAt<object>(1))));
        var context = new DefaultHttpContext();
        context.Features.Set(response);
        using IServiceScope requestScope = provider.CreateScope();
        context.RequestServices = requestScope.ServiceProvider;
        context.Request.Method = "POST";
        context.Request.Path = WorksPlatformDispositionProbe.Route;
        context.Request.Headers[WorksPlatformDispositionProbe.NonceHeader] = Nonce;
        context.Request.Headers[WorksPlatformDispositionProbe.KindHeader] = kind;
        context.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "workload:eventstore-admin"),
            new Claim(EventStoreWorkloadAuthenticationDefaults.WorkloadClaimType, "eventstore-admin"),
            new Claim(EventStoreWorkloadAuthenticationDefaults.ActorBindingClaimType, "synthetic-operator-416"),
            new Claim(EventStoreWorkloadAuthenticationDefaults.DomainBindingClaimType, s_source.Domain),
            new Claim(EventStoreWorkloadAuthenticationDefaults.TenantBindingClaimType, kind == "wrong-tenant" ? "foreign" : s_source.Tenant),
            new Claim("eventstore:bound-aggregate", kind == "wrong-aggregate" ? "foreign" : s_source.Aggregate),
            new Claim("eventstore:bound-purpose", kind == "wrong-purpose" ? "foreign" : "works.process-disposition"),
        ], "SyntheticProbeTest"));
        using var body = new MemoryStream();
        context.Request.Body = body;
        ReplaceBody(context, JsonSerializer.Serialize(new { source = s_source }, JsonSerializerOptions.Web));
        mutate?.Invoke(context);
        string expectedBody = Encoding.UTF8.GetString(body.ToArray());
        var builder = new ApplicationBuilder(provider);
        new WorksPlatformDispositionProbe([s_source]).Configure(app => app.Run(async receiver =>
        {
            using var reader = new StreamReader(receiver.Request.Body, leaveOpen: true);
            (await reader.ReadToEndAsync(TestContext.Current.CancellationToken).ConfigureAwait(false)).ShouldBe(expectedBody);
        }))(builder);

        await builder.Build()(context).ConfigureAwait(false);
        store.SuccessfulWriteKeys.ShouldBeEmpty("A response that has not completed cannot qualify as receiver evidence.");
        requestScope.Dispose();
        context.RequestServices = null!;
        foreach ((Func<object, Task> callback, object state) in completions.AsEnumerable().Reverse())
            await callback(state).ConfigureAwait(false);
        store.SuccessfulWriteKeys.Count.ShouldBeLessThanOrEqualTo(1);
        return (await store.GetAsync<JsonObject>("statestore", WorksPlatformDispositionProbe.Key(s_source, kind),
            TestContext.Current.CancellationToken).ConfigureAwait(false)).Value;
    }

    private static void ReplaceBody(DefaultHttpContext context, string body)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(body);
        context.Request.Body.SetLength(0);
        context.Request.Body.Write(bytes);
        context.Request.Body.Position = 0;
        context.Request.ContentLength = bytes.Length;
    }
}
