"""Successor downloader; an absent API digest supplies no digest-match claim."""
import argparse
import datetime
import json
from pathlib import Path
import subprocess

from acceptance_support import sha256, verified_members, write_json_new


def digest_evidence(metadata, actual_sha256):
    declared = metadata.get("digest")
    if declared is None:
        return {"declaredDigest": None, "declaredDigestStatus": "unavailable",
                "declaredDigestMatched": None}
    if declared != "sha256:" + actual_sha256:
        raise ValueError("Artifact API digest mismatch")
    return {"declaredDigest": declared, "declaredDigestStatus": "matched",
            "declaredDigestMatched": True}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("artifact_id", type=int)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    folder = args.output
    folder.mkdir(parents=True, exist_ok=False)
    command = ["gh", "api", "repos/Hexalith/Hexalith.Works/actions/artifacts/" + str(args.artifact_id)]
    raw = subprocess.run(command, capture_output=True, check=True).stdout
    (folder / "metadata.json").write_bytes(raw)
    meta = json.loads(raw)
    if (meta["workflow_run"]["id"] != 40407181685
            or meta["workflow_run"]["head_sha"] != "09cc8e19c23ec15a0542499892bbf5cd4b8f38dd"):
        raise ValueError("Artifact does not belong to the selected run/head")
    archive = folder / "original-artifact.zip"
    download = ["gh", "api", command[-1] + "/zip"]
    with archive.open("xb") as stream:
        subprocess.run(download, stdout=stream, stderr=subprocess.PIPE, check=True)
    digest = sha256(archive.read_bytes())
    declared = digest_evidence(meta, digest)
    members = verified_members(archive, digest)
    extracted = folder / "extracted"
    extracted.mkdir()
    for name, data in members.items():
        path = extracted / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
    record = {"downloadedAt": datetime.datetime.now(datetime.UTC).isoformat(),
              "metadataCommand": command, "downloadCommand": download,
              "artifactId": args.artifact_id, "name": meta["name"],
              "zipBytes": archive.stat().st_size, "zipSha256": digest, **declared,
              "expandedBytes": sum(map(len, members.values())),
              "members": {name: {"bytes": len(data), "sha256": sha256(data)}
                          for name, data in members.items()}}
    write_json_new(folder / "download-and-extraction.json", record)
    print(json.dumps({key: value for key, value in record.items() if key != "members"}, indent=2))


if __name__ == "__main__":
    main()
