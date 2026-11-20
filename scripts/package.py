"""Package the modifier and its sources from explicit file lists."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import zipfile

ROOT = Path(__file__).resolve().parents[1]
LAUNCHER = '暴力摩托增强修改器.exe'
LEGACY_ENTRY = '暴力摩托增强启动器.exe'
SOURCE_FILES = (
    'src/LegacyEntry/Program.cs',
    'src/RankBatch.c', 'src/LauncherRank.cs', 'src/LauncherRankNative.cs', 'tests/RankTests.cs', 'docs/development/rank-batching.md',
    'src/LauncherKnockdown.cs', 'src/LauncherKnockdownNative.cs', 'src/Knockdown.c',
    'assets/knockdown-hud.png', 'scripts/import-knockdown-voice.py', 'tests/KnockdownTests.cs',
    'tests/knockdown_sprite_replay.py', 'tests/knockdown_atlas_audit.py', 'docs/development/knockdown-sprites.md',
    'README.md', 'LICENSE', 'THIRD_PARTY.md', 'CHANGELOG.md', 'CONTRIBUTING.md',
    'VERSION', '.gitignore', '.gitattributes',
    'src/Launcher.cs', 'src/LauncherUi.cs', 'src/LauncherFixes.cs', 'src/LauncherAudio.cs', 'src/PatchBridge.cs',
    'src/Wpf/RoadRash.Launcher.csproj', 'src/Wpf/MainWindow.xaml', 'src/Wpf/MainWindow.xaml.cs',
    'src/Wpf/Theme.xaml', 'src/Wpf/Settings.cs', 'src/Wpf/Schema.cs', 'src/Wpf/LauncherStrings.cs', 'src/Wpf/Converters.cs',
    'config/locales/zh-CN.json', 'src/LauncherOpponents.cs', 'src/LauncherOpponentNative.cs', 'src/OpponentRules.c',
    'src/Wpf/WorkbenchViewModel.cs', 'src/Wpf/GameService.cs', 'src/Wpf/Program.cs', 'src/Wpf/OfflineVerification.cs',
    'src/GameAudioSession.cs', 'src/LauncherMotion.cs', 'src/LauncherCharacter.cs', 'src/LauncherRace.cs', 'src/launcher.manifest',
    'src/LauncherAi.cs', 'src/LauncherAiNative.cs', 'src/LongRunAi.c', 'scripts/build-ai.py', 'tests/RunAiTests.cs', 'tests/RunLaneTests.cs',
    'src/LauncherTraffic.cs', 'src/LauncherTrafficNative.cs', 'src/TrafficDrive.c', 'tests/TrafficTests.cs',
    'src/LauncherTrackLength.cs', 'src/LauncherNearbyAudio.cs', 'src/LauncherImpactAudioNative.cs', 'src/ImpactAudio.c', 'tests/FeatureTests.cs',
    'tests/MotionTests.cs', 'tests/CharacterTests.cs', 'tests/RaceTests.cs', 'tests/RuntimeStateTests.cs', 'tests/BikeProbeTests.cs',
    'tests/GameCarFrameTests.cs', 'tests/LauncherUiTests.cs', 'tests/MidiAudioTests.cs',
    'tests/RuntimeCompatibilityTests.cs', 'tests/RuntimeCompatibilityTests.csproj',
    'scripts/build.ps1', 'scripts/test.ps1', 'scripts/test-wpf.ps1', 'scripts/package.py', 'scripts/make-car.ps1',
    'config/launcher_settings.example.ini', 'assets/player_car.png', 'assets/workbench.ico',
    'dependencies/README.md', 'dependencies/verified-files.json',
    'third_party/licenses/Chinese-LICENSE.txt', 'third_party/licenses/HD-LICENSE.txt',
    'docs/development/wpf-workbench.md', 'docs/players/guide.txt', 'docs/history/stage-2026-11-17.md',
    'docs/development/character-features.md', 'docs/development/long-run.md', 'docs/images/gameplay.png',
    'docs/history/compatibility-2026-11-17.md', 'docs/releases/v0.5.1.md', 'docs/releases/v0.5.2.md',
)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def make_zip(output, files, version, kind):
    manifest = {'version': version, 'kind': kind, 'game_included': False,
                'third_party_game_binaries_included': False,
                'external_enhancer_included': kind == 'player',
                'bundled_runtime': '.NET 10 / WPF and CommunityToolkit.Mvvm' if kind != 'source' else None,
                'files': {n: {'bytes': len(b), 'sha256': digest(b)}
                          for n, b in sorted(files.items())}}
    files = dict(files)
    files['package_manifest.json'] = json.dumps(manifest, ensure_ascii=False, indent=2).encode('utf-8')
    output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(output, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for name, data in sorted(files.items()):
            # Fixed timestamps keep repeated packages identical for identical inputs.
            entry = zipfile.ZipInfo(name, (2026, 1, 1, 0, 0, 0))
            entry.compress_type = zipfile.ZIP_DEFLATED
            entry.external_attr = 0o100644 << 16
            archive.writestr(entry, data)
    with zipfile.ZipFile(output) as archive:
        if archive.testzip() is not None or set(archive.namelist()) != set(files):
            raise RuntimeError('ZIP verification failed')
        for name, data in files.items():
            if archive.read(name) != data:
                raise RuntimeError('ZIP content mismatch: ' + name)
    checksum = digest(output.read_bytes())
    output.with_suffix('.zip.sha256.txt').write_text(checksum + '  ' + output.name + '\n', encoding='utf-8')
    print(json.dumps({'zip': str(output), 'files': len(files), 'bytes': output.stat().st_size,
                      'sha256': checksum}, ensure_ascii=True))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument('--source-only', action='store_true', help='Package sources without requiring a compiled EXE.')
    mode.add_argument('--player-enhancer', type=Path, help='Make the player edition with the verified original v1.1.0 enhancer.')
    mode.add_argument('--release-set', type=Path, metavar='ENHANCER',
                      help='Write player, standalone and source ZIPs, checksums, and release notes for publication.')
    parser.add_argument('--binary-dir', type=Path, default=ROOT / 'artifacts/bin')
    parser.add_argument('--output-dir', type=Path, default=ROOT / 'artifacts/release')
    args = parser.parse_args()
    version = (ROOT / 'VERSION').read_text(encoding='utf-8').strip()
    if not re.fullmatch(r'\d+\.\d+\.\d+(?:-[a-zA-Z0-9.]+)?', version):
        raise ValueError('Invalid VERSION')
    source = {name: (ROOT / name).read_bytes() for name in SOURCE_FILES}
    release_notes = source[f'docs/releases/v{version}.md'] if args.release_set else None
    enhancer_path = args.release_set or args.player_enhancer
    runtime = None
    player = None
    if not args.source_only:
        executable = args.binary_dir / LAUNCHER
        if not executable.is_file():
            executable = args.binary_dir / 'RoadRash30thLauncher.exe'  # Earlier build directories.
        binary = executable.read_bytes()
        if not binary.startswith(b'MZ'):
            raise ValueError('Build the Windows launcher before packaging')
        runtime = {name: data for name, data in source.items()
                   if name.startswith(('docs/', 'third_party/', 'dependencies/'))
                   or name in ('README.md', 'CONTRIBUTING.md', 'LICENSE', 'THIRD_PARTY.md', 'CHANGELOG.md', 'VERSION')}
        guide = source['docs/players/guide.txt']
        runtime.update({LAUNCHER: binary, LEGACY_ENTRY: (args.binary_dir / LEGACY_ENTRY).read_bytes(),
                        '安装说明.txt': guide, 'Tools/LAUNCHER_README.txt': guide,
                        'launcher_settings.example.ini': source['config/launcher_settings.example.ini'],
                        'Tools/player_car.png': source['assets/player_car.png'],
                        'Tools/import-knockdown-voice.py': source['scripts/import-knockdown-voice.py'],
                        'Tools/Locales/zh-CN.json': source['config/locales/zh-CN.json']})
        for name in ('Tools/RoadRash.PatchBridge.exe',
                     'Tools/licenses/CommunityToolkit-MVVM-LICENSE.txt',
                     'Tools/licenses/CommunityToolkit-THIRD-PARTY-NOTICES.txt',
                     'Tools/licenses/dotnet-runtime-LICENSE.txt',
                     'Tools/licenses/dotnet-runtime-THIRD-PARTY-NOTICES.txt',
                     'Tools/licenses/dotnet-desktop-LICENSE.txt'):
            runtime[name] = (args.binary_dir / name).read_bytes()
        if enhancer_path:
            name = 'RoadRash30thEnhancement-v1.1.0.exe'
            expected = json.loads(source['dependencies/verified-files.json'].decode('utf-8-sig'))['files'][name]
            enhancer = enhancer_path.read_bytes()
            if len(enhancer) != expected['bytes'] or digest(enhancer) != expected['sha256']:
                raise ValueError('Player edition requires the verified v1.1.0 enhancer')
            player = {n: data for n, data in runtime.items()
                      if n.startswith(('Tools/', 'docs/', 'dependencies/', 'third_party/'))
                      or n in (LAUNCHER, LEGACY_ENTRY, '安装说明.txt', 'LICENSE', 'VERSION', 'THIRD_PARTY.md', 'CHANGELOG.md')}
            player['Tools/' + name] = enhancer
    prefix = f'RoadRash30th-EnhancedMod-{version}'
    make_zip(args.output_dir / (prefix + '-source.zip'), source, version, 'source')
    if runtime is not None and not args.player_enhancer:
        suffix = '-standalone.zip' if args.release_set else '-launcher.zip'
        make_zip(args.output_dir / (prefix + suffix), runtime, version, 'launcher-only')
    if player is not None:
        filename = prefix + '-player.zip' if args.release_set else f'暴力摩托增强修改器-{version}-玩家版.zip'
        make_zip(args.output_dir / filename, player, version, 'player')
    if release_notes is not None:
        (args.output_dir / '发布说明.md').write_bytes(release_notes)


if __name__ == '__main__':
    main()
