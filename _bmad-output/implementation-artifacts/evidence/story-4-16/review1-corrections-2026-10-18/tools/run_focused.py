"""Run one live Platform fact against a verifier --prepare-only output, with the verifier's exact runtime environment.

Focused diagnostics only: the canonical verifier run is the sole admission evidence.
Usage: run_focused.py <prepared artifacts> <output dir> <fully qualified method or class> [--class]
"""
import hashlib
import json
import os
import subprocess
import sys
import time
from pathlib import Path

prepared, output, target = Path(sys.argv[1]), Path(sys.argv[2]), sys.argv[3]
by_class = "--class" in sys.argv[4:]
output.mkdir(parents=True, exist_ok=False)
ledger = json.loads((prepared / "ledger.json").read_text())
if ledger.get("status") != "preparation-passed; parity-incomplete":
    raise SystemExit("Preparation did not pass: " + str(ledger.get("status")))
platform = Path("/home/administrator/projects/hexalith/platform")
works = Path("/home/administrator/projects/hexalith/works")
version = ledger["selectedSdk"]["version"]
assembly = Path(ledger["host"]["assembly"])
runtime_output = prepared / "runtime"
runtime_sdk = Path(ledger["sourceProducers"]["worksDebugAndRuntimeSdk"]["root"])
adapter = next(entry for entry in ledger["commands"] if entry["name"] == "works-consumer-release")["command"]
adapter = next(value.split("=", 1)[1] for value in adapter if value.startswith("-p:HexalithAspireHostingDaprVersion="))
environment = os.environ.copy()
environment.update({
    "WORKS_PLATFORM_APPHOST_ASSEMBLY": str(assembly), "WORKS_PLATFORM_APPHOST_SHA256": hashlib.sha256(assembly.read_bytes()).hexdigest(),
    "WORKS_PLATFORM_ROOT": str(platform), "WORKS_PLATFORM_WORKS_ROOT": str(works),
    "Platform__Works__SourceRoot": str(works), "Platform__Identity__Enabled": "false",
    "Version": version, "MinVerVersionOverride": version,
    "ArtifactsPath": str(runtime_output), "UseArtifactsOutput": "true", "WORKS_PLATFORM_RUNTIME_ARTIFACTS": str(runtime_output),
    "UseHexalithProjectReferences": "false", "UseNuGetDeps": "true",
    "HexalithEventStoreRoot": str(runtime_sdk), "HexalithEventStoreVersion": version, "HexalithAspireHostingDaprVersion": adapter,
    "Platform__Works__EventStoreSourceRoot": str(runtime_sdk), "WORKS_PLATFORM_SDK_SOURCE_ROOT": str(runtime_sdk),
    "WORKS_PLATFORM_HOST_EVIDENCE": str(output / "host-starts.jsonl"),
    "WORKS_SDK_PROOF_EVIDENCE_PATH": str(output / "persisted-states.jsonl"), "WORKS_APPHOST_TEST_STARTUP_TIMEOUT_SECONDS": "180"})
executable = prepared / "consumer/bin/Hexalith.Works.IntegrationTests/release/Hexalith.Works.IntegrationTests"
command = [str(executable), "-noLogo", "-class" if by_class else "-method", target, "-result-xml", str(output / "result.xml")]
started = time.time()
with (output / "test.log").open("w") as log:
    code = subprocess.run(command, cwd=works, env=environment, stdout=log, stderr=subprocess.STDOUT).returncode
(output / "run.json").write_text(json.dumps({"command": command, "cwd": str(works), "exitCode": code,
                                             "seconds": round(time.time() - started, 1), "prepared": str(prepared)}, indent=2) + "\n")
print(json.dumps({"exitCode": code, "seconds": round(time.time() - started, 1)}))
