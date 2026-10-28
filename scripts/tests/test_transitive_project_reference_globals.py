"""Keep SDK-added source references in the same build context as their parent edges."""

import json
import os
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
PIN = ET.parse(ROOT / "Directory.Packages.props").findtext(".//HexalithEventStoreVersion")


class TransitiveProjectReferenceGlobalsTests(unittest.TestCase):
    def test_sdk_added_source_edges_preserve_selected_globals_in_both_configurations(self):
        for configuration in ("Debug", "Release"):
            for explicit_version in (False, True):
                with self.subTest(configuration=configuration, explicit_version=explicit_version):
                    self.verify_graph(configuration, explicit_version)

    def verify_graph(self, configuration, explicit_version):
        with tempfile.TemporaryDirectory(prefix="works-transitive-globals-") as directory:
            root = Path(directory)
            works = root / "works"
            external = root / "external"
            feed = root / "feed"
            for path in (works, external, feed):
                path.mkdir()
            for filename in ("Directory.Build.props", "Directory.Build.targets", "global.json"):
                shutil.copyfile(ROOT / filename, works / filename)
            (works / "Directory.Packages.props").write_text(
                f"<Project><PropertyGroup><HexalithEventStoreVersion>{PIN}</HexalithEventStoreVersion>"
                "</PropertyGroup></Project>")
            (external / "Directory.Build.props").write_text(
                "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>")
            for parent in (works, external):
                (parent / "NuGet.Config").write_text(
                    '<configuration><packageSources><clear /><add key="offline" '
                    f'value="{feed}" /></packageSources></configuration>')
            leaf = external / "Hexalith.SourceLeaf.csproj"
            leaf.write_text('<Project Sdk="Microsoft.NET.Sdk" />')
            middle = external / "Hexalith.SourceParent.csproj"
            middle.write_text('<Project Sdk="Microsoft.NET.Sdk"><ItemGroup>'
                              f'<ProjectReference Include="{leaf}" /></ItemGroup></Project>')
            project = works / "Hexalith.Works.GraphProbe.csproj"
            project.write_text('<Project Sdk="Microsoft.NET.Sdk"><ItemGroup>'
                               '<PackageReference Remove="MinVer" />'
                               f'<ProjectReference Include="{middle}" /></ItemGroup></Project>')
            environment = {
                key: value for key, value in os.environ.items()
                if not key.casefold().startswith("hexalith")
                and key.casefold() not in {"configuration", "usehexalithprojectreferences", "usenugetdeps",
                                           "worksallowlocaleventstorecandidate", "restoreadditionalprojectsources"}
            }
            common = ["dotnet", "msbuild", str(project), "-nologo", f"-p:Configuration={configuration}",
                      "-p:NuGetAudit=false"]
            if explicit_version:
                common.append(f"-p:HexalithEventStoreVersion={PIN}")
            restore = subprocess.run([*common, "-t:Restore"], cwd=works, env=environment,
                                     capture_output=True, text=True, timeout=60, check=False)
            self.assertEqual(restore.returncode, 0, restore.stdout + restore.stderr)
            query = subprocess.run([*common, "-t:IncludeTransitiveProjectReferences", "-getItem:ProjectReference"],
                                   cwd=works, env=environment, capture_output=True, text=True,
                                   timeout=60, check=False)
            self.assertEqual(query.returncode, 0, query.stdout + query.stderr)
            references = json.loads(query.stdout)["Items"]["ProjectReference"]
            selected = {Path(item["FullPath"]): item for item in references}
            self.assertIn(middle, selected)
            self.assertIn(leaf, selected, "The real SDK must add the transitive reference during target execution")
            expected = {"HexalithEventStoreVersion": PIN, "Configuration": configuration,
                        "ShouldUnsetParentConfigurationAndPlatform": "false"}
            for path in (middle, leaf):
                values = dict(part.split("=", 1) for part in selected[path].get("AdditionalProperties", "").split(";")
                              if "=" in part)
                for name, value in expected.items():
                    self.assertEqual(values.get(name), value, f"{path.name} lost {name} after SDK expansion")


class ParallelSolutionNodeIdentityTests(unittest.TestCase):
    def test_parallel_diamond_builds_each_canonical_project_once_in_both_configurations(self):
        for configuration in ("Debug", "Release"):
            for explicit_version in (False, True):
                with (self.subTest(configuration=configuration, explicit_version=explicit_version),
                      tempfile.TemporaryDirectory(prefix="works-parallel-node-identity-") as directory):
                    self.verify_solution(Path(directory), configuration, explicit_version)

    def test_node_identity_oracle_refuses_missing_transitive_property_forwarding(self):
        with tempfile.TemporaryDirectory(prefix="works-mutated-node-identity-") as directory:
            # Serializing this negative avoids compiler/output races between the deliberately duplicated
            # nodes. The four positive lanes above exercise actual parallel solution builds.
            records, projects, _ = self.build_solution(
                Path(directory), "Debug", explicit_version=False, remove_forwarding=True)
            duplicates = [path for path in projects if len(records[path]) > 1]
            self.assertTrue(duplicates, "Removing the real target must reproduce distinct build contexts")
            with self.assertRaisesRegex(AssertionError, "expected one build execution"):
                self.assert_node_identity(records, projects, "Debug")

    def verify_solution(self, root, configuration, explicit_version):
        records, projects, outputs = self.build_solution(root, configuration, explicit_version)
        self.assert_node_identity(records, projects, configuration)
        for output in outputs:
            self.assertTrue(output.is_file(), f"The real solution build must compile {output}")

    def assert_node_identity(self, records, projects, configuration):
        self.assertEqual(set(records), set(projects), "Every expected canonical project must execute")
        for project in projects:
            self.assertEqual(len(records[project]), 1, f"{project}: expected one build execution; {records[project]}")
            expected = [configuration, PIN, "false"]
            self.assertEqual(records[project][0], expected, f"{project}: selected build properties diverged")

    def build_solution(self, root, configuration, explicit_version, remove_forwarding=False):
        works, external, feed, audit = [root / name for name in ("works", "external", "feed", "audit")]
        for path in (works, external, feed, audit):
            path.mkdir()
        for filename in ("Directory.Build.props", "Directory.Build.targets", "global.json"):
            shutil.copyfile(ROOT / filename, works / filename)
        self.write_xml(works / "Directory.Packages.props", ET.fromstring(
            f"<Project><PropertyGroup><HexalithEventStoreVersion>{PIN}</HexalithEventStoreVersion>"
            "</PropertyGroup></Project>"))
        self.write_xml(external / "Directory.Build.props", ET.fromstring(
            "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>"))
        for owner in (works, external):
            config = ET.Element("configuration")
            sources = ET.SubElement(config, "packageSources")
            ET.SubElement(sources, "clear")
            ET.SubElement(sources, "add", key="offline", value=str(feed))
            self.write_xml(owner / "NuGet.Config", config)
            targets = ET.parse(owner / "Directory.Build.targets").getroot() if owner == works else ET.Element("Project")
            if remove_forwarding and owner == works:
                forwarding = targets.find("Target[@Name='ForwardWorksTransitiveProjectReferenceProperties']")
                self.assertIsNotNone(forwarding, "The negative must remove the actual forwarding guard")
                targets.remove(forwarding)
            target = ET.SubElement(targets, "Target", Name="RecordBuildNodeIdentity", BeforeTargets="PrepareForBuild")
            ET.SubElement(target, "WriteLinesToFile", File=str(audit / "$(MSBuildProjectName).txt"),
                          Lines="$(MSBuildProjectFullPath)|$(Configuration)|$(HexalithEventStoreVersion)|"
                                "$(ShouldUnsetParentConfigurationAndPlatform)", Overwrite="false")
            self.write_xml(owner / "Directory.Build.targets", targets)

        leaf = external / "Leaf" / "Hexalith.SourceLeaf.csproj"
        middle = external / "Middle" / "Hexalith.SourceMiddle.csproj"
        left = external / "Left" / "Hexalith.SourceLeft.csproj"
        right = external / "Right" / "Hexalith.SourceRight.csproj"
        first = works / "First" / "Hexalith.Works.First.csproj"
        second = works / "Second" / "Hexalith.Works.Second.csproj"
        graph = {leaf: (), middle: (leaf,), left: (middle,), right: (middle,),
                 first: (second, left), second: (right,)}
        for path, references in graph.items():
            project = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
            items = ET.SubElement(project, "ItemGroup")
            if path in (first, second):
                ET.SubElement(items, "PackageReference", Remove="MinVer")
            for reference in references:
                ET.SubElement(items, "ProjectReference", Include=str(reference))
            self.write_xml(path, project)
        solution = ET.Element("Solution")
        for path in (first, second):
            ET.SubElement(solution, "Project", Path=str(path))
        solution_path = works / "Probe.slnx"
        self.write_xml(solution_path, solution)
        environment = {
            key: value for key, value in os.environ.items()
            if not key.casefold().startswith("hexalith")
            and key.casefold() not in {"configuration", "usehexalithprojectreferences", "usenugetdeps",
                                       "worksallowlocaleventstorecandidate", "restoreadditionalprojectsources"}
        }
        common = ["dotnet", "msbuild", str(solution_path), "-nologo", "-nr:false",
                  f"-p:Configuration={configuration}", "-p:NuGetAudit=false", "-p:UseSharedCompilation=false"]
        if explicit_version:
            common.append(f"-p:HexalithEventStoreVersion={PIN}")
        restore = subprocess.run([*common, "-t:Restore"], cwd=works, env=environment,
                                 capture_output=True, text=True, timeout=60, check=False)
        self.assertEqual(restore.returncode, 0, restore.stdout + restore.stderr)
        for record in audit.glob("*.txt"):
            record.unlink()
        parallelism = ["-m:1", "-p:BuildInParallel=false"] if remove_forwarding else ["-m:4", "-p:BuildInParallel=true"]
        build = subprocess.run([*common, "-t:Build", *parallelism], cwd=works, env=environment,
                               capture_output=True, text=True, timeout=60, check=False)
        self.assertEqual(build.returncode, 0, build.stdout + build.stderr)
        records = {}
        for record in audit.glob("*.txt"):
            for line in record.read_text().splitlines():
                project, *properties = line.split("|")
                records.setdefault(Path(project).resolve(), []).append(properties)
        outputs = [path.parent / "bin" / configuration / "net10.0" / f"{path.stem}.dll" for path in graph]
        return records, graph, outputs

    @staticmethod
    def write_xml(path, element):
        path.parent.mkdir(parents=True, exist_ok=True)
        ET.ElementTree(element).write(path, encoding="utf-8", xml_declaration=False)
