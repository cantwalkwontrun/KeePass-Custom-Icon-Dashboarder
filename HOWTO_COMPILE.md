---
lang: en-GB
---

# Building the development fork

Use a Windows Visual Studio Developer PowerShell with MSBuild, the .NET Framework 4.8 targeting pack and the .NET SDK installed. Extract a verified [official KeePass portable package](https://keepass.info/download.html) into `KeePass-2.X` at the repository root. Builds reference this isolated copy; they do not install the plugin or restart KeePass.

```powershell
msbuild CustomIconDashboarder.csproj /p:Configuration=Release /v:minimal
dotnet build tests/RegressionTests.csproj -c Release
./tests/bin/Release/net48/RegressionTests.exe
```

To reference a different KeePass executable, pass `/p:KeePassPath="absolute path to KeePass.exe"` to MSBuild and `-p:KeePassPath="absolute path to KeePass.exe"` to `dotnet build`. No KeePass source checkout is needed.

The output is `bin/Release/CustomIconDashboarderPlugin.dll`, built for AnyCPU and .NET Framework 4.8. LomsonLib sources are compiled into the same DLL, matching the single-assembly approach of the original PLGX; no separate LomsonLib binary is required.

The regression harness constructs unshown forms and synthetic in-memory databases. It does not open or save an existing password database or request real favicons. A successful build/test run does not replace manual testing inside an isolated KeePass instance.

For a manual test, use a disposable database and keep only one version of Dashboarder in the test instance's `Plugins` directory. Do not load the original PLGX and the development DLL together.

The PLGX project template includes the new worker helper. The inherited `createPlgx.cmd` has not been used or validated for this fork; it contains machine-specific copy/cleanup commands. No rebuilt PLGX or binary release has been published.
