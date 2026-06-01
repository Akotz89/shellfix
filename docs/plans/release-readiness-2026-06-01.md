# Shellfix Release Readiness Plan - 2026-06-01

## Objective

Finish Shellfix as a professional, release-ready GitHub project with verified
runtime behavior, clean Linear tracking, accurate public docs, and a repeatable
release process.

## Current Authoritative State

### Repo and Runtime

- Repo root: `C:\Users\Aaron\Azyrra\projects\shellfix`
- Default GitHub branch: `master`
- Current public repo: `https://github.com/Akotz89/shellfix`
- Latest public release: `v1.7.1`, published 2026-05-20
- Latest remote CI baseline: success on `b2d884f3dfb69c15c66b58c098fb13893365652e`
- Current release-candidate branch: `release/shellfix-antigravity-guard`
- Current release-candidate commit: PR #38 head; verify the current SHA and CI
  run in GitHub before merge or release.
- Current release-candidate content diff, ignoring CRLF/stat churn:
  - `.github/workflows/ci.yml`
  - `.github/workflows/release.yml`
  - `CHANGELOG.md`
  - `README.md`
  - `docs/CLAIM_EVIDENCE.md`
  - `docs/TRACKING.md`
  - `docs/plans/release-readiness-2026-06-01.md`
  - `shim/PowerShellShim.cs`
  - `src/Shellfix.Cli/Commands/GuardCommand.cs`
  - `src/Shellfix.Cli/Commands/DoctorCommand.cs`
  - `src/Shellfix.Cli/Commands/TestCommand.cs`
  - `src/Shellfix.Cli/Models/InstallState.cs`
  - `src/Shellfix.Cli/ShellfixCli.cs`
  - `src/Shellfix.Cli/Services/AntigravityRunCommandGuard.cs`
  - `src/Shellfix.Cli/Services/AntigravitySettingsManager.cs`
  - `src/Shellfix.Cli/Utilities/Backup.cs`
  - `src/Shellfix.Core/NativeToolResolver.cs`
  - `test-ci-smoke.ps1`
  - `test-proxy.ps1`
  - `test-replay.ps1`
  - `test.ps1`

### Verified Local Behavior

- Installed Shellfix doctor currently passes when invoked by absolute path.
- Installed runtime reports Antigravity settings patched in both known settings
  trees, no live bypassing Antigravity PowerShell child processes, and
  `install-drift: pass`.
- The installed product guard at
  `C:\Users\Aaron\AppData\Local\Programs\Shellfix\shellfix.exe guard
  antigravity-run-command` denies the PixelSim-style inline WSL/bash loop with
  exit 2 and allows script-file WSL commands with exit 0.
- Local Antigravity `run_command` hooks on Aaron's machine now call the
  installed Shellfix guard directly. This proves the local mitigation path, but
  release closure still requires published release assets.

### GitHub State

- GitHub is public and has MIT license metadata.
- CI workflow is green on the latest pushed `master` commit.
- PR #38 exists for the current release-candidate branch:
  `https://github.com/Akotz89/shellfix/pull/38`.
- PR #38 is open and mergeable; verify the current head's CI run in GitHub
  before merge.
- PR #38 CI is the authoritative GitHub check for the release-candidate branch.
- No tag or release exists for the current local changes yet.
- All GitHub issues are currently closed, including older run-command issues
  `#31`, `#32`, and `#33`.

### Linear State

- `AZR-132` is Done and covers the older "shim not active in run_command"
  diagnosis.
- `AZR-142` is In Progress and matches the current remaining release gate:
  fragile inline WSL/bash commands are handled by the product guard, but the
  guard is not in a published release asset until PR #38 is merged and released.
- `AZR-142` has PR, CI, local verification, installed-runtime, and remaining
  release-gate evidence comments as of 2026-06-02.
- `AZR-132` has a clarification comment distinguishing the completed historical
  shim-bypass issue from the current AZR-142 inline command-shape limitation.

## Release Requirements

### Product Requirements

- Shellfix installs and uninstalls reversibly.
- Shellfix doctor accurately distinguishes:
  - installed shim health;
  - installed CLI health;
  - PATH precedence;
  - Antigravity settings health;
  - live Antigravity bypass process health;
  - native tool resolution health.
- Native Windows developer tools remain native-first.
- NPM/NPX resolution is deterministic on nvm4w installations.
- Antigravity settings repair covers both `Antigravity IDE` and `Antigravity`
  settings trees.
- Public docs do not imply that all Antigravity `run_command` inline WSL/bash
  payloads are fixed when current evidence shows a remaining limitation.

### Repository Requirements

- README, SECURITY, CONTRIBUTING, CHANGELOG, and claim evidence are consistent
  with the current release candidate.
- CI builds both shim and CLI and runs meaningful tests.
- Release workflow publishes:
  - `powershell.exe`
  - `shellfix.exe`
  - `install.ps1`
  - `Microsoft.PowerShell_profile.ps1`
  - `launch-ide.bat`
  - `checksums.txt`
- PR template and issue templates exist and match the project workflow.
- Dirty working tree is reduced to intentional release-candidate changes before
  PR.

### Linear Requirements

- `AZR-142` must be updated with current evidence and next acceptance criteria.
- `AZR-132` should receive a clarification comment that the old activation
  failure is not the same as the current inline command-shape limitation.
- Stale Shellfix issues should not be bulk-closed without evidence. Done issues
  need PR/commit/test/release links before being treated as release evidence.

## Remaining Gaps

1. **Current release-candidate changes are on GitHub but not merged.**
   Evidence now present: PR #38 is open and mergeable, with CI required on the
   current head before merge.
   Evidence still needed: merged commit on `master` or an explicit release
   branch decision, with the GitHub PR status check green at merge time.

2. **No current release exists for the local fixes.**
   Evidence needed: tag, release assets, checksum verification, and release notes.
   Current evidence: latest public release remains `v1.7.1`, published
   2026-05-20.

3. **Antigravity inline `run_command` guard is PR-only until released.**
   Evidence now present: PR #38 and CI prove `shellfix guard
   antigravity-run-command` on the release-candidate branch; local installed
   runtime also verifies the guard behavior. Evidence still needed: merged
   commit and release tag/assets containing the guard and README hook
   instructions.

4. **Final release evidence is still missing from Linear.**
   Evidence now present: PR URL, passing CI run, and installed-runtime checks
   are linked back to `AZR-142`. Evidence still needed: release tag and asset
   checksum verification.

5. **Line-ending and Windows-mount stat churn must be handled deliberately.**
   Evidence needed: either a separate normalization commit or explicit staging of
   only the 14 real release-candidate files. `git diff --name-only` is the
   authoritative content-diff list; `git status` may show extra no-patch files on
   this Windows mount after line-ending refresh.

6. **The full local suite is now release-clean for this host, with scoped skips.**
   Evidence from 2026-06-01: `test.ps1` now reports 48/48 pass and 14 explicit
   environment skips. The skipped checks require direct host-shell visibility for
   WSL/native tools; the shim-focused WSL, proxy, replay, native-resolution, and
   doctor paths pass.

## Execution Plan

### Phase 1 - Stabilize Release Candidate

- Keep the current native-tool and Antigravity-settings code changes scoped.
- Add or confirm direct regression tests for:
  - `npx -y npm@latest --version`;
  - full-path native tool fallback;
  - multiple Antigravity settings trees;
  - direct `for ...; do ...; done` WSL routing claim.
- Ship and document `shellfix guard antigravity-run-command` as the
  product-owned Antigravity hook mechanism for fragile inline WSL/bash payloads.

### Phase 2 - Verify Locally

Run, at minimum:

```powershell
dotnet publish shim/PowerShellShim.csproj -c Release -o shim/out --nologo
dotnet publish src/Shellfix.Cli/Shellfix.Cli.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o src/Shellfix.Cli/out --nologo
.\test-ci-smoke.ps1 -ShimPath .\shim\out\powershell.exe -WslDistro Ubuntu-24.04
.\test.ps1 -ShimPath .\shim\out\powershell.exe -WslDistro Ubuntu-24.04
.\test-proxy.ps1 -ShimPath .\shim\out\powershell.exe -WslDistro Ubuntu-24.04
.\test-replay.ps1 -ShimPath .\shim\out\powershell.exe -WslDistro Ubuntu-24.04
.\src\Shellfix.Cli\out\shellfix.exe test --antigravity-settings
.\src\Shellfix.Cli\out\shellfix.exe test --antigravity-guard
.\src\Shellfix.Cli\out\shellfix.exe test --incidents
.\src\Shellfix.Cli\out\shellfix.exe doctor --json
```

Current 2026-06-01 verification snapshot:

- `dotnet publish shim/PowerShellShim.csproj -c Release -o shim/out --nologo`:
  pass after rerunning serially.
- `dotnet publish src/Shellfix.Cli/Shellfix.Cli.csproj ... -o src/Shellfix.Cli/out --nologo`:
  pass.
- `.\test-ci-smoke.ps1 -ShimPath .\shim\out\powershell.exe -WslDistro Ubuntu-24.04`:
  pass, 8/8.
- `.\test-proxy.ps1 -ShimPath .\shim\out\powershell.exe -WslDistro Ubuntu-24.04`:
  pass, 34/34.
- `.\test-replay.ps1 -ShimPath .\shim\out\powershell.exe -WslDistro Ubuntu-24.04`:
  pass, 25/25.
- `.\src\Shellfix.Cli\out\shellfix.exe test --antigravity-settings`: pass.
- `.\src\Shellfix.Cli\out\shellfix.exe test --antigravity-guard`: pass.
- `.\src\Shellfix.Cli\out\shellfix.exe test --incidents`: pass.
- `.\src\Shellfix.Cli\out\shellfix.exe doctor --json`: pass, including
  Antigravity settings in 2 files and no live bypassing child processes.
- `.\test.ps1 -ShimPath .\shim\out\powershell.exe -WslDistro Ubuntu-24.04`:
  pass, 48/48 with 14 explicit environment skips.
- `git diff --check`: pass after line-ending and trailing-whitespace cleanup.

Post-cleanup rerun after LF normalization:

- `.\test-ci-smoke.ps1 -ShimPath .\shim\out\powershell.exe -WslDistro Ubuntu-24.04`:
  pass, 8/8.
- `.\test.ps1 -ShimPath .\shim\out\powershell.exe -WslDistro Ubuntu-24.04`:
  pass, 48/48 with 14 explicit environment skips.
- `.\test-proxy.ps1 -ShimPath .\shim\out\powershell.exe -WslDistro Ubuntu-24.04`:
  pass, 34/34.
- `.\test-replay.ps1 -ShimPath .\shim\out\powershell.exe -WslDistro Ubuntu-24.04`:
  pass, 25/25.
- Manual guard check with Antigravity-style JSON on stdin: fragile inline
  `wsl ... bash -c 'for f ... "$f" ...'` returns `{"decision":"deny",...}` and
  exit 2; script-file WSL command returns `{"decision":"allow"}` and exit 0.

### Phase 3 - Clean Tracking

- Add evidence comment to `AZR-142` with:
  - failing transcript timestamp;
  - verified guard mitigation timestamp;
  - distinction between Shellfix doctor pass and Antigravity inline command gap;
  - acceptance criteria for product completion.
- Add clarification comment to `AZR-132` if needed.
- Update `docs/TRACKING.md` with `AZR-142` and the current release status.

### Phase 4 - GitHub PR

- Create a branch for the intentional release-candidate diff.
- Avoid staging unrelated CRLF-only changes unless the release includes a
  deliberate normalization commit.
- Open a PR using `.github/PULL_REQUEST_TEMPLATE.md`.
- Include local verification results and Linear links.
- Wait for CI to pass.

### Phase 5 - Release

- Convert `[Unreleased]` to a concrete version entry.
- Tag only the CI-verified merge commit.
- Confirm release assets and `checksums.txt`.
- Download and hash-check the release assets.
- Add final evidence comments to Linear and GitHub issues.

## Completion Definition

The goal is complete only when:

- current code is merged to GitHub;
- CI is green on the release commit;
- release assets are published and checksum-verified;
- docs accurately state shipped behavior and known limitations;
- Linear Shellfix issues have evidence comments and stale states are reconciled;
- `shellfix doctor`, local tests, and release workflow evidence all agree.
