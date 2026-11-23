# AI Assistant Instructions

This is a location-independent baseline. Its normalized text is intentionally
shared by Codex, Claude, and GitHub Copilot entry points in the superproject
and its root-declared submodules. It contains shared safeguards only; repository
documentation and configuration remain authoritative for repository-specific
rules.

## Required Hexalith LLM Baseline

Before working in a Hexalith repository, locate, read, and follow
`hexalith-llm-instructions.md`.

- If the current repository contains that file at its root, read that copy.
- Otherwise, use `git rev-parse --show-superproject-working-tree` to locate an
  enclosing superproject. When it returns no path, use the current repository
  root as the workspace. Then read
  `<workspace>/references/Hexalith.AI.Tools/hexalith-llm-instructions.md`.
- Before using that workspace copy, confirm its root `.gitmodules` declares
  `references/Hexalith.AI.Tools` as a root submodule.
- Do not initialize or update a nested submodule to locate this file. If no
  permitted location exists, stop and report the missing baseline as a blocker.

## Working in a Repository

- Work from the repository that owns the change.
- Before changing code, configuration, data, or documentation, inspect the
  relevant tracked repository guidance and configuration, including build files,
  `.editorconfig`, `.gitattributes`, tests, and architecture documentation.
- Preserve user changes. Do not revert, overwrite, clean, stage, commit, push,
  branch, or update dependencies unless the task explicitly requires it.
- Validate changes with the narrowest relevant checks and report any blocker
  with the exact command and result.

## Agent Skills

- A repository-local agent skill is a `SKILL.md` manifest and its supporting
  files.
- Never discover, load, or execute an agent skill located in a repository's
  `references/` directory. This restriction does not prevent reading ordinary
  source files or documentation in that directory when the task requires it.
- If a requested skill is available only from `references/`, explain that it is
  unavailable and use an allowed alternative.

## Git and Submodules

- Before Git work, inspect the current repository's branch, working tree,
  remotes, and recent history.
- Use Conventional Commits whenever a commit is requested. Never bypass commit
  validation.
- In an umbrella workspace, initialize or update only dependencies declared by
  the top-level workspace `.gitmodules` file.
- Never initialize or update a submodule's nested submodules unless the user
  explicitly requests that nested work. Never use recursive or remote submodule
  updates by default.
- If nested submodules were initialized accidentally, deinitialize them before
  continuing.

## Shared Entry Points

- Keep `AGENTS.md`, `CLAUDE.md`, and `.github/copilot-instructions.md`
  synchronized as normalized text when intentionally updating this shared
  baseline.
- Keep repository-specific instructions in repository documentation or
  configuration, not in these universal entry points.
<!-- hexalith-cicd:begin -->
CI/CD is owned by Hexalith.CiCd.

Hexalith.CiCd owns `.github/workflows/**`, `.github/hexalith-ci.json`,
`.github/dependabot.yml`, the `references/Hexalith.Builds` gitlink and its
`.gitmodules` section, the delivered CI/CD instruction blocks in `AGENTS.md`,
`CLAUDE.md` and `.github/copilot-instructions.md`, and the required
`permissions.deny` entries in `.claude/settings.json`. Do not edit those owned
files or portions from a module. Propose policy changes in Hexalith.CiCd.
Preserve instructions, settings and submodule sections outside those portions.

At the repository root, the guard forbids release configuration
(`.releaserc*`, `release.config.*`) and commitlint configuration
(`commitlint.config.*`, `.commitlintrc*`). In the root `package.json` it forbids the
top-level `release` and `commitlint` keys and dependency entries for
`semantic-release`, `@semantic-release/*` and `@commitlint/*` under
`dependencies`, `devDependencies`, `optionalDependencies` and
`peerDependencies`. It also forbids commitlint lines in `.husky/commit-msg`
and files matching `**/Directory.Build.rsp` or `**/bmad-testarch-ci/**`
throughout the repository.

When CI fails, fix the product code, tests or dependencies. If the pipeline is
defective, report the failure and run link to Hexalith.CiCd without working
around it. Before every push, run `python3 references/Hexalith.Builds/check.py`
from the module root and require success. Do not disable or suppress dependency
audits, skip or filter out test projects, weaken checks, or add CI skip markers
(including `[skip ci]` variants) or `skip-checks` commit trailers. Do not write
tests that assert workflow YAML text.

Keep product gates: compilation, tests, security and dependency scans, commit
format and package publishing. Module-specific product checks belong in custom
actions enabled through Hexalith.CiCd. Checks of CI itself, agent tooling, story
evidence or workflow YAML do not belong in module CI. Releases follow the
centrally scheduled nightly process.

Start each product branch from current `main`: fetch `origin main` immediately
before creating the branch from `origin/main`. Do not reuse an older base that
predates delivered CI/CD changes. Use the restricted module credential. Report
permission refusals without switching credentials or broadening access.
GitHub permits PR Update branch with the restricted module credential even
when it imports changed workflows. Do not use it to import owned CI/CD changes.
Fetch current `main`, create a new product branch, and reapply only product
changes instead.

Launch Claude Code from the module root. The project-local `Edit` deny rules
protect the whole-owned workflow/config files and Builds subtree while keeping
reads available. File rules cannot target JSON keys or instruction marker
ranges: these instructions and the shared guard protect the owned portions of
settings, instruction files and `.gitmodules`. Never install these denies in
machine-wide settings or work around them through another tool.
<!-- hexalith-cicd:end -->
