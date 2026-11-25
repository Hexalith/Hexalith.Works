using Shouldly;

namespace Hexalith.Works.IntegrationTests;

public sealed class WorksPlatformResiliencyOverlayTests
{
    [Fact]
    public void RelaxForProcessOperationBudgetStopsAppRetriesWithoutTouchingPubsubInbound()
    {
        string path = Path.Combine(Path.GetTempPath(), "works-resiliency-" + Guid.NewGuid().ToString("N") + ".yaml");
        File.WriteAllText(path, """
spec:
  policies:
    retries:
      defaultRetry:
        policy: constant
        duration: 1s
        maxRetries: 3
      pubsubRetryInbound:
        policy: exponential
        maxInterval: 30s
        maxRetries: 10
    circuitBreakers:
      defaultBreaker:
        maxRequests: 1
        interval: 10s
        timeout: 30s
        trip: consecutiveFailures > 3
""");
        try
        {
            WorksPlatformResiliencyOverlay.RelaxForProcessOperationBudget(path).ShouldBeTrue();
            string text = File.ReadAllText(path);
            text.ShouldContain("maxRetries: 0");
            text.ShouldContain("trip: consecutiveFailures > 20");
            text.ShouldContain("timeout: 5s");
            text.ShouldContain("maxRetries: 10");
            text.ShouldNotContain("consecutiveFailures > 3");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RelaxForProcessOperationBudgetLeavesRetainedHostDocumentsUnchanged()
    {
        string path = Path.Combine(Path.GetTempPath(), "works-resiliency-" + Guid.NewGuid().ToString("N") + ".yaml");
        const string retained = "apiVersion: dapr.io/v1alpha1\nkind: Resiliency\nmetadata:\n  name: resiliency\n";
        File.WriteAllText(path, retained);
        try
        {
            WorksPlatformResiliencyOverlay.RelaxForProcessOperationBudget(path).ShouldBeFalse();
            File.ReadAllText(path).ShouldBe(retained);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
