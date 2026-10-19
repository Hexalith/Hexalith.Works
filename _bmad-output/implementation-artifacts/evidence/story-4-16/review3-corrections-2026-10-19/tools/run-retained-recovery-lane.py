"""Story 4.16 review iteration 3 (T159): the retained Works AppHost's own live recovery lane on the final bytes.

Usage: python3 run-retained-recovery-lane.py <empty-output-directory>

Adapted from the public 3.125.1 packet's run-source-debug-lane.py. The lane builds Works and the exact isolated v3.125.1
SDK export in Debug source mode into owned output, then runs the whole WorksSdkRecoveryPipelineSmokeTests class once
with the default (retained) host selection. It never selects the Platform target, never edits a checkout and refuses
skipped, failed or missing tests; both live facts must execute and pass.
"""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import time
import xml.etree.ElementTree as ET

WORKS = Path("/home/administrator/projects/hexalith/works")
SDK = Path("/tmp/story416-sdk-3.125.1-public-source")
VERSION = "3.125.1"
CLASS = "Hexalith.Works.IntegrationTests.WorksSdkRecoveryPipelineSmokeTests"
LIVE = {"SdkRemindersAndCascadeConvergeThroughPersistedReceiptsAcrossRestart",
        "SuccessfulCallbacksRegisterSdkReminderAndProcessWithoutPeriodicRecovery"}


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def inventory(root):
    files = [path for path in (root / "src").rglob("*") if path.is_file() and path.suffix in (".cs", ".csproj")
             and not {"bin", "obj"}.intersection(path.relative_to(root).parts)]
    files += [root / name for name in ("Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props")]
    return hashlib.sha256(json.dumps({str(path.relative_to(root)): sha(path) for path in sorted(files)}, sort_keys=True).encode()).hexdigest()


def fixtures():
    """Every IntegrationTests C# source outside build outputs: the shared fixture and harness bytes this lane runs."""
    root = WORKS / "tests/Hexalith.Works.IntegrationTests"
    return {str(path.relative_to(WORKS)): sha(path) for path in sorted(root.rglob("*.cs"))
            if not {"bin", "obj"}.intersection(path.relative_to(root).parts)}


def main():
    out = Path(sys.argv[1]).resolve()
    if out.exists() and any(out.iterdir()):
        raise SystemExit("Output directory must be empty")
    out.mkdir(parents=True, exist_ok=True)
    runtime = out / "runtime"
    results = {"scope": "Story 4.16 review iteration 3 T159: retained Works AppHost live recovery lane on the final fixture and harness bytes",
               "producer": {"version": VERSION, "tag": "v" + VERSION, "root": str(SDK), "inventorySha256": inventory(SDK)},
               "worksHead": subprocess.check_output(["git", "-C", str(WORKS), "rev-parse", "HEAD"], text=True).strip(),
               "worksInventorySha256": inventory(WORKS), "fixtureSources": fixtures(), "commands": []}

    def save():
        (out / "results.json").write_text(json.dumps(results, indent=2) + "\n")

    def run(name, command, environment=None, xml=None):
        started = time.time()
        with (out / (name + ".log")).open("w") as log:
            code = subprocess.run(command, cwd=WORKS, env=environment, stdout=log, stderr=subprocess.STDOUT, timeout=3600).returncode
        entry = {"name": name, "command": [str(value) for value in command], "exitCode": code,
                 "seconds": round(time.time() - started, 3), "logSha256": sha(out / (name + ".log"))}
        if xml is not None and Path(xml).is_file():
            assembly = ET.parse(xml).getroot().find("assembly")
            entry["tests"] = {key: assembly.get(key) for key in ("total", "passed", "failed", "errors", "skipped", "not-run", "time")}
            entry["xmlSha256"] = sha(xml)
            entry["executed"] = [(test.get("method"), test.get("result")) for test in ET.parse(xml).getroot().iter("test")]
        results["commands"].append(entry)
        save()
        if code:
            raise RuntimeError(name + " exited " + str(code))
        if xml is not None:
            tests = entry.get("tests") or {}
            passed = {method for method, result in entry.get("executed", []) if result == "Pass"}
            if tests.get("total") != tests.get("passed") or not LIVE.issubset(passed) \
                    or any(tests.get(key, "0") != "0" for key in ("failed", "errors", "skipped", "not-run")):
                raise RuntimeError(name + " did not execute and pass every fact, including both live facts")

    properties = ["-p:Version=" + VERSION, "-p:MinVerVersionOverride=" + VERSION, "-p:NuGetAudit=false",
                  "-p:TreatWarningsAsErrors=true", "-p:UseHexalithProjectReferences=true", "-p:UseNuGetDeps=false",
                  "-p:HexalithEventStoreRoot=" + str(SDK), "-p:HexalithEventStoreVersion=" + VERSION, "-m:1"]
    try:
        consumer = out / "consumer"
        run("consumer-debug-build", ["dotnet", "build", "tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj",
                                     "-c", "Debug", "--artifacts-path", str(consumer), *properties])
        projects = [WORKS / "src/Hexalith.Works/Hexalith.Works.csproj",
                    *[SDK / "src" / name / (name + ".csproj") for name in
                      ("Hexalith.EventStore", "Hexalith.EventStore.Operations", "Hexalith.EventStore.Admin.Server.Host")],
                    WORKS / "src/Hexalith.Works.AppHost/Hexalith.Works.AppHost.csproj"]
        for index, project in enumerate(projects):
            run("runtime-debug-prebuild-" + str(index), ["dotnet", "build", str(project), "-c", "Debug", "--artifacts-path", str(runtime), *properties])
        results["runtimeArtifacts"] = {str(path): sha(path) for project in projects
                                       for path in sorted((runtime / "bin" / project.stem / "debug").glob("Hexalith*.dll"))}
        executable = consumer / "bin/Hexalith.Works.IntegrationTests/debug/Hexalith.Works.IntegrationTests"
        results["consumerArtifacts"] = {str(path): sha(path) for path in sorted(executable.parent.glob("Hexalith*.dll"))}
        save()
        environment = {key: value for key, value in os.environ.items() if not key.startswith("WORKS_PLATFORM_APPHOST")}
        environment.update({"WORKS_APPHOST_TEST_STARTUP_TIMEOUT_SECONDS": "180", "WORKS_PLATFORM_WORKS_ROOT": str(WORKS),
                            "WORKS_PLATFORM_SDK_SOURCE_ROOT": str(SDK), "HexalithEventStoreRoot": str(SDK),
                            "HexalithEventStoreVersion": VERSION, "Version": VERSION, "MinVerVersionOverride": VERSION,
                            "UseHexalithProjectReferences": "true", "UseNuGetDeps": "false",
                            "ArtifactsPath": str(runtime), "UseArtifactsOutput": "true", "WORKS_PLATFORM_RUNTIME_ARTIFACTS": str(runtime)})
        results["environment"] = {key: environment[key] for key in sorted(environment) if key.startswith(("WORKS_", "Hexalith", "Version", "MinVer", "Use", "Artifacts"))}
        evidence = out / "retained-recovery-states.jsonl"
        xml = out / "retained-recovery.xml"
        run("retained-recovery", [executable, "-noLogo", "-class", CLASS, "-result-xml", xml],
            environment | {"WORKS_SDK_PROOF_EVIDENCE_PATH": str(evidence)}, xml)
        results["commands"][-1]["phases"] = [json.loads(line)["phase"] for line in evidence.read_text().splitlines() if line] if evidence.is_file() else []
        save()
        for path, digest in {**results["runtimeArtifacts"], **results["consumerArtifacts"]}.items():
            if sha(path) != digest:
                raise RuntimeError("A qualified Debug artifact changed during execution: " + path)
        if inventory(SDK) != results["producer"]["inventorySha256"] or inventory(WORKS) != results["worksInventorySha256"] \
                or fixtures() != results["fixtureSources"]:
            raise RuntimeError("A qualified source graph changed during execution")
        results["status"] = "passed"
        return 0
    except Exception as error:  # Record every failure exactly; never relabel it as a pass.
        results["status"] = "failed"
        results["failure"] = str(error)
        return 1
    finally:
        save()
        print(json.dumps({"status": results.get("status"), "failure": results.get("failure")}), flush=True)


if __name__ == "__main__":
    raise SystemExit(main())
