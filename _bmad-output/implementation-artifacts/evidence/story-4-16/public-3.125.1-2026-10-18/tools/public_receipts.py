"""Record official NuGet index, archive and tag receipts for the selected public EventStore producer."""
import datetime, hashlib, json, re, sys, urllib.request, zipfile, io
from pathlib import Path

VERSION, TAG, COMMIT = "3.125.1", "v3.125.1", "55432c50d4b6f1666b4bd6d4a60bfc263b2d0e0b"
PACKAGES = ["Contracts", "Client", "ServiceDefaults", "DomainService", "Server", "Admin.Abstractions", "Testing", "Gateway", "Aspire"]
out = Path(sys.argv[1])

def get(url, accept="application/json"):
    request = urllib.request.Request(url, headers={"User-Agent": "Hexalith-Works-Story416-Receipts", "Accept": accept})
    with urllib.request.urlopen(request, timeout=60) as response:
        return response.status, response.read()

def stable_key(value):
    return tuple(int(part) for part in value.split("."))

indexes, archives = [], []
for name in PACKAGES:
    package = "Hexalith.EventStore." + name
    lower = package.lower()
    url = f"https://api.nuget.org/v3-flatcontainer/{lower}/index.json"
    status, body = get(url)
    versions = json.loads(body)["versions"]
    stable = sorted((v for v in versions if re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+", v)), key=stable_key)
    indexes.append({"package": package, "url": url, "httpStatus": status, "latestStable": stable[-1],
                    "responseSha256": hashlib.sha256(body).hexdigest()})
    archive_url = f"https://api.nuget.org/v3-flatcontainer/{lower}/{VERSION}/{lower}.{VERSION}.nupkg"
    status, data = get(archive_url, "application/octet-stream")
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        nuspec = archive.read(next(n for n in archive.namelist() if n.endswith(".nuspec"))).decode()
        dlls = {n: hashlib.sha256(archive.read(n)).hexdigest() for n in archive.namelist() if n.endswith(".dll")}
    repository = re.search(r'<repository type="git" url="([^"]+)" commit="([0-9a-f]{40})"', nuspec)
    cache = Path.home() / ".nuget/packages" / lower / VERSION / f"{lower}.{VERSION}.nupkg"
    cache_meta = cache.with_name(".nupkg.metadata")
    archives.append({"package": package, "version": VERSION, "archiveUrl": archive_url, "httpStatus": status,
                     "sha256": hashlib.sha256(data).hexdigest(), "sha512Base64": __import__("base64").b64encode(hashlib.sha512(data).digest()).decode(),
                     "repositoryUrl": repository.group(1) if repository else None, "repositoryCommit": repository.group(2) if repository else None,
                     "dllSha256": dlls,
                     "localCache": {"path": str(cache), "present": cache.is_file(),
                                    "sha256": hashlib.sha256(cache.read_bytes()).hexdigest() if cache.is_file() else None,
                                    "source": json.loads(cache_meta.read_text()).get("source") if cache_meta.is_file() else None}})
status, body = get("https://api.github.com/repos/Hexalith/Hexalith.EventStore/git/ref/tags/" + TAG, "application/vnd.github+json")
tag = json.loads(body)
result = {"checkedUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(), "selectedVersion": VERSION,
          "indexes": indexes, "allLatestStableSelected": all(row["latestStable"] == VERSION for row in indexes),
          "archives": archives,
          "oneProducerCommit": sorted({row["repositoryCommit"] for row in archives}),
          "allArchivesBindNamedProducer": all(row["repositoryCommit"] == COMMIT and row["repositoryUrl"] == "https://github.com/Hexalith/Hexalith.EventStore" for row in archives),
          "allLocalCachesMatchOfficialArchives": all(row["localCache"]["sha256"] == row["sha256"] and row["localCache"]["source"] == "https://api.nuget.org/v3/index.json" for row in archives),
          "namedTag": {"tag": TAG, "url": "https://api.github.com/repos/Hexalith/Hexalith.EventStore/git/ref/tags/" + TAG, "httpStatus": status,
                       "responseSha256": hashlib.sha256(body).hexdigest(), "object": tag.get("object"),
                       "bindsProducer": tag.get("object", {}).get("type") == "commit" and tag.get("object", {}).get("sha") == COMMIT}}
(out / "public-producer-receipts.json").write_text(json.dumps(result, indent=2) + "\n")
print(json.dumps({k: result[k] for k in ("checkedUtc", "allLatestStableSelected", "oneProducerCommit", "allArchivesBindNamedProducer", "allLocalCachesMatchOfficialArchives")}, indent=1), result["namedTag"]["bindsProducer"])
