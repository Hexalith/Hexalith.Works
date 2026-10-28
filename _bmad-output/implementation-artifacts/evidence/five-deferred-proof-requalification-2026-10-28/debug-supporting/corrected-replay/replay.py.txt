"""Build and execute isolated Debug baseline and mutation conformance controls.

Usage: python3 replay.py <works-root> <new-temporary-output-directory>
Both builds use explicit Debug/source flags and separate artifact roots. The
ordinary Integration output's complete bytes and membership are compared before
and after replay; no preexisting assembly is executed by this script.
"""

import ast
import collections
import hashlib
import json
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
from pathlib import Path

SOURCE_FLAGS = [
    "-p:UseHexalithProjectReferences=true",
    "-p:UseNuGetDeps=false",
    "-p:HexalithEventStoreFromSource=true",
    "-p:HexalithPolymorphicSerializationsFromSource=true",
]
METHOD = (
    "Hexalith.Works.IntegrationTests.WorksPlatformIdentityFixtureTests."
    "PublishedClientReadsCurrentSyntheticEvidenceAndAnonymousTransportIsDenied"
)
MARKER = "controlled-debug-runtime-mutation"


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n")


def inventory(directory):
    """Include all ordinary output file bytes, directories and their membership."""
    if not directory.is_dir() or directory.is_symlink():
        raise ValueError("The ordinary output must be an existing real directory")
    result = {}
    for path in sorted(directory.rglob("*")):
        name = path.relative_to(directory).as_posix()
        if path.is_symlink():
            raise ValueError("Runtime output cannot be a symbolic link: " + name)
        if path.is_file():
            result[name] = {"kind": "file", "sha256": sha256(path)}
        elif path.is_dir():
            result[name] = {"kind": "directory"}
        else:
            raise ValueError("Unsupported runtime output entry: " + name)
    return result


def mutation_target(original, mutated):
    """Use an XML writer so both quote forms and XML metacharacters survive."""
    project = ET.Element("Project")
    target = ET.SubElement(project, "Target", {
        "Name": "ApplyControlledWorksDebugMutation",
        "BeforeTargets": "CoreCompile",
        "Condition": "'$(MSBuildProjectName)' == 'Hexalith.Works.IntegrationTests'",
    })
    items = ET.SubElement(target, "ItemGroup")
    ET.SubElement(items, "Compile", {"Remove": "WorksPlatformIdentityFixtureTests.cs"})
    ET.SubElement(items, "Compile", {"Remove": str(original)})
    ET.SubElement(items, "Compile", {
        "Include": str(mutated), "Link": "WorksPlatformIdentityFixtureTests.cs",
    })
    return ET.tostring(project, encoding="unicode") + "\n"


def target_parse_controls():
    result = []
    for value in ("single'quote", 'double"quote', 'both\'"and&<tags>'):
        original = "/source/" + value + "/WorksPlatformIdentityFixtureTests.cs"
        mutated = "/temporary/" + value + "/WorksPlatformIdentityFixtureTests.cs"
        generated = mutation_target(original, mutated)
        parsed = ET.fromstring(generated)
        compile_items = parsed.findall("./Target/ItemGroup/Compile")
        if compile_items[1].attrib["Remove"] != original or compile_items[2].attrib["Include"] != mutated:
            raise RuntimeError("Quoted mutation target path did not round-trip")
        result.append({"original": original, "mutated": mutated, "xml": generated, "passed": True})
    return result


def run(root, output, label, command, expected=0):
    write_json(output / (label + ".command.json"), {"command": command, "cwd": str(root)})
    started = time.monotonic()
    with (output / (label + ".log")).open("w") as log:
        completed = subprocess.run(
            command, cwd=root, stdout=log, stderr=subprocess.STDOUT, timeout=600
        )
    write_json(output / (label + ".result.json"), {
        "exitCode": completed.returncode, "durationSeconds": time.monotonic() - started,
    })
    if completed.returncode != expected:
        raise RuntimeError(label + " returned " + str(completed.returncode))


def build(root, output, label, artifacts, targets=None):
    command = [
        "dotnet", "build", str(root / "tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj"),
        "--configuration", "Debug", "--artifacts-path", str(artifacts), "-m:1", "--nologo", *SOURCE_FLAGS,
        "-p:HexalithEventStoreRoot=" + str(root / "references/Hexalith.EventStore"),
        "-p:HexalithPolymorphicSerializationsRoot=" + str(root / "references/Hexalith.PolymorphicSerializations"),
    ]
    if targets is not None:
        command += ["-p:CustomAfterMicrosoftCommonTargets=" + str(targets)]
    run(root, output, label, command)
    return artifacts / "bin/Hexalith.Works.IntegrationTests/debug/Hexalith.Works.IntegrationTests.dll"


def require_method(xml, result):
    assembly = ET.parse(xml).getroot().find("assembly")
    tests = list(assembly.iter("test"))
    if len(tests) != 1 or tests[0].attrib["type"] + "." + tests[0].attrib["method"] != METHOD:
        raise RuntimeError("The exact selected method did not execute")
    if tests[0].attrib["result"] != result:
        raise RuntimeError("Selected method had the wrong result")
    if any(assembly.attrib[name] != "0" for name in ("errors", "skipped", "not-run")):
        raise RuntimeError("Method execution has errors, skips or unexecuted tests")
    return assembly


def main():
    root, output = Path(sys.argv[1]).resolve(), Path(sys.argv[2]).resolve()
    packet = Path(__file__).resolve().parent
    output.mkdir(parents=True, exist_ok=False)
    write_json(output / "target-parse-controls.json", target_parse_controls())
    selector = root / "references/Hexalith.Platform/eng/verify_works_host.py"
    module = ast.parse(selector.read_text())
    classes = next(
        ast.literal_eval(node.value) for node in module.body
        if isinstance(node, ast.Assign)
        and any(isinstance(target, ast.Name) and target.id == "SUPPORTING_CLASSES" for target in node.targets)
    )
    if list(classes) != json.loads((packet / "supporting-classes.json").read_text()):
        raise RuntimeError("Current verifier supporting classes differ from this packet")
    ordinary = root / "tests/Hexalith.Works.IntegrationTests/bin/Debug/net10.0"
    original_inventory = inventory(ordinary)
    write_json(output / "ordinary-before.json", original_inventory)
    original = root / "tests/Hexalith.Works.IntegrationTests/WorksPlatformIdentityFixtureTests.cs"
    if sha256(original) != sha256(packet / "original-source.cs.txt"):
        raise RuntimeError("Mutation source changed; reassess the control before replay")
    original_source_sha256 = sha256(original)
    write_json(output / "inputs.json", {
        "replaySha256": sha256(Path(__file__)), "selectorSha256": sha256(selector),
        "classes": list(classes), "method": METHOD, "sourceFlags": SOURCE_FLAGS,
        "originalSourceSha256": original_source_sha256,
    })
    dll = build(root, output, "baseline-build", output / "baseline-artifacts")
    write_json(output / "baseline-runtime-sha256.json", inventory(dll.parent))
    command = ["dotnet", str(dll), "-noLogo", "-explicit", "off"]
    for name in classes:
        command += ["-class", "Hexalith.Works.IntegrationTests." + name]
    command += ["-result-xml", str(output / "supporting.xml")]
    run(root, output, "supporting", command)
    assembly = ET.parse(output / "supporting.xml").getroot().find("assembly")
    counts = collections.Counter(test.attrib["type"] for test in assembly.iter("test"))
    selected = {"Hexalith.Works.IntegrationTests." + name for name in classes}
    if set(counts) != selected or any(
        assembly.attrib[name] != "0" for name in ("errors", "failed", "skipped", "not-run")
    ):
        raise RuntimeError("The complete supporting selection did not pass")
    run(root, output, "baseline-method", [
        "dotnet", str(dll), "-noLogo", "-method", METHOD,
        "-result-xml", str(output / "baseline-method.xml"),
    ])
    require_method(output / "baseline-method.xml", "Pass")
    mutated = output / original.name
    mutated.write_bytes((packet / "mutated-source.cs.txt").read_bytes())
    targets = output / "mutation.targets"
    targets.write_text(mutation_target(original, mutated))
    mutant_dll = build(root, output, "mutation-build", output / "mutation-artifacts", targets)
    write_json(output / "mutant-runtime-sha256.json", inventory(mutant_dll.parent))
    run(root, output, "mutation", [
        "dotnet", str(mutant_dll), "-noLogo", "-method", METHOD,
        "-result-xml", str(output / "mutation.xml"),
    ], expected=1)
    require_method(output / "mutation.xml", "Fail")
    if MARKER not in (output / "mutation.xml").read_text():
        raise RuntimeError("Mutation failure was unrelated to the Debug-only assertion")
    final_inventory = inventory(ordinary)
    write_json(output / "ordinary-after.json", final_inventory)
    if original_inventory != final_inventory:
        raise RuntimeError("Ordinary Debug output bytes or complete membership changed during replay")
    if sha256(original) != original_source_sha256:
        raise RuntimeError("The tracked mutation-source input changed during replay")
    write_json(output / "summary.json", {
        "supporting": dict(assembly.attrib), "classCounts": dict(sorted(counts.items())),
        "baselineAssemblySha256": sha256(dll), "mutantAssemblySha256": sha256(mutant_dll),
        "baselineMethod": "1 passed", "mutantMethod": "1 expected failure; " + MARKER,
        "ordinaryOutputInventoryUnchanged": True, "ordinaryOutputEntries": len(original_inventory),
        "originalSourceUnchanged": True, "targetParseControls": "3 passed",
    })
    print(
        "Isolated Debug supporting selection passed; isolated Debug-only mutation detected; "
        "ordinary output inventory unchanged."
    )


if __name__ == "__main__":
    main()
