param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\bin'), [string]$DotNet, [switch]$BuildOnly, [switch]$Offline)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$destination = [IO.Path]::GetFullPath($OutputDirectory)
$bridgeDirectory = Join-Path $root 'artifacts\bridge'
[IO.Directory]::CreateDirectory($bridgeDirectory) | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Windows .NET Framework compiler is required for the patch compatibility bridge.' }
$source = Get-ChildItem (Join-Path $root 'src') -Filter '*.cs' -File | ForEach-Object FullName
$bridge = Join-Path $bridgeDirectory 'RoadRash.PatchBridge.exe'
& $compiler /nologo /target:winexe /main:PatchBridge /platform:anycpu /optimize+ /codepage:65001 `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.Xml.Linq.dll "/out:$bridge" $source
if ($LASTEXITCODE -ne 0) { throw "Bridge build failed: $LASTEXITCODE" }
if (-not $DotNet) {
    $localSdk = Join-Path $root 'artifacts\tools\dotnet\dotnet.exe'
    $DotNet = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { 'dotnet' }
}
$env:DOTNET_CLI_HOME = Join-Path $root 'artifacts\dotnet-home'
$env:NUGET_PACKAGES = Join-Path $root 'artifacts\nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$project = Join-Path $root 'src\Wpf\RoadRash.Launcher.csproj'
$restoreArgs = @()
if ($Offline) { $restoreArgs = @("-p:RestoreSources=$env:NUGET_PACKAGES", '-p:NuGetAudit=false') }
if ($BuildOnly) { & $DotNet build $project -c Release --nologo @restoreArgs }
else { & $DotNet publish $project -c Release -r win-x64 --self-contained true -o $destination --nologo @restoreArgs }
if ($LASTEXITCODE -ne 0) { throw "WPF build failed: $LASTEXITCODE" }
if (-not $BuildOnly) {
    Move-Item -LiteralPath (Join-Path $destination 'RoadRash30thLauncher.exe') -Destination (Join-Path $destination '暴力摩托增强修改器.exe') -Force
    Copy-Item -LiteralPath (Join-Path $root 'docs\players\guide.txt') -Destination (Join-Path $destination 'Tools\LAUNCHER_README.txt') -Force
    $notices = Join-Path $destination 'Tools\licenses'
    [IO.Directory]::CreateDirectory($notices) | Out-Null
    Copy-Item -LiteralPath (Join-Path $env:NUGET_PACKAGES 'communitytoolkit.mvvm\8.4.0\License.md') -Destination (Join-Path $notices 'CommunityToolkit-MVVM-LICENSE.txt') -Force
    Copy-Item -LiteralPath (Join-Path $env:NUGET_PACKAGES 'communitytoolkit.mvvm\8.4.0\ThirdPartyNotices.txt') -Destination (Join-Path $notices 'CommunityToolkit-THIRD-PARTY-NOTICES.txt') -Force
    $runtime = Get-ChildItem (Join-Path $env:NUGET_PACKAGES 'microsoft.netcore.app.runtime.win-x64') -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    $desktop = Get-ChildItem (Join-Path $env:NUGET_PACKAGES 'microsoft.windowsdesktop.app.runtime.win-x64') -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    Copy-Item -LiteralPath (Join-Path $runtime.FullName 'LICENSE.TXT') -Destination (Join-Path $notices 'dotnet-runtime-LICENSE.txt') -Force
    Copy-Item -LiteralPath (Join-Path $runtime.FullName 'THIRD-PARTY-NOTICES.TXT') -Destination (Join-Path $notices 'dotnet-runtime-THIRD-PARTY-NOTICES.txt') -Force
    Copy-Item -LiteralPath (Join-Path $desktop.FullName 'LICENSE') -Destination (Join-Path $notices 'dotnet-desktop-LICENSE.txt') -Force
    Get-ChildItem -LiteralPath $destination -File | Select-Object Name, Length
}
