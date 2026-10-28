"""Replay the three retired P1R tooling dispositions against exact owner Git trees."""

import argparse
import json
import os
import subprocess
from pathlib import Path

CURRENT = "36c78f3435d2d492a73f1b0707cf865c8735d740"
RETIRED = "6a0b6441f6d78a3007678a1d0ec336e9cb8ff961"
PARENT = "b7ba9bb1fdff85fb47817d35506bd6ca81b23d70"
PREFIX = "_bmad-output/implementation-artifacts/evidence/6-1-p1r-3109/"
PATHS = {
    "consumer/Consumer.csproj": "58a8bab61dab098137e9a0674be1f4b3e4534598",
    "rollback-probe/v3109/Probe.csproj": "0820d77ddaadfcb8dcfb85e15fcd7fbd7c00e7c8",
    "rollback-probe/v370/Probe.csproj": "a868b2f067c524fa279bcba83eeeff046dd2e137",
    "verify_public_packages.py": "0a962bf97d34f2505010cf1f7a0b8fb8276ffcf1",
    "rollback-probe/Program.cs": "d506a6a853683311f45b1ae65f5b115a5f87cf9d",
}


def verify(workspace):
    owner = workspace / "references/Hexalith.EventStore"
    commands = []
    environment = {key: value for key, value in os.environ.items() if not key.upper().startswith("GIT_")}
    environment.update(GIT_NO_REPLACE_OBJECTS="1", GIT_NO_LAZY_FETCH="1", GIT_OPTIONAL_LOCKS="0")

    def git(*arguments):
        command = ["git", "-C", str(owner), *arguments]
        result = subprocess.run(command, capture_output=True, text=True, timeout=60,
                                env=environment, check=False)
        commands.append({"arguments": list(arguments), "exitCode": result.returncode,
                         "stdout": result.stdout, "stderr": result.stderr})
        if result.returncode:
            raise ValueError(f"Git command failed: {command!r}: {result.stderr}")
        return result.stdout.strip()

    if not (owner / ".git").exists() or git("rev-parse", "HEAD") != CURRENT:
        raise ValueError("Replay requires the recorded initialized owner checkout")
    if Path(git("rev-parse", "--show-toplevel")).resolve() != owner.resolve():
        raise ValueError("Git selected a different owner worktree")
    if git("rev-parse", f"{RETIRED}^") != PARENT:
        raise ValueError("Retirement parent changed")
    git("merge-base", "--is-ancestor", RETIRED, CURRENT)
    expected = {PREFIX + path: blob for path, blob in PATHS.items()}
    selected = git("ls-tree", "-r", PARENT, "--", *expected)
    actual = {}
    for line in selected.splitlines():
        mode, kind, blob, path = line.split(maxsplit=3)
        if mode != "100644" or kind != "blob":
            raise ValueError(f"Unexpected historical tree entry: {line}")
        actual[path] = blob
    if actual != expected:
        raise ValueError("Historical parent paths/blob identities do not match")
    for revision in (RETIRED, CURRENT):
        if git("ls-tree", "-r", revision, "--", *expected):
            raise ValueError(f"Retired machinery still exists at {revision}")
    if any((owner / path).exists() for path in expected):
        raise ValueError("Retired machinery has reappeared in the consumed checkout")
    return {"status": "passed", "ownerRevision": CURRENT, "retirementRevision": RETIRED,
            "parentRevision": PARENT, "retiredPaths": expected, "commands": commands,
            "scope": "Retirement only; no current public-package/runtime qualification"}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--workspace", type=Path, default=Path(__file__).resolve().parents[4])
    arguments = parser.parse_args()
    print(json.dumps(verify(arguments.workspace.resolve()), indent=2))


if __name__ == "__main__":
    main()
