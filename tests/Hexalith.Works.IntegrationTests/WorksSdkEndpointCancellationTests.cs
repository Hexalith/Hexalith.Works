using System.Text.Json;

using Hexalith.EventStore.Client.Aggregates;
using Hexalith.EventStore.Contracts.Projections;
using Hexalith.EventStore.Contracts.Queries;
using Hexalith.EventStore.Contracts.Replay;
using Hexalith.EventStore.DomainService;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using NSubstitute;
using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Checks request-abort binding through the consumed SDK's actual mapped HTTP delegates.</summary>
public sealed class WorksSdkEndpointCancellationTests
{
    /// <summary>Each mapped operation forwards a live token and refuses success after its handler cancels it.</summary>
    /// <param name="route">The SDK route whose generated HTTP delegate is invoked.</param>
    /// <param name="scenario">Successful, pre-aborted, handler-cancelled or suspended-handler cancellation.</param>
    [Theory]
    [InlineData("/query", "success")]
    [InlineData("/query", "cancel-in-handler")]
    [InlineData("/query", "pre-aborted")]
    [InlineData("/query", "cancel-pending")]
    [InlineData("/replay-state", "success")]
    [InlineData("/replay-state", "cancel-in-handler")]
    [InlineData("/replay-state", "pre-aborted")]
    [InlineData("/replay-state", "cancel-pending")]
    [InlineData("/project", "success")]
    [InlineData("/project", "cancel-in-handler")]
    [InlineData("/project", "pre-aborted")]
    public async Task MappedOperationsForwardAndObserveRequestAborted(string route, string scenario)
    {
        using var caller = new CancellationTokenSource();
        CancellationToken observed = default;
        int invocations = 0;
        object? boundRequest = null;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken testToken = TestContext.Current.CancellationToken;
        void Observe(CancellationToken token)
        {
            observed = token;
            token.IsCancellationRequested.ShouldBeFalse();
            invocations++;
            entered.TrySetResult();
            if (scenario == "cancel-in-handler")
            {
                caller.Cancel();
            }
        }

        IDomainQueryHandler queryHandler = Substitute.For<IDomainQueryHandler>();
        queryHandler.Domain.Returns("token-probe");
        queryHandler.QueryType.Returns("get-probe");
        queryHandler.ExecuteAsync(Arg.Any<QueryEnvelope>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            boundRequest = call.ArgAt<QueryEnvelope>(0);
            Observe(call.ArgAt<CancellationToken>(1));
            if (scenario == "cancel-pending")
            {
                await release.Task.WaitAsync(TimeSpan.FromSeconds(5), testToken).ConfigureAwait(true);
            }
            return QueryResult.FromPayload(JsonSerializer.SerializeToElement(new { value = 1 }));
        });

        IDomainProjectionHandler projectionHandler = Substitute.For<IDomainProjectionHandler>();
        projectionHandler.Domain.Returns("token-probe");
        projectionHandler.Project(Arg.Any<ProjectionRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            boundRequest = call.ArgAt<ProjectionRequest>(0);
            Observe(call.ArgAt<CancellationToken>(1));
            return new ProjectionResponse("probe-summary", JsonSerializer.SerializeToElement(new { value = 1 }));
        });

        IAsyncAggregateReplay replayHandler = Substitute.For<IAsyncAggregateReplay>();
        replayHandler.CanReplayAggregateType("probe").Returns(true);
        replayHandler.ReplayAsync(Arg.Any<AggregateReconstructionRequest>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            boundRequest = call.ArgAt<AggregateReconstructionRequest>(0);
            Observe(call.ArgAt<CancellationToken>(1));
            if (scenario == "cancel-pending")
            {
                await release.Task.WaitAsync(TimeSpan.FromSeconds(5), testToken).ConfigureAwait(true);
            }
            return AggregateReconstructionResult.Succeeded("{}", 0);
        });

        WebApplicationBuilder builder = WebApplication.CreateBuilder(["--Logging:LogLevel:Default=Error"]);
        builder.Configuration["EventStore:DomainService:AppId"] = "cancellation-probe";
        builder.Configuration["EventStore:DomainService:ServiceVersion"] = "v1";
        builder.AddEventStoreDomainService();
        builder.Services.AddSingleton(queryHandler);
        builder.Services.AddSingleton(projectionHandler);
        builder.Services.AddKeyedSingleton<IAsyncAggregateReplay>("token-probe", replayHandler);
        await using WebApplication app = builder.Build();
        app.MapEventStoreDomainService();

        object request = route switch
        {
            "/query" => new QueryEnvelope("tenant", "token-probe", "probe", "get-probe", [123, 125], "correlation", "user"),
            "/replay-state" => new AggregateReconstructionRequest("tenant", "token-probe", "probe", "probe", 0, [], false, null),
            "/project" => new ProjectionRequest("tenant", "token-probe", "probe", []),
            _ => throw new ArgumentOutOfRangeException(nameof(route)),
        };
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(request, request.GetType(), JsonSerializerOptions.Web);
        await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            RequestAborted = caller.Token,
        };
        IHttpRequestBodyDetectionFeature bodyDetection = Substitute.For<IHttpRequestBodyDetectionFeature>();
        bodyDetection.CanHaveBody.Returns(true);
        context.Features.Set(bodyDetection);
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = route;
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = body.Length;
        using var requestBody = new MemoryStream(body);
        using var responseBody = new MemoryStream();
        context.Request.Body = requestBody;
        context.Response.Body = responseBody;
        RouteEndpoint endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText == route);
        RequestDelegate invoke = endpoint.RequestDelegate.ShouldNotBeNull();

        if (scenario == "pre-aborted")
        {
            caller.Cancel();
        }
        Task invocation = invoke(context);
        try
        {
            if (scenario == "cancel-pending")
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), testToken).ConfigureAwait(true);
                invocation.IsCompleted.ShouldBeFalse();
                caller.Cancel();
                release.TrySetResult();
            }

            if (scenario != "success")
            {
                OperationCanceledException exception = await Should.ThrowAsync<OperationCanceledException>(
                    () => invocation.WaitAsync(TimeSpan.FromSeconds(5), testToken)).ConfigureAwait(true);
                exception.CancellationToken.ShouldBe(caller.Token);
                responseBody.Length.ShouldBe(0);
            }
            else
            {
                await invocation.WaitAsync(TimeSpan.FromSeconds(5), testToken).ConfigureAwait(true);
                context.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
                responseBody.Position = 0;
                switch (route)
                {
                    case "/query":
                        QueryResult result = (await JsonSerializer.DeserializeAsync<QueryResult>(responseBody,
                            JsonSerializerOptions.Web, testToken).ConfigureAwait(true)).ShouldNotBeNull();
                        result.Success.ShouldBeTrue();
                        using (JsonDocument payload = JsonDocument.Parse(result.PayloadBytes.ShouldNotBeNull()))
                        {
                            payload.RootElement.GetProperty("value").GetInt32().ShouldBe(1);
                        }
                        break;
                    case "/replay-state":
                        AggregateReconstructionResult replay = (await JsonSerializer.DeserializeAsync<AggregateReconstructionResult>(
                            responseBody, JsonSerializerOptions.Web, testToken).ConfigureAwait(true)).ShouldNotBeNull();
                        replay.Status.ShouldBe(AggregateReconstructionStatus.Succeeded);
                        replay.StateJson.ShouldBe("{}");
                        break;
                    case "/project":
                        ProjectionResponse projection = (await JsonSerializer.DeserializeAsync<ProjectionResponse>(responseBody,
                            JsonSerializerOptions.Web, testToken).ConfigureAwait(true)).ShouldNotBeNull();
                        projection.ProjectionType.ShouldBe("probe-summary");
                        projection.State.GetProperty("value").GetInt32().ShouldBe(1);
                        break;
                }
            }

            invocations.ShouldBe(scenario == "pre-aborted" ? 0 : 1);
            if (scenario != "pre-aborted")
            {
                observed.ShouldBe(caller.Token);
                observed.CanBeCanceled.ShouldBeTrue();
                object actual = boundRequest.ShouldNotBeNull();
                JsonElement.DeepEquals(
                    JsonSerializer.SerializeToElement(actual, actual.GetType(), JsonSerializerOptions.Web),
                    JsonSerializer.SerializeToElement(request, request.GetType(), JsonSerializerOptions.Web)).ShouldBeTrue();
            }
        }
        finally
        {
            release.TrySetResult();
        }
    }
}
