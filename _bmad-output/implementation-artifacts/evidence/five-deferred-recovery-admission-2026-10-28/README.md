# Five deferred recovery admission checks

This packet supports [the completed-work specification](../../spec-five-deferred-recovery-admission.md).
The baseline was clean `main` at `3b28359c6d3c633999aa6992aab01479eaee19d8`.
The manifest binds the changed test sources and compressed evidence with SHA-256.

The original Redis helper fails both new field-expiration cases (`red.log.gz`, `red.xml.gz`).
The intermediate inventory diagnostic identifies the logically empty hash that Redis can retain
physically. Final checks cover expiration before and after restore, exact live field bytes and
deadlines, partial and complete hash expiry, whole-key expiry, and corruption refusal.

The other four closures verify current contracts: bounded protection metadata and cumulative byte
budgets; SDK refusal of duplicate/noncanonical catalog tenants; replacement of the retired event
processor by validated SDK wakeups; and explicit mixed-mode restore refusal before package resolution.
The absent-package sensitivity control confirms that a valid mode reaches package resolution.
Repeated PackageVersion ledger observations count once.

The independent review's capture-acquisition race remains a separate unverified follow-up. These
checks concern fields present at capture that expire afterward. Capture still refuses if acquisition
cannot produce compatible evidence.

The exact Platform baseline startup failed with NU1605 downgrades. See `baseline.log.gz` and the
specification for the command and selected versions. No AppHost started, no dependencies changed,
and this packet does not qualify a new live Platform run or public SDK producer.
