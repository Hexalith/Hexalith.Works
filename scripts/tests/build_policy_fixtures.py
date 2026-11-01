"""Minimal root-declared source markers for package-free MSBuild policy fixtures."""

from pathlib import Path


def add_source_root_markers(works: Path) -> None:
    """Model the real root selectors without introducing unrelated project edges."""
    builds = works / "references/Hexalith.Builds/Props/Directory.Packages.props"
    builds.parent.mkdir(parents=True, exist_ok=True)
    builds.write_text("<Project />")
    for relative in (
        "references/Hexalith.Commons/src/libraries/Hexalith.Commons.UniqueIds",
        "references/Hexalith.McpCli/src/Hexalith.McpCli.Abstractions",
    ):
        (works / relative).mkdir(parents=True, exist_ok=True)
