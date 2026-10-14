using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Dapr.Actors;
using Dapr.Actors.Client;

using Hexalith.EventStore.Authorization;
using Hexalith.EventStore.Client.Security;
using Hexalith.EventStore.Client.Streams;
using Hexalith.EventStore.Contracts.Identity;
using Hexalith.EventStore.Contracts.Queries;
using Hexalith.EventStore.Contracts.Streams;
using Hexalith.Parties.Authorization;
using Hexalith.Parties.Queries;
using Hexalith.Parties.Contracts;
using Hexalith.Parties.Contracts.Events;
using Hexalith.Parties.Contracts.State;
using Hexalith.Parties.Contracts.ValueObjects;
using Hexalith.EventStore.Contracts.Security;
using Hexalith.EventStore.Controllers;
using Hexalith.EventStore.Server.Actors;
using Hexalith.EventStore.Server.Identity;
using Hexalith.EventStore.Server.Pipeline.Queries;
using Hexalith.EventStore.Server.Queries;
using Hexalith.Parties.Contracts.Models;
using Hexalith.Platform.Identity;

using MediatR;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

using NSubstitute;
using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Canonical authenticated gateway and real Platform admission over authoritative synthetic sources.</summary>
internal sealed class WorksSdkPlatformAuthorityFixture(string tenant, string actor, string issuer, string jwtKey,
    Func<PartyIdentityResult> identity)
{
    /// <summary>Gets the source lookups admitted with a verified exact-scope signature.</summary>
    internal int VerifiedQueries { get; private set; }

    /// <summary>Builds a real canonical gateway; fixtures supply only registry and Party source evidence.</summary>
    internal WebApplication Build(string? failure = null)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(["--Logging:LogLevel:Default=Error"]);
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = issuer, ValidAudience = "parties-identity-reader",
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                ValidateIssuer = true, ValidateAudience = true, ValidateIssuerSigningKey = true, ValidateLifetime = true,
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256], ClockSkew = TimeSpan.Zero,
            };
        });
        builder.Services.AddAuthorization();
        builder.Services.AddControllers().AddApplicationPart(typeof(QueriesController).Assembly);
        var options = Options.Create(new PlatformIdentityOptions
        {
            AuthorityRevision = 1, RegistryServiceSourceId = "registry-service",
            ReaderSources = failure == "disabled-reader" ? [] : ["works-reader"],
            JwtReaders = [new() { SourceId = "works-reader", Issuer = issuer,
                Subject = failure == "untrusted-reader" ? "different-service" : "works-identity-reader", ClientId = "works" }],
        });
        (IdentityAdmissionSigner signer, IdentityAdmissionProof verifier) = Keys();
        (IdentityAdmissionSigner registrySigner, IdentityAdmissionProof registryVerifier) = Keys();
        IIdentityActorRegistryActor registrySource = Substitute.For<IIdentityActorRegistryActor>();
        registrySource.ReadAsync(Arg.Any<string>(), false, Arg.Any<string>(), Arg.Any<string>()).Returns(call =>
        {
            string lookup = call.ArgAt<string>(0);
            string messageId = call.ArgAt<string>(2);
            var scope = new IdentityAdmissionScope("identity-authority", "identity-registry", IdentityActorRegistryActor.RegistryNamespace,
                "RegistryRead", messageId, messageId, IdentityAdmissionProof.Digest(JsonSerializer.SerializeToUtf8Bytes(new { lookup, byAlias = false })));
            registryVerifier.Verify(call.ArgAt<string>(3), scope).ShouldNotBeNull().SourceId.ShouldBe("registry-service");
            return lookup == actor ? new ActorRegistryEntry(actor, 1, failure != "inactive-actor", "synthetic-registry") : null;
        });
        IActorProxyFactory factory = Substitute.For<IActorProxyFactory>();
        factory.CreateActorProxy<IIdentityActorRegistryActor>(Arg.Is<ActorId>(id => id.GetId() == IdentityActorRegistryActor.RegistryNamespace),
            IdentityActorRegistryActor.ActorTypeName).Returns(registrySource);
        var registry = new PlatformActorRegistry(factory, registrySigner, options, TimeProvider.System);
        var tenantValidator = new ClaimsTenantValidator();
        var rbacValidator = new ClaimsRbacValidator();
        var admission = new PlatformIdentityGatewayAdmission(options, signer, registry, tenantValidator, rbacValidator,
            TimeProvider.System, new PlatformOperatorProvenanceVerifier(verifier, options));
        builder.Services.AddSingleton<Hexalith.EventStore.Authorization.ITenantValidator>(tenantValidator);
        builder.Services.AddSingleton<IRbacValidator>(rbacValidator);
        builder.Services.AddSingleton<IIdentityGatewayAdmission>(admission);
        builder.Services.AddSingleton(Substitute.For<IETagService>());
        var partyAuthority = new PartyIdentityAuthority(verifier);
        var source = Substitute.For<IAuthoritativeEventStreamReader>();
        var custody = Substitute.For<IIdentityHistoryCustody>();
        var identityOptions = Substitute.For<IOptionsMonitor<PartyIdentityOptions>>();
        identityOptions.CurrentValue.Returns(new PartyIdentityOptions
        {
            PolicyId = "policy", Retention = TimeSpan.FromDays(1), ExpiryTrigger = "binding-effective-at",
        });
        source.ReadAsync(Arg.Any<AggregateIdentity>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            AggregateIdentity requested = call.Arg<AggregateIdentity>();
            PartyIdentityEvidence sample = identity().Evidence!;
            HumanActorBindingEvidence binding = sample.HumanBinding!;
            binding = binding with { Custody = binding.Custody with { ExpiresAt = binding.ValidFrom.AddDays(1) } };
            if (failure == "binding-actor") binding = binding with { ActorId = "01ARZ3NDEKTSV4RRFFQ69G5FAA" };
            if (failure == "expired-binding") binding = binding with { ValidUntil = DateTimeOffset.UtcNow.AddMinutes(-1) };
            if (failure == "unsafe-custody") binding = binding with { Custody = binding.Custody with { RestoreSafe = false } };
            var created = new PartyCreated { Type = PartyType.Person, CreatedAt = binding.ValidFrom };
            var established = new HumanActorBindingEstablished(new HumanActorBinding(binding, "logical-binding", "intent-binding"),
                binding.ValidFrom, 0);
            StreamReadEvent[] events = [
                new(1, typeof(PartyCreated).FullName!, JsonSerializer.SerializeToUtf8Bytes(created, PartiesJsonOptions.Default),
                    "json", 1, "created", null, null, binding.ValidFrom, null),
                new(2, typeof(HumanActorBindingEstablished).FullName!, JsonSerializer.SerializeToUtf8Bytes(established, PartiesJsonOptions.Default),
                    "json", 1, "binding", null, null, binding.ValidFrom, null),
            ];
            return Task.FromResult(new AuthoritativeStreamReadResult(new AuthoritativeEventStream(requested, 2,
                DateTimeOffset.UtcNow, events, "synthetic-source-observation"), null));
        });
        custody.CanReadAsync(Arg.Any<AggregateIdentity>(), Arg.Any<IdentityHistoryCustodyEvidence>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<AggregateIdentity>().TenantId == tenant && failure != "unavailable-custody");
        var partyService = new PartyIdentityQueryService(partyAuthority, TimeProvider.System, source, custody,
            identityOptions: identityOptions);
        IMediator mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<IRequest<SubmitQueryResult>>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            SubmitQuery query = (SubmitQuery)call.Arg<IRequest<SubmitQueryResult>>();
            var scope = new IdentityAdmissionScope(query.Tenant, query.Domain, query.AggregateId, query.QueryType,
                query.CorrelationId, query.CorrelationId, IdentityAdmissionProof.Digest(query.Payload));
            IdentityAdmissionEvidence proof = verifier.Verify(query.IdentityAdmissionProof, scope).ShouldNotBeNull();
            proof.SourceId.ShouldBe("works-reader");
            proof.TargetActorId.ShouldBe(actor);
            proof.ActorActive.ShouldBeTrue();
            query.Tenant.ShouldBe(tenant);
            using JsonDocument payload = JsonDocument.Parse(query.Payload);
            payload.RootElement.GetProperty("expectedActorId").GetString().ShouldBe(actor);
            proof.Scope.AggregateId.ShouldBe(payload.RootElement.GetProperty("partyId").GetString());
            // Replaying the actual signature for another actor or target cannot authorize lookup.
            verifier.Verify(query.IdentityAdmissionProof, scope with { AggregateId = "foreign" }).ShouldBeNull();
            verifier.Verify(query.IdentityAdmissionProof, scope with { PayloadDigest = "other-actor" }).ShouldBeNull();
            VerifiedQueries++;
            var envelope = new QueryEnvelope(query.Tenant, query.Domain, query.AggregateId, query.QueryType, query.Payload,
                query.CorrelationId, query.UserId, null, false, null, null, null, false, null, null)
            {
                IdentityAdmissionProof = query.IdentityAdmissionProof,
            };
            return ResolveAsync(envelope, partyService, call.Arg<CancellationToken>());
        });
        builder.Services.AddSingleton(mediator);
        WebApplication app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        return app;
    }

    private static async Task<SubmitQueryResult> ResolveAsync(QueryEnvelope envelope, PartyIdentityQueryService service,
        CancellationToken cancellationToken)
    {
        PartyIdentityResult result = await service.ResolveAsync(envelope, cancellationToken).ConfigureAwait(false);
        return new SubmitQueryResult(envelope.CorrelationId, JsonSerializer.SerializeToElement(result));
    }

    private static (IdentityAdmissionSigner, IdentityAdmissionProof) Keys()
    {
        using RSA rsa = RSA.Create(2048);
        IOptionsMonitor<IdentityAdmissionSigningOptions> signing = Substitute.For<IOptionsMonitor<IdentityAdmissionSigningOptions>>();
        signing.CurrentValue.Returns(new IdentityAdmissionSigningOptions { PrivateKeyPem = rsa.ExportRSAPrivateKeyPem() });
        IOptionsMonitor<IdentityAdmissionOptions> verification = Substitute.For<IOptionsMonitor<IdentityAdmissionOptions>>();
        verification.CurrentValue.Returns(new IdentityAdmissionOptions { AuthorityRevision = 1, VerificationKeyPem = rsa.ExportSubjectPublicKeyInfoPem() });
        return (new IdentityAdmissionSigner(signing), new IdentityAdmissionProof(verification, TimeProvider.System));
    }
}
