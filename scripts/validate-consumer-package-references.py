#!/usr/bin/env python3
"""Build isolated PackageReference-only consumers for all Works packages."""

from __future__ import annotations

import argparse
import json
import os
import shutil
import subprocess
import sys
import tempfile
import textwrap
import zipfile
from pathlib import Path
from xml.etree import ElementTree
from xml.sax.saxutils import quoteattr


PACKAGE_PROBES = {
    "Hexalith.Works.Contracts": "Hexalith.Works.Contracts.WorksContractsAssembly",
    "Hexalith.Works.Server": "Hexalith.Works.Server.WorksServerAssembly",
    "Hexalith.Works.Projections": "Hexalith.Works.Projections.WorksProjectionsAssembly",
    "Hexalith.Works.Reactor": "Hexalith.Works.Reactor.WorksReactorAssembly",
    "Hexalith.Works.Testing": "Hexalith.Works.Testing.WorksTestingAssembly",
}


def write_isolation_boundaries(directory: Path) -> None:
    """Prevent a scratch consumer from inheriting repository MSBuild policy."""

    (directory / "Directory.Build.props").write_text("<Project />\n", encoding="utf-8")
    (directory / "Directory.Build.targets").write_text("<Project />\n", encoding="utf-8")
    (directory / "Directory.Packages.props").write_text(
        "<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>"
        "</PropertyGroup></Project>\n",
        encoding="utf-8",
    )


def package_versions(package_directory: Path) -> dict[str, str]:
    versions: dict[str, str] = {}
    for package_path in package_directory.glob("*.nupkg"):
        if ".symbols." in package_path.name:
            continue
        with zipfile.ZipFile(package_path) as package:
            nuspec_names = [name for name in package.namelist() if name.endswith(".nuspec")]
            if len(nuspec_names) != 1:
                raise ValueError(f"{package_path.name}: expected exactly one .nuspec file")
            root = ElementTree.fromstring(package.read(nuspec_names[0]))
            namespace = {"n": root.tag.split("}")[0].strip("{")} if root.tag.startswith("{") else {}
            id_element = root.find(".//n:metadata/n:id", namespace) if namespace else root.find(".//metadata/id")
            version_element = root.find(".//n:metadata/n:version", namespace) if namespace else root.find(".//metadata/version")
            if id_element is None or version_element is None or not id_element.text or not version_element.text:
                raise ValueError(f"{package_path.name}: missing id or version metadata")
            versions[id_element.text.strip()] = version_element.text.strip()

    if set(versions) != set(PACKAGE_PROBES):
        raise ValueError(
            f"Consumer validation requires exactly {sorted(PACKAGE_PROBES)}; found {sorted(versions)}"
        )
    if len(set(versions.values())) != 1:
        raise ValueError(f"Works packages do not share one version: {sorted(set(versions.values()))}")
    return versions


def write_nuget_config(directory: Path, package_directory: Path) -> Path:
    config = directory / "NuGet.Config"
    config.write_text(
        textwrap.dedent(f"""\
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
          <packageSources>
            <clear />
            <add key="local-works-packages" value={quoteattr(str(package_directory))} />
            <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
          </packageSources>
          <packageSourceMapping>
            <packageSource key="local-works-packages">
              <package pattern="Hexalith.Works.*" />
            </packageSource>
            <packageSource key="nuget.org">
              <package pattern="*" />
            </packageSource>
          </packageSourceMapping>
        </configuration>
        """),
        encoding="utf-8",
    )
    return config


def write_consumer(directory: Path, package_id: str, version: str, probe_type: str) -> Path:
    project = directory / "PackageConsumer.csproj"
    project.write_text(
        textwrap.dedent(f"""\
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="{package_id}" Version="{version}" />
          </ItemGroup>
        </Project>
        """),
        encoding="utf-8",
    )
    (directory / "Program.cs").write_text(
        f"Console.WriteLine(typeof({probe_type}).Assembly.FullName);\n",
        encoding="utf-8",
    )
    if "ProjectReference" in project.read_text(encoding="utf-8"):
        raise ValueError(f"{package_id}: isolated consumer contains a ProjectReference")
    return project


def run(command: list[str], directory: Path, packages_folder: Path) -> None:
    environment = {
        **os.environ,
        "DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER": "1",
        "MSBUILDDISABLENODEREUSE": "1",
        "NUGET_PACKAGES": str(packages_folder),
    }
    subprocess.run(command, cwd=directory, env=environment, check=True)


def assert_package_only(project: Path, package_id: str, version: str) -> None:
    with (project.parent / "obj" / "project.assets.json").open("r", encoding="utf-8") as handle:
        assets = json.load(handle)
    libraries = assets.get("libraries")
    if not isinstance(libraries, dict):
        raise ValueError(f"{package_id}: restored assets do not contain a libraries object")
    project_libraries = [name for name, value in libraries.items() if value.get("type") == "project"]
    if project_libraries:
        raise ValueError(f"{package_id}: consumer restored project libraries: {project_libraries}")
    expected = f"{package_id}/{version}".casefold()
    if expected not in {name.casefold() for name in libraries}:
        raise ValueError(f"{package_id}: exact local package version {version} was not restored")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("package_directory", type=Path)
    args = parser.parse_args()

    package_directory = args.package_directory.resolve()
    versions = package_versions(package_directory)
    with tempfile.TemporaryDirectory(prefix="hexalith-works-package-consumers-") as temporary:
        root = Path(temporary)
        write_isolation_boundaries(root)
        config = write_nuget_config(root, package_directory)
        packages_folder = root / ".nuget" / "packages"
        for package_id, probe_type in PACKAGE_PROBES.items():
            consumer = root / package_id
            consumer.mkdir()
            project = write_consumer(consumer, package_id, versions[package_id], probe_type)
            run(
                [
                    "dotnet",
                    "restore",
                    str(project),
                    "--configfile",
                    str(config),
                    "-p:UseHexalithProjectReferences=false",
                ],
                consumer,
                packages_folder,
            )
            run(
                [
                    "dotnet",
                    "build",
                    str(project),
                    "--configuration",
                    "Release",
                    "--no-restore",
                    "-warnaserror",
                    "-p:UseHexalithProjectReferences=false",
                ],
                consumer,
                packages_folder,
            )
            assert_package_only(project, package_id, versions[package_id])
            shutil.rmtree(consumer / "bin")

    version = next(iter(versions.values()))
    print(f"Validated five isolated PackageReference-only Works consumers at {version}.")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except subprocess.CalledProcessError as error:
        print(f"Consumer validation failed with exit code {error.returncode}.", file=sys.stderr)
        raise SystemExit(error.returncode)
    except Exception as error:  # noqa: BLE001 - the CI entry point must report concise failures.
        print(f"Consumer validation failed: {error}", file=sys.stderr)
        raise SystemExit(1)
