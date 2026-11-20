param(
    [string]$GameDirectory,
    [switch]$RenderUi
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x C# compiler not found.' }
$gameExe = $null
$music = $null
if ($GameDirectory) {
    $gameExe = Join-Path $GameDirectory 'RoadRash95.exe'
    $music = Join-Path $GameDirectory 'AUDIO\MUSIC'
    if (-not (Test-Path -LiteralPath $gameExe -PathType Leaf)) { throw "Missing game executable: $gameExe" }
    if (-not (Test-Path -LiteralPath $music -PathType Container)) { throw "Missing music directory: $music" }
    if (@(Get-ChildItem -LiteralPath $music -Filter '*.MID' -File).Count -eq 0) { throw 'No game MIDI fixtures found.' }
}
$testDirectory = Join-Path $root ('artifacts\tests\' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testDirectory) | Out-Null
$source = @('Launcher.cs', 'LauncherUi.cs', 'LauncherFixes.cs', 'LauncherAudio.cs', 'GameAudioSession.cs', 'LauncherMotion.cs', 'LauncherCharacter.cs', 'LauncherRace.cs', 'LauncherAi.cs', 'LauncherAiNative.cs', 'LauncherTraffic.cs', 'LauncherTrafficNative.cs', 'LauncherNearbyAudio.cs', 'LauncherImpactAudioNative.cs', 'LauncherTrackLength.cs') | ForEach-Object { Join-Path $root "src\$_" }
$source += @('LauncherOpponents.cs', 'LauncherOpponentNative.cs') | ForEach-Object { Join-Path $root "src\$_" }
$source += @('LauncherKnockdown.cs', 'LauncherKnockdownNative.cs') | ForEach-Object { Join-Path $root "src\$_" }
$source += @('LauncherRank.cs', 'LauncherRankNative.cs') | ForEach-Object { Join-Path $root "src\$_" }
function Invoke-TestSuite([string]$Name, [string]$Platform, [string[]]$TestArguments = @()) {
    $output = Join-Path $testDirectory "$Name.exe"
    $suite = @((Join-Path $root "tests\$Name.cs"))
    if ($Name -eq 'RaceTests') { $suite += Join-Path $root 'tests\RankTests.cs' }
    & $compiler /nologo /target:exe "/platform:$Platform" /optimize+ /codepage:65001 `
        "/main:$Name" /r:System.Windows.Forms.dll /r:System.Drawing.dll `
        /r:System.Core.dll /r:System.Web.Extensions.dll "/resource:$root\assets\knockdown-hud.png,KnockdownHud.png" "/out:$output" $source $suite
    if ($LASTEXITCODE -ne 0) { throw "$Name compilation failed: $LASTEXITCODE" }
    & $output @TestArguments
    if ($LASTEXITCODE -ne 0) { throw "$Name failed: $LASTEXITCODE" }
}
Invoke-TestSuite -Name RuntimeStateTests -Platform anycpu
Invoke-TestSuite -Name BikeProbeTests -Platform x86
Invoke-TestSuite -Name MotionTests -Platform x86
$characterArgs = @()
if ($gameExe) { $characterArgs = @($gameExe) }
Invoke-TestSuite -Name CharacterTests -Platform x86 -TestArguments $characterArgs
$raceArgs = @()
if ($gameExe) { $raceArgs = @($gameExe, (Join-Path $root 'src\RankBatch.c')) }
Invoke-TestSuite -Name RaceTests -Platform x86 -TestArguments $raceArgs
Invoke-TestSuite -Name RunAiTests -Platform x86 -TestArguments @((Join-Path $root 'src\LongRunAi.c'))
Invoke-TestSuite -Name RunLaneTests -Platform x86
Invoke-TestSuite -Name TrafficTests -Platform x86 -TestArguments (@((Join-Path $root 'src\TrafficDrive.c'))+$characterArgs)
$uiArgs = @()
if ($RenderUi) { $uiArgs = @(Join-Path $root 'artifacts\ui') }
Invoke-TestSuite -Name LauncherUiTests -Platform anycpu -TestArguments $uiArgs
$audioArgs = @()
if ($music) { $audioArgs = @($music) }
Invoke-TestSuite -Name MidiAudioTests -Platform x86 -TestArguments $audioArgs
if ($gameExe) {
    Invoke-TestSuite -Name KnockdownTests -Platform x86 -TestArguments @($GameDirectory)
    Invoke-TestSuite -Name FeatureTests -Platform x86 -TestArguments @($gameExe, (Join-Path $root 'src\ImpactAudio.c'), (Join-Path $root 'src\OpponentRules.c'))
    Invoke-TestSuite -Name GameCarFrameTests -Platform x86 -TestArguments @($gameExe)
} else {
    Write-Host 'SKIP original-game frame tests. Supply -GameDirectory for game integration tests.'
}
Write-Host "All requested suites passed. Test executables: $testDirectory"
