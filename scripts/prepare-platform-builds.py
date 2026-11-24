#!/usr/bin/env python3
"""Bind Platform's test-only build dependency to its verified published archive."""

import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile


def prepare(root):
    owner = root / "references/Hexalith.Platform"
    builds = root / "references/Hexalith.Builds"

    # Git inspection must refer to these physical checkouts, even from a redirected caller.
    environment = os.environ.copy()
    for name in tuple(environment):
        if name.startswith("GIT_"):
            environment.pop(name)
    environment.update(GIT_NO_REPLACE_OBJECTS="1", GIT_NO_LAZY_FETCH="1", GIT_OPTIONAL_LOCKS="0")

    def owner_revision():
        return subprocess.check_output(["git", "-C", str(owner), "rev-parse", "--verify", "HEAD"],
                                       env=environment).rstrip(b"\n")

    def owner_is_clean():
        status = subprocess.check_output(["git", "-C", str(owner), "status", "--porcelain", "--untracked-files=all"],
                                         env=environment)
        if status:
            return False
        entries = subprocess.check_output(["git", "-C", str(owner), "ls-files", "-v"], env=environment)
        return re.search(rb"(^|\n)[a-zS] ", entries) is None

    revision = owner_revision()
    if not owner_is_clean():
        raise ValueError("Published Builds preparation requires clean recorded Platform source.")

    temp = os.environ.get("RUNNER_TEMP") or os.environ.get("TMPDIR") or "/tmp"
    artifacts = Path(tempfile.mkdtemp(prefix="works-published-builds.", dir=temp))
    workspace = artifacts / "workspace"
    workspace.mkdir()
    platform = workspace / "Hexalith.Platform"
    try:
        # Use the immutable published producer in a separate checkout. Keep the current
        # package catalog and all nested dependencies unchanged.
        subprocess.run(["git", "-c", "submodule.recurse=false", "clone", "--quiet", "--shared", "--no-checkout",
                        "--", str(owner), str(platform)], env=environment, check=True)
        subprocess.run(["git", "-C", str(platform), "-c", "submodule.recurse=false", "checkout", "--quiet",
                        "--detach", os.fsdecode(revision)], env=environment, check=True)
        subprocess.run(["python3", str(platform / "eng/bound_builds.py"), "--platform-root", str(platform),
                        "--source-root", str(builds), "--output", str(workspace / "Hexalith.Builds")],
                       env=environment, stdout=subprocess.DEVNULL, check=True)
        published = subprocess.check_output(
            ["python3", str(platform / "eng/prepare_builds_artifacts.py"), "--require-recorded",
             "--output", str(artifacts / "verified")], env=environment).rstrip(b"\n")
        tool_root = os.fsdecode(published)
        if (not tool_root or "\n" in tool_root or "\r" in tool_root
                or not (Path(tool_root) / "Hexalith.Builds.Tooling.dll").is_file()):
            raise ValueError("Platform did not return a verified published Builds tool directory.")
        if owner_revision() != revision or not owner_is_clean():
            raise ValueError("Platform source changed during published Builds preparation; rerun on settled inputs.")

        export = os.environ.get("GITHUB_ENV")
        if export:
            with open(export, "ab") as output:
                output.write(b"HexalithBuildsPublishedToolRoot=" + published + b"\n")
        sys.stdout.buffer.write(published + b"\n")
    finally:
        shutil.rmtree(workspace)


if __name__ == "__main__":
    try:
        prepare(Path(__file__).absolute().parent.parent)
    except subprocess.CalledProcessError as error:
        raise SystemExit(error.returncode if error.returncode >= 0 else 128 - error.returncode)
    except FileNotFoundError as error:
        print(error, file=sys.stderr)
        raise SystemExit(127)
    except (OSError, ValueError) as error:
        print(error, file=sys.stderr)
        raise SystemExit(1)
