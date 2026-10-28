"""Execute Works build policy with MSBuild and isolated, offline NuGet feeds."""

from __future__ import annotations

import hashlib
import json
import os
import shutil
import subprocess
import tempfile
import unittest
import zipfile
from pathlib import Path
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
PIN = ET.parse(ROOT / "Directory.Packages.props").findtext(".//HexalithEventStoreVersion")


class ReleaseBuildPolicyTests(unittest.TestCase):
    """Guard producer admission and preserve later solution project targets."""

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="works-release-policy-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        shutil.copyfile(ROOT / "Directory.Build.props", self.root / "Directory.Build.props")
        shutil.copyfile(ROOT / "global.json", self.root / "global.json")
        # The guard reads the actual committed pin; the package-free fixtures need no central imports.
        (self.root / "Directory.Packages.props").write_text(
            f"<Project><PropertyGroup><HexalithEventStoreVersion>{PIN}</HexalithEventStoreVersion>"
            "</PropertyGroup></Project>"
        )
        self.project = self.root / "Hexalith.Works.Policy.proj"
        self.project.write_text('<Project><Import Project="Directory.Build.props" /></Project>')
        self.environment = {
            key: value for key, value in os.environ.items()
            if key.casefold() not in {
                "hexalitheventstoreversion", "worksallowlocaleventstorecandidate",
                "usehexalithprojectreferences", "usenugetdeps", "configuration",
                "hexalitheventstorefromsource", "hexalithpolymorphicserializationsfromsource",
                "restoreadditionalprojectsources", "workssdkcandidatefeed", "worksplatformcandidatefeed",
            }
        }
        self.environment.update({
            "NUGET_PACKAGES": str(self.root / "packages"),
            "NUGET_HTTP_CACHE_PATH": str(self.root / "http-cache"),
            "DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER": "1",
            "MSBUILDDISABLENODEREUSE": "1",
        })

    def msbuild(self, *arguments, environment=None):
        return subprocess.run(
            ["dotnet", "msbuild", str(self.project), "-nologo", *arguments],
            cwd=self.root, env=environment or self.environment,
            capture_output=True, text=True, timeout=60, check=False,
        )

    def test_release_default_and_exact_pin_pass(self):
        for arguments in ([], [f"-p:HexalithEventStoreVersion={PIN}"]):
            with self.subTest(arguments=arguments):
                result = self.msbuild("-t:ValidateHexalithDependencyMode", "-p:Configuration=Release", *arguments)
                self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_restore_refuses_mixed_source_modes_before_package_resolution(self):
        self.project = self.root / "Hexalith.Works.MixedMode.csproj"
        self.project.write_text(
            '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup>'
            '<PackageReference Remove="MinVer" />'
            '<PackageReference Include="Works.Deliberately.Missing" Version="0.0.0" />'
            '</ItemGroup></Project>')
        feed = self.root / "empty-feed"
        feed.mkdir()
        for configuration in ("Debug", "Release"):
            for source in ("true", "false"):
                with self.subTest(configuration=configuration, source=source):
                    opposite = "false" if source == "true" else "true"
                    result = self.msbuild(
                        "-t:Restore", f"-p:Configuration={configuration}",
                        f"-p:UseHexalithProjectReferences={source}", f"-p:UseNuGetDeps={opposite}",
                        f"-p:HexalithEventStoreFromSource={opposite}", "-p:NuGetAudit=false",
                        f"-p:RestoreSources={feed}")
                    self.assertNotEqual(0, result.returncode, result.stdout + result.stderr)
                    self.assertIn("HXW0001", result.stdout)
                    self.assertIn("HexalithEventStoreFromSource must agree", result.stdout)
                    self.assertNotIn("NU1010", result.stdout + result.stderr)
                    self.assertNotIn("NU1101", result.stdout + result.stderr)
        control = self.msbuild("-t:Restore", "-p:Configuration=Debug", "-p:NuGetAudit=false",
                               f"-p:RestoreSources={feed}")
        self.assertNotEqual(0, control.returncode)
        self.assertIn("NU1101", control.stdout + control.stderr)
        self.assertIn("Works.Deliberately.Missing", control.stdout + control.stderr)
        self.assertNotIn("HXW0001", control.stdout + control.stderr)

    def test_release_refuses_ambient_and_command_line_drift(self):
        for version in ("0.0.1", f"{PIN}-unqualified"):
            for ambient in (True, False):
                with self.subTest(version=version, ambient=ambient):
                    environment = dict(self.environment)
                    arguments = []
                    if ambient:
                        environment["HexalithEventStoreVersion"] = version
                    else:
                        arguments.append(f"-p:HexalithEventStoreVersion={version}")
                    result = self.msbuild("-t:ValidateHexalithDependencyMode", "-p:Configuration=Release",
                                          *arguments, environment=environment)
                    self.assertNotEqual(0, result.returncode)
                    self.assertIn("HXW0003", result.stdout)
                    self.assertIn(PIN, result.stdout)
                    self.assertIn(version, result.stdout)

    def test_explicit_candidate_admission_and_source_debug_pass(self):
        for configuration, arguments in (
            ("Release", ["-p:WorksAllowLocalEventStoreCandidate=true"]),
            ("Debug", []),
        ):
            with self.subTest(configuration=configuration):
                result = self.msbuild("-t:ValidateHexalithDependencyMode", f"-p:Configuration={configuration}",
                                      f"-p:HexalithEventStoreVersion={PIN}-story415.policy", *arguments)
                self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_false_candidate_admission_does_not_allow_pin_drift(self):
        result = self.msbuild(
            "-t:ValidateHexalithDependencyMode", "-p:Configuration=Release",
            "-p:HexalithEventStoreVersion=0.0.1", "-p:WorksAllowLocalEventStoreCandidate=false",
        )
        self.assertNotEqual(0, result.returncode)
        self.assertIn("Release requires the committed EventStore pin", result.stdout)

    def test_invalid_candidate_switch_refuses_with_the_committed_pin_in_both_configurations(self):
        for configuration in ("Release", "Debug"):
            for value in ("yes", "on", "1"):
                with self.subTest(configuration=configuration, value=value):
                    result = self.msbuild(
                        "-t:ValidateHexalithDependencyMode", f"-p:Configuration={configuration}",
                        f"-p:HexalithEventStoreVersion={PIN}", f"-p:WorksAllowLocalEventStoreCandidate={value}",
                    )
                    self.assertNotEqual(0, result.returncode)
                    self.assertIn("HXW0003", result.stdout)
                    self.assertIn("WorksAllowLocalEventStoreCandidate must be exactly true or false", result.stdout)
                    self.assertNotIn("Release requires the committed EventStore pin", result.stdout)

    def test_candidate_feeds_require_opt_in_and_preserve_explicit_sources(self):
        for story in ("story-4-14", "story-4-15"):
            (self.root / ".artifacts" / story / "feed").mkdir(parents=True)
        for admission in ("", "false", "true"):
            with self.subTest(admission=admission):
                result = self.msbuild("-getProperty:RestoreAdditionalProjectSources", "-p:Configuration=Release",
                                      "-p:RestoreAdditionalProjectSources=explicit-source",
                                      f"-p:WorksAllowLocalEventStoreCandidate={admission}")
                # Command-line global properties cannot be appended by projects. Test inherited sources below.
                self.assertEqual(0, result.returncode, result.stdout + result.stderr)
                self.assertEqual("explicit-source", result.stdout.strip())
                environment = {**self.environment, "RestoreAdditionalProjectSources": "inherited-source"}
                result = self.msbuild("-getProperty:RestoreAdditionalProjectSources", "-p:Configuration=Release",
                                      f"-p:WorksAllowLocalEventStoreCandidate={admission}", environment=environment)
                self.assertEqual(0, result.returncode, result.stdout + result.stderr)
                sources = result.stdout.strip().split(";")
                self.assertEqual("inherited-source", sources[0])
                expected = [str(self.root / ".artifacts" / story / "feed") for story in ("story-4-14", "story-4-15")]
                self.assertEqual(expected if admission == "true" else [], sources[1:])

    def candidate_project(self, story, version="0.0.1", package_id="Hexalith.EventStore.PolicyProbe"):
        feed = self.root / ".artifacts" / story / "feed"
        feed.mkdir(parents=True)
        package = feed / f"{package_id}.{version}.nupkg"
        with zipfile.ZipFile(package, "w") as archive:
            archive.writestr(
                f"{package_id}.nuspec",
                f"<package><metadata><id>{package_id}</id><version>{version}</version>"
                "<authors>Fixture</authors><description>Offline candidate control</description>"
                "</metadata></package>",
            )
        (self.root / "public-feed").mkdir(exist_ok=True)
        (self.root / "NuGet.Config").write_text(
            '<configuration><packageSources><clear /><add key="public" value="public-feed" />'
            '</packageSources><fallbackPackageFolders><clear /></fallbackPackageFolders></configuration>'
        )
        (self.root / "Directory.Packages.props").write_text(
            f"<Project><PropertyGroup><HexalithEventStoreVersion>{PIN}</HexalithEventStoreVersion>"
            "<ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>"
        )
        self.project = self.root / "Hexalith.Works.Policy.csproj"
        self.project.write_text(
            '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><PackageReference Remove="MinVer" />'
            f'<PackageReference Include="{package_id}" Version="{version}" />'
            '</ItemGroup></Project>'
        )
        return package

    def test_fresh_and_warm_cache_restore_cannot_consume_unadmitted_candidate(self):
        for story in ("story-4-14", "story-4-15"):
            with self.subTest(story=story):
                package = self.candidate_project(story)
                # Default candidate cache is also fresh for each source-provenance control.
                candidate_cache = self.root / ".artifacts/sdk-candidate-packages"
                if candidate_cache.exists():
                    shutil.rmtree(candidate_cache)
                self.environment["NUGET_PACKAGES"] = str(self.root / f"public-packages-{story}")
                for admission in ("false", "true", "false"):
                    result = self.msbuild("-t:Restore", "-p:Configuration=Release", "-p:NuGetAudit=false",
                                          "-p:RestoreForce=true", f"-p:WorksAllowLocalEventStoreCandidate={admission}")
                    if admission == "false":
                        self.assertNotEqual(0, result.returncode)
                        self.assertIn("NU1101", result.stdout)
                    else:
                        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
                        restored = candidate_cache / "hexalith.eventstore.policyprobe/0.0.1"
                        metadata = json.loads((restored / ".nupkg.metadata").read_text())
                        self.assertEqual(str(package.parent), metadata["source"])
                        actual = restored / "hexalith.eventstore.policyprobe.0.0.1.nupkg"
                        self.assertEqual(
                            hashlib.sha256(package.read_bytes()).digest(), hashlib.sha256(actual.read_bytes()).digest(),
                        )
                package.unlink()

    def test_no_restore_release_build_refuses_candidate_assets(self):
        for version in (f"{PIN}-story415.policy", PIN):
            with self.subTest(version=version):
                self.candidate_project(f"story-{version}", version, "Hexalith.EventStore.Contracts")
                feed = self.root / ".artifacts" / f"story-{version}" / "feed"
                result = self.msbuild("-t:Restore", "-p:Configuration=Release", "-p:NuGetAudit=false",
                                      "-p:WorksAllowLocalEventStoreCandidate=true",
                                      f"-p:WorksSdkCandidateFeed={feed}", f"-p:HexalithEventStoreVersion={version}")
                self.assertEqual(0, result.returncode, result.stdout + result.stderr)
                result = self.msbuild("-t:Build", "-p:Configuration=Release",
                                      "-p:WorksAllowLocalEventStoreCandidate=false")
                self.assertNotEqual(0, result.returncode)
                self.assertIn("HXW0003", result.stdout)
                self.assertIn("Release assets", result.stdout)
                self.assertIn("candidate cache" if version == PIN else version, result.stdout)

    def test_version_validation_runs_before_restore_collection_and_compile(self):
        self.project.write_text(
            '<Project><Import Project="Directory.Build.props" />'
            '<Target Name="CollectPackageReferences"><Message Text="COLLECTED" Importance="high" /></Target>'
            '<Target Name="CoreCompile"><Message Text="COMPILED" Importance="high" /></Target></Project>'
        )
        for target in ("CollectPackageReferences", "CoreCompile"):
            with self.subTest(target=target):
                result = self.msbuild(f"-t:{target}", "-p:Configuration=Release", "-p:HexalithEventStoreVersion=0.0.1")
                self.assertNotEqual(0, result.returncode)
                self.assertIn("HXW0003", result.stdout)
                self.assertNotIn("COLLECTED", result.stdout)
                self.assertNotIn("COMPILED", result.stdout)

    def test_solution_build_then_pack_reaches_every_project(self):
        shutil.copyfile(ROOT / "Directory.Solution.props", self.root / "Directory.Solution.props")
        solution_targets = ROOT / "Directory.Solution.targets"
        if solution_targets.exists():
            shutil.copyfile(solution_targets, self.root / solution_targets.name)
        for name in ("First", "Second"):
            (self.root / f"{name}.csproj").write_text(
                '<Project><Target Name="Build"><Message Text="BUILD_' + name + '" Importance="high" />'
                '</Target><Target Name="Pack"><Message Text="PACK_' + name + '" Importance="high" />'
                '</Target></Project>'
            )
        self.project = self.root / "Probe.slnx"
        self.project.write_text('<Solution><Project Path="First.csproj" /><Project Path="Second.csproj" /></Solution>')
        result = self.msbuild("-t:Build,Pack", "-p:Configuration=Release")
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        for name in ("First", "Second"):
            self.assertEqual(1, result.stdout.count(f"BUILD_{name}"), result.stdout)
            self.assertEqual(1, result.stdout.count(f"PACK_{name}"), result.stdout)


if __name__ == "__main__":
    unittest.main()
