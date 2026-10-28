# Input recheck scope correction — 2026-10-28

The retained `tools/bind_inputs.py` checks hashes of recorded Platform and Works consumer paths. It does not enumerate complete consumer membership. Its historical `input-binding.json` scope sentence therefore overstates that part of the evidence: an added consumer source could escape the original recheck. The separate fixture helper does compare fixture membership. The original helper and result remain unchanged for historical reproduction; they do not qualify complete consumer source membership.

Use the repository's `scripts/recheck-platform-proof-inputs.py` for a fresh complete inventory comparison:

```bash
python3 scripts/recheck-platform-proof-inputs.py LEDGER.json ISOLATION.json OUTPUT.json --current-platform-root /absolute/platform/root
```

The supplied isolation descriptor selects the original isolated Platform and Works roots. All selected roots must still exist; a missing historical isolation is a refusal, not a new proof. The replacement compares added, changed and removed Platform C#/project files (plus apphost.cs), Works C#/project files and three root build files, and the complete fixture inventory. It separately checks the recorded configuration, overlays, verifier and helpers. Unrelated current Platform source differences are reported separately and remain unqualified. This does not authenticate a caller-supplied ledger or requalify historical/live behavior.

Current Platform `require_unchanged_inputs` also compares complete consumer membership. Its 19 executing input controls and the root helper's independent CLI controls are recorded in the [requalification packet](../../five-deferred-proof-requalification-2026-10-28/README.md). This amendment corrects the historical scope claim and adds new checks; it does not rewrite the original run.
