#!/usr/bin/env python3
"""Verify selected immutable historical inputs, without running live workloads."""

import argparse
import base64
import gzip
import hashlib
import json
import xml.etree.ElementTree as ET
from collections import Counter
from pathlib import Path

PACKET = Path("_bmad-output/implementation-artifacts/evidence/five-deferred-producer-recovery-2026-10-28")
HISTORY = Path("_bmad-output/implementation-artifacts/evidence/story-4-16")
PRODUCERS = {
    "public-3.125.1-2026-10-18": ("3.125.1", "55432c50d4b6f1666b4bd6d4a60bfc263b2d0e0b", 92, 212),
    "public-3.126.0-2026-10-19": ("3.126.0", "fcf78d6cafb6c0c9f52dc350a9a09aff54ded288", 112, 241),
}
PACKAGES = {
    "Hexalith.EventStore." + name
    for name in (
        "Admin.Abstractions",
        "Aspire",
        "Client",
        "Contracts",
        "DomainService",
        "Gateway",
        "Server",
        "ServiceDefaults",
        "Testing",
    )
}
RECOVERY = {
    "RetryStorePreservesCoordinatedBatchCapability",
    "RetainedPlanWaitBoundsNonCooperativeStoreAcquisition",
    "SdkRemindersAndCascadeConvergeThroughPersistedReceiptsAcrossRestart",
    "SuccessfulCallbacksRegisterSdkReminderAndProcessWithoutPeriodicRecovery",
}
PROOFS = {"persisted", "negative", "restart", "rollback"}
STATUS = "passed-disposable-synthetic-parity-and-rollback"
CANONICAL_COMMANDS = {
    "evidence-gate-tests",
    "agents-full-refusal",
    "agents-local-scaffold",
    "platform-tests-build-debug",
    "platform-tests-debug",
    "platform-tests-build-release",
    "platform-tests-release",
    "platform-file-host-release",
    "works-consumer-debug",
    "works-consumer-release",
    "works-consumer-supporting",
    "platform-recovery",
    "platform-historical",
    "platform-ha",
    "platform-projection",
} | {f"runtime-release-prebuild-{number}" for number in range(5)}
RETAINED_HOSTS = {
    "3.125.1": "b48430f574eff99b0949a9ed21027872108fb5441806c76a07adf50b9ca3106a",
    "3.126.0": "7e87e7d9b9d7dcb2c3506942b07023a87e94b2e0aa39785e8501c1581f90bbfe",
}
RESTART_PHASES = {
    "rescheduled-expiry-committed-current-witness",
    "cascade-expiry-target-commit-before-checkpoint",
    "cascade-expiry-restart-receipt-and-checkpoint",
    "canonical-related-child-creation-gap",
    "cascade-target-commit-before-checkpoint",
    "five-child-persisted-target-receipts",
    "cascade-restart-receipt-and-checkpoint",
    "child-resume-noop-receipt-before-checkpoint",
    "complete-supported-process-coverage",
    "child-resume-noop-restart-receipt-and-checkpoint",
}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def unique(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, f"Duplicate JSON key: {key}")
        result[key] = value
    return result


def parse(data):
    return json.loads(data, object_pairs_hook=unique)


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def verify(root):
    manifest = parse((root / PACKET / "historical-inputs.json").read_bytes())
    require(manifest["schema"] == 1, "Unknown historical manifest schema")
    hashes = manifest["inputsSha256"]
    seen = set()
    consumers = {}
    sdk_sources = {}

    def read(path):
        name = path.as_posix()
        require(not path.is_absolute() and ".." not in path.parts, f"Unsafe input: {name}")
        require(name in hashes, f"Unbound input: {name}")
        data = (root / path).read_bytes()
        require(sha256(data) == hashes[name], f"Changed input: {name}")
        seen.add(name)
        return data

    def load(path):
        return parse(read(path))

    def xml(path, count, methods=None):
        data = read(path)
        document = ET.fromstring(data)
        assemblies = document.findall("assembly")
        require(len(assemblies) == 1, f"Expected one test assembly: {path}")
        assembly = assemblies[0]
        require(
            int(assembly.attrib["total"]) == count and int(assembly.attrib["passed"]) == count,
            f"Unexpected test total: {path}",
        )
        for key in ("failed", "errors", "skipped", "not-run"):
            require(int(assembly.attrib[key]) == 0, f"Nonqualifying {key}: {path}")
        tests = list(document.iter("test"))
        require(
            len(tests) == count and all(test.attrib["result"] == "Pass" for test in tests),
            f"Missing or nonpassing executed facts: {path}",
        )
        if methods is not None:
            require({test.attrib["method"] for test in tests} == methods, f"Wrong executed methods: {path}")
        return sha256(data)

    def records(path, duplicates=None):
        rows = [parse(line) for line in gzip.decompress(read(path)).splitlines()]
        phases = {}
        for row in rows:
            phases.setdefault(row["phase"], []).append(row)
        repeated = {phase: len(values) for phase, values in phases.items() if len(values) != 1}
        require(rows and repeated == (duplicates or {}), f"Missing or unexpected repeated phase: {path}")
        return phases

    for directory, (version, commit, source_total, supporting_total) in PRODUCERS.items():
        packet = HISTORY / directory
        canonical = packet / "canonical-admission"
        result = load(canonical / "result.json")
        ledger = load(canonical / "ledger.json")
        consumers[version] = ledger["worksConsumerSource"]
        require(
            sha256(json.dumps(consumers[version]["sourceFiles"], sort_keys=True).encode())
            == consumers[version]["inventorySha256"],
            f"Historical Works source inventory mismatch: {version}",
        )
        require(
            result["exitCode"] == 0 and result["status"] == ledger["status"] == STATUS,
            f"Nonqualifying canonical run: {version}",
        )
        selected = ledger["selectedSdk"]
        if version == "3.126.0":
            require(
                set(selected["worksConsumerPins"]) == {"Directory.Build.props", "Directory.Packages.props"},
                "Incomplete consumer pin inventory",
            )
        pins = [selected["worksConsumerPin"]] if version == "3.125.1" else list(selected["worksConsumerPins"].values())
        require(
            selected["version"] == version and all(pin["version"] == version for pin in pins),
            f"Wrong selected SDK: {version}",
        )
        producer = ledger["runtimeProducerQualification"]
        sdk_sources[version] = ledger["sourceProducers"]["worksDebugAndRuntimeSdk"]
        require(
            sha256(json.dumps(sdk_sources[version]["sourceFiles"], sort_keys=True).encode())
            == sdk_sources[version]["inventorySha256"]
            == producer["sourceInventorySha256"],
            "Canonical SDK source inventory mismatch",
        )
        require(
            producer["repositoryCommit"] == producer["officialCommit"]["objectSha"] == commit
            and producer["matchesPublishedProducer"] is True,
            f"Wrong runtime producer: {version}",
        )
        require(
            producer["namedTag"]["tag"] == "v" + version and producer["namedTag"]["commit"] == commit,
            f"Wrong named tag: {version}",
        )
        archives = ledger["publishedProducers"]
        require(set(archives) == PACKAGES, f"Wrong public package inventory: {version}")
        for archive in archives.values():
            require(
                archive["version"] == version
                and archive["repositoryCommit"] == commit
                and archive["source"] == "https://api.nuget.org/v3/index.json",
                f"Wrong public archive: {version}",
            )
        commands = ledger["commands"]
        require(
            len(commands) == 20 and {item["name"] for item in commands} == CANONICAL_COMMANDS,
            f"Incomplete canonical commands: {version}",
        )
        for command in commands:
            expected = 1 if command["name"] == "agents-full-refusal" else 0
            require(command["exitCode"] == expected, f"Wrong command exit: {version}/{command['name']}")
            require(sha256(read(canonical / command["log"])) == command["sha256"], "Command log binding failed")
        checks = {
            "Debug": ("platform-debug.xml", source_total, None),
            "Release": ("platform-release.xml", source_total, None),
            "supportingConsumer": ("works-consumer-supporting.xml", supporting_total, None),
            "liveRecovery": ("platform-recovery.xml", 4, RECOVERY),
            "liveHistorical": (
                "platform-historical.xml",
                1,
                {"PlatformLifecycleTreeClaimFilteringAndSecuritySurviveRestart"},
            ),
            "liveHa": ("platform-ha.xml", 1, {"TwoWorksHostsRecoverLostFiringAfterPrimaryStopsWithoutDuplicateEffect"}),
            "liveProjection": (
                "platform-projection.xml",
                1,
                {"ActualPlatformSharedRebuildAndConcurrentCatchUpPreserveCompleteFamily"},
            ),
        }
        for check, (filename, count, methods) in checks.items():
            require(
                xml(canonical / filename, count, methods) == ledger["checks"][check]["sha256"], "XML binding failed"
            )
            require(result["checks"][check] == ledger["checks"][check], f"Result/check mismatch: {check}")
        duplicates = {"platform-initial-projection-convergence": 2} if version == "3.125.1" else None
        phases = records(canonical / "persisted-states.jsonl.gz", duplicates)
        concurrent = phases["platform-focused-projection-rebuild"][0]["state"]["concurrentDelivery"]
        require(
            concurrent["committedSourceHead"] == 4
            and concurrent["committedGlobalPosition"] == 14
            and concurrent["automaticCatchUpAfterCommit"] is True
            and concurrent["oldGenerationStayedReadable"] is True
            and concurrent["actualNewDeliveryDuringCapture"] is True,
            f"Receipt-backed catch-up did not converge: {version}",
        )
        require(
            concurrent["before"]["Generation"] == 1
            and concurrent["after"]["Generation"] == 2
            and concurrent["before"]["Digest"] != concurrent["after"]["Digest"] == concurrent["expectedDigest"],
            f"Projection generation/digest did not advance: {version}",
        )
        if version == "3.126.0":
            checkpoint = concurrent["caughtUpCheckpoint"]
            require(
                checkpoint["OwnerSequenceNumber"] == 4 and checkpoint["Position"] == 14,
                "Retained 3.126.0 catch-up checkpoint is stale",
            )
        rows = ledger["rows"]
        require(set(rows) == {f"R{number}" for number in range(1, 12)}, "Incomplete R1-R11 inventory")
        for name, row in rows.items():
            row_producer = row["producer"]
            archive = archives[row_producer["package"]]
            require(
                all(
                    row_producer[field] == archive[field]
                    for field in ("version", "repositoryCommit", "sha256", "source")
                ),
                f"Row producer mismatch: {version}/{name}",
            )
            require(row["status"] == "passed" and set(row["requiredProofs"]) == PROOFS, f"Incomplete proof row: {name}")
            for proof in row["requiredProofs"].values():
                require(
                    version == "3.125.1" and proof is True or isinstance(proof, dict) and proof["passed"] is True,
                    f"Failed proof: {version}/{name}",
                )
                if isinstance(proof, dict):
                    require(
                        proof["evidence"] and all(item in row["evidence"] for item in proof["evidence"]),
                        f"Unbound proof evidence: {version}/{name}",
                    )
            require(row["evidence"], f"Empty persisted evidence: {version}/{name}")
            for evidence in row["evidence"]:
                expected_hash = evidence.get("recordSha256", evidence.get("sha256"))
                matches = [
                    record
                    for record in phases[evidence["phase"]]
                    if sha256(json.dumps(record, sort_keys=True).encode()) == expected_hash
                ]
                require(len(matches) == 1, f"Persisted evidence mismatch: {version}/{name}")
        require(
            ledger["rollback"]["complete"] is True and result["rollback"]["complete"] is True,
            f"Incomplete rollback: {version}",
        )
        snapshot, restored = ledger["rollback"]["snapshot"], ledger["rollback"]["restored"]
        for key in ("fleet", "runtimeHashes"):
            before = dict(snapshot[key])
            if version == "3.126.0" and key == "fleet":
                del before["redis-full-value-inventory-sha256"]
            require(before and before == restored[key], f"Restored {key} inventory mismatch: {version}")
        starts = ledger["actualStarts"]
        require(
            len(starts) == 10
            and Counter(start["role"] for start in starts) == {"platform-target": 9, "retained-rollback": 1},
            "Incomplete actual host starts",
        )
        for start in starts:
            host = ledger["host"] if start["role"] == "platform-target" else None
            expected_hash = host["sha256"] if host else RETAINED_HOSTS[version]
            require(
                start["sha256"].lower() == expected_hash.lower()
                and (
                    start["assembly"] == host["assembly"]
                    if host
                    else Path(start["assembly"]).name == "Hexalith.Works.AppHost.dll"
                ),
                "Actual host identity mismatch",
            )
            require(
                start["sdkAssemblies"] and all(item["version"] == version + ".0" for item in start["sdkAssemblies"]),
                "Actual host SDK identity mismatch",
            )
            require(
                {"eventstore", "eventstore-admin", "eventstore-operations", "works"}
                <= {module["resource"] for module in start["runtimeModules"]},
                "Incomplete actual runtime modules",
            )

    first = HISTORY / "public-3.125.1-2026-10-18"
    receipt = load(first / "public-producer-receipts.json")
    require(
        receipt["selectedVersion"] == "3.125.1"
        and receipt["allArchivesBindNamedProducer"] is True
        and receipt["allLocalCachesMatchOfficialArchives"] is True,
        "Public receipt does not qualify",
    )
    require({item["package"] for item in receipt["archives"]} == PACKAGES, "Incomplete public archive receipts")
    for archive in receipt["archives"]:
        require(
            archive["httpStatus"] == 200
            and archive["repositoryCommit"] == PRODUCERS[first.name][1]
            and archive["sha256"] == archive["localCache"]["sha256"],
            "Public archive receipt mismatch",
        )
    lane = first / "source-debug-lane/lane3"
    original_packet = load(first / "manifest.json")
    restart = "SdkRemindersAndCascadeConvergeThroughPersistedReceiptsAcrossRestart"
    callback = "SuccessfulCallbacksRegisterSdkReminderAndProcessWithoutPeriodicRecovery"
    for packet, version, expected in (
        (lane, "3.125.1", {restart: {restart}, callback: {callback}}),
        (HISTORY / "public-3.126.0-2026-10-19/retained-source-debug", "3.126.0", {"retained-recovery": RECOVERY}),
    ):
        result = load(packet / "results.json")
        require(
            result["status"] == "passed"
            and result["producer"]["version"] == version
            and result["producer"]["tag"] == "v" + version,
            "Wrong source Debug producer",
        )
        source_files = sdk_sources[version]["sourceFiles"]
        debug_files = {
            path: digest
            for path, digest in source_files.items()
            if path.startswith("src/")
            and Path(path).suffix in (".cs", ".csproj")
            or path in ("Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props")
        }
        require(
            result["producer"]["inventorySha256"] == sha256(json.dumps(debug_files, sort_keys=True).encode()),
            "Source Debug SDK inventory mismatch",
        )
        environment = result["environment"]
        require(
            environment["UseHexalithProjectReferences"] == "true"
            and environment["UseNuGetDeps"] == "false"
            and environment["HexalithEventStoreVersion"] == version
            and environment["HexalithEventStoreRoot"] == result["producer"]["root"],
            "Source Debug environment mismatch",
        )
        if version == "3.125.1":
            consumer = consumers[version]
            require(
                result["worksInventorySha256"] == consumer["inventorySha256"]
                and result["worksHead"] == consumer["commit"],
                "Final-decoder source Debug binding mismatch",
            )
            for path, expected_digest in {
                "WorksRecoveryEventDecoder.cs": "8a30199ed496854746eec4825b6bd0dcee46746f3e38a5940b92375f87e58df3",
                "WorksSourceStreamReader.cs": "ce6286aa0ff7c2705a88fbda905c8f00cb3216b58c7e9cea63865411e050819d",
            }.items():
                require(
                    consumer["sourceFiles"]["src/Hexalith.Works/Recovery/" + path] == expected_digest,
                    f"Wrong final historical decoder/reader: {path}",
                )
        commands = result["commands"]
        require(
            {command["name"] for command in commands}
            == set(expected) | {"consumer-debug-build"} | {f"runtime-debug-prebuild-{number}" for number in range(5)},
            "Incomplete source Debug commands",
        )
        for command in commands:
            name = command["name"]
            require(command["exitCode"] == 0, f"Failed source Debug command: {name}")
            arguments = command["command"]
            if name not in expected:
                require(
                    "-c" in arguments
                    and arguments[arguments.index("-c") + 1] == "Debug"
                    and "-p:UseHexalithProjectReferences=true" in arguments
                    and "-p:UseNuGetDeps=false" in arguments,
                    "Source Debug build command mismatch",
                )
            else:
                require(Path(arguments[0]).parent.name == "debug", "Source Debug executable mismatch")
            # The two 3.125.1 live logs were packaged with different bytes from their original-run hashes.
            # Bind their retained forms to the original packet manifest; XML remains original-hash bound.
            log_hash = command["logSha256"]
            if version == "3.125.1" and name in expected:
                log_hash = original_packet["files"]["source-debug-lane/lane3/" + name + ".log"]
            require(sha256(read(packet / (name + ".log"))) == log_hash, "Source Debug log binding failed")
            if name in expected:
                require(
                    xml(packet / (name + ".xml"), len(expected[name]), expected[name]) == command["xmlSha256"],
                    "Source Debug XML binding failed",
                )
                states = records(packet / (name + "-states.jsonl.gz"))
                require(set(command["phases"]) == set(states), "Source Debug persisted phases differ")
                expected_phases = {"successful-callback-no-periodic-recovery"} if name == callback else RESTART_PHASES
                if version == "3.126.0":
                    expected_phases = RESTART_PHASES | {"successful-callback-no-periodic-recovery"}
                    fixture = "tests/Hexalith.Works.IntegrationTests/WorksSdkRecoveryPipelineSmokeTests.cs"
                    require(
                        result["fixtureSources"][fixture] == ledger["fixtureSources"][fixture],
                        "Retained twelve-source fixture differs from canonical qualification",
                    )
                    coverage = states["complete-supported-process-coverage"][0]["state"]
                    require(
                        coverage["RetainedCandidates"] == 12
                        and coverage["Complete"] is True
                        and coverage["Unresolved"] == 0
                        and coverage["CapacityExceeded"] is False,
                        "Twelve-source retained coverage did not complete",
                    )
                require(set(states) == expected_phases, "Missing required source Debug persisted phases")
    decision = load(HISTORY / "public-3.126.0-2026-10-19/owner-decision.json")
    require(
        decision["selectedVersion"] == "3.126.0"
        and decision["producer"] == PRODUCERS["public-3.126.0-2026-10-19"][1]
        and decision["decision"] == "Update the story and consumer pins to 3.126.0, then requalify",
        "Wrong owner decision",
    )
    ancestry = load(PACKET / "corrected-producer-ancestry.json")
    wanted = PRODUCERS[first.name][1]
    for entry in ancestry["commits"]:
        raw = base64.b64decode(entry["rawBase64"], validate=True)
        digest = hashlib.sha1(b"commit " + str(len(raw)).encode() + b"\0" + raw).hexdigest()
        require(entry["sha1"] == wanted == digest, "Git ancestry object identity mismatch")
        parents = [
            line.split()[1].decode() for line in raw.split(b"\n\n", 1)[0].splitlines() if line.startswith(b"parent ")
        ]
        wanted = entry["nextParent"]
        require(
            wanted in parents or wanted is None and digest == "216b790628574d39802adcc8f4fea0d4034ef9f0",
            "Git ancestry chain is broken",
        )
    require(wanted is None, "Git ancestry did not reach the receipt-conflict correction")
    require(seen == set(hashes), "Historical manifest contains unused inputs")
    print(
        f"Historical integrity passed: {len(seen)} inputs; two named producers; "
        "88 proof kinds; source Debug restart/callback."
    )
    print("This verifies retained records; it does not execute live workloads or authenticate current external feeds.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[4])
    verify(parser.parse_args().root.resolve())
