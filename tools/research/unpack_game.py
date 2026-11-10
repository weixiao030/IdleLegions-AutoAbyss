"""使用游戏自带 Godot 读取本地资源；原游戏与账号数据均不写入。

所有输出必须放在本仓库外。只发布研究工具，不发布游戏代码或素材。
"""
import argparse
import collections
import hashlib
import json
from pathlib import Path, PurePosixPath
import shutil
import struct
import subprocess


def inventory(pack):
    with pack.open('rb') as stream:
        header = stream.read(112)
        magic, version, major, minor, patch, flags = struct.unpack_from('<6I', header)
        if magic != 0x43504447 or version != 4 or flags & 1:
            raise ValueError('需要目录未加密的 Godot v4 PCK')
        base, directory = struct.unpack_from('<QQ', header, 24)
        stream.seek(directory)
        count, = struct.unpack('<I', stream.read(4))
        rows = []
        for _ in range(count):
            length, = struct.unpack('<I', stream.read(4))
            name = stream.read(length).rstrip(b'\0').decode('utf-8')
            path = PurePosixPath(name.removeprefix('res://'))
            if path.is_absolute() or '..' in path.parts or ':' in str(path) or '\\' in name:
                raise ValueError('资源路径越界：' + name)
            offset, size = struct.unpack('<QQ', stream.read(16))
            digest = stream.read(16).hex()
            file_flags, = struct.unpack('<I', stream.read(4))
            rows.append(dict(path=str(path), offset=base + offset, size=size,
                             md5=digest, flags=file_flags))
        return dict(version=version, engine=f'{major}.{minor}.{patch}', entries=rows)


def fixture_pack(folder, files):
    names = [p.encode() + b'\0' * (-len(p.encode()) % 4) for p in files]
    pos = 100 + sum(4 + len(n) + 36 for n in names)
    pos += -pos % 16
    raw = bytearray(struct.pack('<6IQ16I', 0x43504447, 2, 4, 7, 0, 0, 0, *([0] * 16)))
    raw += struct.pack('<I', len(files))
    payload = bytearray()
    for name, content in zip(names, files.values()):
        raw += struct.pack('<I', len(name)) + name + struct.pack('<QQ', pos, len(content))
        raw += hashlib.md5(content).digest() + struct.pack('<I', 0)
        payload += content + b'\0' * (-len(content) % 16)
        pos += len(content) + (-len(content) % 16)
    raw += b'\0' * (-len(raw) % 16)
    (folder / 'client.pck').write_bytes(raw + payload)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    game, output = args.game.resolve(), args.output.resolve()
    if output.is_relative_to(root) or output == game or output.is_relative_to(game) or output.exists():
        raise ValueError('输出必须是游戏和仓库外尚不存在的新目录')
    pack = game / 'client.pck'
    manifest = inventory(pack)
    output.mkdir(parents=True)
    runner, extracted = output / 'runner', output / 'extracted'
    runner.mkdir()
    extracted.mkdir()
    for name in ['client.exe', 'libgodotsteam.windows.template_release.x86_64.dll', 'steam_api64.dll']:
        shutil.copy2(game / name, runner / name)
    config = dict(pack=pack.as_posix(), output=extracted.as_posix(), paths=[r['path'] for r in manifest['entries']])
    files = {
        'res://project.godot': b'config_version=5\n[application]\nconfig/name="IdleLegions Local Resource Reader"\nrun/main_scene="res://reader.tscn"\n[rendering]\nrenderer/rendering_method="gl_compatibility"\n',
        'res://reader.tscn': b'[gd_scene load_steps=2 format=3]\n[ext_resource type="Script" path="res://reader.gd" id="1"]\n[node name="Reader" type="Node"]\nscript = ExtResource("1")\n',
        'res://.godot/global_script_class_cache.cfg': b'list=[]\n',
        'res://reader-config.json': json.dumps(config, ensure_ascii=False).encode(),
        'res://reader.gd': '''extends Node
func _ready() -> void:
    var config: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://reader-config.json"))
    # Replace fixture paths too, so the original script cache is extracted.
    # Mounting resources does not start the original game scene or autoloads.
    if not ProjectSettings.load_resource_pack(config.pack, true):
        push_error("Unable to load original local pack")
        get_tree().quit(1)
        return
    var failures: Array = []
    var count: int = 0
    for path in config.paths:
        var source: FileAccess = FileAccess.open("res://" + path, FileAccess.READ)
        if source == null:
            failures.append(path)
            continue
        var target: String = config.output + "/" + path
        DirAccess.make_dir_recursive_absolute(target.get_base_dir())
        var destination: FileAccess = FileAccess.open(target, FileAccess.WRITE)
        if destination == null:
            failures.append(path)
            continue
        destination.store_buffer(source.get_buffer(source.get_length()))
        destination.close()
        source.close()
        count += 1
    print("RESOURCE_READ_COMPLETE ", count, " failures=", JSON.stringify(failures))
    get_tree().quit(0 if failures.is_empty() else 1)
'''.encode(),
    }
    fixture_pack(runner, files)
    result = subprocess.run([str(runner / 'client.exe'), '--headless'], cwd=runner,
                            capture_output=True, timeout=120)
    (output / 'reader.log').write_bytes(result.stdout + result.stderr)
    failures = []
    for row in manifest['entries']:
        file = extracted / row['path']
        if not file.is_file() or file.stat().st_size != row['size'] or hashlib.md5(file.read_bytes()).hexdigest() != row['md5']:
            failures.append(row['path'])
    manifest.update(packSha256=hashlib.sha256(pack.read_bytes()).hexdigest(),
                    executableSha256=hashlib.sha256((game / 'client.exe').read_bytes()).hexdigest(),
                    count=len(manifest['entries']), failures=failures,
                    extensions=dict(collections.Counter(Path(r['path']).suffix for r in manifest['entries'])),
                    readerExitCode=result.returncode)
    (output / 'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({k: v for k, v in manifest.items() if k != 'entries'}, ensure_ascii=False))
    if failures or result.returncode:
        raise SystemExit(1)


if __name__ == '__main__':
    main()
