## Summary

- Describe the change.

## Linked Issue

Fixes #
Linear:

## Verification

List the exact commands, GitHub Actions runs, or release URLs that prove the change.

- [ ] `dotnet publish shim/PowerShellShim.csproj -c Release -o shim/out --nologo`
- [ ] `dotnet publish src/Shellfix.Cli/Shellfix.Cli.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o src/Shellfix.Cli/out --nologo`
- [ ] `.\test-ci-smoke.ps1 -ShimPath .\shim\out\powershell.exe`
- [ ] `.\test.ps1 -ShimPath .\shim\out\powershell.exe`
- [ ] `.\test-proxy.ps1 -ShimPath .\shim\out\powershell.exe`
- [ ] `.\test-replay.ps1 -ShimPath .\shim\out\powershell.exe`
- [ ] `.\src\Shellfix.Cli\out\shellfix.exe test`
- [ ] Other:

## Install Or Release Impact

- [ ] No install/release impact
- [ ] Installer behavior changed
- [ ] Release assets/checksums changed
- [ ] README/CHANGELOG/SECURITY updated
- [ ] New tag/release required after merge

## Risk

- [ ] No runtime behavior change
- [ ] Shim routing/classification changed
- [ ] Profile behavior changed
- [ ] Installer/PATH/shortcut behavior changed
- [ ] Trust/security surface changed

Notes:

## Not Done

List intentional omissions or follow-up issues.
