"""Exercise preparation with a newer workspace catalog and an exact recorded producer."""

import hashlib
import json
import os
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def git(root, *arguments):
    return subprocess.check_output(["git", "-C", str(root), *arguments], text=True).strip()


def commit(root, message):
    git(root, "add", ".")
    git(root, "-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid",
        "-c", "commit.gpgsign=false", "-c", "core.hooksPath=/dev/null", "commit", "--quiet", "-m", message)
    return git(root, "rev-parse", "HEAD")


def fixture(root):
    (root / "scripts").mkdir()
    shutil.copyfile(ROOT / "scripts/prepare-platform-builds.sh", root / "scripts/prepare-platform-builds.sh")
    builds = root / "references/Hexalith.Builds"
    (builds / "Props").mkdir(parents=True)
    git(builds, "init", "--quiet")
    catalog = builds / "Props/Directory.Packages.props"
    catalog.write_text("<Project />\n")
    recorded = commit(builds, "build: record fixture producer")
    binding = {"producer": {"sourceRevision": recorded, "catalog": {
        "path": "Props/Directory.Packages.props",
        "blob": git(builds, "rev-parse", "HEAD:Props/Directory.Packages.props"),
        "sha256": hashlib.sha256(catalog.read_bytes()).hexdigest(),
    }}}
    catalog.write_text("<Project><PropertyGroup><NewCatalog>true</NewCatalog></PropertyGroup></Project>\n")
    current = commit(builds, "build: advance fixture catalog")
    owner = root / "references/Hexalith.Platform"
    (owner / "eng").mkdir(parents=True)
    (owner / "references/Hexalith.Builds").mkdir(parents=True)
    git(owner, "init", "--quiet")
    (owner / "eng/binding.json").write_text(json.dumps(binding))
    shutil.copyfile(ROOT / "references/Hexalith.Platform/eng/builds_tuple.py", owner / "eng/builds_tuple.py")
    (owner / "eng/prepare_builds_artifacts.py").write_text(
        "import json, os, pathlib, subprocess, sys\n"
        "from builds_tuple import resolve_builds_root, validate_recorded_source, verify_catalog\n"
        "assert '--require-recorded' in sys.argv\n"
        "owner = pathlib.Path(__file__).resolve().parents[1]\n"
        "binding = json.loads((owner / 'eng/binding.json').read_text())\n"
        "pin = subprocess.check_output(['git', 'rev-parse', 'HEAD:references/Hexalith.Builds'],\n"
        "                              cwd=owner, text=True).strip()\n"
        "validate_recorded_source(binding, pin, False)\n"
        "selected = resolve_builds_root(owner)\n"
        "verify_catalog(binding, selected)\n"
        "assert not (owner / 'references/Hexalith.Builds/.git').exists()\n"
        "if os.environ.get('FIXTURE_REFUSE') == 'true': raise SystemExit(23)\n"
        "tool = pathlib.Path(sys.argv[sys.argv.index('--output') + 1])\n"
        "tool.mkdir(parents=True)\n"
        "if os.environ.get('FIXTURE_MISSING_TOOL') != 'true':\n"
        "    (tool / 'Hexalith.Builds.Tooling.dll').write_bytes(b'fixture')\n"
        "(tool / 'selected.json').write_text(json.dumps({'producer': pin, 'buildsRoot': str(selected)}))\n"
        "if os.environ.get('FIXTURE_CHANGE_SOURCE'):\n"
        "    pathlib.Path(os.environ['FIXTURE_CHANGE_SOURCE']).write_text('changed during verification')\n"
        "if os.environ.get('FIXTURE_ADVANCE_SOURCE'):\n"
        "    subprocess.run(['git', '-C', os.environ['FIXTURE_ADVANCE_SOURCE'], 'checkout', '--quiet',\n"
        "                    '--detach', os.environ['FIXTURE_ADVANCE_REVISION']], check=True)\n"
        "print(tool)\n"
    )
    git(owner, "update-index", "--add", "--cacheinfo", "160000", recorded, "references/Hexalith.Builds")
    owner_revision = commit(owner, "build: bind fixture producer")
    return owner, builds, owner_revision, recorded, current


class PlatformBuildsPreparationTests(unittest.TestCase):
    def run_hook(self, root, overrides=None):
        exported = root / "github.env"
        exported.write_text("existing=preserved\n")
        environment = os.environ.copy() | {
            "RUNNER_TEMP": str(root), "GITHUB_ENV": str(exported), "PYTHONDONTWRITEBYTECODE": "1",
        } | (overrides or {})
        result = subprocess.run(["bash", str(root / "scripts/prepare-platform-builds.sh")],
                                env=environment, text=True, capture_output=True, check=False)
        return result, exported.read_text()

    def test_only_successful_owner_verification_exports_the_recorded_producer(self):
        with tempfile.TemporaryDirectory(prefix="works published builds ") as temporary:
            root = Path(temporary)
            owner, builds, owner_revision, recorded, current = fixture(root)
            # An uncommitted newer catalog must also survive preparation unchanged.
            catalog = builds / "Props/Directory.Packages.props"
            catalog.write_text(catalog.read_text() + "<!-- workspace edit -->\n")
            before = catalog.read_bytes()
            for overrides, code in (({"FIXTURE_REFUSE": "true"}, 23),
                                    ({"FIXTURE_MISSING_TOOL": "true"}, 1), ({}, 0)):
                with self.subTest(overrides=overrides):
                    result, exported = self.run_hook(root, overrides)
                    self.assertEqual(code, result.returncode, result.stderr)
                    if code:
                        self.assertEqual("existing=preserved\n", exported)
                    else:
                        self.assertEqual("existing=preserved\nHexalithBuildsPublishedToolRoot="
                                         + result.stdout, exported)
                        tool = Path(result.stdout.strip())
                        self.assertTrue((tool / "Hexalith.Builds.Tooling.dll").is_file())
                        selected = json.loads((tool / "selected.json").read_text())
                        self.assertEqual(recorded, selected["producer"])
                        self.assertFalse(Path(selected["buildsRoot"]).exists())
                    self.assertEqual(owner_revision, git(owner, "rev-parse", "HEAD"))
                    self.assertEqual("", git(owner, "status", "--porcelain"))
                    self.assertEqual(current, git(builds, "rev-parse", "HEAD"))
                    self.assertEqual(before, catalog.read_bytes())
                    self.assertFalse((owner / "references/Hexalith.Builds/.git").exists())
                    self.assertTrue(all(not (path / "workspace").exists()
                                        for path in root.glob("works-published-builds.*")))

    def test_dirty_original_owner_refuses_before_exporting(self):
        for tracked in (True, False):
            with self.subTest(tracked=tracked), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                owner, _, _, _, _ = fixture(root)
                path = owner / ("eng/binding.json" if tracked else "untracked.txt")
                path.write_text("uncommitted owner input\n")
                result, exported = self.run_hook(root)
                self.assertNotEqual(0, result.returncode)
                self.assertIn("clean recorded Platform source", result.stderr)
                self.assertEqual("existing=preserved\n", exported)
                self.assertEqual("uncommitted owner input\n", path.read_text())
                self.assertFalse(list(root.glob("works-published-builds.*")))

    def test_owner_changes_during_preparation_refuse_before_exporting(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            owner, _, _, _, _ = fixture(root)
            changed = owner / "changed-during-preparation.txt"
            result, exported = self.run_hook(root, {"FIXTURE_CHANGE_SOURCE": str(changed)})
            self.assertNotEqual(0, result.returncode)
            self.assertIn("Platform source changed", result.stderr)
            self.assertEqual("existing=preserved\n", exported)
            self.assertEqual("changed during verification", changed.read_text())

    def test_clean_owner_revision_change_during_preparation_refuses_before_exporting(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            owner, _, original, _, _ = fixture(root)
            (owner / "future.txt").write_text("next clean source revision\n")
            future = commit(owner, "build: advance fixture owner")
            git(owner, "checkout", "--quiet", "--detach", original)
            result, exported = self.run_hook(root, {
                "FIXTURE_ADVANCE_SOURCE": str(owner), "FIXTURE_ADVANCE_REVISION": future,
            })
            self.assertNotEqual(0, result.returncode)
            self.assertIn("Platform source changed", result.stderr)
            self.assertEqual("existing=preserved\n", exported)
            self.assertEqual(future, git(owner, "rev-parse", "HEAD"))
            self.assertEqual("", git(owner, "status", "--porcelain"))

    def test_ambient_git_selectors_cannot_replace_the_physical_repositories(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            owner, builds, original, recorded, _ = fixture(root)
            result, exported = self.run_hook(root, {
                "GIT_DIR": str(builds / ".git"), "GIT_WORK_TREE": str(builds),
                "GIT_INDEX_FILE": str(root / "foreign-index"),
            })
            self.assertEqual(0, result.returncode, result.stderr)
            self.assertEqual("existing=preserved\nHexalithBuildsPublishedToolRoot=" + result.stdout, exported)
            selected = json.loads((Path(result.stdout.strip()) / "selected.json").read_text())
            self.assertEqual(recorded, selected["producer"])
            self.assertEqual(original, git(owner, "rev-parse", "HEAD"))
            self.assertFalse((root / "foreign-index").exists())


if __name__ == "__main__":
    unittest.main()
