"""Run the production bridge in an offline, standalone Godot fixture PCK.

Usage: python tools/bridge-recovery/verify.py --engine PATH_TO_GODOT_EXE
No game pack, account, installed bridge or running game is accessed.
"""
import argparse
import hashlib
import shutil
import struct
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--engine', type=Path, required=True)
    parser.add_argument('--bridge', type=Path, default=ROOT / 'bridge/frost_bridge.gd')
    args = parser.parse_args()
    project = ROOT / 'output/recovery/godot'
    project.mkdir(parents=True, exist_ok=True)
    for name in [args.engine.name, 'libgodotsteam.windows.template_release.x86_64.dll', 'steam_api64.dll']:
        source = args.engine.parent / name
        if source.is_file():
            target = project / name
            if not target.exists():
                shutil.copy2(source, target)
    sources = {
        'project.godot': 'config_version=5\n[application]\nconfig/name="FrostAbyssOfflineRecoveryTest"\nrun/main_scene="res://check.tscn"\n[rendering]\nrenderer/rendering_method="gl_compatibility"\n',
        'check.tscn': '[gd_scene load_steps=2 format=3]\n[ext_resource type="Script" path="res://check.gd" id="1"]\n[node name="Check" type="Node"]\nscript = ExtResource("1")\n',
        'check.gd': (Path(__file__).parent / 'check.gd').read_text(encoding='utf-8'),
        'frost_bridge.gd': args.bridge.read_text(encoding='utf-8'),
        'test_bridge.gd': 'extends "res://frost_bridge.gd"\nfunc _ready() -> void:\n\tset_process(false)\nfunc _main() -> Node:\n\treturn null\nfunc _ensure_cfg() -> void:\n\tpass\n',
        'util/nombre_objeto.gd': 'extends RefCounted\nstatic func de(item: Dictionary) -> String:\n\treturn str(item.get("name", "fixture"))\n',
        '.godot/global_script_class_cache.cfg': 'list=[]\n',
    }
    data = [value.encode('utf-8') for value in sources.values()]
    names = [('res://' + name).encode('utf-8') for name in sources]
    names = [name + b'\0' * ((-len(name)) % 4) for name in names]
    start = 100 + sum(4 + len(name) + 8 + 8 + 16 + 4 for name in names)
    start += (-start) % 16
    offsets, position = [], start
    for content in data:
        offsets.append(position)
        position += len(content) + (-len(content)) % 16
    pack = bytearray(struct.pack('<6IQ16I', 0x43504447, 2, 4, 7, 0, 0, 0, *([0] * 16)) + struct.pack('<I', len(sources)))
    for name, content, offset in zip(names, data, offsets):
        pack += struct.pack('<I', len(name)) + name + struct.pack('<QQ', offset, len(content)) + hashlib.md5(content).digest() + struct.pack('<I', 0)
    pack += b'\0' * (start - len(pack))
    for content in data:
        pack += content
        pack += b'\0' * ((-len(pack)) % 16)
    fixture = project / Path(args.engine.name).with_suffix('.pck').name
    fixture.write_bytes(pack)
    result = subprocess.run([str((project / args.engine.name).resolve()), '--headless'], cwd=project, capture_output=True, timeout=25, creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0))
    log = (result.stdout + result.stderr).decode('utf-8', errors='replace')
    (project.parent / 'bridge-check.log').write_text(log, encoding='utf-8')
    print(log)
    if result.returncode != 0 or 'BRIDGE_RECOVERY_OK' not in log or 'ERROR:' in log:
        raise SystemExit('Bridge fixture failed')


if __name__ == '__main__':
    if hasattr(sys.stdout, 'reconfigure'):
        sys.stdout.reconfigure(encoding='utf-8')
    main()
