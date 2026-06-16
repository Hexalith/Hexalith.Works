using System.Reflection;

using Hexalith.Works.Contracts;
using Hexalith.Works.Contracts.Ports;
using Hexalith.Works.Projections;
using Hexalith.Works.Server;

using Shouldly;

namespace Hexalith.Works.ArchitectureTests.FitnessTests;

public sealed class BoundaryPortTests
{
    private const string PortsNamespace = "Hexalith.Works.Contracts.Ports";

    private static readonly Assembly ContractsAssembly = typeof(WorksContractsAssembly).Assembly;
    private static readonly Assembly ServerAssembly = typeof(WorksServerAssembly).Assembly;
    private static readonly Assembly ProjectionsAssembly = typeof(WorksProjectionsAssembly).Assembly;

    [Fact]
    public void ExpectationResolver_isDeclaredAsAPortInContracts()
    {
        Type port = typeof(IExpectationResolver);

        port.IsInterface.ShouldBeTrue("IExpectationResolver must be a domain-owned abstraction.");
        port.Assembly.ShouldBe(ContractsAssembly, "IExpectationResolver must be declared in Hexalith.Works.Contracts.");
        port.Namespace.ShouldBe(PortsNamespace);
    }

    [Fact]
    public void ExecutorRouter_isDeclaredAsAPortInContracts()
    {
        Type port = typeof(IExecutorRouter);

        port.IsInterface.ShouldBeTrue("IExecutorRouter must be a domain-owned abstraction.");
        port.Assembly.ShouldBe(ContractsAssembly, "IExecutorRouter must be declared in Hexalith.Works.Contracts.");
        port.Namespace.ShouldBe(PortsNamespace);
    }

    [Fact]
    public void ExecutorRouter_hasNoConcreteImplementationInTheWorksKernel()
    {
        // Vacuous-pass guard: prove the kernel assemblies actually yielded concrete types before
        // asserting that none of them implements the port, so the empty assertion is meaningful.
        Type[] kernelConcreteTypes = ConcreteTypes(ContractsAssembly, ServerAssembly, ProjectionsAssembly);
        kernelConcreteTypes.ShouldNotBeEmpty(
            "Expected to discover concrete kernel types; the abstraction-only assertion would otherwise pass vacuously.");

        Type[] implementers = [.. kernelConcreteTypes.Where(typeof(IExecutorRouter).IsAssignableFrom)];

        implementers.ShouldBeEmpty(
            "IExecutorRouter is an abstraction-only seam in v1 (Theme 4); no concrete type in the Works kernel may implement it.");
    }

    [Fact]
    public void ExpectationResolver_hasAtLeastOneConcreteImplementationInServer()
    {
        // Vacuous-pass guard: prove Server yielded concrete types before asserting an implementer exists.
        Type[] serverConcreteTypes = ConcreteTypes(ServerAssembly);
        serverConcreteTypes.ShouldNotBeEmpty(
            "Expected to discover concrete Hexalith.Works.Server types for the implementation assertion to be meaningful.");

        Type[] implementers = [.. serverConcreteTypes.Where(typeof(IExpectationResolver).IsAssignableFrom)];

        implementers.ShouldNotBeEmpty(
            "A no-LLM IExpectationResolver implementation must exist in Hexalith.Works.Server (AC #1).");
    }

    [Fact]
    public void NoContractsType_exposesAnInterpretedExpectation()
    {
        // NFR-11 / FR-2: the interpreted Expectation is resolved on demand and must NEVER be persisted.
        // Across the whole Contracts surface (events, commands, state, value objects), only the stable
        // ExpectationReference may be carried — no type may expose a property typed as Expectation.
        Type[] contractTypes = [.. ContractsAssembly.GetTypes().Where(type => type is { IsClass: true } or { IsValueType: true })];

        // Vacuous-pass guard: prove the surface was actually discovered before asserting absence.
        contractTypes.ShouldNotBeEmpty(
            "Expected to discover Contracts types; the no-persisted-Expectation assertion would otherwise pass vacuously.");

        string[] offenders = [.. contractTypes
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(property => property.PropertyType == typeof(Expectation))
            .Select(property => $"{property.DeclaringType!.FullName}.{property.Name}")];

        offenders.ShouldBeEmpty(
            "The interpreted Expectation must never be carried on a Contracts type (NFR-11, FR-2); only ExpectationReference may be persisted.");
    }

    private static Type[] ConcreteTypes(params Assembly[] assemblies)
        => [.. assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false })];
}
