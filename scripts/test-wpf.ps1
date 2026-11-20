param(
    [string]$BinaryDirectory = (Join-Path $PSScriptRoot '..\artifacts\bin'),
    [string]$GameDirectory,
    [switch]$RenderUi
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root ('artifacts\wpf-tests\' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($out) | Out-Null
$launcher = Join-Path ([IO.Path]::GetFullPath($BinaryDirectory)) '暴力摩托增强修改器.exe'
$mode = if ($RenderUi) { '--render-ui' } else { '--self-test' }
$p = Start-Process -FilePath $launcher -ArgumentList @($mode, ('"' + $out + '"')) -WindowStyle Hidden -Wait -PassThru
if ($p.ExitCode -ne 0) { Get-Content (Join-Path $out 'results.txt') -Tail 10; throw 'WPF verification failed.' }
Get-Content (Join-Path $out 'results.txt') -Tail 1
if ($GameDirectory) {
    # Derive a pristine fixture from the existing enhancement tool's baseline.
    # All mutations below target this new fixture, never the live game directory.
    $game = [IO.Path]::GetFullPath($GameDirectory)
    $statePath = Join-Path $game '.RoadRash30thPatch\state.json'
    if (-not (Test-Path -LiteralPath $statePath)) { throw 'A patch baseline is required for this isolated integration test.' }
    $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    $fixture = Join-Path $out 'fixture'
    [IO.Directory]::CreateDirectory((Join-Path $fixture 'DATA')) | Out-Null
    [IO.Directory]::CreateDirectory((Join-Path $fixture 'TEXT')) | Out-Null
    [IO.Directory]::CreateDirectory((Join-Path $fixture 'Tools')) | Out-Null
    $targets = @('RoadRash95.exe', 'TEXT\ENU.DLL', 'DATA\GLOBAL.RSC')
    for ($i = 0; $i -lt 3; $i++) { [IO.File]::WriteAllBytes((Join-Path $fixture $targets[$i]), [Convert]::FromBase64String($state.Baseline[$i])) }
    Copy-Item -LiteralPath (Join-Path $game 'Tools\RoadRash30thEnhancement-v1.1.0.exe') -Destination (Join-Path $fixture 'Tools')
    $before = @{}; foreach ($target in $targets) { $before[$target] = (Get-FileHash -LiteralPath (Join-Path $fixture $target)).Hash }
    $bridge = Join-Path ([IO.Path]::GetFullPath($BinaryDirectory)) 'Tools\RoadRash.PatchBridge.exe'
    $request = Join-Path $out 'factory-options.xml'
    function Invoke-Bridge([string]$Operation, [string]$ReplyName) {
        $reply = Join-Path $out $ReplyName
        $arguments = @($Operation, ('"' + $fixture + '"'), ('"' + $request + '"'), ('"' + $reply + '"'))
        $child = Start-Process -FilePath $bridge -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
        if (-not (Test-Path -LiteralPath $reply)) { throw 'Bridge returned no result.' }
        [xml]$response = Get-Content -LiteralPath $reply -Raw
        if ($child.ExitCode -ne 0 -or $response.reply.ok -ne 'true') { throw ($response.reply.error -join '; ') }
        return $response
    }
    $null = Invoke-Bridge 'inspect' 'inspect-pristine.xml'
    $null = Invoke-Bridge 'apply' 'apply.xml'
    $applied = Invoke-Bridge 'inspect' 'inspect-applied.xml'
    [xml]$expected = Get-Content -LiteralPath $request -Raw
    foreach ($option in $expected.options.option) {
        $actual = @($applied.reply.option | Where-Object { $_.name -eq $option.name })
        if ($actual.Count -ne 1 -or $actual[0].InnerText -ne $option.InnerText) { throw ('Bridge mapping mismatch: ' + $option.name) }
    }
    $null = Invoke-Bridge 'restore' 'restore.xml'
    foreach ($target in $targets) { if ((Get-FileHash -LiteralPath (Join-Path $fixture $target)).Hash -ne $before[$target]) { throw ('Restore hash mismatch: ' + $target) } }
    # Exercise the modern process boundary, not only the Framework executable.
    $reply = Join-Path $out 'modern-inspect.xml'
    $child = Start-Process -FilePath $launcher -ArgumentList @('--inspect-game', ('"' + $fixture + '"'), ('"' + $reply + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($child.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $reply)) { throw 'Modern-to-Framework inspection failed.' }
    'PASS isolated bridge: inspect, apply all 18 options, restore exact original hashes, modern process boundary'
}
Write-Host "WPF verification output: $out"
