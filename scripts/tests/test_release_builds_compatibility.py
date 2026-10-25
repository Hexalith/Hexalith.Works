"""Compare the effective build steps in the caller's exact CI and release producers."""

from __future__ import annotations

import re
import subprocess
import textwrap
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
BUILDS = ROOT / "references" / "Hexalith.Builds"


def git(*arguments: str) -> str:
    """Read committed Builds inputs without fetching or changing the checkout."""

    return subprocess.run(
        ["git", "-C", str(BUILDS), *arguments],
        check=True, capture_output=True, text=True, timeout=15,
    ).stdout


def revision(text: str, action: str) -> str:
    """Resolve one immutable action reference from its actual consumer."""

    matches = re.findall(re.escape(f"Hexalith/Hexalith.Builds/{action}@") + r"([0-9a-f]{40})\b", text)
    if len(matches) != 1:
        raise ValueError(f"Expected one immutable reference for {action}")
    canonical = git("rev-parse", "--verify", matches[0] + "^{commit}").strip()
    if canonical != matches[0]:
        raise ValueError("Action reference is not the canonical full revision")
    return canonical


def step(text: str, name: str) -> str:
    """Extract one YAML step at its own indentation, rejecting ambiguous names."""

    matches = list(re.finditer(r"(?m)^( +)- name: " + re.escape(name) + r"\s*$", text))
    if len(matches) != 1:
        raise ValueError(f"Expected one step named {name}")
    match = matches[0]
    end = re.search(r"(?m)^" + re.escape(match[1]) + r"- ", text[match.end():])
    return text[match.end():match.end() + end.start()] if end else text[match.end():]


def field(text: str, name: str) -> str:
    matches = re.findall(r"(?m)^ +" + re.escape(name) + r": ([^\n]+)$", text)
    if len(matches) != 1:
        raise ValueError(f"Expected one scalar field {name}")
    return matches[0].strip()


def input_default(text: str, name: str) -> str:
    """Resolve the default behind a shared action/workflow input expression."""

    match = re.search(r"(?m)^( +)" + re.escape(name) + r":\s*$", text)
    if match is None:
        raise ValueError(f"Missing input {name}")
    end = re.search(r"(?m)^" + re.escape(match[1]) + r"\S", text[match.end():])
    block = text[match.end():match.end() + end.start()] if end else text[match.end():]
    return field(block, "default").strip("'\"")


def mapping(text: str, name: str, indent: int) -> str:
    """Read one specifically scoped mapping in these committed workflow shapes."""

    match = re.search(r"(?m)^" + " " * indent + re.escape(name) + r":\s*$", text)
    if match is None:
        raise ValueError(f"Missing scoped mapping {name}")
    end = re.search(r"(?m)^ {0," + str(indent) + r"}\S", text[match.end():])
    return text[match.end():match.end() + end.start()] if end else text[match.end():]


def enabled(block: str, condition: str | None = None) -> None:
    """Refuse disabled or failure-ignoring execution even when both sides agree."""

    for key, value in re.findall(r"(?m)^ +(if|continue-on-error): ([^\n]+)$", block):
        if key == "if" and value.strip() == condition:
            continue
        if key == "continue-on-error" and value.strip() == "false":
            continue
        raise ValueError("incompatible execution activation")


def normalized(block: str, values: dict[str, str]) -> tuple[str, ...]:
    for name, value in values.items():
        block = block.replace("${{ inputs." + name + " }}", value)
    return tuple(line.rstrip() for line in textwrap.dedent(block).splitlines()
                 if line.strip() and not line.lstrip().startswith("#") and line.strip() != "shell: bash")


def build_contract(text: str, initialization: str, values: dict[str, str]) -> tuple:
    """Compare complete selected steps, including their execution context and order."""

    header = text.split("    steps:", 1)[0]
    enabled(header)
    if re.search(r"(?m)^(env|defaults):|^    (env|defaults):", header):
        raise ValueError("incompatible inherited producer execution context")
    names = ("Initialize root-declared submodules", "Initialize .NET", "Restore", "Build")
    positions = [text.index("- name: " + name + "\n") for name in names]
    if positions != sorted(positions):
        raise ValueError("incompatible build step order")
    enabled(step(initialization, "Initialize root-declared submodules"))
    blocks = []
    for name in names[1:]:
        block = step(text, name)
        enabled(block)
        blocks.append(normalized(block, values))
    return (initialization, tuple(blocks))


def require_compatible(ci: str, release: str, ci_init: str, release_init: str,
                       ci_caller: str, release_caller: str) -> None:
    ci_call = mapping(ci_caller, "ci", 2)
    release_call = step(release_caller, "Prepare domain release")
    release_job = mapping(release_caller, "release", 2).split("    steps:", 1)[0]
    for block in (ci_call.split("    with:", 1)[0], release_call, release_job):
        enabled(block)
    for workflow in (ci_caller, release_caller):
        if re.search(r"(?m)^(env|defaults):", workflow):
            raise ValueError("incompatible inherited caller execution context")
    if re.search(r"(?m)^    (env|defaults):", ci_call + release_job):
        raise ValueError("incompatible inherited job execution context")
    values = []
    for producer, caller in ((ci, ci_call), (release, release_call)):
        supplied = mapping(caller, "with", len(re.search(r"(?m)^( +)with:", caller)[1]))
        resolved = {}
        for name in ("solution", "dotnet-global-json"):
            resolved[name] = (field(supplied, name) if re.search(r"(?m)^ +" + name + ": ", supplied)
                              else input_default(producer, name))
        values.append(resolved)
    if values[0] != values[1]:
        raise ValueError("incompatible effective caller inputs")
    ci_inputs = mapping(ci_call, "with", 4)
    governed = (field(ci_inputs, "governed-ci") if re.search(r"(?m)^ +governed-ci: ", ci_inputs)
                else input_default(ci, "governed-ci"))
    if governed != "false":
        raise ValueError("incompatible governed CI route")
    enabled(step(ci, "Initialize root-declared submodules"), "${{ !inputs.governed-ci }}")
    enabled(step(release, "Initialize root-declared submodules"))
    checkout = step(release, "Checkout approved Builds actions")
    enabled(checkout)
    expected = {"uses": field(step(release_caller, "Check out dispatched source"), "uses"),
                "repository": "Hexalith/Hexalith.Builds", "path": ".hexalith/builds-execution",
                "ref": "${{ inputs.builds-execution-sha }}", "submodules": "false",
                "persist-credentials": "false"}
    if any(field(checkout, name) != value for name, value in expected.items()):
        raise ValueError("incompatible approved Builds checkout")
    if (release.index("- name: Checkout approved Builds actions")
            > release.index("- name: Initialize root-declared submodules")):
        raise ValueError("incompatible checkout order")
    if field(step(release, "Initialize root-declared submodules"), "uses") != (
        "./.hexalith/builds-execution/Github/initialize-build"
    ):
        raise ValueError("incompatible release initialization")
    if field(release_call, "builds-execution-sha") != revision(release_call, "Github/prepare-domain-release"):
        raise ValueError("incompatible release execution identity")
    if build_contract(ci, ci_init, values[0]) != build_contract(release, release_init, values[1]):
        raise ValueError("CI and release preparation have incompatible effective build inputs")


class ReleaseBuildsCompatibilityTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        ci_caller = (ROOT / ".github/workflows/ci.yml").read_text(encoding="utf-8")
        release_caller = (ROOT / ".github/workflows/release.yml").read_text(encoding="utf-8")
        cls.ci_caller, cls.release_caller = ci_caller, release_caller
        cls.ci_revision = revision(ci_caller, ".github/workflows/domain-ci.yml")
        cls.release_revision = revision(release_caller, "Github/prepare-domain-release")
        cls.ci_workflow = git("show", cls.ci_revision + ":.github/workflows/domain-ci.yml")
        job = re.search(r"(?ms)^  build-and-test:\n(.*?)(?=^  [\w-]+:\n|\Z)", cls.ci_workflow)
        if job is None:
            raise ValueError("CI build-and-test job is missing")
        cls.ci = cls.ci_workflow.split("\njobs:\n", 1)[0] + job[1]
        cls.release = git("show", cls.release_revision + ":Github/prepare-domain-release/action.yml")
        cls.ci_init_revision = revision(
            step(cls.ci, "Initialize root-declared submodules"), "Github/initialize-build",
        )
        cls.ci_init = git("show", cls.ci_init_revision + ":Github/initialize-build/action.yml")
        cls.release_init = git("show", cls.release_revision + ":Github/initialize-build/action.yml")

    def compatible(self, **changes: str) -> None:
        arguments = {name: getattr(self, name) for name in
                     ("ci", "release", "ci_init", "release_init", "ci_caller", "release_caller")}
        require_compatible(**(arguments | changes))

    def test_exact_committed_ci_and_release_builds_are_compatible(self) -> None:
        self.compatible()

    def test_different_sdk_action_is_rejected(self) -> None:
        sdk = field(step(self.release, "Initialize .NET"), "uses")
        changed = self.release.replace(sdk, "actions/setup-dotnet@" + "0" * 40, 1)
        with self.assertRaisesRegex(ValueError, "incompatible"):
            self.compatible(release=changed)

    def test_different_sdk_pin_input_is_rejected(self) -> None:
        changed = self.release.replace(
            "global-json-file: ${{ inputs.dotnet-global-json }}", "global-json-file: other-global.json", 1,
        )
        with self.assertRaisesRegex(ValueError, "incompatible"):
            self.compatible(release=changed)

    def test_different_sdk_default_is_rejected(self) -> None:
        changed = self.release.replace("default: global.json", "default: other-global.json", 1)
        with self.assertRaisesRegex(ValueError, "incompatible"):
            self.compatible(release=changed)

    def test_different_build_arguments_and_output_are_rejected(self) -> None:
        build = field(step(self.release, "Build"), "run")
        for replacement in (build.replace("Release", "Debug"), build + " --output other-output",
                            build.replace("-warnaserror", ""), build.replace("--no-restore", "")):
            with self.subTest(command=replacement), self.assertRaisesRegex(ValueError, "incompatible"):
                self.compatible(release=self.release.replace(build, replacement, 1))

    def test_different_restore_sources_are_rejected(self) -> None:
        restore = field(step(self.release, "Restore"), "run")
        changed = self.release.replace(restore, restore + " --source unapproved-feed", 1)
        with self.assertRaisesRegex(ValueError, "incompatible"):
            self.compatible(release=changed)

    def test_different_dependency_initialization_is_rejected(self) -> None:
        changed = self.release_init.replace("submodule update --init", "submodule update --init --remote", 1)
        self.assertNotEqual(self.release_init, changed)
        with self.assertRaisesRegex(ValueError, "incompatible"):
            self.compatible(release_init=changed)
    def test_step_activation_and_context_changes_are_rejected(self) -> None:
        for addition in ("if: false", "continue-on-error: true", "working-directory: other",
                         "env:\n        MSBuildSDKsPath: other"):
            changed = self.release.replace("    - name: Build\n", "    - name: Build\n      " + addition + "\n", 1)
            with self.subTest(change=addition), self.assertRaisesRegex(ValueError, "incompatible"):
                self.compatible(release=changed)

    def test_setup_dotnet_override_is_rejected(self) -> None:
        changed = self.release.replace("global-json-file:", "dotnet-version: 9.0.x\n        global-json-file:", 1)
        with self.assertRaisesRegex(ValueError, "incompatible"):
            self.compatible(release=changed)

    def test_build_before_restore_is_rejected(self) -> None:
        changed = self.release.replace("- name: Restore\n", "- name: Temporary\n", 1)
        changed = changed.replace("- name: Build\n", "- name: Restore\n", 1)
        changed = changed.replace("- name: Temporary\n", "- name: Build\n", 1)
        with self.assertRaisesRegex(ValueError, "incompatible"):
            self.compatible(release=changed)

    def test_checkout_identity_and_activation_changes_are_rejected(self) -> None:
        for before, after in (("repository: Hexalith/Hexalith.Builds", "repository: Other/Builds"),
                              ("path: .hexalith/builds-execution", "path: other"),
                              ("ref: ${{ inputs.builds-execution-sha }}", "ref: main"),
                              ("- name: Checkout approved Builds actions\n",
                               "- name: Checkout approved Builds actions\n      if: false\n")):
            with self.subTest(change=after), self.assertRaisesRegex(ValueError, "incompatible"):
                self.compatible(release=self.release.replace(before, after, 1))

    def test_effective_caller_solution_and_sdk_overrides_are_compared(self) -> None:
        for caller in ("ci_caller", "release_caller"):
            text = getattr(self, caller)
            for addition in ("solution: Other.slnx", "dotnet-global-json: other-global.json"):
                if addition.startswith("dotnet-global-json"):
                    changed = re.sub(r"(?m)^( +)solution: Hexalith.Works.slnx$",
                                     lambda match, item=addition: match[0] + "\n" + match[1] + item, text, count=1)
                else:
                    changed = text.replace("solution: Hexalith.Works.slnx", addition, 1)
                with self.subTest(caller=caller, change=addition), self.assertRaisesRegex(ValueError, "incompatible"):
                    self.compatible(**{caller: changed})

    def test_equal_explicit_caller_inputs_remain_compatible(self) -> None:
        callers = {}
        for name in ("ci_caller", "release_caller"):
            callers[name] = re.sub(
                r"(?m)^( +)solution: Hexalith.Works.slnx$",
                lambda match: match[1] + "solution: Other.slnx\n" + match[1] + "dotnet-global-json: other.json",
                getattr(self, name), count=1,
            )
        self.compatible(**callers)

    def test_inherited_and_caller_activation_context_is_rejected(self) -> None:
        mutations = {
            "ci": self.ci.replace("    runs-on:", "    env:\n      MSBuildSDKsPath: other\n    runs-on:", 1),
            "ci_caller": self.ci_caller.replace("  ci:\n", "  ci:\n    if: false\n", 1),
            "release_caller": self.release_caller.replace(
                "- name: Prepare domain release\n", "- name: Prepare domain release\n        if: false\n", 1,
            ),
        }
        for name, changed in mutations.items():
            with self.subTest(context=name), self.assertRaisesRegex(ValueError, "incompatible"):
                self.compatible(**{name: changed})



if __name__ == "__main__":
    unittest.main()
