"""Run the three existing Works-preview composition checks against the qualified isolated Platform graph.

Usage: run_composition.py <platform root> <bound Builds source> <SDK source> <evidence directory>
"""
import json
import os
from pathlib import Path
import subprocess
import sys
from datetime import datetime, timezone

platform, builds, sdk, evidence = (Path(value).resolve() for value in sys.argv[1:])
evidence.mkdir(parents=True, exist_ok=False)
output = platform / "artifacts/story416-review6-composition"
environment = os.environ.copy()
environment.update(HexalithBuildsRoot=str(builds), HexalithEventStoreRoot=str(sdk))
commands = [
    ["dotnet", "build", "tests/Hexalith.Platform.Composition.Tests/Hexalith.Platform.Composition.Tests.csproj",
     "-c", "Release", "--artifacts-path", str(output), "-m:1", "-p:TreatWarningsAsErrors=true", "-p:NuGetAudit=false",
     "-p:Version=3.126.0", "-p:MinVerVersionOverride=3.126.0", "-p:UseHexalithProjectReferences=false", "-p:UseNuGetDeps=true"],
    [str(output / "bin/Hexalith.Platform.Composition.Tests/release/Hexalith.Platform.Composition.Tests"), "-noLogo",
     "-method", "Hexalith.Platform.Composition.Tests.PlatformAppModelTests.WorksPreview*",
     "-method", "Hexalith.Platform.Composition.Tests.TimesheetsHostBindingTests.FileEntryRefusesMixedWorksAndTimesheetsBeforeSelectingEitherGraph", "-result-xml", str(evidence / "results.xml")],
]
results = []
for index, command in enumerate(commands):
    record = {"command": command, "cwd": str(platform), "startedAt": datetime.now(timezone.utc).isoformat()}
    with (evidence / (str(index) + ".log")).open("w") as log:
        result = subprocess.run(command, cwd=platform, env=environment, stdout=log, stderr=subprocess.STDOUT)
    record.update(exitCode=result.returncode, completedAt=datetime.now(timezone.utc).isoformat())
    results.append(record)
    (evidence / "commands.json").write_text(json.dumps(results, indent=2) + "\n")
    if result.returncode:
        raise SystemExit(result.returncode)
sys.path.insert(0, str(platform / "eng"))
from verify_works_host import require_passed_tests
summary = require_passed_tests(evidence / "results.xml", required_methods=[
    "WorksPreviewPreservesItsMissingSiblingRefusal", "WorksPreviewComposesItsPreservedGraphWithoutDeclarationServers",
    "FileEntryRefusesMixedWorksAndTimesheetsBeforeSelectingEitherGraph"])
(evidence / "result.json").write_text(json.dumps(summary, indent=2) + "\n")
print(json.dumps(summary))
