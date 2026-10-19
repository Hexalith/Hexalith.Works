"""Diagnostic-only rerun with extra ASP.NET Core logging; never qualifies acceptance."""
import json, os, subprocess, sys, threading, time, shutil, glob
from pathlib import Path
run, out, method = Path(sys.argv[1]), Path(sys.argv[2]), sys.argv[3]
ledger = json.loads((run / "ledger.json").read_text())
env = os.environ.copy(); env.update(ledger["runtimeEnvironment"])
env["WORKS_PLATFORM_HOST_EVIDENCE"] = str(out / "host-starts.jsonl")
env["WORKS_SDK_PROOF_EVIDENCE_PATH"] = str(out / "persisted-states.jsonl")
for key in ["Microsoft.AspNetCore", "Microsoft.AspNetCore.Authentication", "Microsoft.AspNetCore.Authorization", "Microsoft.AspNetCore.Server.Kestrel", "Microsoft.AspNetCore.Diagnostics", "Microsoft.AspNetCore.RateLimiting"]:
    env["Logging__LogLevel__" + key] = "Debug"
exe = Path(os.environ.get("DIAG_CONSUMER", str(run / "consumer"))) / "bin/Hexalith.Works.IntegrationTests/release/Hexalith.Works.IntegrationTests"
cmd = [str(exe), "-noLogo", "-method", method, "-result-xml", str(out / "result.xml")]
(out / "command.json").write_text(json.dumps({"command": cmd, "extraEnv": {k: v for k, v in env.items() if k.startswith("Logging__")}, "scope": "diagnostic only"}, indent=2))
before = set(glob.glob("/tmp/aspire-dcp*"))
proc = subprocess.Popen(cmd, cwd="/home/administrator/projects/hexalith/works", env=env, stdout=(out / "run.log").open("w"), stderr=subprocess.STDOUT)
dcp = out / "dcp"; dcp.mkdir(exist_ok=True)
while proc.poll() is None:
    for d in set(glob.glob("/tmp/aspire-dcp*")) - before:
        for f in glob.glob(d + "/*_out") + glob.glob(d + "/*_err") + glob.glob(d + "/resource-executable-*.log"):
            try: shutil.copy(f, dcp / (Path(d).name + "-" + Path(f).name))
            except OSError: pass
    time.sleep(5)
print("exit", proc.returncode)
