"""Execute target-host evidence; never promote supporting checks to AD-20 acceptance."""
import argparse
from datetime import datetime
import hashlib
import io
import json
import os
import socket
from pathlib import Path
import subprocess
import tempfile
import tarfile
import re
import xml.etree.ElementTree as ET
import zipfile

import isolated_daprd
import retained_works_host
import works_host_snapshot
import works_toolchain
from works_verifier_command import run_owned

from verify_works_producer import (builds_input_snapshot, qualify_runtime_source, require_unchanged_runtime_source,
                                   runtime_source_snapshot, source_files)

CATALOG = "references/Hexalith.Builds/Props/Directory.Packages.props"
VERIFIER_HELPERS = (
    "verify_works_producer.py", "works_toolchain.py", "test_works_toolchain.py", "test_verify_works_host.py", "test_verify_works_producer.py",
    "isolated_daprd.py", "test_isolated_daprd.py", "retained_works_host.py", "test_retained_works_host.py",
    "retained-works-host/manifest.json", "retained-works-host/works-source.tar.gz",
    "works_host_snapshot.py", "test_works_host_snapshot.py", "verify-agents-host.sh", "verify-platform-pins.py",
    "test_verify_platform_pins.py", "test_local_dapr_security_cleanup.py", "test-local-dapr-security.py",
    "ext-host-1/verify_full.py", "ext-host-1/prepare_acceptance.py",
    "ext-host-1/test_verify_full.py", "ext-host-1/test_prepare_acceptance.py",
    "works_verifier_command.py", "test_works_verifier_command.py", "test_works_verifier_inputs.py",
)

ROWS = {
    "R1": ("Aspire/Dapr composition", "Hexalith.EventStore.Aspire", "AddHexalithEventStore/AddEventStoreDomainModule", "P/src/Hexalith.Platform.Composition/WorksPreviewModel.cs"),
    "R2": ("SDK defaults, readiness and telemetry", "Hexalith.EventStore.DomainService", "AddEventStoreDomainService", "src/Hexalith.Works/Runtime/WorksHost.cs"),
    "R3": ("Origin, redelivery and quarantine", "Hexalith.EventStore.DomainService", "UseEventStoreDomainService", "src/Hexalith.Works/Runtime/WorksHost.cs"),
    "R4": ("Projection family and fenced rebuild", "Hexalith.EventStore.Client", "SharedProjectionEpochCoordinator", "src/Hexalith.Works/Projections/SharedRebuild/WorkItemSharedProjectionRebuildHandler.cs"),
    "R5": ("Authorized and filtered queries", "Hexalith.EventStore.DomainService", "IDomainQueryHandler", "src/Hexalith.Works/Queries/WhatsNextQueryHandler.cs"),
    "R6": ("Typed reminders and lost firing/HA", "Hexalith.EventStore.DomainService", "AddEventStoreReminders", "src/Hexalith.Works/Runtime/WorksRecoveryExtensions.cs"),
    "R7": ("Paged process, reissue and restart", "Hexalith.EventStore.DomainService", "AddEventStoreProcesses", "src/Hexalith.Works/Recovery/WorksProcessTranslator.cs"),
    "R8": ("Recovery operations, audit and alerts", "Hexalith.EventStore.DomainService", "ProcessDispositionService", "src/Hexalith.Works/Recovery/WorksProcessDispositionEndpoints.cs"),
    "R9": ("Works SDK consumption", "Hexalith.EventStore.DomainService", "AddEventStoreDomainService/UseEventStoreDomainService", "src/Hexalith.Works/Runtime/WorksHost.cs"),
    "R10": ("AD-24 transport and production admission", "Hexalith.EventStore.ServiceDefaults", "EventStoreWorkloadAuthenticationDefaults", "src/Hexalith.Works/Runtime/WorksDomainAdapters.cs"),
    "R11": ("Trusted submission and receipt replay", "Hexalith.EventStore.Client", "HttpTrustedEffectSubmitter", "src/Hexalith.Works/Recovery/WorksProcessDelegationProvider.cs"),
}


def target_commands(commands):
    """Attach every executed live and retained-readiness command to the row audit."""
    names = {"retained-host-preflight", "platform-recovery", "platform-historical", "platform-ha", "platform-mtls", "platform-projection"}
    return [entry for entry in commands if entry["name"] in names]


def verification_profile(name):
    """CI checks public-package Release consumers; full local qualification also checks source Debug."""
    if name not in ("full", "ci"):
        raise ValueError("Unknown Works verification profile: " + str(name))
    return {"name": name, "consumerConfigurations": ["Debug", "Release"] if name == "full" else ["Release"],
            "agentsConfiguration": "Debug" if name == "full" else "Release",
            "sourceConformance": "required" if name == "full" else "not-requested; full-local-profile-only",
            "liveProofs": "all-R1-R11-and-complete-fleet-rollback"}


def consumer_build_command(project, configuration, version, output=None, extra=()):
    """Keep each consumer's configuration and dependency mode coupled in both profiles."""
    command = ["dotnet", "build", project, "-c", configuration, "-p:TreatWarningsAsErrors=true",
               "-p:NuGetAudit=false", "-p:Version=" + version, "-p:MinVerVersionOverride=" + version,
               "-p:MSBuildWarningsAsErrors=MSB3277",
               "-p:UseHexalithProjectReferences=" + ("true" if configuration == "Debug" else "false"),
               "-p:UseNuGetDeps=" + ("false" if configuration == "Debug" else "true")]
    if output:
        command += ["--artifacts-path", str(output)]
    command += ["-p:AspireUseCliBundle=true"] if project.endswith(".cs") else ["-m:1"]
    return command + list(extra)


def sha256(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def require_archive_assembly(artifact, assembly):
    """Bind the executed package consumer assembly to its selected archive, including extracted-cache contents."""
    assembly = Path(assembly)
    digest = sha256(assembly)
    with zipfile.ZipFile(artifact) as archive:
        matches = [name for name in archive.namelist() if name.startswith("lib/") and Path(name).name == assembly.name
                   and hashlib.sha256(archive.read(name)).hexdigest() == digest]
    if not matches:
        raise ValueError("Consumer assembly differs from its selected public archive: " + str(assembly))
    return {"consumerAssemblySha256": digest, "archiveAssemblyEntry": matches[0]}


def published_producers(assets_path, consumer_directory, version):
    """Bind every selected public SDK package and its consumer DLL to the restored NuGet archive."""
    packages = json.loads(Path(assets_path).read_text())
    producer_evidence = {}
    for key, library in packages["libraries"].items():
        if not key.startswith("Hexalith.EventStore.") or library["type"] != "package":
            continue
        package, restored = key.split("/")
        if restored != version:
            raise ValueError("Unexpected published producer: " + key)
        folder = next(Path(root) / library["path"] for root in packages["packageFolders"] if (Path(root) / library["path"]).exists())
        artifact = folder / (package.lower() + "." + restored + ".nupkg")
        metadata = json.loads((folder / ".nupkg.metadata").read_text())
        if metadata.get("source") != "https://api.nuget.org/v3/index.json":
            raise ValueError("Published producer lacks public NuGet provenance: " + package)
        with zipfile.ZipFile(artifact) as archive:
            nuspec_name = next(name for name in archive.namelist() if name.endswith(".nuspec"))
            repository = ET.fromstring(archive.read(nuspec_name)).find(".//{*}repository")
        if repository is None or not repository.get("commit"):
            raise ValueError("Published producer lacks a repository commit: " + package)
        producer_evidence[package] = {"version": restored, "sha256": sha256(artifact), "source": metadata["source"],
            "nugetSha512": library.get("sha512"), "repositoryCommit": repository.get("commit"), "repositoryUrl": repository.get("url")}
        producer_evidence[package].update(require_archive_assembly(artifact, consumer_directory / (package + ".dll")))
    return producer_evidence


def selected_sdk_version(catalog):
    """Read the one stable public EventStore version selected by the authoritative Builds catalog."""
    values = [item.text for item in ET.parse(catalog).getroot().iter("HexalithEventStoreVersion")]
    if len(values) != 1 or not re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+", (values[0] or "").strip()):
        raise ValueError("The Builds catalog must select exactly one stable public EventStore version: " + str(catalog))
    return values[0].strip()


def require_consumer_pin(props, version):
    """The Works consumer's own default must name the producer this run qualifies."""
    values = [item.text for item in ET.parse(props).getroot().iter("HexalithEventStoreVersion")]
    if len(values) != 1 or (values[0] or "").strip() != version:
        raise ValueError("Works consumer default EventStore pin " + ", ".join(str(value) for value in values)
                         + " differs from the Platform catalog selection " + version + ": " + str(props))
    return {"path": str(props), "version": version, "sha256": sha256(props)}


# Directory.Build.props sets the effective conditional default before Directory.Packages.props is imported,
# so both Works files must name the qualified producer.
CONSUMER_PIN_FILES = ("Directory.Build.props", "Directory.Packages.props")


def require_consumer_pins(works, version):
    return {name: require_consumer_pin(Path(works) / name, version) for name in CONSUMER_PIN_FILES}


HOST_PACKAGE = re.compile(r"^#:package\s+(Hexalith\.EventStore\.[A-Za-z0-9.]+)@(\S+)\s*$")


def require_host_package_pins(apphost, version):
    """The file host's own EventStore package directives must select the catalog producer."""
    pins = {}
    for line in Path(apphost).read_text().splitlines():
        match = HOST_PACKAGE.match(line.strip())
        if match:
            if match.group(1) in pins:
                raise ValueError("The Platform file host repeats an EventStore package directive: " + match.group(1))
            pins[match.group(1)] = match.group(2)
    if not pins:
        raise ValueError("The Platform file host declares no EventStore package directive: " + str(apphost))
    differing = sorted(name + "@" + pinned for name, pinned in pins.items() if pinned != version)
    if differing:
        raise ValueError("Platform file host EventStore packages differ from the catalog selection " + version + ": " + ", ".join(differing))
    return {"path": str(apphost), "version": version, "packages": sorted(pins), "sha256": sha256(apphost)}


def require_passed_tests(path, required_methods=(), required_types=()):
    """Reject empty results, test failures, skips, and filters that miss required methods."""
    root = ET.parse(path).getroot()
    assemblies = root.findall("assembly")
    tests = root.findall(".//test")
    if not assemblies or not tests:
        raise ValueError("No executed tests in " + str(path))
    for assembly in assemblies:
        for key in ("errors", "failed", "skipped", "not-run"):
            if int(assembly.get(key, "0")):
                raise ValueError(f"{path}: {key} must be zero")
        if int(assembly.get("total", "0")) != len(assembly.findall(".//test")):
            raise ValueError(f"{path}: test count does not match execution inventory")
    if any(test.get("result") != "Pass" for test in tests):
        raise ValueError(str(path) + ": every required test must execute and pass")
    methods = {test.get("method") for test in tests}
    if not set(required_methods).issubset(methods):
        raise ValueError(str(path) + ": required test methods did not execute")
    if not set(required_types).issubset({test.get("type") for test in tests}):
        raise ValueError(str(path) + ": required test classes did not execute")
    return {"total": len(tests), "passed": len(tests), "skipped": 0, "sha256": sha256(path)}


PLATFORM_RUNTIME = "Hexalith.Platform.Works.Runtime"
# Platform's host output and the Works consumer compile the same runtime project in different artifact roots. Mapping
# each root to one deterministic path makes the embedded PDB path, and therefore every byte, identical.
DETERMINISTIC_BUILD_ROOT = "/_/hexalith-works-platform-build"
# Every failure inside the run, whatever its Exception type (for example http.client.IncompleteRead from a tag
# receipt fetch), is recorded in the ledger before the nonzero exit. Only the empty-artifacts precondition refuses
# without a ledger, because writing one would overwrite existing evidence.
RECORDED_FAILURES = (Exception,)


def failure_text(error):
    return str(error) if isinstance(error, ValueError) else type(error).__name__ + ": " + str(error)


def path_map(output):
    return "-p:PathMap=" + str(output) + "=" + DETERMINISTIC_BUILD_ROOT


def require_identical_runtime(host_runtime, consumer_runtime):
    """The runtime DLL the live hook loads (the consumer's compile) must be byte-identical to Platform's host output."""
    host, consumer = sha256(host_runtime), sha256(consumer_runtime)
    if host != consumer:
        raise ValueError("Path-mapped deterministic builds still differ: Platform host-output " + PLATFORM_RUNTIME + ".dll "
                         + host + " versus the consumer's " + consumer + "; the live hook would not load Platform's bytes")
    return {"hostOutput": str(host_runtime), "consumer": str(consumer_runtime), "sha256": host, "identical": True,
            "deterministicBuildRoot": DETERMINISTIC_BUILD_ROOT}


def fixture_sources(works):
    """Bind test projects, helper sources, copied resources and runner settings outside build outputs."""
    works = Path(works)
    files = []
    for name in ("Hexalith.Works.IntegrationTests", "Hexalith.Works.Testing"):
        root = works / "tests" / name
        if root.is_symlink():
            raise ValueError("Fixture input cannot be a symbolic link: " + str(root))
        for path in root.rglob("*"):
            if {"bin", "obj", "__pycache__", "TestResults"}.intersection(path.relative_to(root).parts):
                continue
            if path.is_symlink():
                raise ValueError("Fixture input cannot be a symbolic link: " + str(path))
            if path.is_file():
                files.append(path)
    for path in (works / "xunit.runner.json", works / "src/Hexalith.Works.AppHost/ProjectMetadataPaths.cs"):
        if path.is_symlink():
            raise ValueError("Fixture input cannot be a symbolic link: " + str(path))
        if path.is_file():
            files.append(path)
    return {str(path.relative_to(works)): sha256(path) for path in sorted(files)}


def require_unchanged_fixture_sources(works, baseline):
    """Refuse changed bytes, added files and removed files since the pre-command fixture inventory."""
    current = fixture_sources(works)
    if current != baseline:
        changed = sorted(name for name in current.keys() | baseline.keys() if current.get(name) != baseline.get(name))
        raise ValueError("Qualified fixtures changed since the pre-command baseline: " + ", ".join(changed))


# Deterministic Works consumer classes the admission gate runs on the Release qualification consumer. They support,
# and never substitute for, the live target phases.
SUPPORTING_CLASSES = ("WorksSdkPartitionedRebuildTests", "WorksSdkProjectionQueryTests",
                      "WorkItemSharedProjectionRebuildHandlerTests", "WorksSdkProtectedHttpTests",
                      "WorksQueryPolicyDeadlineTests", "WorksSdkProofRedisOwnershipTests",
                      "WorksPlatformDispositionProbeTests", "WorksPlatformSelectedSdkPathsTests",
                      "WorksPlatformFleetRestoreTests", "WorksAppHostDiscoveryIsolationTests",
                      "WorksPlatformHostingCompositionTests", "WorksPlatformTelemetryCollectorTests",
                      "WorksPlatformIdentityFixtureTests", "WorksPlatformCompositionGuardTests",
                      "WorksAppHostSelectionTests", "WorksSdkPackageContractTests")

PLATFORM_HOSTING_STARTUP = PLATFORM_RUNTIME + ".WorksRecoveryHostingStartup"
PROOF_HOSTING_ASSEMBLY = "Hexalith.Works.IntegrationTests"


def require_platform_hosting(record, resource, host_runtime=None):
    """A Platform-composed Works host must run Platform's own hook and hosting startup before the proof.

    With host_runtime, every loaded Platform runtime artifact (hosting startup and first startup hook) must be
    byte-identical to Platform's own host-output DLL, never only to the consumer's compile."""
    observed = (record.get("hostingStartups") or {}).get(resource)
    if not isinstance(observed, dict):
        raise ValueError("A platform-target start did not record the hosting startups that loaded in " + resource)
    names = observed.get("hostingStartupAssemblies") or []
    hooks = observed.get("startupHooks") or []
    startups = {entry.get("name"): entry for entry in observed.get("startups") or [] if isinstance(entry, dict)}
    platform = startups.get(PLATFORM_RUNTIME) or {}
    if (PLATFORM_RUNTIME not in names or PROOF_HOSTING_ASSEMBLY not in names
            or names.index(PLATFORM_RUNTIME) > names.index(PROOF_HOSTING_ASSEMBLY)):
        raise ValueError("Platform's hosting startup must load before the proof in " + resource)
    if not hooks or Path(hooks[0]).name != PLATFORM_RUNTIME + ".dll":
        raise ValueError("Platform's runtime startup hook must stay first in " + resource)
    if platform.get("loaded") is not True or PLATFORM_HOSTING_STARTUP not in (platform.get("hostingStartupTypes") or []):
        raise ValueError("Platform's WorksRecoveryHostingStartup did not load in " + resource)
    location = platform.get("location")
    if not location or not Path(location).is_file() or str(platform.get("sha256", "")).lower() != sha256(location):
        raise ValueError("Platform's loaded hosting startup is not bound to its artifact in " + resource)
    if host_runtime is not None:
        expected = sha256(host_runtime)
        if str(platform.get("sha256", "")).lower() != expected or not Path(hooks[0]).is_file() or sha256(hooks[0]) != expected:
            raise ValueError("The loaded Platform runtime differs from Platform's host-output " + PLATFORM_RUNTIME + ".dll in " + resource)
    if observed.get("platformRuntimeEnabled") is not True or observed.get("platformTelemetryRegisteredBeforeProof") is not True:
        raise ValueError("Platform's WORKS_PLATFORM_RUNTIME hosting startup did not run before the proof in " + resource)
    if observed.get("telemetryPublisherRegistrations") != 1:
        raise ValueError("Works recovery telemetry must be registered exactly once in " + resource)
    return {"resource": resource, "hostingStartupAssemblies": names, "platformHostingStartupSha256": platform["sha256"].lower()}


# The only Platform-composed keys a fixture may write on a platform-target start, and the shape each must keep.
PERMITTED_OVERLAPS = {
    "DOTNET_STARTUP_HOOKS": "extends-composed-list",
    "ASPNETCORE_HOSTINGSTARTUPASSEMBLIES": "extends-composed-list",
    "APP_API_TOKEN": "captured-restorable-synthetic-secret",
    "OTEL_EXPORTER_OTLP_ENDPOINT": "redirects-otlp-export-to-owned-loopback-collector",
    "OTEL_EXPORTER_OTLP_PROTOCOL": "redirects-otlp-export-to-owned-loopback-collector",
    "OTEL_METRIC_EXPORT_INTERVAL": "redirects-otlp-export-to-owned-loopback-collector",
}
TRACKED_KEYS = ("DOTNET_ENVIRONMENT", "Works__Recovery__UseSdkRuntime", "Works__Projection__UseSdkDelivery")
TRACKED_PREFIXES = ("EventStore__RateLimiting__", "Authentication__Workload__AllowedCallers__")
# Each Platform resource must compose these tracked literal families; the values come from Platform's own source.
REQUIRED_COMPOSITION = {"eventstore": ("EventStore__RateLimiting__",),
                        "works": ("Authentication__Workload__AllowedCallers__", "Works__Recovery__UseSdkRuntime", "Works__Projection__UseSdkDelivery")}
COMPOSED_RESOURCES = ("eventstore", "works", "eventstore-operations", "eventstore-admin")
LITERAL_TUPLE = re.compile(r'\("([A-Za-z0-9_]+)",\s*"([^"]*)"\)')
LITERAL_ENVIRONMENT = re.compile(r'\.WithEnvironment\("([A-Za-z0-9_]+)",\s*"([^"]*)"\)')


def tracked(key):
    return key in TRACKED_KEYS or key.startswith(TRACKED_PREFIXES)


def platform_composition_literals(source):
    """Read the tracked literal values Platform's Works preview composes, straight from its bound source."""
    text = Path(source).read_text()
    literals = {}
    for key, value in LITERAL_TUPLE.findall(text) + LITERAL_ENVIRONMENT.findall(text):
        if tracked(key):
            if key in literals and literals[key] != value:
                raise ValueError("Platform composes conflicting literals for " + key)
            literals[key] = value
    if not any(key.startswith("EventStore__RateLimiting__") for key in literals) or \
            not any(key.startswith("Authentication__Workload__AllowedCallers__") for key in literals):
        raise ValueError("Platform's Works preview composition literals could not be read: " + str(source))
    return literals


def require_platform_composition(record, literals):
    """A platform-target start runs at Platform's composition: the fixture set no composed key it may not, and every
    tracked effective value equals the value Platform's own callbacks composed, which equals Platform's source literal."""
    guard = record.get("compositionGuard")
    effective = record.get("effectiveEnvironment")
    if not isinstance(guard, dict) or not isinstance(effective, dict):
        raise ValueError("A platform-target start did not record Platform's composition and the effective environment")
    for resource in COMPOSED_RESOURCES:
        entry = guard.get(resource)
        if not isinstance(entry, dict) or not isinstance(entry.get("composition"), dict):
            raise ValueError("A platform-target start did not record Platform's composition for " + resource)
        overlap = set(entry.get("platformComposedKeys") or []) & set(entry.get("fixtureKeys") or [])
        permitted = {item.get("key"): item.get("rule") for item in entry.get("permittedOverlaps") or [] if isinstance(item, dict)}
        offending = sorted((overlap ^ set(permitted)) | {key for key, rule in permitted.items() if PERMITTED_OVERLAPS.get(key) != rule})
        if offending:
            raise ValueError("The fixture set a key Platform composed for " + resource + ": " + ", ".join(offending))
        composition = entry["composition"]
        if composition.get("DOTNET_ENVIRONMENT") != "Development":
            raise ValueError("Platform did not compose the Development environment for " + resource)
        for prefix in REQUIRED_COMPOSITION.get(resource, ()):
            expected = {key: value for key, value in literals.items() if key.startswith(prefix)}
            actual = {key: value for key, value in composition.items() if key.startswith(prefix)}
            if not expected or actual != expected:
                raise ValueError("Platform's composed " + prefix + " values for " + resource + " differ from its source")
        for key, value in composition.items():
            if key in literals and value != literals[key]:
                raise ValueError("Platform's composed " + key + " for " + resource + " differs from its source literal")
    observed = {}
    for resource in [*COMPOSED_RESOURCES, "works-ha"]:
        if resource not in effective:
            if resource == "works-ha":
                continue
            raise ValueError("A platform-target start did not record the effective environment of " + resource)
        # The HA replica is fixture-composed and mirrors exactly Platform's composition for its works host.
        expected = guard["works" if resource == "works-ha" else resource]["composition"]
        views = [("launched", effective[resource])]
        hosting = (record.get("hostingStartups") or {}).get(resource)
        if resource in ("eventstore", "works", "works-ha"):
            if not isinstance(hosting, dict) or not isinstance(hosting.get("environment"), dict):
                raise ValueError("A platform-target host did not record its in-process environment: " + resource)
            views.append(("in-process", hosting["environment"]))
        for view, values in views:
            values = {key: value for key, value in values.items() if tracked(key)} if isinstance(values, dict) else None
            if values != expected:
                raise ValueError("The " + view + " effective environment of " + resource + " differs from Platform's composition")
        observed[resource] = expected
    return observed


def verify_host_records(path, assembly, platform, version, runtime_artifacts=None, host_runtime=None, composition_literals=None, control_plane_ports=None, retained_assembly=None, retained_preflight=False):
    """Bind target starts and the deliberate retained-host rollback to their exact artifacts."""
    assembly_version = version + ".0"
    records = [json.loads(line) for line in Path(path).read_text().splitlines() if line]
    target = [record for record in records if record.get("role") == "platform-target"]
    if retained_preflight:
        if target or len(records) != 1 or retained_assembly is None or records[0].get("role") != "retained-rollback":
            raise ValueError("Retained startup preflight requires exactly one separately bound historical host; it cannot supply parity or rollback proof")
    else:
        if not target:
            raise ValueError("No actual Platform starts were recorded")
        if not any("works-ha" in {module.get("resource") for module in record.get("runtimeModules", [])} for record in target):
            raise ValueError("No platform-target start bound the independent works-ha replica module")
    for record in records:
        if control_plane_ports is not None and record.get("controlPlanePorts") != control_plane_ports:
            raise ValueError("A live start did not bind the run-owned control-plane ports")
        if control_plane_ports is not None and record.get("dependencyBuildsSuppressed") is not True:
            raise ValueError("A live start did not preserve the serially prebuilt dependency outputs")
        binary = Path(record["assembly"])
        if binary.parent.name != "release":
            raise ValueError("A qualifying host must use the selected Release deployment output")
        if not binary.is_file() or record["sha256"].lower() != sha256(binary):
            raise ValueError("A live start used an unbound or changed host artifact")
        if not record["schedulerVolume"].startswith("works-sdk416-"):
            raise ValueError("A parity start did not own its isolated Scheduler fleet")
        if record.get("actorType") != "WorksSdkReminderActor":
            raise ValueError("A parity start did not consume the SDK reminder actor")
        expectation = record.get("worksReadinessExpectation")
        if expectation == "Healthy":
            if record.get("readinessPassed") is not True:
                raise ValueError("A selected host that never passed readiness cannot qualify as a live start")
        elif expectation == "Degraded":
            # The fixture disabled periodic writers, so Works only reached Running and asserts Degraded itself.
            if record.get("readinessPassed") is not False or record.get("role") != "platform-target":
                raise ValueError("A Degraded-expected start must record that Works readiness did not pass")
        else:
            raise ValueError("A live start did not record its Works readiness expectation")
        modules = record.get("runtimeModules", [])
        if not {"eventstore", "works", "eventstore-operations", "eventstore-admin"}.issubset({module["resource"] for module in modules}):
            raise ValueError("A live start did not bind the complete runtime module set")
        for module in modules:
            if module.get("configuration") != "Release" or Path(module["entry"]).parent.name != "release":
                raise ValueError("A qualifying live module must bind its actual Release configuration and output")
            if sha256(module["entry"]) != module["entrySha256"].lower() or not module["sdkAssemblies"]:
                raise ValueError("A live module used an unbound deployment artifact")
            if runtime_artifacts is not None and runtime_artifacts.get(module["entry"]) != module["entrySha256"].lower():
                raise ValueError("A live module entry is not one of the prebuilt runtime artifacts: " + module["entry"])
            for sdk in module["sdkAssemblies"]:
                if sdk["version"] != assembly_version or sha256(sdk["path"]) != sdk["sha256"].lower():
                    raise ValueError("A live module used an unbound or unpublished runtime SDK assembly")
        # The top-level list is the test runner's loaded SDK assemblies; hosts bind theirs per module above.
        sdk_assemblies = record.get("sdkAssemblies", [])
        if not sdk_assemblies:
            raise ValueError("A live start did not bind the test runner's loaded SDK assemblies")
        for sdk in sdk_assemblies:
            if not sdk["name"].startswith("Hexalith.EventStore.") or sdk["version"] != assembly_version or sha256(sdk["path"]) != sdk["sha256"].lower():
                raise ValueError("A live start's test runner used an unbound or unpublished SDK assembly")
        if record.get("role") == "platform-target":
            if binary.resolve() != assembly.resolve() or Path(record["directory"]).resolve() != platform.resolve():
                raise ValueError("A target start used another host or source root")
            record["platformHosting"] = [require_platform_hosting(record, resource, host_runtime) for resource in ("works", "works-ha")
                                         if resource in {module["resource"] for module in modules}]
            if composition_literals is not None:
                record["platformComposition"] = require_platform_composition(record, composition_literals)
        elif record.get("role") == "retained-rollback":
            if (binary.resolve() == assembly.resolve() or "Hexalith.Works.AppHost" not in binary.name
                    or (retained_assembly is not None and binary.resolve() != Path(retained_assembly).resolve())):
                raise ValueError("Rollback did not select the retained Works host")
        else:
            raise ValueError("An unknown host role cannot qualify parity")
    return records


# Each row binds its four proofs to named live phases and the qualifying state fields those phases must carry.
# Supporting unit suites never supply these phase records. A proof is passed only when every listed phase
# exists exactly once and every field resolves to a qualifying value; nothing is inferred from phase names.
MISSING = object()


def _resolve(state, path):
    value = state
    for part in [segment for segment in path.split(".") if segment]:
        if isinstance(value, dict) and part in value:
            value = value[part]
        elif isinstance(value, list) and part.isdigit() and int(part) < len(value):
            value = value[int(part)]
        else:
            return MISSING
    return value


def _integer(value):
    return isinstance(value, int) and not isinstance(value, bool)


def is_true(value):
    return value is True


def is_false(value):
    return value is False


def equals(expected):
    return lambda value: type(value) is type(expected) and value == expected


def one_of(*expected):
    return lambda value: any(type(value) is type(item) and value == item for item in expected)


def positive(value):
    return _integer(value) and value > 0


def at_least(minimum):
    return lambda value: _integer(value) and value >= minimum


def refused_status(value):
    return _integer(value) and 400 <= value <= 599


def count(expected):
    return lambda value: isinstance(value, (list, dict)) and len(value) == expected


def nonempty(value):
    return isinstance(value, (list, dict, str)) and len(value) > 0


def digest(value):
    return isinstance(value, str) and re.fullmatch(r"[0-9a-f]{64}", value) is not None


def prefixed(prefix):
    return lambda value: isinstance(value, str) and value.startswith(prefix) and len(value) > len(prefix)


def same(first, second):
    """Both state paths resolve to one equal, present value."""
    return lambda state: (_resolve(state, first) is not MISSING and _resolve(state, first) == _resolve(state, second))


def greater(first, second):
    return lambda state: (_integer(_resolve(state, first)) and _integer(_resolve(state, second))
                          and _resolve(state, first) > _resolve(state, second))


TIMESTAMP = re.compile(r"(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:\.(\d{1,7}))?(Z|[+-]\d{2}:\d{2})")


def utc_ticks(value):
    """Exact 100-nanosecond UTC ticks of an ISO-8601 timestamp with an explicit offset, as .NET serializes a
    DateTimeOffset; None when the value is absent or malformed. Seven fractional digits are kept, never rounded."""
    match = TIMESTAMP.fullmatch(value) if isinstance(value, str) else None
    if match is None:
        return None
    try:
        whole = datetime.fromisoformat(match.group(1) + match.group(3))
    except ValueError:
        return None
    return int(whole.timestamp()) * 10_000_000 + int((match.group(2) or "").ljust(7, "0"))


def timestamp(value):
    return utc_ticks(value) is not None


def alert_raised_after(name, points_field, observed_field):
    """An alert=1 point the collector received strictly after the phase's observed Degraded state. The publisher starts
    Unavailable and exports alert=1 before any health publish. Both export and receipt must follow the observation."""
    def check(state):
        if not isinstance(state, dict):
            return False
        observed, points = utc_ticks(_resolve(state, observed_field)), _resolve(state, points_field)
        if observed is None or not isinstance(points, list):
            return False
        return any(isinstance(point, dict) and point.get("Name") == name and point.get("Value") == 1
                   and (utc_ticks(point.get("ReceivedAt")) or 0) > observed
                   and _integer(point.get("UnixTimeNanoseconds")) and point["UnixTimeNanoseconds"] > observed * 100 for point in points)
    return check


def alert_cleared_after_raise(name):
    """A recovery alert was raised and a later exported point (by exporter time) clears it."""
    def check(points):
        if not isinstance(points, list):
            return False
        valid = [point for point in points if isinstance(point, dict) and point.get("Name") == name and _integer(point.get("UnixTimeNanoseconds"))]
        raised = [point["UnixTimeNanoseconds"] for point in valid if point.get("Value") == 1]
        cleared = [point["UnixTimeNanoseconds"] for point in valid if point.get("Value") == 0]
        return bool(raised) and bool(cleared) and max(cleared) > max(raised)
    return check


def delegated_identity(value):
    """A delegated Executor query under the composed gateway workload, resolved through the Parties identity."""
    return (isinstance(value, dict) and value.get("isDelegated") is True and value.get("authenticatedWorkloadId") == "eventstore"
            and isinstance(value.get("originalActorId"), str) and len(value["originalActorId"]) > 0
            and value.get("originalActorId") == value.get("userId") == value.get("resolvedActor")
            and isinstance(value.get("partyId"), str) and len(value["partyId"]) > 0 and value.get("partyId") == value.get("resolvedParty")
            and isinstance(value.get("tenant"), str) and len(value["tenant"]) > 0 and value.get("tenant") == value.get("resolvedTenant")
            and positive(value.get("identityCalls")))


DUMP_INVENTORY = "redis-full-value-inventory-sha256"


def restored_fleet_matches(restored, snapshot):
    """The re-observed restored fleet equals the snapshot on every entry a re-observation can reproduce."""
    if not isinstance(restored, dict) or not isinstance(snapshot, dict) or DUMP_INVENTORY in restored or DUMP_INVENTORY not in snapshot:
        return False
    expected = {key: value for key, value in snapshot.items() if key != DUMP_INVENTORY}
    return bool(expected) and restored == expected


def every(field, predicate):
    return lambda items: isinstance(items, list) and len(items) > 0 and all(
        isinstance(item, dict) and predicate(_resolve(item, field)) for item in items)


def all_of(*predicates):
    return lambda value: all(predicate(value) for predicate in predicates)


def converged_heads(expected):
    def check(heads):
        return isinstance(heads, dict) and len(heads) == expected and all(
            isinstance(head, dict) and isinstance(head.get("observed"), dict)
            and _integer(head.get("expectedOwnerSequence")) and head["observed"].get("OwnerSequenceNumber") == head["expectedOwnerSequence"]
            and _integer(head.get("expectedGlobalPosition")) and head["observed"].get("Position") == head["expectedGlobalPosition"]
            for head in heads.values())
    return check


ALERT = "eventstore.domain.recovery.alert"
RESTORE_PHASE = "complete-restore-retained-host-query-and-receipt-replay"
SNAPSHOT_PHASE = "complete-platform-fleet-snapshot"
HISTORICAL = "platform-historical-lifecycle-tree-concurrent-claim-persisted"
HISTORICAL_RESTART = "platform-historical-restart-replay-and-family"
REFUSALS = "platform-executor-filtering-and-origin-tenant-publish-refusals"
REDELIVERY = "platform-named-redelivery-capture-through-commit-complete-family"
RESTART_CONVERGENCE = "platform-initial-projection-convergence"
FOCUSED_REBUILD = "platform-focused-projection-rebuild"
QUERY_REPLAY = "platform-query-and-explicit-receipt-replay"
HA = "platform-two-host-ha-lost-firing-stale-callback-recovery"
CALLBACK = "successful-callback-no-periodic-recovery"
DISPOSITION = "protected-scoped-disposition-audit-before-mutation"
CALLBACK_DISPOSITION = "callback-protected-scoped-disposition-audit-before-mutation"
ALERT_CLEAR = "durable-recovery-alert-export-and-clear"
COVERAGE = "complete-supported-process-coverage"
LEGACY_ROUTE = "platform-sdk-named-mode-legacy-route-refused"
QUARANTINE = "platform-named-conflict-durable-quarantine-with-unchanged-family"

# row -> proof -> [(phase, {field path: (predicate, qualifying description)})]. An empty path checks the whole state.
PROOFS = {
    "R1": {
        "persisted": [(HISTORICAL, {"familyDigest": (digest, "SHA-256 family digest"), "claimOutcomes": (count(2), "two concurrent claims"),
                                    "lifecycle.Events": (count(7), "complete seven-event lifecycle"), "replay.Status": (equals("Completed"), "replayed Completed")})],
        "negative": [(REFUSALS, {"wrongCaller": (refused_status, "wrong caller refused"), "wrongPort": (refused_status, "direct application port refused"),
                                 "forbiddenPublish": (equals(403), "forbidden publisher refused")})],
        "restart": [(HISTORICAL_RESTART, {"completed": (is_true, "lifecycle replays Completed after restart"), "claimCount": (equals(1), "one claim after restart"),
                                          "familyDigest": (digest, "family digest after restart")})],
        "rollback": [(RESTORE_PHASE, {"convergence.query.found": (is_true, "retained host query finds the item"),
                                      "convergence.queryIdentity": (delegated_identity, "retained host query is a delegated Executor query"),
                                      "restoredConfigurationVerified": (is_true, "restored configuration bytes verified")})],
    },
    "R2": {
        "persisted": [(COVERAGE, {"Complete": (is_true, "complete recovery sweep"), "Unresolved": (equals(0), "zero unresolved"),
                                  "RetainedCandidates": (equals(12), "twelve retained candidates")})],
        "negative": [("periodic-writers-disabled-readiness-degraded", {"status": (equals("Degraded"), "SDK readiness Degraded with periodic writers disabled")}),
                     ("disabled-recovery-otlp-alert-export", {"degradedObservedAt": (timestamp, "the phase's Degraded observation time"),
                                                              "": (alert_raised_after(ALERT, "points", "degradedObservedAt"),
                                                                   "OTLP recovery alert received after the observed Degraded state")})],
        "restart": [(ALERT_CLEAR, {"": (alert_cleared_after_raise(ALERT), "recovery alert clears after its last raise, after restart")})],
        "rollback": [(RESTORE_PHASE, {"convergence.restoredSweep.Complete": (is_true, "restored sweep complete"),
                                      "convergence.restoredSweep.Unresolved": (equals(0), "restored sweep has zero unresolved")})],
    },
    "R3": {
        "persisted": [(REDELIVERY, {"redelivered.Outcomes.0.Status": (one_of(0, 1), "redelivery Completed or AlreadyCompleted"),
                                    "concurrentDelivery.redeliveryLogicalCount": (equals(1), "one logical redelivery"),
                                    "queryIdentity": (delegated_identity, "rebuild queries are delegated Executor queries"),
                                    "": (same("before.Digest", "after.Digest"), "redelivery and capture preserve the family digest")})],
        "negative": [(QUARANTINE, {"": (same("acceptedFamilySha256", "unchangedFamilySha256"), "conflict leaves the accepted family unchanged"),
                                   "repeatedConflictCount": (equals(1), "repeated conflict keeps one evidence record"),
                                   "queryIdentity": (delegated_identity, "the refused query is a delegated Executor query"),
                                   "conflict.SourceStream": (nonempty, "durable conflict evidence")}),
                     (REFUSALS, {"forbiddenPublish": (equals(403), "forbidden origin publish refused")})],
        "restart": [(RESTART_CONVERGENCE, {"naturalCatchUp": (is_true, "natural catch-up after restarts"),
                                           "heads": (converged_heads(12), "all twelve owner/global heads converged")})],
        "rollback": [(RESTORE_PHASE, {"convergence.receipt.Replayed": (is_true, "retained host replays the receipt")})],
    },
    "R4": {
        "persisted": [(FOCUSED_REBUILD, {"concurrentDelivery.automaticCatchUpAfterCommit": (is_true, "automatic catch-up after Commit"),
                                         "concurrentDelivery.oldGenerationStayedReadable": (is_true, "old generation readable during capture"),
                                         "": (same("concurrentDelivery.after.Digest", "concurrentDelivery.expectedDigest"), "committed family equals the independent fold"),
                                         "queryIdentity": (delegated_identity, "focused rebuild queries are delegated Executor queries"),
                                         "sources": (count(11), "eleven captured sources"), "committed.Phase": (equals(3), "rebuild Committed")})],
        "negative": [(REDELIVERY, {"fenced.Outcomes.0.Status": (equals(2), "delivery during capture is fenced Retryable"),
                                   "negatives.wrongOrigin": (refused_status, "wrong origin refused"), "negatives.wrongOperation": (equals(403), "wrong operation refused")})],
        "restart": [(RESTART_CONVERGENCE, {"heads": (converged_heads(12), "named projection heads converge after restarts")})],
        "rollback": [(RESTORE_PHASE, {"convergence.query.conversationCorrelationId.value": (prefixed("synthetic-capture-416-"), "restored family includes the post-capture event"),
                                      "convergence.queryIdentity": (delegated_identity, "restored family read by a delegated Executor query")})],
    },
    "R5": {
        "persisted": [(REFUSALS, {"own.found": (is_true, "executor reads own item"), "visible": (count(3), "executor queue view"),
                                  "queryIdentity": (delegated_identity, "historical queries are delegated Executor queries")})],
        "negative": [(REFUSALS, {"hidden.found": (is_false, "foreign item filtered"), "wrongActor": (equals("Forbidden"), "wrong actor denied"),
                                 "wrongTenant": (equals("Forbidden"), "wrong tenant denied")}),
                     (QUERY_REPLAY, {"nonDelegatedFullTenantQuery.error": (equals("Forbidden"), "non-delegated full-tenant query refused"),
                                     "nonDelegatedFullTenantQuery.isDelegated": (is_false, "the refused query carried no delegated identity"),
                                     "nonDelegatedFullTenantQuery.authenticatedWorkloadId": (equals("eventstore"), "refused under the composed gateway workload"),
                                     "nonDelegatedFullTenantQuery.identityCalls": (equals(0), "refused before any identity resolution")})],
        "restart": [(QUERY_REPLAY, {"query.found": (is_true, "query usable after restarts"), "query.latestAcceptedSourceSequence": (positive, "query reflects accepted source"),
                                    "queryIdentity": (delegated_identity, "restart query is a delegated Executor query")})],
        "rollback": [(RESTORE_PHASE, {"convergence.query.found": (is_true, "retained host query usable"),
                                      "convergence.queryIdentity": (delegated_identity, "retained host query is a delegated Executor query")})],
    },
    "R6": {
        "persisted": [(CALLBACK, {"witness.entries": (count(1), "persisted SDK reminder witness"), "processCandidate": (is_true, "process candidate persisted")})],
        "negative": [(HA, {"actualStaleCallbackObserved": (is_true, "stale Scheduler callback observed"), "staleSchedulerFireResumed": (equals(1), "stale firing caused no second resume"),
                           "resumeCountAtPrimaryStop": (equals(0), "no resume committed when the primary stopped"),
                           "primaryStoppedBeforeDue": (is_true, "primary stopped before the scheduled date"),
                           "retiredReminderRemovedAfterFiring": (is_true, "retired reminder removed"), "": (greater("afterCallbacks", "beforeCallbacks"), "a real callback fired")})],
        "restart": [("cascade-expiry-restart-receipt-and-checkpoint", {"replay.ReplayObserved": (is_true, "lost receipt replayed after restart"),
                                                                       "childExpired": (equals(1), "one child expiry"), "expiryReceipt.disposition": (equals(0), "expiry receipt Success")}),
                    ("concurrent-sdk-reminder-reissue-after-lost-scheduler-wakeup", {"callers": (equals(2), "two concurrent reissuers"),
                                                                                    "persistedSchedulerReminder.actorType": (equals("WorksSdkReminderActor"), "reissued SDK reminder persisted")})],
        "rollback": [(RESTORE_PHASE, {"dateResumed": (equals(1), "one date resume after restore"), "childExpired": (equals(1), "one child expiry after restore")})],
    },
    "R7": {
        "persisted": [("paged-process-candidates-pre-restart", {"expectedSources": (equals(12), "twelve process sources"), "pages": (count(4), "four linked candidate pages")})],
        "negative": [("canonical-related-child-creation-gap", {"creationGap.ReasonCode": (equals("evidence-unavailable"), "missing child evidence refuses the cascade"),
                                                               "existingChildCancellations": (equals(0), "no partial cascade effect"), "readiness": (equals("Degraded"), "readiness Degraded")})],
        "restart": [("paged-process-restart-and-shared-store-owner-fencing", {"linkedPages": (equals(4), "pages relinked after restart"), "candidates": (count(12), "twelve candidates"),
                                                                              "staleOwnerWriteRefused": (is_true, "stale owner write refused"), "concurrentOwners": (equals(1), "one owner"),
                                                                              "": (greater("successorFence", "priorFence"), "successor fence advanced")})],
        "rollback": [(RESTORE_PHASE, {"convergence.restoredSweep.RetainedCandidates": (equals(12), "restored sweep retains twelve candidates"),
                                      "childCancelled": (equals(1), "one child cancellation after restore")})],
    },
    "R8": {
        "persisted": [(DISPOSITION, {"audit.Attempts": (count(1), "one attributed audit record"), "acceptedCallerStatus": (equals(204), "authorized disposition accepted"),
                                     "": (greater("afterGeneration", "beforeGeneration"), "SDK disposition mutated after audit")})],
        "negative": [(CALLBACK_DISPOSITION, {"wrongPurpose.receiverStatus": (equals(403), "wrong purpose refused"), "wrongTenant.receiverStatus": (equals(403), "wrong tenant refused"),
                                             "wrongAggregate.receiverStatus": (equals(403), "wrong aggregate refused"), "auditOutage.receiverStatus": (equals(500), "audit outage refuses before mutation")})],
        "restart": [(ALERT_CLEAR, {"": (alert_cleared_after_raise(ALERT), "alert clears after its last raise, after restart")})],
        "rollback": [(RESTORE_PHASE, {"convergence.restoredOperatorAuditAttempts": (equals(1), "restored attributed operator audit")})],
    },
    "R9": {
        "persisted": [(CALLBACK, {"processCandidate": (is_true, "SDK process registration from a real callback"), "witness.entries": (count(1), "SDK reminder registration")})],
        "negative": [(LEGACY_ROUTE, {"legacyRouteStatus": (equals(404), "named-mode host serves no legacy route"), "missingChannelStatus": (equals(401), "uncredentialed request refused"),
                                     "": (same("acceptedFamilySha256", "unchangedFamilySha256"), "refusals leave the family unchanged")})],
        "restart": [("child-resume-noop-restart-receipt-and-checkpoint", {"replay.ReplayObserved": (is_true, "SDK process replays after restart"),
                                                                          "replay.Disposition": (equals(2), "NoOp receipt"), "checkpoint.Checkpoint": (equals(4), "checkpoint advanced")})],
        "rollback": [(RESTORE_PHASE, {"convergence.receipt.Disposition": (equals(2), "restored NoOp receipt"), "convergence.resumedCount": (equals(1), "one resume")})],
    },
    "R10": {
        "persisted": [(REFUSALS, {"unchangedFamily": (digest, "refused traffic leaves the persisted family unchanged"), "wrongPort": (refused_status, "direct port refused")})],
        "negative": [(HA, {"trust.trustedClientAcceptedByServer": (is_true, "configured-root client accepted"),
                           "trust.noClientCertificateRefusedByServer": (is_true, "server refuses a client without a certificate"),
                           "trust.foreignCaClientRefusedByServer": (is_true, "server refuses a foreign-CA client"),
                           "trust.nameMismatchRejectedByProbe": (is_true, "probe rejects a server identity mismatch"),
                           "trust.serverIdentity": (equals("spiffe://public/ns/default/works"), "exact SPIFFE server identity")})],
        "restart": [(DISPOSITION, {"wrongTenant.receiverStatus": (equals(403), "receiver refuses wrong tenant after restarts"),
                                   "wrongAggregate.receiverStatus": (equals(403), "receiver refuses wrong aggregate after restarts")})],
        "rollback": [(RESTORE_PHASE, {"restoredConfigurationVerified": (is_true, "restored trust and policy bytes verified")})],
    },
    "R11": {
        "persisted": [("five-child-persisted-target-receipts", {"": (all_of(count(5), every("receipt.purpose", equals("works.cascade-cancel")),
                                                                            every("receipt.disposition", equals(0)), every("cancelled", equals(1))),
                                                                     "five persisted Success cascade receipts, each child cancelled once")})],
        "negative": [(QUERY_REPLAY, {"conflictingReplayStatus": (refused_status, "conflicting replay refused"), "durableCollisionCount": (equals(1), "one durable collision"),
                                     "attributedCollisionAudit": (is_true, "attributed collision audit")})],
        "restart": [("cascade-restart-receipt-and-checkpoint", {"": (same("receipt.effectId", "committedEffect"), "persisted receipt names the committed effect"),
                                                                "checkpoint.Checkpoint": (at_least(7), "checkpoint advanced past the cascade")})],
        "rollback": [(RESTORE_PHASE, {"convergence.receipt.Replayed": (is_true, "restored receipt replays"), "convergence.durableCollisionCount": (equals(1), "restored collision evidence")})],
    },
}
PROOF_NAMES = ("persisted", "negative", "restart", "rollback")

# Bindings of one phase to another, so a restart or restore cannot pass against unrelated state. Each phase records its
# own re-observation; the optional comparator states how a re-observation must match the captured value.
CROSS_CHECKS = [
    (HISTORICAL_RESTART, "familyDigest", HISTORICAL, "familyDigest", "restart preserves the persisted historical family", None),
    (RESTORE_PHASE, "fleet", SNAPSHOT_PHASE, "fleet", "restored fleet equals the complete snapshot", restored_fleet_matches),
    (RESTORE_PHASE, "runtimeHashes", SNAPSHOT_PHASE, "runtimeHashes", "restored runtime binaries equal the snapshot", None),
    (RESTORE_PHASE, "convergence.receipt.EffectId", QUERY_REPLAY, "receipt.EffectId", "retained host replays the Platform-written receipt", None),
]


def required_phases():
    """Derive every required phase from the proof map and cross-checks."""
    phases = {phase for proofs in PROOFS.values() for requirements in proofs.values() for phase, _ in requirements}
    phases.update(name for check in CROSS_CHECKS for name in (check[0], check[2]))
    return phases


def bind_persisted_outcomes(path, rows, starts):
    records = [json.loads(line) for line in Path(path).read_text().splitlines() if line]
    by_phase = {}
    for record in records:
        phase = record.get("phase") if isinstance(record, dict) else None
        if not isinstance(phase, str) or "state" not in record:
            raise ValueError("A persisted outcome record has no phase or state")
        if phase in by_phase:
            raise ValueError("Duplicate persisted outcome phase: " + phase)
        by_phase[phase] = record
    missing = sorted(required_phases() - set(by_phase))
    if missing:
        raise ValueError("Required target outcomes did not execute: " + ", ".join(missing))
    retained = [record for record in starts if record.get("role") == "retained-rollback"]
    if not retained:
        raise ValueError("No retained-host restore start was observed")
    snapshot_volume = by_phase[SNAPSHOT_PHASE]["state"].get("schedulerVolume")
    if not isinstance(snapshot_volume, str) or any(record.get("schedulerVolume") != snapshot_volume for record in retained):
        raise ValueError("The retained-host rollback start did not use the snapshot's Scheduler volume")
    for first, first_path, second, second_path, description, comparator in CROSS_CHECKS:
        left, right = _resolve(by_phase[first]["state"], first_path), _resolve(by_phase[second]["state"], second_path)
        if left is MISSING or right is MISSING or not (comparator(left, right) if comparator else left == right):
            raise ValueError("Cross-phase binding failed: " + description)
    if set(PROOFS) != set(ROWS) or any(set(proofs) != set(PROOF_NAMES) for proofs in PROOFS.values()):
        raise ValueError("Every row must map all four proofs to named phases")
    for key, row in rows.items():
        proofs = {}
        for name in PROOF_NAMES:
            bound = []
            for phase, fields in PROOFS[key][name]:
                state = by_phase[phase]["state"]
                for field, (predicate, description) in fields.items():
                    value = _resolve(state, field) if field else state
                    if value is MISSING:
                        raise ValueError(f"{key} {name} proof: {phase} lacks required field {field}")
                    if not predicate(value):
                        raise ValueError(f"{key} {name} proof: {phase} field {field or '<state>'} does not qualify ({description})")
                bound.append({"phase": phase, "recordSha256": hashlib.sha256(json.dumps(by_phase[phase], sort_keys=True).encode()).hexdigest(),
                              "fields": {field or "<state>": description for field, (_, description) in fields.items()}})
            proofs[name] = {"passed": True, "evidence": bound}
        row["requiredProofs"] = proofs
        row["evidence"] = [item for proof in proofs.values() for item in proof["evidence"]]
        row["status"] = "passed"
    return {"complete": True, "snapshot": by_phase[SNAPSHOT_PHASE]["state"], "restored": by_phase[RESTORE_PHASE]["state"],
            "crossChecks": [check[4] for check in CROSS_CHECKS], "evidenceSha256": sha256(path)}


def source_snapshot(root, allow_archive=False, producer=False):
    """Record source provenance independently of public NuGet archive identity.

    A producer snapshot binds every src/** file except build outputs, because props, targets, JSON and
    resources are build inputs too. The Works consumer inventory records its C# and project sources."""
    root = Path(root)
    files = source_files(root) if producer else [path for path in (root / "src").rglob("*")
             if path.is_file() and path.suffix in (".cs", ".csproj")
             and not {"bin", "obj"}.intersection(path.relative_to(root).parts)]
    files += [root / name for name in ("Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props") if (root / name).is_file()]
    inventory = {path.relative_to(root).as_posix(): sha256(path) for path in sorted(files)}
    commit = None
    try:
        owner = subprocess.check_output(["git", "-C", str(root), "rev-parse", "--show-toplevel"], text=True, stderr=subprocess.DEVNULL).strip()
        if Path(owner).resolve() != root.resolve():
            raise ValueError("Source directory is not its own Git checkout: " + str(root))
        commit = subprocess.check_output(["git", "-C", str(root), "rev-parse", "HEAD"], text=True, stderr=subprocess.DEVNULL).strip()
    except (subprocess.CalledProcessError, ValueError):
        if not allow_archive:
            raise ValueError("Source directory requires an owning Git checkout unless an explicit archive root was selected: " + str(root))
    snapshot = {"root": str(root), "commit": commit, "provenance": "owning Git checkout" if commit else "explicit filesystem archive; no Git HEAD",
                "sourceFiles": inventory, "inventorySha256": hashlib.sha256(json.dumps(inventory, sort_keys=True).encode()).hexdigest()}
    if producer:
        snapshot["buildsInputs"] = builds_input_snapshot(root)
    return snapshot


def require_published_runtime_source(snapshot, published_producers):
    """Runtime-built SDK code must match the published producer, regardless of checkout HEAD metadata."""
    commits = {producer["repositoryCommit"] for producer in published_producers.values()}
    if len(commits) != 1:
        raise ValueError("Published SDK archives do not identify one compatible producer commit")
    commit = commits.pop()
    archive = subprocess.check_output(["git", "-C", snapshot["root"], "archive", commit, "src",
                                       "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props"])
    with tarfile.open(fileobj=io.BytesIO(archive)) as source:
        members = source.getmembers()
        if any(not (entry.isfile() or entry.isdir()) for entry in members):
            raise ValueError("Published producer " + commit + " source contains a link or special file")
        published = {entry.name: hashlib.sha256(source.extractfile(entry).read()).hexdigest()
                     for entry in members if entry.isfile()
                     and not {"bin", "obj"}.intersection(Path(entry.name).parts)}
    if snapshot["sourceFiles"] != published:
        changed = sorted(name for name in snapshot["sourceFiles"].keys() | published.keys()
                         if snapshot["sourceFiles"].get(name) != published.get(name))
        raise ValueError("Runtime SDK differs from published producer " + commit + ": " + ", ".join(changed))
    return {"repositoryCommit": commit, "sourceInventorySha256": snapshot["inventorySha256"],
            "checkoutCommit": snapshot["commit"], "matchesPublishedProducer": True}


def host_ephemeral_port_range(path=Path("/proc/sys/net/ipv4/ip_local_port_range")):
    """Read the actual allocator range used by later Aspire application bind(0) calls."""
    try:
        values = Path(path).read_text().split()
        if len(values) != 2:
            raise ValueError("Expected exactly two range bounds")
        lower, upper = map(int, values)
        if not 1024 <= lower <= upper <= 65535:
            raise ValueError("Invalid unprivileged port range")
        return lower, upper
    except (OSError, ValueError) as error:
        raise ValueError("Cannot establish the host ephemeral application-port range: " + str(path)) from error


def host_reserved_port_ranges(path=Path("/proc/sys/net/ipv4/ip_local_reserved_ports")):
    """Respect administrator-reserved ports without changing the host's networking policy."""
    try:
        value = Path(path).read_text().strip()
        ranges = []
        for token in value.split(",") if value else []:
            if not re.fullmatch(r"[0-9]+(?:-[0-9]+)?", token):
                raise ValueError("Invalid reserved port range")
            bounds = [int(part) for part in token.split("-")]
            lower, upper = bounds[0], bounds[-1]
            if not 1 <= lower <= upper <= 65535:
                raise ValueError("Invalid reserved port range")
            ranges.append((lower, upper))
        return tuple(ranges)
    except (OSError, ValueError) as error:
        raise ValueError("Cannot establish the host reserved-port ranges: " + str(path)) from error


def select_control_plane_ports(ephemeral_range=None, reserved_ranges=None):
    """Use available dual-stack ports outside application and administrator-reserved ranges."""
    lower, upper = host_ephemeral_port_range() if ephemeral_range is None else ephemeral_range
    reserved = host_reserved_port_ranges() if reserved_ranges is None else reserved_ranges
    listeners = []
    try:
        selected = {}
        for name in ("sentry", "placement", "scheduler"):
            # Docker Desktop's WSL proxy accepts bind(65535), but publishing that boundary port
            # leaves even an Alpine container in Created until the start API times out. The exact
            # owned-container counterexample is retained with Story 4.9; adjacent ports pass.
            for port in range(65534, 1023, -1):
                if lower <= port <= upper or any(start <= port <= end for start, end in reserved):
                    continue
                ipv4 = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
                ipv6 = socket.socket(socket.AF_INET6, socket.SOCK_STREAM)
                try:
                    ipv4.bind(("127.0.0.1", port))
                    ipv6.setsockopt(socket.IPPROTO_IPV6, socket.IPV6_V6ONLY, 1)
                    ipv6.bind(("::1", port))
                except OSError:
                    ipv4.close()
                    ipv6.close()
                    continue
                listeners.extend((ipv4, ipv6))
                selected[name] = port
                break
            else:
                raise ValueError("No free dual-stack loopback port outside the host ephemeral range for the owned " + name)
        return selected
    finally:
        for listener in listeners:
            listener.close()


def require_toolchain_authority(qualified, platform):
    """The qualified toolchain must be derived from the same Builds catalog that selects the SDK and control plane."""
    if Path(qualified["catalog"]).resolve() != (Path(platform) / CATALOG).resolve() or qualified["catalogSha256"] != sha256(Path(platform) / CATALOG):
        raise ValueError("The Works proof toolchain was not derived from the Platform Builds catalog: " + str(qualified["catalog"]))
    return qualified


def require_qualified_runtime(binding, qualified):
    """An isolated launch adapter may only execute the exact Platform-qualified daprd bytes."""
    if binding["runtimeSha256"] != qualified["runtimeSha256"] or binding["runtimeSha256"] != qualified["linuxAmd64DaprdSha256"] \
            or Path(binding["runtime"]) != Path(qualified["runtime"]):
        raise ValueError("The isolated daprd differs from the qualified Works proof runtime")
    return binding


def canonical_source_snapshot(root):
    """Bind full canonical revisions and all ordinary build, source, fixture and verifier inputs."""
    output = subprocess.check_output(["git", "-C", str(root), "ls-files", "-z", "--cached", "--others", "--exclude-standard"])
    names = [name for name in output.decode().strip("\0").split("\0") if name]
    directories = {"src", "tests", "eng", "DaprComponents", "config", "schemas", ".config"}
    inventory = {name: sha256(root / name) for name in sorted(set(names))
                 if (len(Path(name).parts) == 1 or Path(name).parts[0] in directories) and (root / name).is_file()}
    return {"root": str(root), "commit": subprocess.check_output(["git", "-C", str(root), "rev-parse", "HEAD"], text=True).strip(),
            "sourceFiles": inventory, "inventorySha256": hashlib.sha256(json.dumps(inventory, sort_keys=True).encode()).hexdigest()}


def linked_fixture_snapshot(works, composition_root):
    """Bind the explicitly source-only Identity/Composition graphs and Parties Compile links used by the proof.

    Composition is bound from the Platform root the consumer actually compiles (its PlatformWorksRuntimeRoot),
    never from the Works dependency copy that the parity consumer build does not select.
    """
    parties = works / "references/Hexalith.Parties"
    identity = works / "references/Hexalith.Platform/src/Hexalith.Platform.Identity"
    composition = Path(composition_root) / "src/Hexalith.Platform.Composition"
    def inputs(paths, owner):
        return {path for path in paths if not {"bin", "obj"}.intersection(path.relative_to(owner).parts)}

    files = inputs(set(identity.rglob("*.cs")) | {identity / "Hexalith.Platform.Identity.csproj"}, identity)
    files |= inputs(set(composition.rglob("*.cs")) | {composition / "Hexalith.Platform.Composition.csproj"}, composition)
    # Composition also consumes explicitly linked sources and embedded resources from its actual root.
    for item in ET.parse(composition / "Hexalith.Platform.Composition.csproj").getroot().iter():
        if item.tag not in ("Compile", "EmbeddedResource"):
            continue
        include = item.get("Include", "")
        if include and "$(" not in include and "*" not in include:
            files |= inputs({Path(os.path.normpath(composition / include.replace("\\", "/")))}, Path(composition_root))
    project = works / "tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj"
    for item in ET.parse(project).getroot().iter("Compile"):
        include = item.get("Include", "")
        if include.startswith("$(HexalithPartiesRoot)/"):
            files |= inputs({parties / include.removeprefix("$(HexalithPartiesRoot)/")}, parties)
    return {"sourceFiles": {str(path): sha256(path) for path in sorted(files)},
            "compositionRoot": str(composition_root),
            "commits": {str(root): subprocess.check_output(["git", "-C", str(root), "rev-parse", "HEAD"], text=True).strip()
                        for root in (parties, identity.parents[1])}}


def platform_consumer_sources(platform):
    """Bind complete source membership as well as bytes for the Platform consumer."""
    paths = [platform / "apphost.cs", *sorted((platform / "src").rglob("*.cs")),
             *sorted((platform / "src").rglob("*.csproj"))]
    return {str(path.relative_to(platform)): sha256(path) for path in paths
            if not {"obj", "bin"}.intersection(path.relative_to(platform).parts)}


def consumer_artifacts(root):
    """Bind the executed consumer, copied resources, runner configuration and compiled test dependencies."""
    root = Path(root)
    if not root.is_dir() or root.is_symlink():
        raise ValueError("Consumer artifacts require an ordinary output directory: " + str(root))
    name = "Hexalith.Works.IntegrationTests"
    paths = {root / (name + suffix) for suffix in ("", ".dll", ".deps.json", ".runtimeconfig.json")}
    for path in root.rglob("*"):
        if path.is_symlink():
            raise ValueError("Consumer artifact cannot be a symbolic link: " + str(path))
        if path.is_file():
            paths.add(path)
    return {str(path): sha256(path) for path in sorted(paths)}


def require_unchanged_inputs(summary, works, platform, snapshot_manifest, ephemeral_range, reserved_ranges):
    """Recheck every qualified input and artifact before accepting live evidence."""
    for source in summary["sourceProducers"].values():
        current = require_unchanged_runtime_source(source) if source["commit"] is None else source_snapshot(source["root"], producer=True)
        if current != source:
            raise ValueError("A qualified SDK source graph changed during execution: " + source["root"])
    works_source = source_snapshot(works)
    if works_source["commit"] != summary["worksConsumerSource"]["commit"] or works_source["inventorySha256"] != summary["worksConsumerSource"]["inventorySha256"]:
        raise ValueError("The qualified Works consumer source graph changed during execution")
    if platform_consumer_sources(platform) != summary["platformConsumerSources"]:
        raise ValueError("The qualified Platform consumer source membership or bytes changed during execution")
    for binary, digest in summary["runtimeArtifacts"].items():
        if sha256(binary) != digest:
            raise ValueError("A qualified runtime artifact changed during execution: " + binary)
    if consumer_artifacts(summary["consumer"]["root"]) != summary["consumer"]["artifacts"]:
        raise ValueError("A qualified Works consumer artifact membership or bytes changed during execution")
    for binary, digest in summary["platformAdapterAssemblies"].items():
        if sha256(binary) != digest:
            raise ValueError("A qualified Platform adapter assembly changed during execution: " + binary)
    if sha256(Path(__file__)) != summary["verifierSha256"]:
        raise ValueError("The verifier changed during execution")
    for name, digest in summary["verifierHelpers"].items():
        if sha256(Path(__file__).parent / name) != digest:
            raise ValueError("A verifier helper changed during execution: " + name)
    require_unchanged_fixture_sources(works, summary["fixtureSources"])
    retained_works_host.verify(summary["retainedHostSource"])
    if linked_fixture_snapshot(works, platform) != summary["linkedFixtureSources"]:
        raise ValueError("Explicit linked test-only Identity or Parties fixture sources changed during execution")
    for name, baseline in summary["canonicalSources"].items():
        if name == "Platform" and snapshot_manifest:
            observed = works_host_snapshot.verify(snapshot_manifest, platform)
            if observed["manifestSha256"] != summary["platformSourceSnapshot"]["manifestSha256"]:
                raise ValueError("The Platform snapshot manifest changed during execution")
            summary["platformSourceSnapshotFinal"] = observed
            continue
        current = canonical_source_snapshot(Path(baseline["root"]))
        if current != baseline:
            raise ValueError("Canonical " + name + " revision or ordinary build/source/test/verifier inputs changed during execution")
    if host_ephemeral_port_range() != ephemeral_range:
        raise ValueError("The host ephemeral application-port range changed during execution")
    if host_reserved_port_ranges() != reserved_ranges:
        raise ValueError("The host reserved-port ranges changed during execution")


def run_verification(args):
    platform = Path(__file__).resolve().parents[1]
    profile = verification_profile(getattr(args, "profile", "full"))
    works = args.works_root.resolve()
    runtime_sdk = args.runtime_sdk_root.resolve() if args.runtime_sdk_root else works / "references/Hexalith.EventStore"
    artifacts = args.artifacts_path.resolve() if args.artifacts_path else Path(tempfile.mkdtemp(prefix="works-platform-proof-"))
    if artifacts.exists() and any(artifacts.iterdir()):
        raise ValueError("Artifacts directory must be empty; existing evidence cannot be reused or overwritten")
    artifacts.mkdir(parents=True, exist_ok=True)
    summary = {"status": "incomplete", "scope": "disposable-synthetic", "profile": profile, "commands": [], "checks": {}, "rows": {},
               "rollback": {"complete": False, "missing": "Platform-write -> complete fleet restore -> retained Works host queries and receipt replay"}}

    def save():
        (artifacts / "ledger.json").write_text(json.dumps(summary, indent=2) + "\n")

    try:
        version = selected_sdk_version(platform / CATALOG)
        summary["selectedSdk"] = {"version": version, "catalog": str(platform / CATALOG), "catalogSha256": sha256(platform / CATALOG),
                                  "worksConsumerPins": require_consumer_pins(works, version),
                                  "hostPackagePins": require_host_package_pins(platform / "apphost.cs", version)}
    except RECORDED_FAILURES as error:
        summary["failure"] = failure_text(error)
        save()
        print(summary["failure"], flush=True)
        return 1

    def run(name, command, cwd=platform, environment=None, expected_refusal=None):
        log = artifacts / (name + ".log")
        entry = {"name": name, "command": [str(value) for value in command], "cwd": str(cwd), "log": log.name}
        summary["commands"].append(entry)
        save()
        print(name + ": " + str(log), flush=True)
        with log.open("w") as output:
            result = run_owned(entry["command"], cwd=cwd, env=environment, stdout=output, timeout=1800)
        entry["exitCode"] = result.returncode
        entry["sha256"] = sha256(log)
        save()
        if expected_refusal:
            if result.returncode != 1 or expected_refusal not in log.read_text():
                raise ValueError(name + ": required closed refusal was not preserved")
        elif result.returncode:
            raise ValueError(name + " failed; see " + str(log))

    def build(name, project, configuration, root, output=None, extra=()):
        run(name, consumer_build_command(project, configuration, version, output, extra), root)

    try:
        for row, (title, package, api, consumer) in ROWS.items():
            consumer_path = platform / consumer[2:] if consumer.startswith("P/") else works / consumer
            summary["rows"][row] = {"title": title, "status": "open", "producer": {"package": package, "version": version, "api": api},
                "consumer": consumer, "consumerRoot": "Platform" if consumer.startswith("P/") else "Works", "consumerSha256": sha256(consumer_path) if consumer_path.is_file() else None,
                "requiredProofs": {"persisted": False, "negative": False, "restart": False, "rollback": False}}
        summary["rows"]["R8"]["additionalSeams"] = {"package": "Hexalith.EventStore.Client", "version": version, "api": "IProcessDispositionAuthorizer/IProcessDispositionAuditSink"}
        summary["rows"]["R10"]["configurationConsumers"] = [{"path": str(path.relative_to(platform)), "sha256": sha256(path)}
            for path in sorted((platform / "DaprComponents").rglob("*.yaml"))
            ] + [{"path": str(path.relative_to(platform)), "sha256": sha256(path)} for path in
                [platform / "config/works-production-profile.schema.json", platform / "src/Hexalith.Platform.Composition/WorksProductionProfile.cs"]]
        runtime_source_key = "worksDebugAndRuntimeSdk" if profile["name"] == "full" else "worksRuntimeSdk"
        summary["sourceProducers"] = {runtime_source_key: runtime_source_snapshot(runtime_sdk) if args.runtime_sdk_root is not None else source_snapshot(runtime_sdk, producer=True)}
        if profile["name"] == "full":
            summary["sourceProducers"]["platformDebugSdk"] = runtime_source_snapshot(runtime_sdk) if args.runtime_sdk_root is not None else source_snapshot(platform / "references/Hexalith.EventStore", producer=True)
        summary["platformConsumerSources"] = platform_consumer_sources(platform)
        summary["worksConsumerSource"] = source_snapshot(works)
        summary["fixtureSources"] = fixture_sources(works)
        snapshot_manifest = getattr(args, "platform_source_binding", None)
        summary["canonicalSources"] = {"Works": canonical_source_snapshot(works)}
        if snapshot_manifest:
            summary["platformSourceSnapshot"] = works_host_snapshot.verify(snapshot_manifest, platform)
            summary["canonicalSources"]["Platform"] = canonical_source_snapshot(Path(summary["platformSourceSnapshot"]["originRoot"]))
        else:
            summary["canonicalSources"]["Platform"] = canonical_source_snapshot(platform)
        summary["linkedFixtureSources"] = linked_fixture_snapshot(works, platform)
        summary["verifierSha256"] = sha256(Path(__file__))
        summary["verifierHelpers"] = {name: sha256(Path(__file__).parent / name) for name in VERIFIER_HELPERS}
        save()
        run("evidence-gate-tests", ["python3", "-m", "unittest", "test_verify_works_host", "test_works_toolchain", "test_verify_works_producer", "test_isolated_daprd", "test_retained_works_host", "test_works_host_snapshot", "test_works_verifier_command", "test_works_verifier_inputs"], platform / "eng")
        run("component-evidence-gate-tests", ["python3", "-m", "unittest", "test_verify_full", "test_prepare_acceptance"], platform / "eng/ext-host-1")
        agents_configuration = ["--configuration", profile["agentsConfiguration"]]
        run("agents-full-refusal", ["bash", "eng/verify-agents-host.sh", *agents_configuration, "--artifacts-path", str(artifacts / "agents-full")],
            expected_refusal="DependencyNotAvailable: EXT-HOST-1")
        summary["checks"]["agentsFull"] = "expected-unavailable; not a Works prerequisite"
        run("agents-local-scaffold", ["bash", "eng/verify-agents-host.sh", "--mode", "LocalScaffold", *agents_configuration, "--artifacts-path", str(artifacts / "agents-local")])
        # These tests locate their checkout through artifact ancestors. Keep their private
        # outputs under Platform while avoiding shared default bin/obj deployment directories.
        (platform / "artifacts").mkdir(exist_ok=True)
        platform_test_output = Path(tempfile.mkdtemp(prefix="works-parity-tests-", dir=platform / "artifacts"))
        summary["platformTestsArtifacts"] = str(platform_test_output)
        for configuration in profile["consumerConfigurations"]:
            build("platform-tests-build-" + configuration.lower(), "tests/Hexalith.Platform.Works.Tests/Hexalith.Platform.Works.Tests.csproj", configuration, platform,
                  platform_test_output / configuration.lower(),
                  extra=["-p:HexalithEventStoreRoot=" + str(runtime_sdk), "-p:HexalithEventStoreVersion=" + version] if args.runtime_sdk_root is not None else [])
            executable = platform_test_output / configuration.lower() / "bin/Hexalith.Platform.Works.Tests" / configuration.lower() / "Hexalith.Platform.Works.Tests"
            xml = artifacts / ("platform-" + configuration.lower() + ".xml")
            run("platform-tests-" + configuration.lower(), [executable, "-noLogo", "-result-xml", xml],
                environment=os.environ.copy() | {"Platform__Works__SourceRoot": str(works)})
            summary["checks"][configuration] = require_passed_tests(xml)
            if configuration == "Debug":
                summary["platformDebugSdkAssemblies"] = {path.name: sha256(path) for path in
                    sorted(executable.parent.glob("Hexalith.EventStore.*.dll"))}
        host_output, consumer_output = artifacts / "host", artifacts / "consumer"
        build("platform-file-host-release", "./apphost.cs", "Release", platform, host_output, extra=[path_map(host_output)])
        # Qualify against the target's adapter in an isolated output. The retained Works graph keeps its own pin.
        catalog = ET.parse(platform / CATALOG).getroot()
        adapter = next(item.text for item in catalog.findall(".//HexalithAspireHostingDaprVersion") if not item.get("Condition"))
        if "Debug" in profile["consumerConfigurations"]:
            build("works-consumer-debug", "tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj", "Debug", works, artifacts / "consumer-debug",
                  ["-p:HexalithAspireHostingDaprVersion=" + adapter, "-p:PlatformWorksRuntimeRoot=" + str(platform),
                   "-p:HexalithEventStoreRoot=" + str(runtime_sdk), "-p:HexalithEventStoreVersion=" + version])
        build("works-consumer-release", "tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj", "Release", works, consumer_output,
              ["-p:HexalithAspireHostingDaprVersion=" + adapter, "-p:PlatformWorksRuntimeRoot=" + str(platform),
               "-p:HexalithEventStoreRoot=" + str(runtime_sdk), "-p:HexalithEventStoreVersion=" + version, path_map(consumer_output)])
        host_runtime = host_output / "bin/release" / (PLATFORM_RUNTIME + ".dll")
        summary["platformRuntimeIdentity"] = require_identical_runtime(
            host_runtime, consumer_output / "bin/Hexalith.Works.IntegrationTests/release" / (PLATFORM_RUNTIME + ".dll"))
        consumer_directory = consumer_output / "bin/Hexalith.Works.IntegrationTests/release"
        summary["consumer"] = {"configuration": "Release", "root": str(consumer_directory),
                               "artifacts": consumer_artifacts(consumer_directory)}
        save()
        supporting_xml = artifacts / "works-consumer-supporting.xml"
        supporting_classes = list(SUPPORTING_CLASSES)
        supporting_command = [consumer_output / "bin/Hexalith.Works.IntegrationTests/release/Hexalith.Works.IntegrationTests", "-noLogo"]
        for name in supporting_classes:
            supporting_command += ["-class", "Hexalith.Works.IntegrationTests." + name]
        run("works-consumer-supporting", supporting_command + ["-result-xml", supporting_xml], works)
        summary["checks"]["supportingConsumer"] = require_passed_tests(supporting_xml,
            required_types=["Hexalith.Works.IntegrationTests." + name for name in supporting_classes])
        if "Debug" in profile["consumerConfigurations"]:
            summary["worksDebugSdkAssemblies"] = {path.name: sha256(path) for path in
                sorted((artifacts / "consumer-debug/bin/Hexalith.Works.IntegrationTests/debug").glob("Hexalith.EventStore.*.dll"))}
        assembly = host_output / "bin/release/apphost.dll"
        summary["host"] = {"source": str(platform / "apphost.cs"), "sourceSha256": sha256(platform / "apphost.cs"),
                           "assembly": str(assembly), "sha256": sha256(assembly), "configuration": "Release"}
        summary["platformAdapterAssemblies"] = {str(path): sha256(path) for path in [
            assembly.parent / "Hexalith.Platform.Composition.dll", assembly.parent / "Hexalith.Platform.Works.Runtime.dll",
            consumer_output / "bin/Hexalith.Works.IntegrationTests/release/Hexalith.Platform.Works.Runtime.dll"]}
        producer_evidence = published_producers(
            consumer_output / "obj/Hexalith.Works.IntegrationTests/project.assets.json", consumer_directory, version)
        summary["publishedProducers"] = producer_evidence
        identity_root = works / "references/Hexalith.Platform/src/Hexalith.Platform.Identity"
        summary["identityProvenance"] = {"scope": "explicit-test-only-source; no published Platform.Identity package",
            "project": str(identity_root / "Hexalith.Platform.Identity.csproj"),
            "projectSha256": sha256(identity_root / "Hexalith.Platform.Identity.csproj"),
            "qualifiedAssemblySha256": sha256(consumer_output / "bin/Hexalith.Works.IntegrationTests/release/Hexalith.Platform.Identity.dll")}
        summary["dependencyModes"] = {"Release": "public NuGet " + version + " consumer graph plus explicitly named test-only Platform.Identity, Platform.Composition, conditional Platform.Works.Runtime and linked Parties authority/query source"}
        if "Debug" in profile["consumerConfigurations"]:
            summary["dependencyModes"]["Debug"] = "sibling source project graph; independently built and hashed"
        for row in summary["rows"].values():
            producer = producer_evidence.get(row["producer"]["package"])
            if producer is None or row["consumerSha256"] is None:
                raise ValueError("A row has an absent published producer or consumer: " + row["title"])
            row["producer"].update(producer)
            if "additionalSeams" in row:
                row["additionalSeams"].update(producer_evidence[row["additionalSeams"]["package"]])
        save()
        if args.runtime_sdk_root is not None:
            summary["runtimeProducerQualification"] = qualify_runtime_source(
                summary["sourceProducers"][runtime_source_key], producer_evidence, tag="v" + version)
        else:
            summary["runtimeProducerQualification"] = require_published_runtime_source(
                summary["sourceProducers"][runtime_source_key], producer_evidence)
        save()
        # These projects run through dotnet run at host startup and are not all consumer references.
        # Prepare their exact owned output graph serially, so a clean checkout never relies
        # on coincidental prior builds or races concurrent SDK/Commons reference-assembly writes.
        runtime_projects = [works / "src/Hexalith.Works/Hexalith.Works.csproj", *[
            runtime_sdk / "src" / name / (name + ".csproj") for name in
            ("Hexalith.EventStore", "Hexalith.EventStore.Operations", "Hexalith.EventStore.Admin.Server.Host")]]
        retained_root = artifacts / "retained-works-source"
        summary["retainedHostSource"] = retained_works_host.prepare(platform / "eng/retained-works-host", works, retained_root)
        runtime_projects += [retained_root / "src/Hexalith.Works.ServiceDefaults/Hexalith.Works.ServiceDefaults.csproj",
                             retained_root / "src/Hexalith.Works.AppHost/Hexalith.Works.AppHost.csproj"]
        runtime_output = artifacts / "runtime"
        runtime_properties = ["-p:Version=" + version, "-p:MinVerVersionOverride=" + version,
            "-p:UseHexalithProjectReferences=false", "-p:UseNuGetDeps=true",
            "-p:HexalithEventStoreRoot=" + str(runtime_sdk), "-p:HexalithEventStoreVersion=" + version,
            "-p:HexalithAspireHostingDaprVersion=" + adapter]
        runtime_directories = []
        for index, project in enumerate(runtime_projects):
            properties = runtime_properties + (["-p:BuildProjectReferences=false"] if "Hexalith.Works.AppHost" in project.stem else [])
            build("runtime-release-prebuild-" + str(index), str(project), "Release", works, runtime_output, extra=properties)
            runtime_directories.append(runtime_output / "bin" / project.stem / "release")
        retained_assembly = runtime_output / "bin/Hexalith.Works.AppHost/release/Hexalith.Works.AppHost.dll"
        summary["retainedHostSource"]["assembly"] = str(retained_assembly)
        summary["retainedHostSource"]["assemblySha256"] = sha256(retained_assembly)
        summary["runtimeArtifacts"] = {str(path): sha256(path) for directory in runtime_directories
            for path in sorted(directory.rglob("*")) if path.is_file()}
        save()
        if args.prepare_only:
            summary["status"] = "preparation-passed; parity-incomplete"
            return 0
        environment = os.environ.copy()
        environment.pop("GITHUB_TOKEN", None)
        ephemeral_range = host_ephemeral_port_range()
        reserved_ranges = host_reserved_port_ranges()
        control_plane_ports = select_control_plane_ports(ephemeral_range, reserved_ranges)
        summary["controlPlanePorts"] = control_plane_ports
        summary["controlPlanePortSelection"] = {"ephemeralRangePath": "/proc/sys/net/ipv4/ip_local_port_range",
            "excludedEphemeralRange": list(ephemeral_range),
            "reservedRangesPath": "/proc/sys/net/ipv4/ip_local_reserved_ports",
            "excludedReservedRanges": [list(bounds) for bounds in reserved_ranges],
            "excludedDockerBoundaryPorts": [65535], "selected": control_plane_ports,
            "policy": "Distinct dual-stack available ports outside application/reserved ranges; Docker WSL boundary port 65535 excluded"}
        summary["qualifiedToolchain"] = require_toolchain_authority(works_toolchain.qualify(platform,
            os.environ.get("WORKS_PLATFORM_DAPRD", str(Path.home() / ".dapr/bin/daprd"))), platform)
        save()
        isolated_runtime = artifacts / "isolated-daprd"
        summary["isolatedRuntime"] = require_qualified_runtime(isolated_daprd.prepare(isolated_runtime,
            os.environ.get("WORKS_PLATFORM_DAPRD", str(Path.home() / ".dapr/bin/daprd")), control_plane_ports["sentry"]),
            summary["qualifiedToolchain"])
        runtime_environment = {"WORKS_PLATFORM_APPHOST_ASSEMBLY": str(assembly), "WORKS_PLATFORM_APPHOST_SHA256": sha256(assembly),
            "WORKS_PLATFORM_ROOT": str(platform), "WORKS_PLATFORM_WORKS_ROOT": str(works),
            "WORKS_RETAINED_HOST_ROOT": str(retained_root), "WORKS_RETAINED_HOST_ASSEMBLY": str(retained_assembly),
            "WORKS_RETAINED_HOST_SHA256": sha256(retained_assembly),
            "Platform__Works__SourceRoot": str(works), "Platform__Identity__Enabled": "false",
            "Version": version, "MinVerVersionOverride": version,
            "ArtifactsPath": str(runtime_output), "UseArtifactsOutput": "true", "WORKS_PLATFORM_RUNTIME_ARTIFACTS": str(runtime_output),
            # Every dependency was built serially above. Parallel dotnet-run resource starts must not
            # rewrite those shared outputs; the final artifact hash checks remain authoritative.
            "BuildProjectReferences": "false",
            "WORKS_PLATFORM_SENTRY_PORT": str(control_plane_ports["sentry"]),
            "WORKS_PLATFORM_PLACEMENT_PORT": str(control_plane_ports["placement"]),
            "WORKS_PLATFORM_SCHEDULER_PORT": str(control_plane_ports["scheduler"]),
            "WORKS_PLATFORM_DAPRD_RUNTIME_PATH": str(isolated_runtime),
            "UseHexalithProjectReferences": "false", "UseNuGetDeps": "true",
            "HexalithEventStoreRoot": str(runtime_sdk), "HexalithEventStoreVersion": version, "HexalithAspireHostingDaprVersion": adapter,
            "Platform__Works__EventStoreSourceRoot": str(runtime_sdk), "WORKS_PLATFORM_SDK_SOURCE_ROOT": str(runtime_sdk),
            "WORKS_PLATFORM_HOST_EVIDENCE": str(artifacts / "host-starts.jsonl"),
            "WORKS_SDK_PROOF_EVIDENCE_PATH": str(artifacts / "persisted-states.jsonl"), "WORKS_APPHOST_TEST_STARTUP_TIMEOUT_SECONDS": "180"}
        environment.update(runtime_environment)
        summary["runtimeEnvironment"] = runtime_environment
        save()
        executable = consumer_output / "bin/Hexalith.Works.IntegrationTests/release/Hexalith.Works.IntegrationTests"
        # The archive must actually start before expensive write/snapshot/restore scenarios begin.
        # Its isolated evidence never enters the matrix's actualStarts or rollback phase records.
        preflight_runtime = artifacts / "isolated-daprd-retained-preflight"
        summary["retainedPreflightRuntime"] = require_qualified_runtime(isolated_daprd.prepare(preflight_runtime,
            os.environ.get("WORKS_PLATFORM_DAPRD", str(Path.home() / ".dapr/bin/daprd")), control_plane_ports["sentry"]),
            summary["qualifiedToolchain"])
        preflight_environment = environment | {"WORKS_PLATFORM_HOST_EVIDENCE": str(artifacts / "retained-preflight-starts.jsonl"),
            "WORKS_SDK_PROOF_EVIDENCE_PATH": str(artifacts / "retained-preflight-states.jsonl"),
            "WORKS_PLATFORM_DAPRD_RUNTIME_PATH": str(preflight_runtime)}
        preflight_xml = artifacts / "retained-preflight.xml"
        run("retained-host-preflight", [executable, "-noLogo", "-explicit", "on", "-class",
            "Hexalith.Works.IntegrationTests.WorksPlatformRetainedHostPreflightTests", "-result-xml", preflight_xml], works, preflight_environment)
        summary["checks"]["retainedStartupPreflight"] = require_passed_tests(preflight_xml, ["ArchivedTopologyReachesHealthyReadiness"])
        summary["retainedPreflightStarts"] = verify_host_records(artifacts / "retained-preflight-starts.jsonl", assembly, platform, version,
            runtime_artifacts=summary["runtimeArtifacts"], control_plane_ports=control_plane_ports,
            retained_assembly=retained_assembly, retained_preflight=True)
        summary["retainedPreflightRuntimeEvidence"] = isolated_daprd.verify(preflight_runtime,
            summary["retainedPreflightRuntime"], summary["retainedPreflightStarts"])
        save()
        xml = artifacts / "platform-recovery.xml"
        run("platform-recovery", [executable, "-noLogo", "-explicit", "on", "-class", "Hexalith.Works.IntegrationTests.WorksSdkRecoveryPipelineSmokeTests", "-result-xml", xml], works, environment)
        summary["checks"]["liveRecovery"] = require_passed_tests(xml,
            ["SuccessfulCallbacksRegisterSdkReminderAndProcessWithoutPeriodicRecovery", "SdkRemindersAndCascadeConvergeThroughPersistedReceiptsAcrossRestart"])
        historical_xml = artifacts / "platform-historical.xml"
        run("platform-historical", [executable, "-noLogo", "-explicit", "on", "-method", "Hexalith.Works.IntegrationTests.WorksPlatformHistoricalPipelineSmokeTests.PlatformLifecycleTreeClaimFilteringAndSecuritySurviveRestart", "-result-xml", historical_xml], works, environment)
        summary["checks"]["liveHistorical"] = require_passed_tests(historical_xml, ["PlatformLifecycleTreeClaimFilteringAndSecuritySurviveRestart"])
        ha_xml = artifacts / "platform-ha.xml"
        run("platform-ha", [executable, "-noLogo", "-explicit", "on", "-method", "Hexalith.Works.IntegrationTests.WorksPlatformHaPipelineSmokeTests.TwoWorksHostsRecoverLostFiringAfterPrimaryStopsWithoutDuplicateEffect", "-result-xml", ha_xml], works, environment)
        summary["checks"]["liveHa"] = require_passed_tests(ha_xml, ["TwoWorksHostsRecoverLostFiringAfterPrimaryStopsWithoutDuplicateEffect"])
        mtls_xml = artifacts / "platform-mtls.xml"
        run("platform-mtls", [executable, "-noLogo", "-explicit", "on", "-class", "Hexalith.Works.IntegrationTests.WorksMtlsAuthorizationSmokeTests", "-result-xml", mtls_xml], works, environment)
        summary["checks"]["liveMtls"] = require_passed_tests(mtls_xml, ["Mtls_allows_event_store_and_denies_an_unauthorized_caller_at_works_process"])
        # The focused eleven-source rebuild is the exact fact that failed on public 3.124.0; R4 binds its outcome.
        projection_xml = artifacts / "platform-projection.xml"
        run("platform-projection", [executable, "-noLogo", "-explicit", "on", "-method", "Hexalith.Works.IntegrationTests.WorksPlatformProjectionPipelineSmokeTests.ActualPlatformSharedRebuildAndConcurrentCatchUpPreserveCompleteFamily", "-result-xml", projection_xml], works, environment)
        summary["checks"]["liveProjection"] = require_passed_tests(projection_xml, ["ActualPlatformSharedRebuildAndConcurrentCatchUpPreserveCompleteFamily"])
        summary["actualStarts"] = verify_host_records(artifacts / "host-starts.jsonl", assembly, platform, version,
            runtime_artifacts=summary["runtimeArtifacts"], host_runtime=host_runtime,
            composition_literals=platform_composition_literals(platform / "src/Hexalith.Platform.Composition/WorksPreviewModel.cs"),
            control_plane_ports=control_plane_ports, retained_assembly=retained_assembly)
        summary["isolatedRuntimeEvidence"] = isolated_daprd.verify(isolated_runtime, summary["isolatedRuntime"], summary["actualStarts"])
        for row in summary["rows"].values():
            row["targetCommands"] = target_commands(summary["commands"])
        require_unchanged_inputs(summary, works, platform, snapshot_manifest, ephemeral_range, reserved_ranges)
        summary["rollback"] = bind_persisted_outcomes(artifacts / "persisted-states.jsonl", summary["rows"], summary["actualStarts"])
        summary["status"] = "passed-disposable-synthetic-parity-and-rollback"
        return 0
    except RECORDED_FAILURES as error:
        summary["failure"] = failure_text(error)
        print(summary["failure"], flush=True)
        return 1
    finally:
        save()
        print("Evidence: " + str(artifacts / "ledger.json"), flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--works-root", type=Path, default=Path(__file__).resolve().parents[2] / "works")
    parser.add_argument("--artifacts-path", type=Path)
    parser.add_argument("--runtime-sdk-root", type=Path, help="Explicit producer source directory; independently qualified against official commit trees and exact Git blobs")
    parser.add_argument("--platform-source-binding", type=Path, help="Explicit adjacent manifest for this run-owned immutable Platform export; exact export and current canonical bytes must remain equal")
    parser.add_argument("--prepare-only", action="store_true", help="Run build/profile checks only; never accepts parity")
    parser.add_argument("--profile", choices=("full", "ci"), default="full", help="full: source Debug plus package Release; ci: package Release only; both require every live proof and complete rollback")
    args = parser.parse_args()
    return run_verification(args)


if __name__ == "__main__":
    raise SystemExit(main())
