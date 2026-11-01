"""Run the published-artifact hook without borrowing real publication evidence."""

import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]


class PlatformBuildsPreparationTests(unittest.TestCase):
    def test_only_successful_owner_verification_exports_the_tool_root(self):
        with tempfile.TemporaryDirectory(prefix="works-published-builds-") as temporary:
            root = Path(temporary)
            (root / "scripts").mkdir()
            hook = root / "scripts/prepare-platform-builds.sh"
            shutil.copyfile(ROOT / "scripts/prepare-platform-builds.sh", hook)
            owner = root / "references/Hexalith.Platform/eng"
            owner.mkdir(parents=True)
            (owner / "prepare_builds_artifacts.py").write_text(
                "import os, pathlib, sys\n"
                "assert '--require-recorded' in sys.argv\n"
                "if os.environ.get('FIXTURE_REFUSE') == 'true': raise SystemExit(23)\n"
                "tool = pathlib.Path(sys.argv[sys.argv.index('--output') + 1])\n"
                "tool.mkdir(parents=True)\n"
                "if os.environ.get('FIXTURE_MISSING_TOOL') != 'true':\n"
                "    (tool / 'Hexalith.Builds.Tooling.dll').write_bytes(b'fixture')\n"
                "print(tool)\n"
            )
            exported = root / "github.env"
            environment = os.environ.copy() | {"RUNNER_TEMP": str(root), "GITHUB_ENV": str(exported)}
            for overrides, code in (({"FIXTURE_REFUSE": "true"}, 23),
                                    ({"FIXTURE_MISSING_TOOL": "true"}, 1), ({}, 0)):
                with self.subTest(overrides=overrides):
                    exported.write_text("existing=preserved\n")
                    result = subprocess.run(["bash", str(hook)], env=environment | overrides,
                                            text=True, capture_output=True, check=False)
                    self.assertEqual(code, result.returncode, result.stderr)
                    if code:
                        self.assertEqual("existing=preserved\n", exported.read_text())
                    else:
                        self.assertEqual("existing=preserved\nHexalithBuildsPublishedToolRoot="
                                         + result.stdout, exported.read_text())


if __name__ == "__main__":
    unittest.main()
