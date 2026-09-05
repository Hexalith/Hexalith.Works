---
status: blocked
---

# BMad Build Auto Result

Status: blocked
Blocking condition: dirty working tree

The mandatory version-control sanity check stopped this run. `git add --refresh -- .`
completed, but the tree remained dirty with staged projection implementation and
spec changes plus untracked projection descriptor files. No background process is
running, and the deferred-work ledger was not edited.
