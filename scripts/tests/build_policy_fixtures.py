"""Minimal root-declared source markers for package-free MSBuild policy fixtures."""

import shutil
from pathlib import Path


def add_source_root_markers(works: Path) -> None:
    """Model the real root selectors without introducing unrelated project edges."""
    catalog = Path("references/Hexalith.Builds/Props/Directory.Packages.props")
    (works / catalog).parent.mkdir(parents=True, exist_ok=True)
    # Release admission reads the literal version from the selected Builds catalog.
    shutil.copyfile(Path(__file__).resolve().parents[2] / catalog, works / catalog)
    for relative in (
        "references/Hexalith.Commons/src/libraries/Hexalith.Commons.UniqueIds",
        "references/Hexalith.McpCli/src/Hexalith.McpCli.Abstractions",
    ):
        (works / relative).mkdir(parents=True, exist_ok=True)
