"""Diagnostic-only rerun of one live fact with a completed verifier run's exact runtime environment."""
import json, os, subprocess, sys
from pathlib import Path
run, out, method = Path(sys.argv[1]), Path(sys.argv[2]), sys.argv[3]
ledger = json.loads((run / "ledger.json").read_text())
env = os.environ.copy(); env.update(ledger["runtimeEnvironment"])
env["WORKS_PLATFORM_HOST_EVIDENCE"] = str(out / "host-starts.jsonl")
env["WORKS_SDK_PROOF_EVIDENCE_PATH"] = str(out / "persisted-states.jsonl")
exe = Path(os.environ.get("DIAG_CONSUMER", str(run / "consumer"))) / "bin/Hexalith.Works.IntegrationTests/release/Hexalith.Works.IntegrationTests"
cmd = [str(exe), "-noLogo", "-method", method, "-result-xml", str(out / "result.xml")]
(out / "command.json").write_text(json.dumps({"command": cmd, "cwd": "/home/administrator/projects/hexalith/works", "scope": "diagnostic only; never qualifies acceptance"}, indent=2))
with (out / "run.log").open("w") as log:
    code = subprocess.run(cmd, cwd="/home/administrator/projects/hexalith/works", env=env, stdout=log, stderr=subprocess.STDOUT).returncode
print("exit", code)
