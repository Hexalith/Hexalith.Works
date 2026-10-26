"""Offline byte bindings shared by the successor acceptance tools."""
import hashlib
import io
import json
from pathlib import Path, PurePosixPath
import stat
import zipfile


ARTIFACTS = {
    "12693567134": "0dcbd6ac49bda95a029551bfcf1af09255b42f6cce58997d28c3ab1dbfcbaec7",
    "12691976576": "d59b453016ad2e8df7b1a0b39405534f1c75c27882168181dfe6d564633a3a17",
}
CI_LOG_SHA256 = "072ce877caaaec5d03c7cba290f6c9c6b52b9f0ec10e25f9eece00056d7afb7b"
HOSTED_MANIFEST_SHA256 = "bc0601d6594a1409d21f22921a43f1a286d9c8e5ca6d3d95d4f3f4dd95c4aa8f"
WORKS_COMMIT = "09cc8e19c23ec15a0542499892bbf5cd4b8f38dd"


def sha256(raw):
    return hashlib.sha256(raw).hexdigest()


def verified_members(archive, expected_sha256, extracted=None):
    """Bind each consumed extraction to its original verified ZIP, then return ZIP bytes.

    Consumers evaluate these returned bytes (or private copies), avoiding a second read
    of a separately mutable extraction after validation.
    """
    archive = Path(archive)
    original = archive.read_bytes()
    if sha256(original) != expected_sha256:
        raise ValueError("ZIP digest mismatch: " + str(archive))
    members = {}
    names = set()
    total = 0
    with zipfile.ZipFile(io.BytesIO(original)) as package:
        for info in package.infolist():
            name = PurePosixPath(info.filename)
            normalized = str(name)
            if (name.is_absolute() or ".." in name.parts or "\\" in info.filename
                    or stat.S_ISLNK(info.external_attr >> 16) or normalized in names
                    or normalized in ("", ".")
                    or info.filename.rstrip("/") != normalized):
                raise ValueError("Unsafe or duplicate ZIP member: " + info.filename)
            names.add(normalized)
            total += info.file_size
            if total >= 500_000_000:
                raise ValueError("ZIP expanded size exceeds limit")
            if not info.is_dir():
                members[normalized] = package.read(info)
    if extracted is not None:
        extracted = Path(extracted)
        actual = set()
        if extracted.is_symlink():
            raise ValueError("Symlink extraction root")
        for path in extracted.rglob("*"):
            if path.is_symlink():
                raise ValueError("Symlink extracted input: " + str(path))
            if path.is_file():
                name = path.relative_to(extracted).as_posix()
                actual.add(name)
                if name not in members or path.read_bytes() != members[name]:
                    raise ValueError("Extracted input differs from verified ZIP member: " + name)
        if actual != set(members):
            raise ValueError("Extraction is missing ZIP members: " + str(sorted(set(members) - actual)))
    return members


def sealed_native_reader(packet):
    """Read retained API bytes only when they agree with the sealed hosted packet."""
    packet = Path(packet)
    manifest_raw = (packet / "manifest.json").read_bytes()
    if sha256(manifest_raw) != HOSTED_MANIFEST_SHA256:
        raise ValueError("Hosted packet manifest digest mismatch")
    records = {item["source"]: item for item in json.loads(manifest_raw)["files"].values()
               if "sourceSha256" in item}

    def read(path):
        path = Path(path)
        item = records.get(str(path))
        raw = path.read_bytes()
        if item is None or sha256(raw) != item["sourceSha256"] or len(raw) != item["sourceBytes"]:
            raise ValueError("Native input differs from sealed packet binding: " + str(path))
        return raw

    return read


def write_json_new(path, value):
    with Path(path).open("x", encoding="utf-8") as stream:
        stream.write(json.dumps(value, indent=2) + "\n")
