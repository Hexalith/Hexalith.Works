#!/usr/bin/env python3
"""Recheck a retained Platform proof's source inventories and recorded auxiliary inputs.

This supersedes the recorded-path-only review6 packet helper. It qualifies current
input equality, never the historical execution or current live Platform behavior.
"""

from __future__ import annotations

import argparse
import hashlib
import json
from datetime import UTC, datetime
from pathlib import Path

BUILD_OUTPUTS = {"bin", "obj"}


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def inventory(root: Path, kind: str) -> dict[str, str]:
    """Use the canonical verifier's Platform, Works consumer and fixture scopes."""
    if kind == "fixture":
        for name in ("Hexalith.Works.IntegrationTests", "Hexalith.Works.Testing"):
            if (root / "tests" / name).is_symlink():
                raise ValueError(f"Fixture input root cannot be a symbolic link: {root / 'tests' / name}")
        paths = [
            path
            for name in ("Hexalith.Works.IntegrationTests", "Hexalith.Works.Testing")
            for path in (root / "tests" / name).rglob("*")
        ]
        paths += [root / "xunit.runner.json", root / "src/Hexalith.Works.AppHost/ProjectMetadataPaths.cs"]
    else:
        if (root / "src").is_symlink():
            raise ValueError(f"Consumer input root cannot be a symbolic link: {root / 'src'}")
        paths = list((root / "src").rglob("*"))
        paths += (
            [root / "apphost.cs"]
            if kind == "platform"
            else [
                root / name for name in ("Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props")
            ]
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


def check_inputs(ledger: dict, isolation: dict, current_platform: Path) -> dict:
    platform, works = Path(isolation["platform"]), Path(isolation["works"])
    for root in (platform, works, current_platform):
        if not root.is_dir() or root.is_symlink():
            raise ValueError(f"Proof input root must be an ordinary directory: {root}")
    mismatches = []

    def compare(kind: str, expected: dict[str, str], actual: dict[str, str]) -> None:
        mismatches.extend({"kind": kind, **row} for row in differences(expected, actual))

    def recorded(kind: str, root: Path, relative: str, expected: str) -> None:
        path = root / relative
        if Path(relative).is_absolute() or ".." in Path(relative).parts:
            raise ValueError(f"Recorded input must be relative to its owner: {relative}")
        if path.is_symlink():
            raise ValueError(f"Proof input cannot be a symbolic link: {path}")
        actual = digest(path) if path.is_file() else None
        if actual != expected:
            mismatches.append({"kind": kind, "path": str(path), "reason": "missing" if actual is None else "changed"})

    compare("isolatedPlatformConsumer", ledger["platformConsumerSources"], inventory(platform, "platform"))
    compare("currentWorksConsumer", ledger["worksConsumerSource"]["sourceFiles"], inventory(works, "works"))
    compare("currentWorksFixture", ledger["fixtureSources"], inventory(works, "fixture"))
    current_differences = differences(ledger["platformConsumerSources"], inventory(current_platform, "platform"))
    for item in ledger["rows"]["R10"]["configurationConsumers"]:
        recorded("isolatedConfiguration", platform, item["path"], item["sha256"])
        recorded("currentConfiguration", current_platform, item["path"], item["sha256"])
    for relative, expected in isolation["overlays"].items():
        recorded("isolatedReviewedOverlay", platform, relative, expected)
        recorded("currentReviewedOverlay", current_platform, relative, expected)
    recorded("isolatedVerifier", platform, "eng/verify_works_host.py", ledger["verifierSha256"])
    for relative, expected in ledger["verifierHelpers"].items():
        recorded("isolatedVerifierHelper", platform / "eng", relative, expected)
        recorded("currentVerifierHelper", current_platform / "eng", relative, expected)
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
    parser.add_argument("output", type=Path)
    parser.add_argument("--current-platform-root", type=Path, required=True)
    args = parser.parse_args()
    helper = Path(__file__).resolve()
    if args.output.resolve() in (args.ledger.resolve(), args.isolation.resolve(), helper):
        parser.exit(2, "Proof input recheck refused: output must be separate from its inputs and helper.\n")
    try:
        ledger_bytes, isolation_bytes = args.ledger.read_bytes(), args.isolation.read_bytes()
        result = check_inputs(json.loads(ledger_bytes), json.loads(isolation_bytes), args.current_platform_root)
        result["inputs"] = {
            "ledger": {"path": str(args.ledger.resolve()), "sha256": hashlib.sha256(ledger_bytes).hexdigest()},
            "isolation": {"path": str(args.isolation.resolve()), "sha256": hashlib.sha256(isolation_bytes).hexdigest()},
            "helper": {"path": str(helper), "sha256": digest(helper)},
        }
        args.output.write_text(json.dumps(result, indent=2) + "\n")
        print(
            json.dumps(
                {"mismatches": result["mismatches"], "currentPlatformDifferences": result["currentPlatformDifferences"]}
            )
        )
        return int(bool(result["mismatches"]))
    except (OSError, ValueError, KeyError, TypeError, AttributeError) as error:
        refusal = {
            "checkedAt": datetime.now(UTC).isoformat(),
            "error": str(error),
            "mismatches": [{"kind": "inputValidation", "reason": str(error)}],
            "scope": "Input recheck refused; no input comparison qualified.",
        }
        try:
            args.output.write_text(json.dumps(refusal, indent=2) + "\n")
        except OSError as output_error:
            parser.exit(2, f"Proof input recheck refused: {error}; cannot write refusal: {output_error}\n")
        parser.exit(2, f"Proof input recheck refused: {error}\n")


if __name__ == "__main__":
    raise SystemExit(main())
