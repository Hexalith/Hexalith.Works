#!/usr/bin/env python3
"""Verify the reviewed source/package pairs and compile the local Debug solution."""

from __future__ import annotations

import argparse
import contextlib
import hashlib
import json
import os
import signal
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DEPENDENCIES = {
    "references/Hexalith.EventStore": ("HexalithEventStoreVersion", "HexalithEventStoreRoot"),
    "references/Hexalith.PolymorphicSerializations": (
        "HexalithPolymorphicSerializationsVersion", "HexalithPolymorphicSerializationsRoot"),
}


def validate_manifest(manifest):
    """Validate the complete manifest shape before inspecting any external producer."""
    if (not isinstance(manifest, dict) or manifest.get("schemaVersion") != 1
            or not isinstance(manifest.get("dependencies"), dict)
            or set(manifest["dependencies"]) != set(DEPENDENCIES)):
        raise ValueError("Source dependency manifest must name exactly the two governed dependencies")
    for path, (version_property, _) in DEPENDENCIES.items():
        pair = manifest["dependencies"][path]
        if not isinstance(pair, dict):
            raise ValueError(f"{path}: expected a source/package pair object")
        revision = pair.get("sourceRevision", "")
        if (not isinstance(revision, str) or len(revision) != 40
                or any(value not in "0123456789abcdef" for value in revision)):
            raise ValueError(f"{path}: invalid full source revision")
        if (pair.get("releaseProperty") != version_property
                or not isinstance(pair.get("releaseVersion"), str) or not pair["releaseVersion"].strip()):
            raise ValueError(f"{path}: invalid Release property/version")
    return manifest["dependencies"]


def validate_pairs(manifest, gitlinks, checkouts, versions):
    """Refuse independently changed producers instead of assuming source/package equivalence."""
    dependencies = validate_manifest(manifest)
    if not isinstance(versions, dict):
        raise ValueError("Release evaluation must return a property object")
    for path, (version_property, _) in DEPENDENCIES.items():
        pair = dependencies[path]
        revision = pair["sourceRevision"]
        if gitlinks.get(path) != revision or checkouts.get(path) != revision:
            raise ValueError(f"{path}: source revision drift; review the manifest, gitlink and checkout together")
        if versions.get(version_property) != pair["releaseVersion"]:
            raise ValueError(f"{path}: Release version drift; review the supported source/package pair")


def execute(command, root, environment, artifacts, records, timeout):
    """Retain a bounded command's output and exit status, including failed launches/timeouts."""
    log = artifacts / f"command-{len(records) + 1:02}.log"
    record = {"command": command, "timeoutSeconds": timeout, "log": log.name}
    records.append(record)
    try:
        with log.open("wb") as output:
            process = subprocess.Popen(command, cwd=root, env=environment, stdout=output,
                                       stderr=subprocess.STDOUT, start_new_session=os.name == "posix")
            try:
                record["exitCode"] = process.wait(timeout=timeout)
            except subprocess.TimeoutExpired:
                record["timedOut"] = True
                if os.name == "posix":
                    with contextlib.suppress(ProcessLookupError):
                        os.killpg(process.pid, signal.SIGKILL)
                else:
                    try:
                        subprocess.run(["taskkill", "/PID", str(process.pid), "/T", "/F"],
                                       capture_output=True, timeout=5, check=False)
                    finally:
                        if process.poll() is None:
                            process.kill()
                process.wait(timeout=5)
                record["exitCode"] = 124
        if record.get("timedOut"):
            with log.open("a", encoding="utf-8") as output:
                output.write(f"\nCommand exceeded {timeout} seconds; process tree termination requested\n")
    except (OSError, subprocess.TimeoutExpired) as error:
        record["exitCode"] = 124 if isinstance(error, subprocess.TimeoutExpired) else 127
        log.write_text(str(error), encoding="utf-8")
    if record["exitCode"]:
        raise RuntimeError(f"Command failed ({record['exitCode']}): {command!r}; see {log}")
    return log.read_text(encoding="utf-8", errors="replace")


def inspection_environment():
    """Inspect physical worktrees without ambient Git redirection, replacements or lazy fetching."""
    environment = {key: value for key, value in os.environ.items() if not key.upper().startswith("GIT_")}
    return dict(environment, GIT_NO_REPLACE_OBJECTS="1", GIT_NO_LAZY_FETCH="1", GIT_OPTIONAL_LOCKS="0")


def msbuild_escape(value):
    """Escape MSBuild syntax independently of shell argument quoting."""
    return "".join(f"%{ord(character):02X}" if character in "%$@();,?'*" else character for character in str(value))


def source_snapshot(checkout, run):
    """Bind Git-visible tracked and untracked source contents, not only their filenames."""
    prefix = ["git", "-C", str(checkout)]
    patch = run([*prefix, "diff", "--no-ext-diff", "--no-textconv", "--binary", "--no-renames", "HEAD"])
    untracked = run([*prefix, "ls-files", "--others", "--exclude-standard", "-z"])
    return {
        "revision": run([*prefix, "rev-parse", "HEAD"]).strip(),
        "trackedDiffSha256": hashlib.sha256(patch.encode()).hexdigest(),
        "untrackedFiles": {
            path: hashlib.sha256((checkout / path).read_bytes()).hexdigest()
            for path in untracked.split("\0") if path
        },
    }


def verify(root, artifacts, *, check_only=False):
    """Preflight exact identities, then restore and compile all source-mode solution projects."""
    artifacts.mkdir(parents=True, exist_ok=True)
    report = {"status": "running", "configuration": "Release" if check_only else "Debug",
              "mode": "preflight" if check_only else "source-debug", "commands": []}
    result_path = artifacts / "result.json"
    # Invalidate any previous successful run before starting the next command.
    result_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    environment = inspection_environment()
    builds = f"-p:HexalithBuildsRoot={msbuild_escape(root / 'references/Hexalith.Builds')}"

    def run(command, timeout=60):
        return execute(command, root, environment, artifacts, report["commands"], timeout)

    try:
        manifest = json.loads((root / "tools/source-debug-dependencies.json").read_text(encoding="utf-8"))
        validate_manifest(manifest)
        if Path(run(["git", "rev-parse", "--show-toplevel"]).strip()).resolve() != root.resolve():
            raise ValueError("Root Git inspection selected a different worktree")
        gitlinks = {}
        checkouts = {}
        overlays = {}
        for path in DEPENDENCIES:
            entry = run(["git", "ls-files", "--stage", "--", path]).split()
            if len(entry) != 4 or entry[0] != "160000" or entry[2:] != ["0", path]:
                raise ValueError(f"{path}: missing root-declared gitlink")
            gitlinks[path] = entry[1]
            checkout = root / path
            # Do not allow git to discover the parent repository for an uninitialized submodule.
            if not (checkout / ".git").exists():
                raise ValueError(f"{path}: initialize the root dependency before source verification")
            if Path(run(["git", "-C", str(checkout), "rev-parse", "--show-toplevel"]).strip()).resolve() != checkout:
                raise ValueError(f"{path}: Git inspection selected a different worktree")
            checkouts[path] = run(["git", "-C", str(checkout), "rev-parse", "HEAD"]).strip()
            overlays[path] = source_snapshot(checkout, run)
        project = str(root / "src/Hexalith.Works.Contracts/Hexalith.Works.Contracts.csproj")
        release = [builds, "-p:Configuration=Release", "-p:UseHexalithProjectReferences=false", "-p:UseNuGetDeps=true",
                   "-p:HexalithEventStoreFromSource=false", "-p:HexalithPolymorphicSerializationsFromSource=false"]
        properties = ",".join(value[0] for value in DEPENDENCIES.values())
        versions = json.loads(run(["dotnet", "msbuild", project, "-nologo", *release,
                                  f"-getProperty:{properties}"]))["Properties"]
        validate_pairs(manifest, gitlinks, checkouts, versions)
        report.update({"dependencies": manifest["dependencies"], "sourceOverlays": overlays, "gitlinkSurface": "index"})
        if check_only:
            report["status"] = "passed"
            return 0
        solution = str(root / "Hexalith.Works.slnx")
        source = [builds, "-p:UseHexalithProjectReferences=true", "-p:UseNuGetDeps=false",
                  "-p:HexalithEventStoreFromSource=true", "-p:HexalithPolymorphicSerializationsFromSource=true"]
        source += [f"-p:{value[1]}={msbuild_escape(root / path)}" for path, value in DEPENDENCIES.items()]
        run(["dotnet", "restore", solution, "-p:Configuration=Debug", *source], timeout=1800)
        run(["dotnet", "build", solution, "--configuration", "Debug", "--no-restore", "-m:1", *source],
            timeout=1800)
        if {path: source_snapshot(root / path, run) for path in DEPENDENCIES} != overlays:
            raise ValueError("Governed source contents changed during verification; rerun against settled inputs")
        report["status"] = "passed"
        return 0
    except (ValueError, KeyError, OSError, RuntimeError) as error:
        report["status"] = "failed"
        report["error"] = str(error)
        print(error, file=sys.stderr)
        return 1
    finally:
        result_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--artifacts-path", type=Path, default=ROOT / ".artifacts/source-debug")
    parser.add_argument("--check-only", action="store_true",
                        help="Check source/package identities using Release evaluation without restoring or building")
    arguments = parser.parse_args()
    return verify(ROOT, arguments.artifacts_path.resolve(), check_only=arguments.check_only)


if __name__ == "__main__":
    sys.exit(main())
