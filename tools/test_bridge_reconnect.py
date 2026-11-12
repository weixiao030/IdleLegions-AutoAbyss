"""Run bridge reconnect fixtures with a local Godot game runtime, without the game pack or network."""
import argparse
import importlib.util
from pathlib import Path
import shutil
import subprocess


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    runner = args.output.resolve() / 'isolated-runner'
    runner.mkdir(parents=True, exist_ok=True)
    for name in ['client.exe', 'libgodotsteam.windows.template_release.x86_64.dll', 'steam_api64.dll']:
        shutil.copy2(args.game / name, runner / name)
    spec = importlib.util.spec_from_file_location('unpack', root / 'tools/research/unpack_game.py')
    unpack = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(unpack)
    unpack.fixture_pack(runner, {
        'res://project.godot': b'config_version=5\n[application]\nconfig/name="Frost Bridge Isolated Tests"\nrun/main_scene="res://reader.tscn"\n[rendering]\nrenderer/rendering_method="gl_compatibility"\n',
        'res://reader.tscn': b'[gd_scene load_steps=2 format=3]\n[ext_resource type="Script" path="res://reader.gd" id="1"]\n[node name="Reader" type="Node"]\nscript = ExtResource("1")\n',
        'res://.godot/global_script_class_cache.cfg': b'list=[]\n',
        'res://reader.gd': (root / 'tools/tests/bridge_reconnect.gd').read_bytes(),
        'res://bridge.gd': (root / 'bridge/frost_bridge.gd').read_bytes(),
    })
    run = subprocess.run([str(runner / 'client.exe'), '--headless'], cwd=runner, capture_output=True, timeout=60)
    log = run.stdout + run.stderr
    (args.output / 'bridge-reconnect.log').write_bytes(log)
    print(log.decode('utf-8', errors='replace'))
    if run.returncode or b'BRIDGE_RECONNECT_TESTS_COMPLETE 18' not in log or b'SCRIPT ERROR' in log or b'FAILED:' in log:
        raise SystemExit(1)


if __name__ == '__main__':
    main()
