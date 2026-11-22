#!/usr/bin/env python3
"""Recheck a retained Platform proof's source inventories and recorded auxiliary inputs.

This supersedes the recorded-path-only review6 packet helper. It qualifies current
input equality, never the historical execution or current live Platform behavior.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import stat
import tempfile
from collections.abc import Iterator
from datetime import UTC, datetime
from pathlib import Path

BUILD_OUTPUTS = {"bin", "obj"}
FIXTURE_PROJECTS = ("Hexalith.Works.IntegrationTests", "Hexalith.Works.Testing")
FIXTURE_FILES = ("xunit.runner.json", "src/Hexalith.Works.AppHost/ProjectMetadataPaths.cs")
WORKS_BUILD_FILES = ("Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props")


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def inventory(root: Path, kind: str) -> dict[str, str]:
    """Use the canonical verifier's Platform, Works consumer and fixture scopes."""
    if kind == "fixture":
        for name in FIXTURE_PROJECTS:
            if (root / "tests" / name).is_symlink():
                raise ValueError(f"Fixture input root cannot be a symbolic link: {root / 'tests' / name}")
        paths = [
            path
            for name in FIXTURE_PROJECTS
            for path in (root / "tests" / name).rglob("*")
        ]
        paths += [root / name for name in FIXTURE_FILES]
    else:
        if (root / "src").is_symlink():
            raise ValueError(f"Consumer input root cannot be a symbolic link: {root / 'src'}")
        paths = list((root / "src").rglob("*"))
        paths += (
            [root / "apphost.cs"]
            if kind == "platform"
            else [root / name for name in WORKS_BUILD_FILES]
        )
    result = {}
    for path in sorted(paths):
        relative = path.relative_to(root)
        excluded = BUILD_OUTPUTS | {"__pycache__", "TestResults"} if kind == "fixture" else BUILD_OUTPUTS
        if excluded.intersection(relative.parts):
            continue
        if path.is_symlink():
            raise ValueError(f"Proof input cannot be a symbolic link: {path}")
        if path.is_file() and (kind == "fixture" or path.parent == root or path.suffix in (".cs", ".csproj")):
            result[relative.as_posix()] = digest(path)
    return result


def differences(expected: dict[str, str], actual: dict[str, str]) -> list[dict[str, str]]:
    """Compare the union, so newly added inputs cannot escape the check."""
    return [
        {"path": name, "reason": "added" if name not in expected else "missing" if name not in actual else "changed"}
        for name in sorted(expected.keys() | actual.keys())
        if expected.get(name) != actual.get(name)
    ]


def recorded_inputs(
    ledger: dict, isolation: dict, platform: Path, current_platform: Path,
) -> Iterator[tuple[str, Path, str]]:
    """Yield validated auxiliary paths and digests shared by the check and output guard."""
    for item in ledger["rows"]["R10"]["configurationConsumers"]:
        yield ("isolatedConfiguration", owned_path(platform, item["path"]), item["sha256"])
        yield ("currentConfiguration", owned_path(current_platform, item["path"]), item["sha256"])
    for relative, expected in isolation["overlays"].items():
        yield ("isolatedReviewedOverlay", owned_path(platform, relative), expected)
        yield ("currentReviewedOverlay", owned_path(current_platform, relative), expected)
    yield ("isolatedVerifier", owned_path(platform, "eng/verify_works_host.py"), ledger["verifierSha256"])
    for relative, expected in ledger["verifierHelpers"].items():
        yield ("isolatedVerifierHelper", owned_path(platform, relative, "eng"), expected)
        yield ("currentVerifierHelper", owned_path(current_platform, relative, "eng"), expected)


def owned_path(root: Path, relative: str, subdirectory: str = "") -> Path:
    if Path(relative).is_absolute() or ".." in Path(relative).parts:
        raise ValueError(f"Recorded input must be relative to its owner: {relative}")
    path = root / subdirectory / relative
    if not path.resolve().is_relative_to(root.resolve()):
        raise ValueError(f"Recorded input resolves outside its owner: {path}")
    return path


def directory_identity(directory: Path) -> tuple[int, int]:
    metadata = directory.stat()
    return metadata.st_dev, metadata.st_ino


def report_path(
    output: Path, platform: Path, works: Path, current_platform: Path, inputs: list[Path],
) -> tuple[Path, tuple[int, int]]:
    """Reserve inventoried trees and exact inputs, including absent recorded files."""
    destination = output.parent.resolve() / output.name
    trees = [
        *(root / "src" for root in (platform, works, current_platform)),
        *(works / "tests" / name for name in FIXTURE_PROJECTS),
    ]
    files = [
        platform / "apphost.cs", current_platform / "apphost.cs",
        *(works / name for name in (*WORKS_BUILD_FILES, *FIXTURE_FILES)), *inputs,
    ]
    if any(destination.is_relative_to(tree.resolve()) for tree in trees) or any(
        destination == path.resolve() for path in files
    ):
        raise ValueError("Output must be separate from proof inputs and inventoried source directories.")
    return destination, directory_identity(destination.parent)


def write_report(destination: tuple[Path, tuple[int, int]], result: dict) -> None:
    """Replace an ordinary report without truncating any existing inode."""
    output, parent_identity = destination

    def require_same_directory() -> None:
        if output.parent.resolve() != output.parent or directory_identity(output.parent) != parent_identity:
            raise ValueError("Report directory changed during the input comparison.")

    require_same_directory()
    try:
        existing = output.lstat()
    except FileNotFoundError:
        pass
    else:
        if not stat.S_ISREG(existing.st_mode) or existing.st_nlink != 1:
            raise ValueError("Output must be an ordinary file without symbolic or hard links.")
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(mode="w", encoding="utf-8", dir=output.parent, delete=False) as report:
            temporary = Path(report.name)
            report.write(json.dumps(result, indent=2) + "\n")
        require_same_directory()
        temporary.replace(output)
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)


def check_inputs(
    ledger: dict, isolation: dict, platform: Path, works: Path, current_platform: Path,
    recorded: list[tuple[str, Path, str]],
) -> dict:
    for root in (platform, works, current_platform):
        if not root.is_dir() or root.is_symlink():
            raise ValueError(f"Proof input root must be an ordinary directory: {root}")
    mismatches = []

    def compare(kind: str, expected: dict[str, str], actual: dict[str, str]) -> None:
        mismatches.extend({"kind": kind, **row} for row in differences(expected, actual))

    compare("isolatedPlatformConsumer", ledger["platformConsumerSources"], inventory(platform, "platform"))
    compare("currentWorksConsumer", ledger["worksConsumerSource"]["sourceFiles"], inventory(works, "works"))
    compare("currentWorksFixture", ledger["fixtureSources"], inventory(works, "fixture"))
    current_differences = differences(ledger["platformConsumerSources"], inventory(current_platform, "platform"))
    for kind, path, expected in recorded:
        if path.is_symlink():
            raise ValueError(f"Proof input cannot be a symbolic link: {path}")
        actual = digest(path) if path.is_file() else None
        if actual != expected:
            mismatches.append({"kind": kind, "path": str(path), "reason": "missing" if actual is None else "changed"})
    return {
        "checkedAt": datetime.now(UTC).isoformat(),
        "isolatedPlatformBaseCommit": isolation["platformBaseCommit"],
        "roots": {
            "isolatedPlatform": str(platform.resolve()),
            "works": str(works.resolve()),
            "currentPlatform": str(current_platform.resolve()),
        },
        "mismatches": mismatches,
        "currentPlatformDifferences": current_differences,
        "scope": "Complete isolated Platform C#/project inventory, current Works C#/project/build inventory "
        "and Works fixture inventory; recorded configuration, overlays, verifier and helpers. "
        "Current Platform source differences are reported separately and remain unqualified.",
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("ledger", type=Path)
    parser.add_argument("isolation", type=Path)
    parser.add_argument("output", type=Path, help="Report path outside inventoried source trees and proof inputs.")
    parser.add_argument("--current-platform-root", type=Path, required=True)
    args = parser.parse_args()
    helper = Path(__file__).resolve()
    metadata_inputs = [args.ledger, args.isolation, helper]
    destination = None
    try:
        isolation_bytes = args.isolation.read_bytes()
        isolation = json.loads(isolation_bytes)
        platform, works = Path(isolation["platform"]), Path(isolation["works"])
        ledger_bytes = args.ledger.read_bytes()
        ledger = json.loads(ledger_bytes)
        recorded = list(recorded_inputs(ledger, isolation, platform, args.current_platform_root))
        destination = report_path(
            args.output, platform, works, args.current_platform_root,
            [*metadata_inputs, *(path for _, path, _ in recorded)],
        )
        result = check_inputs(ledger, isolation, platform, works, args.current_platform_root, recorded)
        result["inputs"] = {
            "ledger": {"path": str(args.ledger.resolve()), "sha256": hashlib.sha256(ledger_bytes).hexdigest()},
            "isolation": {"path": str(args.isolation.resolve()), "sha256": hashlib.sha256(isolation_bytes).hexdigest()},
            "helper": {"path": str(helper), "sha256": digest(helper)},
        }
        write_report(destination, result)
        print(
            json.dumps(
                {"mismatches": result["mismatches"], "currentPlatformDifferences": result["currentPlatformDifferences"]}
            )
        )
        return int(bool(result["mismatches"]))
    except (OSError, ValueError, KeyError, TypeError, AttributeError, RuntimeError) as error:
        refusal = {
            "checkedAt": datetime.now(UTC).isoformat(),
            "error": str(error),
            "mismatches": [{"kind": "inputValidation", "reason": str(error)}],
            "scope": "Input recheck refused; no input comparison qualified.",
        }
        if destination is None:
            parser.exit(2, f"Proof input recheck refused: {error}; no fresh report was written.\n")
        try:
            write_report(destination, refusal)
        except (OSError, ValueError) as output_error:
            parser.exit(2, f"Proof input recheck refused: {error}; cannot write refusal: {output_error}\n")
        parser.exit(2, f"Proof input recheck refused: {error}\n")


if __name__ == "__main__":
    raise SystemExit(main())
