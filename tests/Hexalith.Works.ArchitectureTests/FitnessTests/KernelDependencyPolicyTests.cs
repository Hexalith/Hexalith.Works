using System.Text.Json.Nodes;

using Shouldly;

namespace Hexalith.Works.ArchitectureTests.FitnessTests;

/// <summary>
/// Verifies fail-closed evaluated dependency parsing and kernel dependency classification.
/// </summary>
public sealed class KernelDependencyPolicyTests
{
    /// <summary>
    /// Verifies the policy owns the exact four-project kernel set.
    /// </summary>
    [Fact]
    public void GovernedProjectSetIsExact()
    {
        KernelDependencyPolicy.GovernedProjects.ToArray().ShouldBe(
            [
                "Hexalith.Works.Contracts",
                "Hexalith.Works.Server",
                "Hexalith.Works.Projections",
                "Hexalith.Works.Reactor",
            ],
            ignoreOrder: false);
    }

    /// <summary>
    /// Verifies approved low-level dependencies and framework references remain allowed.
    /// </summary>
    [Fact]
    public void SafeEvaluatedClosureIsAccepted()
    {
        string[] violations = KernelDependencyPolicy.EvaluateJson(
            "Hexalith.Works.Contracts",
            "synthetic/project.assets.json",
            CreateAssetsJson("Hexalith.EventStore.Contracts"));

        violations.ShouldBeEmpty();
    }

    /// <summary>
    /// Verifies every independently required forbidden family is classified and reported.
    /// </summary>
    [Theory]
    [InlineData("Dapr.Client", "Dapr runtime")]
    [InlineData("Hexalith.EventStore.DomainService", "EventStore client/runtime")]
    [InlineData("Hexalith.Parties.UI", "UI adapter")]
    [InlineData("ModelContextProtocol", "MCP adapter")]
    [InlineData("Microsoft.SemanticKernel", "LLM adapter")]
    [InlineData("Microsoft.Extensions.AI.Abstractions", "LLM adapter")]
    [InlineData("Azure.AI.OpenAI", "LLM adapter")]
    [InlineData("Radzen.Blazor", "UI adapter")]
    [InlineData("Blazorise", "UI adapter")]
    [InlineData("Serilog.Sinks.Console", "telemetry adapter")]
    [InlineData("NLog", "telemetry adapter")]
    [InlineData("Microsoft.OpenApi", "OpenAPI adapter")]
    [InlineData("Microsoft.Extensions.Hosting", "hosting runtime")]
    [InlineData("OpenTelemetry", "telemetry adapter")]
    [InlineData("Microsoft.EntityFrameworkCore", "persistence adapter")]
    [InlineData("Contoso.Security", "named adapter family")]
    [InlineData("Contoso.Channel", "named adapter family")]
    [InlineData("Hexalith.Parties.Picker", "named adapter family")]
    [InlineData("Hexalith.Tenants.Client", "named adapter family")]
    [InlineData("Azure.Storage.Blobs", "network/messaging client")]
    [InlineData("Azure.Messaging.ServiceBus", "network/messaging client")]
    [InlineData("RabbitMQ.Client", "network/messaging client")]
    [InlineData("Confluent.Kafka", "network/messaging client")]
    [InlineData("MassTransit", "network/messaging client")]
    [InlineData("Grpc.Net.Client", "network/messaging client")]
    [InlineData("Refit", "network/messaging client")]
    [InlineData("AWSSDK.S3", "network/messaging client")]
    public void ForbiddenDependencyFamilyIsReported(string dependencyName, string expectedFamily)
    {
        string[] violations = KernelDependencyPolicy.EvaluateJson(
            "Hexalith.Works.Server",
            "synthetic/project.assets.json",
            CreateAssetsJson(dependencyName));

        violations.ShouldHaveSingleItem();
        violations[0].ShouldContain(dependencyName, Case.Sensitive);
        violations[0].ShouldContain(expectedFamily, Case.Sensitive);
        violations[0].ShouldContain("Hexalith.Works.Server", Case.Sensitive);
    }

    /// <summary>
    /// Verifies namespace-aware adapter matching does not reject safe near-matches.
    /// </summary>
    [Theory]
    [InlineData("System.Security.Cryptography")]
    [InlineData("System.Threading.Channels")]
    [InlineData("System.Net.Http")]
    [InlineData("Microsoft.Extensions.DependencyInjection.Abstractions")]
    [InlineData("Hexalith.EventStore.Contracts")]
    [InlineData("Acme.ClientUtilities")]
    [InlineData("Acme.SecurityTools")]
    [InlineData("Acme.Serilogic")]
    [InlineData("Microsoft.Extensions.AIfoundation")]
    public void SafeNearMatchDependencyIsAccepted(string dependencyName)
    {
        string[] violations = KernelDependencyPolicy.EvaluateJson(
            "Hexalith.Works.Contracts",
            "synthetic/project.assets.json",
            CreateAssetsJson(dependencyName));

        violations.ShouldBeEmpty();
    }

    /// <summary>
    /// Verifies the original synthetic transitive EventStore Client to Dapr chain remains non-vacuous.
    /// </summary>
    [Fact]
    public void ForbiddenTransitiveEventStoreClientAndDaprDependenciesAreReported()
    {
        string[] violations = KernelDependencyPolicy.EvaluateJson(
            "Hexalith.Works.Server",
            "synthetic/project.assets.json",
            _forbiddenTransitiveAssetsJson);

        violations.Length.ShouldBe(2);
        violations.ShouldContain(violation => violation.Contains("Hexalith.Works.Server", StringComparison.Ordinal));
        violations.ShouldContain(violation => violation.Contains("Hexalith.EventStore.Client", StringComparison.Ordinal));
        violations.ShouldContain(violation => violation.Contains("Dapr.Client", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies forbidden evaluated framework references are governed alongside target libraries.
    /// </summary>
    [Fact]
    public void ForbiddenFrameworkReferenceIsReported()
    {
        string[] violations = KernelDependencyPolicy.EvaluateJson(
            "Hexalith.Works.Reactor",
            "synthetic/framework-reference-project.assets.json",
            CreateAssetsJson("Hexalith.Works.Contracts", "Microsoft.AspNetCore.App"));

        violations.ShouldHaveSingleItem();
        violations[0].ShouldContain("Microsoft.AspNetCore.App", Case.Sensitive);
        violations[0].ShouldContain("framework reference", Case.Sensitive);
        violations[0].ShouldContain("hosting runtime", Case.Sensitive);
    }

    /// <summary>
    /// Verifies every case-variant matching target object is inspected.
    /// </summary>
    [Fact]
    public void CaseVariantMatchingTargetGraphsAreAllInspected()
    {
        const string caseVariantTargetsJson =
            """
            {
              "project": {
                "frameworks": {
                  "net10.0": {
                    "targetAlias": "net10.0",
                    "frameworkReferences": {
                      "Microsoft.NETCore.App": {}
                    }
                  }
                }
              },
              "targets": {
                "net10.0": {
                  "Hexalith.EventStore.Contracts/3.97.0": {}
                },
                "NET10.0": {
                  "Dapr.Client/1.18.5": {}
                }
              }
            }
            """;

        string[] violations = KernelDependencyPolicy.EvaluateJson(
            "Hexalith.Works.Contracts",
            "synthetic/case-variant-project.assets.json",
            caseVariantTargetsJson);

        violations.ShouldHaveSingleItem();
        violations[0].ShouldContain("Dapr.Client", Case.Sensitive);
    }

    /// <summary>
    /// Verifies malformed framework entries and evaluated framework metadata fail closed.
    /// </summary>
    [Theory]
    [InlineData("[]", "framework entry")]
    [InlineData("{ \"targetAlias\": 42 }", "targetAlias")]
    [InlineData("{ \"targetAlias\": \" \" }", "targetAlias")]
    [InlineData("{ \"targetAlias\": \"net10.0\", \"frameworkReferences\": [] }", "frameworkReferences")]
    public void MalformedFrameworkMetadataIsReported(string frameworkJson, string expectedReason)
    {
        string assetsJson =
            $$"""
            {
              "project": {
                "frameworks": {
                  "net10.0": {{frameworkJson}}
                }
              },
              "targets": {
                "net10.0": {
                  "Hexalith.EventStore.Contracts/3.97.0": {}
                }
              }
            }
            """;

        string[] violations = KernelDependencyPolicy.EvaluateJson(
            "Hexalith.Works.Contracts",
            "synthetic/malformed-framework-project.assets.json",
            assetsJson);

        violations.ShouldContain(violation => violation.Contains(expectedReason, StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies a real assets artifact identifying another project fails closed.
    /// </summary>
    [Fact]
    public void MismatchedAssetsProjectIdentityIsReported()
    {
        DirectoryInfo temporaryRoot = Directory.CreateTempSubdirectory("Hexalith.Works.KernelDependencyPolicyTests-");
        try
        {
            string projectPath = Path.Combine(temporaryRoot.FullName, "Hexalith.Works.Contracts.csproj");
            string assetsPath = Path.Combine(temporaryRoot.FullName, "obj", "project.assets.json");
            Directory.CreateDirectory(Path.GetDirectoryName(assetsPath)!);
            File.WriteAllText(projectPath, "<Project />");
            File.WriteAllText(
                assetsPath,
                CreateAssetsJson(
                    "Hexalith.EventStore.Contracts",
                    restoreProjectPath: Path.Combine(temporaryRoot.FullName, "Hexalith.Works.Server.csproj")));
            SetFreshness(projectPath, assetsPath, assetsIsStale: false);

            string[] violations = KernelDependencyPolicy.EvaluateFile(
                "Hexalith.Works.Contracts",
                projectPath,
                assetsPath);

            violations.ShouldHaveSingleItem();
            violations[0].ShouldContain("identifies project", Case.Sensitive);
            violations[0].ShouldContain("Hexalith.Works.Server.csproj", Case.Sensitive);
            violations[0].ShouldContain("Hexalith.Works.Contracts.csproj", Case.Sensitive);
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
    }

    /// <summary>
    /// Verifies an assets artifact older than its governed project fails closed.
    /// </summary>
    [Fact]
    public void StaleAssetsFileIsReported()
    {
        DirectoryInfo temporaryRoot = Directory.CreateTempSubdirectory("Hexalith.Works.KernelDependencyPolicyTests-");
        try
        {
            string projectPath = Path.Combine(temporaryRoot.FullName, "Hexalith.Works.Reactor.csproj");
            string assetsPath = Path.Combine(temporaryRoot.FullName, "obj", "project.assets.json");
            Directory.CreateDirectory(Path.GetDirectoryName(assetsPath)!);
            File.WriteAllText(projectPath, "<Project />");
            File.WriteAllText(
                assetsPath,
                CreateAssetsJson("Hexalith.EventStore.Contracts", restoreProjectPath: projectPath));
            SetFreshness(projectPath, assetsPath, assetsIsStale: true);

            string[] violations = KernelDependencyPolicy.EvaluateFile(
                "Hexalith.Works.Reactor",
                projectPath,
                assetsPath);

            violations.ShouldHaveSingleItem();
            violations[0].ShouldContain("stale", Case.Insensitive);
            violations[0].ShouldContain(projectPath, Case.Sensitive);
            violations[0].ShouldContain(assetsPath, Case.Sensitive);
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
    }

    /// <summary>
    /// Verifies a missing restored artifact cannot make the policy pass vacuously.
    /// </summary>
    [Fact]
    public void MissingEvaluatedGraphIsReported()
    {
        string root = RepositoryRoot.Locate();
        string projectPath = Path.Combine(root, "src", "Hexalith.Works.Reactor", "Hexalith.Works.Reactor.csproj");
        string missingPath = Path.Combine(root, "missing-kernel-graph", "project.assets.json");

        File.Exists(missingPath).ShouldBeFalse("The negative fixture must remain absent so the missing-artifact proof is non-vacuous.");

        string[] violations = KernelDependencyPolicy.EvaluateFile(
            "Hexalith.Works.Reactor",
            projectPath,
            missingPath);

        violations.ShouldHaveSingleItem();
        violations[0].ShouldContain("Hexalith.Works.Reactor", Case.Sensitive);
        violations[0].ShouldContain(missingPath, Case.Sensitive);
        violations[0].ShouldContain("missing", Case.Insensitive);
    }

    /// <summary>
    /// Verifies invalid JSON is reported with the artifact path.
    /// </summary>
    [Fact]
    public void MalformedEvaluatedGraphIsReported()
    {
        string[] violations = KernelDependencyPolicy.EvaluateJson(
            "Hexalith.Works.Projections",
            "synthetic/malformed-project.assets.json",
            "{ not-json }");

        violations.ShouldHaveSingleItem();
        violations[0].ShouldContain("synthetic/malformed-project.assets.json", Case.Sensitive);
        violations[0].ShouldContain("could not be parsed", Case.Insensitive);
    }

    /// <summary>
    /// Verifies an empty restored artifact cannot make the policy pass vacuously.
    /// </summary>
    [Fact]
    public void EmptyEvaluatedGraphIsReportedWithItsArtifactPath()
    {
        string[] violations = KernelDependencyPolicy.EvaluateJson(
            "Hexalith.Works.Server",
            "synthetic/empty-project.assets.json",
            string.Empty);

        violations.ShouldHaveSingleItem();
        violations[0].ShouldContain("synthetic/empty-project.assets.json", Case.Sensitive);
        violations[0].ShouldContain("empty", Case.Insensitive);
    }

    /// <summary>
    /// Verifies a graph without the declared framework target is unusable.
    /// </summary>
    [Fact]
    public void EvaluatedGraphWithoutMatchingTargetIsReported()
    {
        const string noMatchingTargetJson =
            """
            {
              "project": {
                "frameworks": {
                  "net10.0": {
                    "targetAlias": "net10.0"
                  }
                }
              },
              "targets": {
                "net9.0": {
                  "Hexalith.EventStore.Contracts/3.97.0": {}
                }
              }
            }
            """;

        string[] violations = KernelDependencyPolicy.EvaluateJson(
            "Hexalith.Works.Contracts",
            "synthetic/no-target-project.assets.json",
            noMatchingTargetJson);

        violations.Length.ShouldBe(2);
        violations.ShouldContain(violation => violation.Contains("no target graph matches declared framework 'net10.0'", StringComparison.Ordinal));
        violations.ShouldContain(violation => violation.Contains("target graph 'net9.0' matches no declared framework", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies a target graph no declared framework claims is never left uninspected.
    /// </summary>
    [Fact]
    public void UnclaimedTargetGraphIsReported()
    {
        const string unclaimedTargetJson =
            """
            {
              "project": {
                "frameworks": {
                  "net10.0": {
                    "targetAlias": "net10.0"
                  }
                }
              },
              "targets": {
                "net10.0": {
                  "Hexalith.EventStore.Contracts/3.97.0": {}
                },
                "net9.0": {
                  "Dapr.Client/1.18.5": {}
                }
              }
            }
            """;

        string[] violations = KernelDependencyPolicy.EvaluateJson(
            "Hexalith.Works.Contracts",
            "synthetic/unclaimed-target-project.assets.json",
            unclaimedTargetJson);

        violations.ShouldHaveSingleItem();
        violations[0].ShouldContain("target graph 'net9.0' matches no declared framework", Case.Sensitive);
    }

    /// <summary>
    /// Verifies a forbidden shared framework carried by a transitive library is reported with its carrier.
    /// </summary>
    [Fact]
    public void TransitiveLibraryFrameworkReferenceIsReported()
    {
        string[] violations = KernelDependencyPolicy.EvaluateJson(
            "Hexalith.Works.Server",
            "synthetic/transitive-framework-reference.assets.json",
            CreateAssetsJson(
                "Grpc.AspNetCore.Server",
                libraryFrameworkReferences: """["Microsoft.AspNetCore.App"]"""));

        violations.ShouldHaveSingleItem();
        violations[0].ShouldContain("Microsoft.AspNetCore.App", Case.Sensitive);
        violations[0].ShouldContain("transitive framework reference (via Grpc.AspNetCore.Server)", Case.Sensitive);
        violations[0].ShouldContain("hosting runtime", Case.Sensitive);
        violations[0].ShouldContain("Hexalith.Works.Server", Case.Sensitive);
    }

    /// <summary>
    /// Verifies an allowed shared framework carried by a transitive library stays accepted.
    /// </summary>
    [Fact]
    public void AllowedTransitiveLibraryFrameworkReferenceIsAccepted()
    {
        KernelDependencyPolicy.EvaluateJson(
            "Hexalith.Works.Contracts",
            "synthetic/transitive-framework-reference.assets.json",
            CreateAssetsJson(
                "Hexalith.EventStore.Contracts",
                libraryFrameworkReferences: """["Microsoft.NETCore.App"]"""))
            .ShouldBeEmpty();
    }

    /// <summary>
    /// Verifies malformed transitive framework-reference metadata fails closed.
    /// </summary>
    [Theory]
    [InlineData("{}", "has malformed frameworkReferences")]
    [InlineData("[42]", "contains a malformed framework reference")]
    [InlineData("""[" "]""", "contains a malformed framework reference")]
    public void MalformedTransitiveLibraryFrameworkReferenceIsReported(string frameworkReferencesJson, string expectedReason)
    {
        string[] violations = KernelDependencyPolicy.EvaluateJson(
            "Hexalith.Works.Reactor",
            "synthetic/malformed-transitive-framework-reference.assets.json",
            CreateAssetsJson("Hexalith.EventStore.Contracts", libraryFrameworkReferences: frameworkReferencesJson));

        violations.ShouldHaveSingleItem();
        violations[0].ShouldContain(expectedReason, Case.Sensitive);
        violations[0].ShouldContain("Hexalith.EventStore.Contracts", Case.Sensitive);
    }

    /// <summary>
    /// Verifies a whitespace-padded library key is classified rather than treated as a safe name.
    /// </summary>
    [Fact]
    public void WhitespacePaddedLibraryKeyIsClassified()
    {
        var document = new JsonObject
        {
            ["project"] = new JsonObject
            {
                ["frameworks"] = new JsonObject
                {
                    ["net10.0"] = new JsonObject { ["targetAlias"] = "net10.0" },
                },
            },
            ["targets"] = new JsonObject
            {
                ["net10.0"] = new JsonObject { [" Dapr.Client /1.18.5"] = new JsonObject() },
            },
        };

        string[] violations = KernelDependencyPolicy.EvaluateJson(
            "Hexalith.Works.Contracts",
            "synthetic/whitespace-library-project.assets.json",
            document.ToJsonString());

        violations.ShouldHaveSingleItem();
        violations[0].ShouldContain("'Dapr.Client' is forbidden (Dapr runtime)", Case.Sensitive);
    }

    /// <summary>
    /// Verifies a malformed library key cannot be silently skipped as a safe dependency.
    /// </summary>
    [Theory]
    [InlineData(" /1.0.0")]
    [InlineData("/1.0.0")]
    [InlineData("Dapr.Client/")]
    [InlineData("Dapr.Client")]
    public void MalformedLibraryKeyIsReported(string libraryKey)
    {
        var document = new JsonObject
        {
            ["project"] = new JsonObject
            {
                ["frameworks"] = new JsonObject
                {
                    ["net10.0"] = new JsonObject { ["targetAlias"] = "net10.0" },
                },
            },
            ["targets"] = new JsonObject
            {
                ["net10.0"] = new JsonObject { [libraryKey] = new JsonObject() },
            },
        };

        string[] violations = KernelDependencyPolicy.EvaluateJson(
            "Hexalith.Works.Contracts",
            "synthetic/malformed-library-project.assets.json",
            document.ToJsonString());

        violations.ShouldHaveSingleItem();
        violations[0].ShouldContain("malformed library entry", Case.Sensitive);
    }

    /// <summary>
    /// Verifies the repository-layout entry point the architecture gate calls reports a forbidden closure.
    /// </summary>
    [Fact]
    public void GovernedProjectLayoutReportsForbiddenEvaluatedClosure()
    {
        DirectoryInfo temporaryRoot = Directory.CreateTempSubdirectory("Hexalith.Works.KernelDependencyPolicyTests-");
        try
        {
            (string projectPath, string assetsPath) = CreateGovernedProjectLayout(
                temporaryRoot.FullName,
                "Hexalith.Works.Server",
                _forbiddenTransitiveAssetsJson);

            string[] violations = KernelDependencyPolicy.EvaluateGovernedProject(
                temporaryRoot.FullName,
                "Hexalith.Works.Server");

            violations.Length.ShouldBe(2);
            violations.ShouldAllBe(violation => violation.Contains(assetsPath, StringComparison.Ordinal));
            violations.ShouldContain(violation => violation.Contains("Hexalith.EventStore.Client", StringComparison.Ordinal));
            violations.ShouldContain(violation => violation.Contains("Dapr.Client", StringComparison.Ordinal));
            File.Exists(projectPath).ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
    }

    /// <summary>
    /// Verifies the repository-layout entry point accepts a clean governed closure.
    /// </summary>
    [Fact]
    public void GovernedProjectLayoutAcceptsCleanEvaluatedClosure()
    {
        DirectoryInfo temporaryRoot = Directory.CreateTempSubdirectory("Hexalith.Works.KernelDependencyPolicyTests-");
        try
        {
            _ = CreateGovernedProjectLayout(
                temporaryRoot.FullName,
                "Hexalith.Works.Contracts",
                CreateAssetsJson("Hexalith.EventStore.Contracts"));

            KernelDependencyPolicy.EvaluateGovernedProject(temporaryRoot.FullName, "Hexalith.Works.Contracts")
                .ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
    }

    /// <summary>
    /// Verifies an artifact older than a shared restore input fails closed even when the governed project file is untouched.
    /// </summary>
    [Theory]
    [InlineData("Directory.Packages.props")]
    [InlineData("Directory.Build.props")]
    [InlineData("Directory.Build.targets")]
    [InlineData("Directory.Solution.props")]
    [InlineData("global.json")]
    [InlineData("NuGet.Config")]
    public void AssetsOlderThanSharedRestoreInputAreReported(string sharedRestoreInput)
    {
        DirectoryInfo temporaryRoot = Directory.CreateTempSubdirectory("Hexalith.Works.KernelDependencyPolicyTests-");
        try
        {
            (string projectPath, string assetsPath) = CreateGovernedProjectLayout(
                temporaryRoot.FullName,
                "Hexalith.Works.Projections",
                CreateAssetsJson("Hexalith.EventStore.Contracts"));

            string sharedInputPath = Path.Combine(temporaryRoot.FullName, sharedRestoreInput);
            File.WriteAllText(sharedInputPath, "<Project />");

            var baseline = new DateTime(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(projectPath, baseline.AddMinutes(-10));
            File.SetLastWriteTimeUtc(assetsPath, baseline);
            File.SetLastWriteTimeUtc(sharedInputPath, baseline.AddMinutes(10));

            string[] violations = KernelDependencyPolicy.EvaluateGovernedProject(
                temporaryRoot.FullName,
                "Hexalith.Works.Projections");

            violations.ShouldHaveSingleItem();
            violations[0].ShouldContain("stale", Case.Insensitive);
            violations[0].ShouldContain(sharedInputPath, Case.Sensitive);
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
    }

    private const string _forbiddenTransitiveAssetsJson =
        """
        {
          "project": {
            "frameworks": {
              "net10.0": {
                "targetAlias": "net10.0",
                "frameworkReferences": {
                  "Microsoft.NETCore.App": {}
                }
              }
            }
          },
          "targets": {
            "net10.0": {
              "Hexalith.Works.Contracts/1.0.0": {
                "dependencies": {
                  "Hexalith.EventStore.Client": "3.97.0"
                }
              },
              "Hexalith.EventStore.Client/3.97.0": {
                "dependencies": {
                  "Dapr.Client": "1.18.5"
                }
              },
              "Dapr.Client/1.18.5": {}
            }
          }
        }
        """;

    private static string CreateAssetsJson(
        string dependencyName,
        string? forbiddenFrameworkReference = null,
        string? restoreProjectPath = null,
        string? libraryFrameworkReferences = null)
    {
        var frameworkReferences = new JsonObject
        {
            ["Microsoft.NETCore.App"] = new JsonObject(),
        };
        if (forbiddenFrameworkReference is not null)
        {
            frameworkReferences[forbiddenFrameworkReference] = new JsonObject();
        }

        var project = new JsonObject
        {
            ["frameworks"] = new JsonObject
            {
                ["net10.0"] = new JsonObject
                {
                    ["targetAlias"] = "net10.0",
                    ["frameworkReferences"] = frameworkReferences,
                },
            },
        };
        if (restoreProjectPath is not null)
        {
            project["restore"] = new JsonObject
            {
                ["projectPath"] = restoreProjectPath,
            };
        }

        var library = new JsonObject();
        if (libraryFrameworkReferences is not null)
        {
            library["frameworkReferences"] = JsonNode.Parse(libraryFrameworkReferences);
        }

        var document = new JsonObject
        {
            ["project"] = project,
            ["targets"] = new JsonObject
            {
                ["net10.0"] = new JsonObject
                {
                    [$"{dependencyName}/1.0.0"] = library,
                },
            },
        };

        return document.ToJsonString();
    }

    private static (string ProjectPath, string AssetsPath) CreateGovernedProjectLayout(
        string repositoryRoot,
        string governedProject,
        string assetsJson)
    {
        string projectDirectory = Path.Combine(repositoryRoot, "src", governedProject);
        string projectPath = Path.Combine(projectDirectory, governedProject + ".csproj");
        string assetsPath = Path.Combine(projectDirectory, "obj", "project.assets.json");
        Directory.CreateDirectory(Path.GetDirectoryName(assetsPath)!);
        File.WriteAllText(projectPath, "<Project />");
        File.WriteAllText(assetsPath, InjectRestoreProjectPath(assetsJson, projectPath));
        SetFreshness(projectPath, assetsPath, assetsIsStale: false);

        return (projectPath, assetsPath);
    }

    private static string InjectRestoreProjectPath(string assetsJson, string projectPath)
    {
        JsonNode document = JsonNode.Parse(assetsJson)!;
        document["project"]!["restore"] = new JsonObject
        {
            ["projectPath"] = projectPath,
        };

        return document.ToJsonString();
    }

    private static void SetFreshness(string projectPath, string assetsPath, bool assetsIsStale)
    {
        var baseline = new DateTime(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(projectPath, baseline);
        File.SetLastWriteTimeUtc(assetsPath, assetsIsStale ? baseline.AddMinutes(-1) : baseline.AddMinutes(1));
    }
}
