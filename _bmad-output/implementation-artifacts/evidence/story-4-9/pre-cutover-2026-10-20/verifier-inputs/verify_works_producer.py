"""Bind an explicitly selected SDK source tree to its public package producer without Git mutations."""
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import urllib.request

REPOSITORY_URL = "https://github.com/Hexalith/Hexalith.EventStore"
GIT_API = "https://api.github.com/repos/Hexalith/Hexalith.EventStore/git/"
API = GIT_API + "trees/"
COMMITS_API = GIT_API + "commits/"
TAG_REFS_API = GIT_API + "ref/tags/"
TAGS_API = GIT_API + "tags/"
BUILD_FILES = ("Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props")
MAX_RESPONSE_BYTES = 8 * 1024 * 1024


def source_files(root):
    """Every regular file under src except build outputs: props, targets, JSON and resources are build inputs too."""
    root = Path(root)
    paths = []
    for path in (root / "src").rglob("*"):
        if {"bin", "obj"}.intersection(path.relative_to(root).parts):
            continue
        if path.is_symlink():
            raise ValueError("Runtime source contains a symbolic link: " + str(path.relative_to(root)))
        if path.is_file():
            paths.append(path)
    return paths


def runtime_source_snapshot(root):
    """Record actual runtime bytes; an archive directory has no invented checkout commit."""
    root = Path(root).resolve()
    if not (root / "src").is_dir() or any(not (root / name).is_file() for name in BUILD_FILES):
        raise ValueError("Selected SDK root lacks its runtime source or central build files")
    paths = source_files(root)
    paths += [root / name for name in BUILD_FILES]
    inventory = {str(path.relative_to(root)): hashlib.sha256(path.read_bytes()).hexdigest() for path in sorted(paths)}
    return {"root": str(root), "commit": None, "provenance": "explicit-source-root; requires-public-producer-qualification",
            "sourceFiles": inventory, "inventorySha256": hashlib.sha256(json.dumps(inventory, sort_keys=True).encode()).hexdigest()}


def _fetch(url):
    request = urllib.request.Request(url, headers={"Accept": "application/vnd.github+json",
                                                  "User-Agent": "Hexalith-Works-Story416-Provenance"})
    with urllib.request.urlopen(request, timeout=15) as response:
        body = response.read(MAX_RESPONSE_BYTES + 1)
    if len(body) > MAX_RESPONSE_BYTES:
        raise ValueError("Official SDK source tree exceeds the bounded response limit")
    return body


def _tree(url, fetch, expected_sha):
    raw = fetch(url)
    if len(raw) > MAX_RESPONSE_BYTES:
        raise ValueError("Official SDK source tree exceeds the bounded response limit")
    document = json.loads(raw)
    if document.get("truncated") is not False or document.get("sha") != expected_sha or not isinstance(document.get("tree"), list):
        raise ValueError("Official SDK source tree is truncated, malformed or bound to another object")
    entries = {}
    for item in document["tree"]:
        name = item.get("path")
        if not isinstance(name, str) or not name or PurePosixPath(name).is_absolute() or ".." in PurePosixPath(name).parts or name in entries:
            raise ValueError("Official SDK source tree contains an invalid or duplicate path")
        if not re.fullmatch(r"[0-9a-f]{40}", item.get("sha", "")):
            raise ValueError("Official SDK source tree contains an invalid object identity")
        entries[name] = item
    return entries, {"url": url, "responseSha256": hashlib.sha256(raw).hexdigest(), "objectSha": expected_sha}


def _document(url, fetch):
    raw = fetch(url)
    if len(raw) > MAX_RESPONSE_BYTES:
        raise ValueError("Official SDK reference exceeds the bounded response limit")
    return json.loads(raw), {"url": url, "responseSha256": hashlib.sha256(raw).hexdigest()}


def _named_tag(tag, commit, fetch):
    """Resolve the official release tag, following at most one annotated tag object, to the producer commit."""
    if not re.fullmatch(r"v[0-9]+\.[0-9]+\.[0-9]+", tag or ""):
        raise ValueError("The named producer tag must be a stable release tag: " + str(tag))
    document, receipt = _document(TAG_REFS_API + tag, fetch)
    target = document.get("object", {})
    receipts = [receipt | {"ref": document.get("ref")}]
    if document.get("ref") != "refs/tags/" + tag:
        raise ValueError("Official SDK tag reference does not name " + tag)
    if target.get("type") == "tag" and re.fullmatch(r"[0-9a-f]{40}", target.get("sha", "")):
        annotated, annotated_receipt = _document(TAGS_API + target["sha"], fetch)
        if annotated.get("sha") != target["sha"] or annotated.get("tag") != tag:
            raise ValueError("Official SDK annotated tag is bound to another object")
        target = annotated.get("object", {})
        receipts.append(annotated_receipt | {"objectSha": annotated["sha"]})
    if target.get("type") != "commit" or target.get("sha") != commit:
        raise ValueError("Official SDK tag " + tag + " does not name the published producer commit " + commit)
    return {"tag": tag, "commit": commit, "receipts": receipts}


def qualify_runtime_source(snapshot, published_producers, fetch=None, tag=None):
    """Require exact Git blob identities and complete runtime file membership from official immutable trees."""
    if not published_producers or any(row.get("repositoryUrl") != REPOSITORY_URL for row in published_producers.values()):
        raise ValueError("Published SDK archives do not identify the expected official repository")
    commits = {row.get("repositoryCommit") for row in published_producers.values()}
    if len(commits) != 1 or not re.fullmatch(r"[0-9a-f]{40}", next(iter(commits)) or ""):
        raise ValueError("Published SDK archives do not identify one compatible producer commit")
    commit = commits.pop()
    fetch = fetch or _fetch
    named_tag = _named_tag(tag, commit, fetch) if tag is not None else None
    commit_url = COMMITS_API + commit
    commit_raw = fetch(commit_url)
    if len(commit_raw) > MAX_RESPONSE_BYTES:
        raise ValueError("Official SDK commit exceeds the bounded response limit")
    commit_document = json.loads(commit_raw)
    root_sha = commit_document.get("tree", {}).get("sha", "")
    if commit_document.get("sha") != commit or not re.fullmatch(r"[0-9a-f]{40}", root_sha):
        raise ValueError("Official SDK commit does not bind the exact published producer tree")
    commit_receipt = {"url": commit_url, "responseSha256": hashlib.sha256(commit_raw).hexdigest(), "objectSha": commit}
    root_entries, root_receipt = _tree(API + root_sha, fetch, root_sha)
    if root_entries.get("src", {}).get("type") != "tree":
        raise ValueError("Official producer lacks its runtime source tree")
    src_sha = root_entries["src"]["sha"]
    source_entries, source_receipt = _tree(API + src_sha + "?recursive=1", fetch, src_sha)
    expected = {}
    for name in BUILD_FILES:
        entry = root_entries.get(name, {})
        if entry.get("type") != "blob" or entry.get("mode") not in ("100644", "100755"):
            raise ValueError("Official producer lacks a regular central build file: " + name)
        expected[name] = entry["sha"]
    for name, entry in source_entries.items():
        if entry.get("type") == "tree" and entry.get("mode") == "040000":
            continue
        if entry.get("type") != "blob" or entry.get("mode") not in ("100644", "100755"):
            raise ValueError("Official runtime source is not a regular file: " + name)
        if {"bin", "obj"}.intersection(PurePosixPath(name).parts):
            raise ValueError("Official runtime source tracks a build-output path: " + name)
        expected["src/" + name] = entry["sha"]
    if len(expected) <= len(BUILD_FILES):
        raise ValueError("Official producer has no runtime source files")
    current = runtime_source_snapshot(snapshot["root"])
    if current["sourceFiles"] != snapshot["sourceFiles"] or current["inventorySha256"] != snapshot["inventorySha256"]:
        raise ValueError("Selected SDK source changed during public producer qualification")
    actual = {}
    for name in current["sourceFiles"]:
        data = (Path(current["root"]) / name).read_bytes()
        if hashlib.sha256(data).hexdigest() != current["sourceFiles"][name]:
            raise ValueError("Selected SDK source changed during public producer qualification")
        actual[name] = hashlib.sha1(b"blob " + str(len(data)).encode() + b"\0" + data).hexdigest()
    if actual != expected:
        changed = sorted(name for name in actual.keys() | expected.keys() if actual.get(name) != expected.get(name))
        raise ValueError("Runtime SDK differs from published producer " + commit + ": " + ", ".join(changed))
    return {"repositoryCommit": commit, "repositoryUrl": REPOSITORY_URL, "checkoutCommit": None,
            "sourceInventorySha256": current["inventorySha256"], "matchesPublishedProducer": True,
            "qualification": "official-commit-tree-and-exact-git-blob-identities", "runtimeFileCount": len(actual),
            "officialCommit": commit_receipt, "officialTrees": [root_receipt, source_receipt], "namedTag": named_tag}
