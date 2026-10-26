import importlib.util
import json
import subprocess
import sys
from pathlib import Path
works=Path(sys.argv[2]).resolve()
spec=importlib.util.spec_from_file_location("works_ci", works / "scripts/verify-platform-parity-ci.py")
coordinator=importlib.util.module_from_spec(spec)
spec.loader.exec_module(coordinator)
artifacts=Path(sys.argv[1]).resolve()
command, environment, record, snapshot=coordinator.bootstrap(works,artifacts)
command[command.index("--profile")+1]="full"
(artifacts / "full-invocation.json").write_text(json.dumps({"command":command,"environmentPins":{k:v for k,v in environment.items() if k.startswith("Hexalith") or k=="DOTNET_USE_POLLING_FILE_WATCHER"}},indent=2)+"\n")
result=subprocess.run(command,env=environment)
if result.returncode:
    raise SystemExit(result.returncode)
if snapshot.origin_files(Path(record["platformOrigin"])) != record["platformSourceFiles"]:
    raise ValueError("Platform source changed during full qualification")
coordinator.require_unchanged_coordinators(record["coordinatorSha256"])
record["status"]="passed-disposable-synthetic-parity-and-rollback"
record["profile"]="full"
(artifacts / "bootstrap.json").write_text(json.dumps(record,indent=2)+"\n")
