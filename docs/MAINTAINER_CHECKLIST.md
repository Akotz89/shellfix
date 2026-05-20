# Maintainer Checklist

Use this checklist before closing implementation work or publishing a release.

## Issue Closeout

Every completed issue needs visible evidence in both GitHub and Linear.

- GitHub issue has a closing PR, commit, tag, or release link.
- Linear issue has a comment with the same evidence.
- The closeout comment includes exact verification commands or GitHub Actions run URLs.
- README, SECURITY, CONTRIBUTING, and CHANGELOG were updated when public behavior changed.
- Release impact is explicit: no release needed, release pending, or released as `vX.Y.Z`.
- Known limitations are documented instead of implied.

Do not use "done", "fixed", or "released" without a command, run, PR, commit, tag, or release URL that proves it.

## PR Review Standard

Check these before merging:

- PR uses `.github/PULL_REQUEST_TEMPLATE.md`.
- CI is passing, including `test-ci-smoke.ps1`.
- Runtime changes include local verification against `shim/out/powershell.exe`.
- Installer changes include install/uninstall or dry-run evidence.
- Documentation claims are present in `docs/CLAIM_EVIDENCE.md` or clearly listed as limitations.
- Public wording is operator-facing and avoids chat-derived language.

## Release Checklist

Before pushing a release tag:

- `master` is up to date with the intended release commit.
- `CHANGELOG.md` has an entry for the release.
- `README.md` does not promise behavior that is not in the release.
- `SECURITY.md` trust model and supported versions are current.
- CI passed on the release commit.
- Local smoke passed:
  - `dotnet publish shim/PowerShellShim.csproj -c Release -o shim/out --nologo`
  - `.\test-ci-smoke.ps1 -ShimPath .\shim\out\powershell.exe`
  - `.\test.ps1 -ShimPath .\shim\out\powershell.exe`
  - `.\test-proxy.ps1 -ShimPath .\shim\out\powershell.exe`
  - `.\test-replay.ps1 -ShimPath .\shim\out\powershell.exe`

After the release workflow finishes:

- GitHub Release exists for the tag.
- Release assets include `powershell.exe`, `install.ps1`, `Microsoft.PowerShell_profile.ps1`, `launch-ide.bat`, and `checksums.txt`.
- `checksums.txt` contains SHA256 entries for all release assets.
- Downloaded `powershell.exe` hash matches `checksums.txt`.
- GitHub/Linear issues included in the release are closed or marked Done with evidence comments.

## Branch, Tag, And Release Rules

- Use short feature branches such as `codex/ci-functional-smoke`.
- Merge through PRs so CI evidence is attached to the change.
- Tags use `vMAJOR.MINOR.PATCH`.
- Do not move published tags.
- Do not publish a release from a commit whose CI status is unknown or failing.
