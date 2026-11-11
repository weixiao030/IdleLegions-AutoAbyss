"""Collect only card definitions from local receipts, then query the game's Chinese locale.

No login, network calls, game scene, account edits or bundled game assets.
Intermediate original-language evidence stays outside the repository.
"""
import argparse
import importlib.util
import json
from pathlib import Path
import shutil
import subprocess


FAMILIES = {'escarcha', 'brasa', 'fulgor', 'ponzona', 'umbra', 'aurora', 'acero'}


def collect_cards(locations):
    cards, sources = {}, {}

    def walk(node, source, badge_context=None):
        if isinstance(node, dict):
            # Pending badge options carry family and threshold on their menu,
            # while owned badges carry them on each card. Preserve both forms.
            family = node.get('familia', (badge_context or {}).get('familia'))
            if all(k in node for k in ['id', 'desc', 'nombre']) and family in FAMILIES:
                ident = node['id']
                if isinstance(ident, str) and not ident.startswith('mal_'):
                    entry = {k: node[k] for k in ['id','familia','desc','nombre','rareza','umbral'] if k in node}
                    entry['familia'] = family
                    if badge_context and 'umbral' not in entry:
                        entry['umbral'] = badge_context['umbral']
                    if entry.get('desc'):
                        cards[ident] = entry
                        sources.setdefault(ident, set()).add(source)
            for key, value in node.items():
                context = {'familia': node['familia'], 'umbral': node['umbral']} if (
                    key == 'opciones' and node.get('familia') in FAMILIES
                    and isinstance(node.get('umbral'), (int, float))
                    and not isinstance(node['umbral'], bool) and node['umbral'] > 0
                ) else None
                walk(value, source, context)
        elif isinstance(node, list):
            for value in node:
                walk(value, source, badge_context)
        elif isinstance(node, str) and node.lstrip().startswith(('{', '[')):
            # Saved command receipts may contain serialized JSON snapshots.
            try:
                walk(json.loads(node), source)
            except (ValueError, RecursionError):
                pass

    for location in locations:
        files = [location] if location.is_file() else sorted(location.rglob('*.json'))
        for file in files:
            if any(p in {'node_modules','game-fixture','ci-diagnostics-42875367400'} for p in file.parts):
                continue
            if file.stat().st_size > 60_000_000:
                continue
            try:
                walk(json.loads(file.read_text(encoding='utf-8-sig')), str(file.resolve()))
            except (ValueError, OSError):
                continue
    return cards, sources


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--evidence', type=Path, required=True)
    parser.add_argument('--receipts', type=Path, nargs='+', required=True)
    parser.add_argument('--images', type=Path)
    args = parser.parse_args()
    args.evidence.mkdir(parents=True, exist_ok=True)
    cards, sources = collect_cards(args.receipts)
    (args.evidence/'observed-cards.json').write_text(json.dumps(list(cards.values()), ensure_ascii=False, indent=2), encoding='utf-8')
    (args.evidence/'card-sources.json').write_text(json.dumps({k:sorted(v) for k,v in sources.items()}, ensure_ascii=False, indent=2), encoding='utf-8')
    runner = args.evidence/'locale-runner'
    runner.mkdir(exist_ok=True)
    for name in ['client.exe','libgodotsteam.windows.template_release.x86_64.dll','steam_api64.dll']:
        shutil.copy2(args.game/name, runner/name)
    spec = importlib.util.spec_from_file_location('unpack', Path(__file__).with_name('unpack_game.py'))
    unpack = importlib.util.module_from_spec(spec); spec.loader.exec_module(unpack)
    config = {'pack':(args.game/'client.pck').as_posix(), 'output':(args.evidence/'translated-cards.json').resolve().as_posix(), 'images':args.images.resolve().as_posix() if args.images else ''}
    script = '''extends Node
func _ready() -> void:
    var cfg: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://config.json"))
    var cards: Array = JSON.parse_string(FileAccess.get_file_as_string("res://queries.json"))
    if not ProjectSettings.load_resource_pack(cfg.pack, false):
        get_tree().quit(1)
        return
    var locale: Translation = load("res://i18n/strings.zh_CN.translation")
    var result: Array = []
    for card in cards:
        var item: Dictionary = card.duplicate()
        item["nameZh"] = locale.get_message(card.nombre)
        item["descZh"] = locale.get_message(card.desc)
        result.append(item)
    var file: FileAccess = FileAccess.open(cfg.output, FileAccess.WRITE)
    file.store_string(JSON.stringify(result, "  "))
    file.close()
    if cfg.images != "":
        DirAccess.make_dir_recursive_absolute(cfg.images)
        var files: PackedStringArray = DirAccess.get_files_at("res://assets/ui/sima/cartas")
        var images: Array = []
        for filename in files:
            if not filename.ends_with(".png.import"):
                continue
            var name: String = filename.trim_suffix(".import")
            var texture: Texture2D = load("res://assets/ui/sima/cartas/" + name)
            if texture != null:
                var picture: Image = texture.get_image()
                if picture.is_compressed():
                    picture.decompress()
                var result_code: int = picture.save_png(cfg.images + "/" + name)
                images.append({"file": name, "error": result_code, "width": picture.get_width(), "height": picture.get_height()})
        var report: FileAccess = FileAccess.open(cfg.output + ".images.json", FileAccess.WRITE)
        report.store_string(JSON.stringify(images, "  "))
        report.close()
        print("CARD_IMAGES_COMPLETE ", images.size())
    print("CARD_LOCALE_COMPLETE ", result.size())
    get_tree().quit()
'''
    unpack.fixture_pack(runner, {
        'res://project.godot':b'config_version=5\n[application]\nconfig/name="Card Locale Reader"\nrun/main_scene="res://reader.tscn"\n[rendering]\nrenderer/rendering_method="gl_compatibility"\n',
        'res://reader.tscn':b'[gd_scene load_steps=2 format=3]\n[ext_resource type="Script" path="res://reader.gd" id="1"]\n[node name="Reader" type="Node"]\nscript = ExtResource("1")\n',
        'res://.godot/global_script_class_cache.cfg':b'list=[]\n',
        'res://config.json':json.dumps(config).encode(),
        'res://queries.json':json.dumps(list(cards.values()),ensure_ascii=False).encode(),
        'res://reader.gd':script.encode(),
    })
    run = subprocess.run([str((runner/'client.exe').resolve()),'--headless'],cwd=runner,capture_output=True,timeout=60)
    (args.evidence/'locale-reader.log').write_bytes(run.stdout+run.stderr)
    print(run.stdout.decode(errors='replace'))
    print('Definitions:',len(cards),'Exit:',run.returncode)
    if run.returncode or not (args.evidence/'translated-cards.json').is_file():
        raise SystemExit(1)


if __name__ == '__main__':
    main()
