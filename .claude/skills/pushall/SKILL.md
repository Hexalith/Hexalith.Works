---
name: pushall
description: Process every root-declared submodule in its own subagent, then commit, merge, push, and safely prune branches in the main repository after all subagents finish. Only invoke this directly with /pushall — never trigger it automatically.
disable-model-invocation: true
allowed-tools: Bash(git *)
---

# /pushall — sync, merge, and prune every branch

Runs the repo-sync procedure for every root-declared submodule in a dedicated subagent. After all
subagents finish, runs the procedure in the superproject so its commit picks up the updated
submodule pointers and its push happens last.

## Required preflight

Before running any Git command, read
`references/Hexalith.AI.Tools/hexalith-git-instructions.md` completely and follow it. Treat it as
authoritative. If it is unavailable, stop `/pushall` and report the missing instructions.

## /pushall-specific rules (never deviate)

- Do not invoke `bmad-build` or any other BMad skill during `/pushall`; execute simple Git
  operations such as commits and pushes directly through this skill's procedure.
- Only delete local branches with `git branch -d` (safe delete, refuses non-merged branches).
  Never use `-D`.
- Only delete an `origin` remote branch after its fetched tip is merged into the default branch and
  that default branch has been pushed successfully. Never delete the remote default branch or
  `origin/HEAD`. Use `--delete` with an exact expected-OID lease so deletion fails if the branch
  moved after it was merged.
- On a merge conflict, inspect the base and both sides and try to resolve it while preserving the
  intent of both branches. Never auto-resolve with `-X ours`/`-X theirs` or blindly accept one
  complete side. If a safe resolution cannot be determined or validated, run `git merge --abort`,
  record that ref as skipped, do not prune it, and continue.
- If a repo has no remote, push fails, or the local default branch has diverged from
  `origin/<default-branch>` (fast-forward not possible), record it as skipped/failed and continue
  with the rest — never stop the whole run for one repo.
- Always end each repo back on its default branch, never detached HEAD or a feature branch.
- Give a one-line progress update per repo as you go, and a final consolidated report (per repo:
  local and remote branches merged, local and remote branches deleted, anything skipped/failed,
  push status).

## Subagent coordination

- Keep the calling agent as the coordinator. It owns only orchestration, the final superproject
  procedure, and the consolidated report.
- If subagents are unavailable, stop before modifying any repository and report that `/pushall`
  requires subagent support. Never fall back to processing submodules in the coordinator.
- Assign each root-declared submodule path to its own dedicated subagent. Run subagents concurrently
  up to the available concurrency limit; when capacity is limited, start the remaining dedicated
  subagents as slots become available.
- Give each subagent exactly one submodule path and the per-repo procedure below. Require it to read
  the Git instructions in the required preflight before running Git commands.
- Restrict each subagent to its assigned submodule repository. It must not run Git commands in the
  superproject or another submodule, and it must not initialize or process nested submodules.
- Require each subagent to return a structured result containing its path, default branch, merged
  refs, deleted local and remote branches, skipped or failed operations, final branch, and push
  status.
- Wait for every declared submodule's dedicated subagent to finish with a success, skipped, or
  failed result. Do not run, commit, or push the superproject while any submodule subagent remains
  active or unreported.
- If a subagent fails without a usable result, record that failure and ensure the subagent is no
  longer running before crossing the barrier. A failed submodule does not cancel the remaining
  subagents or permit an early superproject push.

## Per-repo procedure

Apply this to one repo directory `<dir>` at a time (a submodule path, or `.` for the superproject):

1. `git -C <dir> fetch --all --prune`
2. Determine the default branch: prefer `main`, else `master`, else
   `git -C <dir> remote show origin | sed -n 's/.*HEAD branch: //p'`.
3. If `git -C <dir> status --porcelain` is non-empty, stage and commit everything:
   `git -C <dir> add -A && git -C <dir> commit -m "build: sync local changes via /pushall"`.
4. `git -C <dir> checkout <default-branch>`
5. Try `git -C <dir> merge --ff-only origin/<default-branch>` to catch up with the remote first.
   If this fails because local and remote diverged, record it and skip the rest of this repo.
6. Build the merge list from both kinds of refs:
   - Local branches: `git -C <dir> branch --format='%(refname:short)'`, excluding the default
     branch.
   - Remote branches: `git -C <dir> for-each-ref
     --format='%(refname) %(objectname) %(symref)' refs/remotes/origin/`. Exclude symbolic refs and
     `refs/remotes/origin/<default-branch>`, derive each branch name by removing the
     `refs/remotes/origin/` prefix, and keep its fetched object ID for the deletion lease in step
     10.
7. For every ref in the merge list (local refs first, then `origin` remote refs):
   - `git -C <dir> merge --no-ff <ref> -m "build: merge <ref> into <default-branch> via /pushall"`
   - On conflict, list unresolved paths with `git -C <dir> diff --name-only --diff-filter=U`, inspect
     the base and both sides, and edit the files to preserve compatible changes and intended
     behavior from both branches.
   - Stage the resolved paths, verify that no unresolved paths remain, run the repository's most
     relevant available validation, then complete the merge with `git -C <dir> commit --no-edit`.
   - If the conflict cannot be resolved safely, unresolved paths remain, or validation fails, run
     `git -C <dir> merge --abort`, record the ref as skipped, and continue to the next ref.
8. `git -C <dir> push origin <default-branch>`. Record failure and continue if it's rejected.
9. Delete local branches now fully merged into the default branch (excluding the default branch
   itself): `git -C <dir> branch --merged <default-branch>` minus the default branch, then
   `git -C <dir> branch -d <each>`.
10. Only if step 8 succeeded, prune each non-default `origin` branch whose fetched object ID is an
    ancestor of the default branch:
    - Verify with `git -C <dir> merge-base --is-ancestor <fetched-object-id> <default-branch>`.
    - Delete with `git -C <dir> push
      --force-with-lease=refs/heads/<branch>:<fetched-object-id> origin --delete <branch>`.
    - If the lease or deletion fails, record the branch as not pruned and continue. Never retry
      without the exact lease.
11. `git -C <dir> fetch --all --prune` to remove deleted or otherwise stale remote-tracking refs.

## Execution order

1. Complete the required preflight.
2. Read the root `.gitmodules` for the declared submodule paths (`references/...`).
3. Spawn one dedicated subagent per declared submodule path as described in **Subagent
   coordination**. Queue launches only when the concurrency limit requires it; never assign two
   submodule paths to the same subagent.
4. Collect each finished subagent's structured result and launch queued subagents until every
   declared submodule has a terminal result.
5. After the last submodule subagent has finished and none remain active, run the per-repo procedure
   for the superproject (`.`). Its step 3 auto-commit will pick up the updated submodule gitlink
   pointers from the completed submodule runs along with any other superproject changes. This is
   the only point at which the main repository may be pushed.
6. Print the consolidated report, including every subagent result and the superproject result.
