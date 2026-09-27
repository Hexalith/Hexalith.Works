# Story 4.13 accountable data-owner approval — 2026-09-27

In the Story 4.13 build conversation, after being asked for the data-owner
approval and the remaining Platform, restore, and release evidence, the user
stated: “this project has only one contributor, me Jérôme Piquot, Owner. I
approve”.

Record this as Jérôme Piquot's accountable data-owner approval of the Story
4.13 trusted-effect durable receipt and the retention, legal-hold, and joint
offboarding behavior described in the approved story spec and EventStore
trusted-effects guide. The documented lifecycle keeps source and target
evidence together, pauses erasure under legal hold, and erases registered
evidence through one tenant decision.

This approval does not attest that a backup/restore drill ran, that Platform's
append-only privileged audit or caller mTLS/ACL boundary is operating, or that
an immutable EventStore package release was published. Production trusted-effect
admission remains closed until those independent gates are evidenced.
