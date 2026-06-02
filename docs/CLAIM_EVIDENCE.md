# Claim Evidence

Public behavior claims need test evidence, manual verification, or an explicit limitation.

## Rule

Do not say a behavior is fixed unless at least one of these is true:

- A passing automated test covers it.
- A manual verification command is documented.
- A GitHub Actions run proves it.
- The text is clearly labeled as a known limitation or planned work.

Release notes should separate:

- **Fixed**: verified behavior change with test or manual evidence.
- **Improved**: partial hardening with known remaining limits.
- **Known limitation**: behavior that still requires workaround or setup.
- **Planned**: not shipped.

## README Claim Inventory

| Claim | Evidence | Notes |
|---|---|---|
| `grep "it's" file` no longer hangs | `.\test.ps1` quoting tests: `apostrophe`, `here's` | WSL distro must be available |
| `awk '{print $1, $3}'` preserves dollar references | `Quote-ForBash` escapes `$`; covered by bash wrapper class in `.\test.ps1` | Add a direct regression before expanding claim |
| `find ... -name "*.py"` keeps glob/path intent | `RunWslBash` quotes bare glob patterns; covered by path/glob implementation | Manual edge-case checks still useful |
| `for ...; do ...; done` routes as bash | `.\test.ps1` direct `for x in hello world; do echo item=$x; done` regression | Only proves Shellfix-handled invocations; Antigravity `run_command` can still pre-parse inline payloads before Shellfix sees them |
| `echo "a" && echo "b"` works | `.\test-ci-smoke.ps1`, `.\test.ps1`, and `.\test-proxy.ps1` cover `&&` | One-shot and proxy paths both covered |
| `curl` resolves to real curl, not `Invoke-WebRequest` | `.\test.ps1` alias deconfliction checks `curl is Function`; profile removes alias | Requires profile loading |
| Windows paths are translated to WSL paths | `TranslatePaths()` and `Convert-ToWslPath`; covered indirectly by wrapper tests | Add direct path fixture before stronger claims |
| Complex PowerShell quoting uses `-File` fallback | Manual debug check in README shows `Dangerous quoting detected` and `shellfix_<id>.ps1` | Only for commands classified by `HasDangerousQuoting()` |
| Native stderr does not become red NativeCommandError for wrapped tools | `.\test.ps1` native wrapper and clean output tests | Requires profile loading and wrapper allowlist |
| `dotnet build/test/run/publish` disables Terminal Logger | Profile injects `--tl:off`; covered by native wrapper code path | Add direct dotnet invocation before expanding claim |
| ANSI escape codes are stripped by profile wrappers | `.\test.ps1` ANSI strip function test | Applies to wrapper output, not every process |
| UTF-8 no-BOM file writing works | `.\test.ps1` `Write-Utf8NoBom` test | Profile-layer helper |
| CI validates behavior, not only build output | GitHub Actions `CI` runs `test-ci-smoke.ps1` | Added in PR #25 |
| Release binaries can be verified | Release workflow generates `checksums.txt`; SECURITY.md documents verification | Release workflow evidence required per tag |
| `shellfix doctor` proves Antigravity integration health | `shellfix doctor --json` checks settings files and live bypassing PowerShell child processes | Does not prove every Antigravity `run_command` string reached Shellfix before parsing |
| `shellfix guard antigravity-run-command` blocks fragile inline WSL/bash run_command payloads | `shellfix test --antigravity-guard`; manual stdin JSON guard check | Guard must be wired into Antigravity PreToolUse hooks to affect agent tool calls |
| Native tool fallback finds installed Windows tools from stale IDE PATHs | `NativeToolResolver` fallback candidates and `shellfix doctor` native-tool check | Requires local verification on each release candidate machine |

## Current Verification Commands

```powershell
dotnet publish shim/PowerShellShim.csproj -c Release -o shim/out --nologo
.\test-ci-smoke.ps1 -ShimPath .\shim\out\powershell.exe -WslDistro Ubuntu-24.04
.\test.ps1 -ShimPath .\shim\out\powershell.exe -WslDistro Ubuntu-24.04
.\test-proxy.ps1 -ShimPath .\shim\out\powershell.exe -WslDistro Ubuntu-24.04
.\test-replay.ps1 -ShimPath .\shim\out\powershell.exe -WslDistro Ubuntu-24.04
.\src\Shellfix.Cli\out\shellfix.exe test --antigravity-guard
.\src\Shellfix.Cli\out\shellfix.exe doctor --json
```
