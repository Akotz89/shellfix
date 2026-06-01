# Tracking Map

This map keeps GitHub issues, Linear issues, and release/doc state aligned.

| GitHub | Linear | Status | Evidence |
|---|---|---|---|
| #5 Keep Windows-native dev tools out of WSL routing | OPE-108 | Closed | Fixed in `v1.6.0`; closeout comment links commit evidence |
| #6 Fix prebuilt release install path | OPE-109 | Closed | Fixed in `v1.6.0`; installer checks repo and flat release layouts |
| #7 Guarded profile snippet install | OPE-110 | Closed | Fixed in `v1.7.0`; profile snippet and uninstall support |
| #8 Remove machine-specific paths | OPE-111 | Closed | Fixed in `v1.6.0`; profile and tests no longer hardcode maintainer path |
| #9 Runtime WSL distro configuration | OPE-112 | Closed | Fixed in PR #24; `SHELLFIX_WSL_DISTRO` runtime source |
| #10 Release checksums and trust docs | OPE-113 | Closed | Fixed in `v1.7.0`; release workflow emits `checksums.txt` |
| #11 CI functional smoke tests | OPE-114 | Closed | Fixed in PR #25; CI runs `test-ci-smoke.ps1` |
| #12 Repo-local test suites | OPE-115 | Closed | Fixed in PR #24; tests default to `shim/out/powershell.exe` |
| #13 Shortcut patching and launcher quoting | OPE-116 | Closed by PR #28 | Fixed in `v1.7.1`; shortcut launcher script and `install.ps1 -TestShortcuts` evidence |
| #14 README verification and behavior claims | OPE-117 | Closed | Fixed in PR #26; mode-specific verification docs |
| #15 GitHub repository hygiene | OPE-118 | Closed by PR #27 | PR template, issue templates, label consolidation, maintainer checklist |
| #16 Continuity reconciliation | OPE-119 | Closed by PR #27 | Tracking map, closeout checklist, reconciliation commands |
| #17 Reduce AI smell | OPE-120 | Closed by PR #27 | Public docs wording pass |
| #18 Hallucination guardrails | OPE-121 | Closed by PR #27 | Claim evidence inventory and release-note guidance |
| #29 GitHub Actions runner deprecation notices | OPE-122 | Closed by PR #30 | CI pinned to `windows-2025`; JavaScript actions opted into Node 24 |
| PR #38 Add Antigravity run_command guard | AZR-142 | In Progress | PR #38 is open and mergeable; its current GitHub status check is the source of truth for the release-candidate head. Release/tag/checksum evidence is still required before closure. |

## Current Release Candidate Notes

- Latest public release is `v1.7.1`; the Antigravity guard is not in a published release asset yet.
- PR #38 (`release/shellfix-antigravity-guard`) is open and mergeable; verify the current head's CI run before merge.
- Latest remote CI on `master` is green, but it does not include PR #38 until merge.
- Current release candidate ships `shellfix guard antigravity-run-command`, a first-class Antigravity PreToolUse guard for fragile inline WSL/bash command shapes.
- `AZR-142` has current PR, CI, local verification, and installed-runtime evidence as of 2026-06-02.
- Local Antigravity `run_command` hooks on Aaron's machine now call the installed Shellfix guard directly, but that local hook wiring is machine configuration rather than published release evidence.
- `AZR-132` has a clarification comment separating the completed shim-bypass fix from the current inline command-shape limitation.
- Do not close `AZR-142` until GitHub PR/CI/release evidence proves the guard shipped and the README hook instructions are included in the release.

## Manual Reconciliation

Run before each release:

```powershell
gh issue list --repo Akotz89/shellfix --state all --limit 50
gh pr list --repo Akotz89/shellfix --state open
gh release list --repo Akotz89/shellfix --limit 10
```

Then verify:

- Open GitHub issues match open Linear work.
- Closed GitHub issues have evidence comments.
- Done Linear issues have evidence comments.
- README and CHANGELOG describe only released or merged behavior.
- Release notes do not include planned work as fixed behavior.
