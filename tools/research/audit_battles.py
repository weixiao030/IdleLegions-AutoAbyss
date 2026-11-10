"""只读核对本地战斗回执的互斥伤害账本；输出汇总，不包含账号ID或密钥。"""
import argparse
import collections
import json
from pathlib import Path


def flag(event, key):
    return event.get(key) is True or event.get(key) == 1


def audit(directory):
    totals = collections.Counter()
    sources = collections.Counter()
    timings = collections.Counter()
    missing = collections.Counter()
    for file in sorted(directory.rglob('run-*.json')):
        raw = json.loads(file.read_text(encoding='utf-8-sig'))
        battle = raw.get('authoritativeBattle', raw)
        result = battle.get('result', {})
        totals['battles'] += 1
        totals['wins' if result.get('win') else 'losses'] += 1
        totals['truncated'] += bool(result.get('truncated'))
        totals['events'] += len(result.get('events', []))
        context = {h.get('id'): h for h in battle.get('context', {}).get('heroes', [])}
        aliases = set()
        for hero in battle.get('before', {}).get('heroes', []):
            aliases.update(str(hero[key]).casefold() for key in ('id', 'name', 'nombre', 'clase') if hero.get(key))
            totals['heroContexts'] += hero.get('id') in context
            totals['heroContextsMissing'] += hero.get('id') not in context
        for event in result.get('events', []):
            damage = event.get('dmg', 0)
            if event.get('side') != 'ally' or damage <= 0:
                continue
            totals['damage'] += damage
            totals['damageEvents'] += 1
            kind = next((k for k in ('reaction', 'rel', 'skill') if event.get(k)), 'unmarked')
            sources[kind] += damage
            timing = 'channel' if flag(event, 'canal') else 'periodic' if flag(event, 'tick') else 'direct'
            timings[timing] += damage
            if str(event.get('src', '')).casefold() not in aliases:
                missing['damage'] += damage
                missing['events'] += 1
    assert sum(sources.values()) == totals['damage']
    assert sum(timings.values()) == totals['damage']
    return dict(totals=dict(totals), sources=dict(sources), timings=dict(timings), unmatched=dict(missing))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    result = audit(args.directory)
    text = json.dumps(result, ensure_ascii=False, indent=2)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(text + '\n', encoding='utf-8')
    print(text)
    if result['unmatched']:
        raise SystemExit('存在不能对应参战英雄的来源，需核对原始回执')


if __name__ == '__main__':
    main()
