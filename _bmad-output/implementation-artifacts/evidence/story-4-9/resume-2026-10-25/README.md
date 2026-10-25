# Story 4.9 resumed verification — 2026-10-25

The public-producer prerequisite has cleared. The existing Works pin is **EventStore 3.127.0**; fresh official Client, DomainService and Aspire archives all identify producer `e1d8b17160202761ab5c9606cafbcf4d1a4b92c3`. Platform's unchanged canonical helper accepts all **2,197 runtime inputs**, and the retained-seal fix is present in the selected tag. This probe qualifies source against public package metadata and official trees; it does not replace the complete gate's consumer-assembly qualification.

The requested CI profile, blocking entry point and bounded 60-second real Redis readiness implementation were already present. This attempt adds two null guards in Platform's `PlatformHostedPublishing.Describe` and `ComposeNativeProbe`, resolving the source build's CA1062 failures. Valid graph serialization and probe selection remain unchanged. The exact owner diff is [platform-null-guards.patch](platform-null-guards.patch); [source-inputs.json](source-inputs.json) records revisions, selected hashes, the complete post-edit bootstrap inventory identity and its final equality check. The Platform edit remains uncommitted.

| Executed check | Result |
| --- | --- |
| Official producer prerequisite | Pass, public 3.127.0 and 2,197 runtime inputs |
| Works tooling | 76 passed |
| Platform verifier/producer/snapshot/toolchain contracts | 150 passed |
| Component helper contracts | 13 passed |
| Actual CI entry point, bootstrap-only, before and after the owner fix | Both exit 0; parity remains incomplete |
| Fresh Debug/source solution restore | Exit 0 |
| Initial Debug/source solution build | Exit 1, two CA1062 errors in hosted publishing |
| Resumed Debug/source solution build | Exit 0, zero warnings/errors |
| Unit | 597 passed |
| Property | 3 passed, 300 generated cases |
| Architecture | 398 passed |
| Ordinary integration | 1,004 passed, 16 failed, seven explicit facts not run; zero skips |
| Redis fixture ownership and endpoint cancellation within that integration run | 17 passed |
| Direct nested Platform Composition test build | Exit 1, 24 inherited CA2007 diagnostics |
| Owner-layout diagnostic build | Exit 1, 84 pre-existing Builds style diagnostics |
| Diagnostic retry reusing the current Works-built Tooling assembly | Exit 1, 56 missing transitive reference errors |
| Final diagnostic retry adding the same direct test assembly reference | Exit 1, four missing NuGet reference errors; hosted-publishing facts did not run |

All sixteen ordinary integration failures are `WorksPlatformFleetRestoreTests` cases failing in `WorksSdkProofRedis.DockerAsync` during startup and cleanup, before their proof bodies. No additional exclusions were introduced. `docker version --format '{{.Server.Version}}'` exits 1 with the Docker Desktop WSL-integration message. `/usr/bin/docker` points to a missing binary, and `/var/run/docker.sock` does not answer a five-second HTTP ping. The Windows `docker.exe` reaches Docker 29.8.2 through named pipes, which does not restore the Linux endpoint required by this fleet. [docker-prerequisites.json](docker-prerequisites.json) preserves both observations. No host integration setting, daemon, limit or foreign resource changed.

The owner-layout attempts are explicitly diagnostic. They use the exact exported Platform source; the last two reuse the successful source build's hash-bound Tooling assembly, and the last supplies it to the test project through [diagnostic-owner-reference.targets](diagnostic-owner-reference.targets). No repository test or analyzer setting changed. These attempts neither pass the owner suite nor count as canonical parity evidence. Their original failures remain preserved.

[commands.json](commands.json) contains exact commands, working directories, exit codes and output paths. [test-results.json](test-results.json) lists every integration failure and explicit exclusion. Compressed logs, XML, official responses, source inventories and the post-edit bootstrap/binding are retained under `raw/`; `manifest.json` binds their stored and original bytes.

Story and sprint remain `in-progress`; both qualification-bearing tasks remain unchecked. Restore the Linux Docker prerequisites, resolve the separate owner-suite build environment, and run the unchanged full canonical gate followed by the actual CI profile against fresh unchanged inputs. No new live R1–R11, rollback, hosted CI or production acceptance is claimed. Historical packets, pins, frozen intent and baseline commit are preserved.
