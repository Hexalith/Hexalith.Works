"""Generate a separate successor summary only from checked retained evidence."""
import argparse
from collections import Counter
import json
from pathlib import Path
import re
import xml.etree.ElementTree as ET

from acceptance_support import (ARTIFACTS, CI_LOG_SHA256, WORKS_COMMIT, sealed_native_reader,
                                sha256, verified_members, write_json_new)


EXPECTED_ORDINARY = {"UnitTests": 597, "ArchitectureTests": 398,
                     "PropertyTests": 3, "IntegrationTests": 1023}
REQUIRED_JOBS = {"Lint release tooling", "Platform R1–R11 parity and rollback",
                 "Platform-owned Works proof toolchain", "ci / build-and-test"}
RUN_IDS = {"CI": 40407181685, "Commitlint": 40407181557, "CodeQL": 40407181599}
COMPILED_PROJECTS = ("Hexalith.Platform.Custody", "Hexalith.Platform.Works.Runtime", "Hexalith.Builds.Tooling")


def validate_claims(runs, jobs, ordinary_members, build_log, canonical_members, acceptance):
    """Refuse unsupported claims; derive reported counts from native case records/logs."""
    for name, run_id in RUN_IDS.items():
        run = runs.get(name)
        if (not run or run.get("id") != run_id or run.get("head_sha") != WORKS_COMMIT
                or run.get("status") != "completed" or run.get("conclusion") != "success"):
            raise ValueError("Missing or failed ancillary/run evidence: " + name)
    by_name = {job["name"]: job for job in jobs}
    if len(by_name) != len(jobs) or not REQUIRED_JOBS.issubset(by_name):
        raise ValueError("Missing or duplicate required CI job evidence")
    for name in REQUIRED_JOBS:
        job = by_name[name]
        if job["status"] != "completed" or job["conclusion"] != "success":
            raise ValueError("Required CI job did not succeed: " + name)
    ci = json.loads(canonical_members["gate/ledger.json"])
    if (acceptance.get("status") != "passed" or acceptance.get("errors")
            or acceptance.get("consumedLedgerSha256") != sha256(canonical_members["gate/ledger.json"])
            or acceptance.get("verifiedOriginalZips") != ARTIFACTS
            or acceptance.get("extractedBytesBoundDirectlyToOriginalZips") is not True):
        raise ValueError("Successor acceptance report is absent, failed or bound to different inputs")
    methods = set()
    fact_count = 0
    for name, raw in canonical_members.items():
        if name.startswith("gate/") and name.endswith(".xml"):
            for test in ET.fromstring(raw).iter("test"):
                if test.get("result") != "Pass":
                    raise ValueError("Canonical fact is not an executed Pass")
                fact_count += 1
                methods.add(test.get("type", "") + "." + test.get("method", ""))
    proofs = sum(len(row["requiredProofs"]) for row in ci["rows"].values())
    if (acceptance.get("canonicalFacts") != fact_count or acceptance.get("rows") != len(ci["rows"])
            or acceptance.get("proofs") != proofs or acceptance.get("rollbackComplete") is not True
            or ci["rollback"]["complete"] is not True):
        raise ValueError("Acceptance counts/rollback disagree with retained canonical evidence")
    ordinary, exclusions = {}, []
    namespace = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    expected_paths = {f"Hexalith.Works.{name}/Hexalith.Works.{name}.trx" for name in EXPECTED_ORDINARY}
    if set(ordinary_members) != expected_paths:
        raise ValueError("Ordinary artifact does not contain the four expected TRX inputs")
    for name, expected in EXPECTED_ORDINARY.items():
        path = f"Hexalith.Works.{name}/Hexalith.Works.{name}.trx"
        root = ET.fromstring(ordinary_members[path])
        results = root.findall(".//t:UnitTestResult", namespace)
        outcomes = Counter(result.get("outcome") for result in results)
        counters = root.find(".//t:Counters", namespace)
        if counters is None:
            raise ValueError("Missing ordinary TRX counters: " + name)
        not_runnable = 7 if name == "IntegrationTests" else 0
        if (outcomes != Counter({"Passed": expected, "NotRunnable": not_runnable})
                or int(counters.get("passed", "-1")) != outcomes["Passed"]
                or int(counters.get("total", "-1")) != len(results)
                or int(counters.get("executed", "-1")) != outcomes["Passed"]
                or int(counters.get("notRunnable", "-1")) != not_runnable
                or any(int(counters.get(key, "0")) for key in
                       ["failed", "error", "timeout", "aborted", "inconclusive", "notExecuted",
                        "disconnected", "warning", "passedButRunAborted", "inProgress", "pending"])):
            raise ValueError("Incorrect ordinary counts/outcomes: " + name)
        for result in results:
            if result.get("outcome") == "NotRunnable":
                method = result.get("testName")
                if method not in methods or method in exclusions:
                    raise ValueError("Ordinary exclusion lacks a unique canonical Pass: " + str(method))
                exclusions.append(method)
        ordinary[name] = {"passed": outcomes["Passed"], "notRunnable": outcomes["NotRunnable"],
                          "total": len(results)}
    text = build_log.decode("utf-8")
    compilation = {name: len(re.findall(r"\b" + re.escape(name) + r" -> ", text)) for name in COMPILED_PROJECTS}
    warnings = re.findall(r"(?m)^.*?\b(\d+) Warning\(s\)\s*$", text)
    errors = re.findall(r"(?m)^.*?\b(\d+) Error\(s\)\s*$", text)
    successes = text.count("Build succeeded.")
    if (not successes or len(warnings) != successes or len(errors) != successes
            or any(int(value) for value in warnings + errors)
            or any(value != 1 for value in compilation.values())
            or "MSB3026" in text or "Build FAILED" in text):
        raise ValueError("Compilation/zero-warning/zero-error claim lacks native log support")
    return {"status": "passed", "runs": {name: {key: run[key] for key in
            ["id", "head_sha", "status", "conclusion"]} for name, run in runs.items()},
            "requiredJobs": {name: by_name[name]["conclusion"] for name in sorted(REQUIRED_JOBS)},
            "otherJobs": {name: job["conclusion"] for name, job in by_name.items() if name not in REQUIRED_JOBS},
            "canonicalFacts": fact_count, "rows": len(ci["rows"]), "proofs": proofs,
            "rollbackComplete": ci["rollback"]["complete"], "ordinary": ordinary,
            "ordinaryExclusionsExecutedCanonically": exclusions,
            "build": {"successfulSummaries": successes, "warnings": 0, "errors": 0,
                      "compileOutputCounts": compilation, "copyCollisionCodePresent": False}}


def summary_text(claims):
    """Every numeric/success claim comes from validate_claims, never unchecked prose."""
    ordinary = ", ".join(f"{value['passed']:,} {name}" for name, value in claims["ordinary"].items())
    compilation = ", ".join(f"{name}: {count}" for name, count in claims["build"]["compileOutputCounts"].items())
    return f"""# Corrected hosted evidence summary

Retained native responses confirm {', '.join(claims['runs'])} completed successfully on Works `{WORKS_COMMIT}`. All {len(claims['requiredJobs'])} required CI jobs succeeded. Other job conclusions are recorded in [summary-claims.json](summary-claims.json).

The separately recorded successor audit and native canonical evidence agree on {claims['canonicalFacts']} executed passing facts, {claims['rows']} rows, {claims['proofs']} proofs and complete rollback. Ordinary TRX records {ordinary} passes. Its {len(claims['ordinaryExclusionsExecutedCanonically'])} explicit exclusions each match a canonical Pass. Build logs contain {claims['build']['successfulSummaries']} successful summaries with zero warnings/errors, no MSB3026, and these project compilation output counts: {compilation}.

This is an offline successor audit of retained evidence, not a new qualification run or completion of remaining workflow reviews. The original packets and native inputs remain unchanged. CI runtime binaries are not uploaded for a second local rehash.
"""


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--run", type=Path, default=Path("/tmp/story49-hosted-20261026T215001Z"))
    parser.add_argument("--hosted-packet", type=Path, default=Path(__file__).parent.parent / "ci-build-hosted-qualification-2026-10-26")
    parser.add_argument("--acceptance", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=False)
    try:
        read = sealed_native_reader(args.hosted_packet)
        run = args.run
        latest = json.loads(read(run / "latest.json"))
        final = run / "polls" / latest["at"]
        runs = {name: json.loads(read(run / "run-logs" / str(run_id) / "run.json"))
                for name, run_id in RUN_IDS.items()}
        jobs = json.loads(read(final / "ci-jobs.json"))["jobs"]
        artifacts = {}
        for artifact, digest in ARTIFACTS.items():
            folder = run / "artifacts" / artifact
            artifacts[artifact] = verified_members(folder / "original-artifact.zip", digest, folder / "extracted")
        logs = run / "run-logs/40407181685"
        log_members = verified_members(logs / "original-logs.zip", CI_LOG_SHA256, logs / "extracted")
        build_log = log_members["2_ci _ build-and-test.txt"]
        if read(run / "completed-job-logs/121867993958.log") != build_log:
            raise ValueError("Separately retained build log differs from verified original run ZIP")
        acceptance_raw = args.acceptance.read_bytes()
        claims = validate_claims(runs, jobs, artifacts["12691976576"], build_log,
                                 artifacts["12693567134"], json.loads(acceptance_raw))
        claims["support"] = {"acceptanceSha256": sha256(acceptance_raw),
                             "originalArtifactZips": ARTIFACTS, "originalCiLogsZip": CI_LOG_SHA256,
                             "nativeResponsesBoundToSealedHostedPacket": True}
    except (OSError, ValueError, KeyError, TypeError, ET.ParseError) as error:
        write_json_new(args.output / "summary-claims.json", {"status": "failed", "errors": [str(error)]})
        return 1
    write_json_new(args.output / "summary-claims.json", claims)
    with (args.output / "README.md").open("x", encoding="utf-8") as stream:
        stream.write(summary_text(claims))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
