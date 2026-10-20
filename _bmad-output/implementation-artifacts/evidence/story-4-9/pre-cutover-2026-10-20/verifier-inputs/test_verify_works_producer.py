"""Proof fixtures reject source substitution, incomplete inventories and foreign producer authority."""
import hashlib
import json
from pathlib import Path
import tempfile
import unittest

from verify_works_producer import API, COMMITS_API, BUILD_FILES, REPOSITORY_URL, TAG_REFS_API, TAGS_API, qualify_runtime_source, runtime_source_snapshot

COMMIT = "a" * 40
SOURCE_TREE = "b" * 40
ROOT_TREE = "c" * 40
TAG = "v3.125.1"
TAG_OBJECT = "e" * 40


class ProducerQualificationTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(prefix="works-sdk416-producer-")
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        (self.root / "src/Example").mkdir(parents=True)
        for name in [*BUILD_FILES, "src/Example/Example.cs", "src/Example/Example.csproj"]:
            (self.root / name).write_bytes((name + "\n").encode())
        entry = self.entry
        self.documents = {
            COMMITS_API + COMMIT: {"sha": COMMIT, "tree": {"sha": ROOT_TREE}},
            API + ROOT_TREE: {"sha": ROOT_TREE, "truncated": False, "tree": [
                {"path": "src", "type": "tree", "mode": "040000", "sha": SOURCE_TREE},
                *[entry(name, self.root / name) for name in BUILD_FILES]]},
            API + SOURCE_TREE + "?recursive=1": {"sha": SOURCE_TREE, "truncated": False, "tree": [
                entry("Example/Example.cs", self.root / "src/Example/Example.cs"),
                entry("Example/Example.csproj", self.root / "src/Example/Example.csproj")]}}
        self.documents[TAG_REFS_API + TAG] = {"ref": "refs/tags/" + TAG, "object": {"type": "commit", "sha": COMMIT}}
        self.producers = {"Hexalith.EventStore.Client": {"repositoryCommit": COMMIT, "repositoryUrl": REPOSITORY_URL}}

    @staticmethod
    def entry(name, path):
        data = path.read_bytes()
        return {"path": name, "type": "blob", "mode": "100644",
                "sha": hashlib.sha1(b"blob " + str(len(data)).encode() + b"\0" + data).hexdigest()}

    def fetch(self, url):
        return json.dumps(self.documents[url]).encode()

    def qualify(self):
        return qualify_runtime_source(runtime_source_snapshot(self.root), self.producers, self.fetch)

    def test_exact_public_tree_qualifies_without_inventing_a_checkout(self):
        result = self.qualify()
        self.assertTrue(result["matchesPublishedProducer"])
        self.assertIsNone(result["checkoutCommit"])
        self.assertEqual(5, result["runtimeFileCount"])
        self.assertEqual(2, len(result["officialTrees"]))

    def test_modified_added_and_missing_runtime_files_are_rejected(self):
        for kind in ["modified", "added", "missing"]:
            with self.subTest(kind=kind):
                path = self.root / ("src/Example/Extra.cs" if kind == "added" else "src/Example/Example.cs")
                original = path.read_bytes() if path.exists() else None
                if kind == "missing":
                    path.unlink()
                else:
                    path.write_bytes(b"substituted\n")
                with self.assertRaisesRegex(ValueError, "Runtime SDK differs from published producer"):
                    self.qualify()
                if original is None:
                    path.unlink()
                else:
                    path.write_bytes(original)

    def test_modified_central_build_file_is_rejected(self):
        (self.root / BUILD_FILES[0]).write_bytes(b"changed\n")
        with self.assertRaisesRegex(ValueError, "Runtime SDK differs"):
            self.qualify()

    def test_foreign_repository_and_mixed_producers_are_rejected(self):
        self.producers["Hexalith.EventStore.Client"]["repositoryUrl"] = "https://github.com/foreign/sdk"
        with self.assertRaisesRegex(ValueError, "expected official repository"):
            self.qualify()
        self.producers["Hexalith.EventStore.Client"]["repositoryUrl"] = REPOSITORY_URL
        self.producers["Other"] = {"repositoryUrl": REPOSITORY_URL, "repositoryCommit": "c" * 40}
        with self.assertRaisesRegex(ValueError, "one compatible producer"):
            self.qualify()

    def test_truncated_or_wrong_object_tree_is_rejected(self):
        document = self.documents[API + SOURCE_TREE + "?recursive=1"]
        document["truncated"] = True
        with self.assertRaisesRegex(ValueError, "truncated"):
            self.qualify()
        document["truncated"] = False
        document["sha"] = "c" * 40
        with self.assertRaisesRegex(ValueError, "another object"):
            self.qualify()

    def test_duplicate_path_or_symlink_source_is_rejected(self):
        entries = self.documents[API + SOURCE_TREE + "?recursive=1"]["tree"]
        entries.append(dict(entries[0]))
        with self.assertRaisesRegex(ValueError, "duplicate path"):
            self.qualify()
        entries.pop()
        entries[0]["mode"] = "120000"
        with self.assertRaisesRegex(ValueError, "not a regular file"):
            self.qualify()

    def test_commit_must_bind_the_published_identity_and_root_tree(self):
        document = self.documents[COMMITS_API + COMMIT]
        document["sha"] = "d" * 40
        with self.assertRaisesRegex(ValueError, "exact published producer tree"):
            self.qualify()
        document["sha"] = COMMIT
        document["tree"]["sha"] = "invalid"
        with self.assertRaisesRegex(ValueError, "exact published producer tree"):
            self.qualify()

    def test_source_drift_after_snapshot_is_rejected(self):
        snapshot = runtime_source_snapshot(self.root)
        (self.root / "src/Example/Example.cs").write_bytes(b"changed\n")
        with self.assertRaisesRegex(ValueError, "changed during"):
            qualify_runtime_source(snapshot, self.producers, self.fetch)

    def test_named_release_tag_must_name_the_published_producer(self):
        result = qualify_runtime_source(runtime_source_snapshot(self.root), self.producers, self.fetch, tag=TAG)
        self.assertEqual({"tag": TAG, "commit": COMMIT}, {key: result["namedTag"][key] for key in ("tag", "commit")})
        self.documents[TAG_REFS_API + TAG]["object"]["sha"] = "d" * 40
        with self.assertRaisesRegex(ValueError, "does not name the published producer"):
            qualify_runtime_source(runtime_source_snapshot(self.root), self.producers, self.fetch, tag=TAG)
        self.documents[TAG_REFS_API + TAG] = {"ref": "refs/tags/v3.125.0", "object": {"type": "commit", "sha": COMMIT}}
        with self.assertRaisesRegex(ValueError, "does not name"):
            qualify_runtime_source(runtime_source_snapshot(self.root), self.producers, self.fetch, tag=TAG)
        for tag in ["3.125.1", "v3.125.1-story416.1", "main"]:
            with self.subTest(tag=tag), self.assertRaisesRegex(ValueError, "stable release tag"):
                qualify_runtime_source(runtime_source_snapshot(self.root), self.producers, self.fetch, tag=tag)

    def test_annotated_release_tag_is_dereferenced_once(self):
        self.documents[TAG_REFS_API + TAG] = {"ref": "refs/tags/" + TAG, "object": {"type": "tag", "sha": TAG_OBJECT}}
        self.documents[TAGS_API + TAG_OBJECT] = {"sha": TAG_OBJECT, "tag": TAG, "object": {"type": "commit", "sha": COMMIT}}
        result = qualify_runtime_source(runtime_source_snapshot(self.root), self.producers, self.fetch, tag=TAG)
        self.assertEqual(2, len(result["namedTag"]["receipts"]))
        self.documents[TAGS_API + TAG_OBJECT]["tag"] = "v3.125.0"
        with self.assertRaisesRegex(ValueError, "another object"):
            qualify_runtime_source(runtime_source_snapshot(self.root), self.producers, self.fetch, tag=TAG)
        self.documents[TAGS_API + TAG_OBJECT] = {"sha": TAG_OBJECT, "tag": TAG, "object": {"type": "tag", "sha": "f" * 40}}
        with self.assertRaisesRegex(ValueError, "does not name the published producer"):
            qualify_runtime_source(runtime_source_snapshot(self.root), self.producers, self.fetch, tag=TAG)

    def test_every_src_build_input_is_qualified_except_outputs(self):
        props = self.root / "src/Example/Directory.Build.props"
        settings = self.root / "src/Example/appsettings.json"
        props.write_bytes(b"<Project />\n")
        settings.write_bytes(b"{}\n")
        with self.assertRaisesRegex(ValueError, "Runtime SDK differs from published producer"):
            self.qualify()
        tree = self.documents[API + SOURCE_TREE + "?recursive=1"]["tree"]
        tree += [{"path": "Example", "type": "tree", "mode": "040000", "sha": "d" * 40},
                 self.entry("Example/Directory.Build.props", props), self.entry("Example/appsettings.json", settings)]
        self.assertEqual(7, self.qualify()["runtimeFileCount"])
        (self.root / "src/Example/obj").mkdir()
        (self.root / "src/Example/obj/generated.json").write_bytes(b"{}\n")
        self.assertEqual(7, self.qualify()["runtimeFileCount"])
        for path in [props, settings]:
            original = path.read_bytes()
            path.write_bytes(b"substituted\n")
            with self.subTest(path=path.name), self.assertRaisesRegex(ValueError, "Runtime SDK differs from published producer"):
                self.qualify()
            path.write_bytes(original)

    def test_local_link_or_official_submodule_is_rejected(self):
        link = self.root / "src/Example/Linked.cs"
        link.symlink_to(self.root / "src/Example/Example.cs")
        with self.assertRaisesRegex(ValueError, "symbolic link"):
            self.qualify()
        link.unlink()
        self.documents[API + SOURCE_TREE + "?recursive=1"]["tree"].append(
            {"path": "Vendored", "type": "commit", "mode": "160000", "sha": "d" * 40})
        with self.assertRaisesRegex(ValueError, "not a regular file"):
            self.qualify()

    def test_unavailable_official_authority_cannot_qualify(self):
        def unavailable(_url):
            raise OSError("Synthetic official authority outage")
        with self.assertRaisesRegex(OSError, "official authority outage"):
            qualify_runtime_source(runtime_source_snapshot(self.root), self.producers, unavailable)


if __name__ == "__main__":
    unittest.main()
