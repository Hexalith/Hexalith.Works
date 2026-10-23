"""Run the exact changed adapter tests against public SDK packages without the unrelated Platform fixture."""

import argparse
from pathlib import Path
import shutil
import subprocess
import tempfile
from xml.sax.saxutils import escape


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project-root", type=Path, default=Path(__file__).resolve().parents[4])
    parser.add_argument("--work-root", type=Path)
    args = parser.parse_args()
    root = args.project_root.resolve()
    work = (args.work_root or Path(tempfile.mkdtemp(prefix="works-sdk-recovery-package-"))).resolve()
    work.mkdir(parents=True, exist_ok=True)
    for name in ("Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props"):
        (work / name).write_text(f'<Project><Import Project="{escape(str(root / name))}" /></Project>\n')
    shutil.copyfile(root / ".editorconfig", work / ".editorconfig")
    names = (
        "DiagnosticUnavailableReadModelStore", "Story48RecordingLogger", "Story48Streams", "ThrowingDiagnosticLogger",
        "WorkItemSharedProjectionRebuildHandlerTests", "WorkItemV1Catalog", "WorksSdkIntentTranslationTests",
        "WorksSdkProjectionQueryTests", "WorksSdkReminderDeliveryTests", "WorksSourceStreamReaderTests",
    )
    links = "\n".join(f'    <Compile Include="{escape(str(root / "tests/Hexalith.Works.IntegrationTests" / (name + ".cs")))}" Link="{name}.cs" />' for name in names)
    project = work / "FocusedAdapters.csproj"
    project.write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <OutputType>Exe</OutputType>
    <IsTestProject>true</IsTestProject>
    <AssemblyName>Hexalith.Works.IntegrationTests</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="Shouldly" />
    <PackageReference Include="NSubstitute" />
    <PackageReference Include="Hexalith.EventStore.Testing" />
    <Using Include="Xunit" />
    <ProjectReference Include="{escape(str(root / "src/Hexalith.Works/Hexalith.Works.csproj"))}" />
{links}
    <None Include="{escape(str(root / "xunit.runner.json"))}" Link="xunit.runner.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
''')
    commands = (
        ("build.log", ["dotnet", "build", str(project), "--configuration", "Release", "-m:1", "-p:MinVerVersionOverride=1.0.0"]),
        ("tests.log", ["dotnet", str(work / "bin/Release/net10.0/Hexalith.Works.IntegrationTests.dll"),
                       "-result-xml", str(work / "tests.xml")]),
    )
    for name, command in commands:
        with (work / name).open("w") as log:
            result = subprocess.run(command, cwd=root, stdout=log, stderr=subprocess.STDOUT, timeout=300, check=False)
        print(f"{name}: exit {result.returncode}; {work / name}", flush=True)
        if result.returncode:
            raise SystemExit(result.returncode)
    print((work / "tests.log").read_text())


if __name__ == "__main__":
    main()
