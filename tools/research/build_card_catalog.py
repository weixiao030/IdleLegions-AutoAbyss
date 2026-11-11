"""Build the local card inventory from translated receipts and extracted art.

Unknown art is listed explicitly without invented names, effects or thresholds.
Usage: python tools/research/build_card_catalog.py --receipts PATH --images PATH
"""
import argparse
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
FAMILIES = dict(escarcha='寒冰', fulgor='雷电', brasa='烈焰', ponzona='毒素', umbra='暗影', acero='钢铁', aurora='光耀')
PREFIXES = dict(es='escarcha', fu='fulgor', br='brasa', po='ponzona', um='umbra', ac='acero', au='aurora')
CATEGORIES = dict(bounce='全体弹跳', critical='必定暴击', freeze='普攻冰冻', reaction='元素反应增伤', statusDamage='异常状态增伤', mark='标记与加深', execute='低血与斩杀增伤', burn='普攻点燃', lightning='普攻带电', poison='普攻中毒', slow='减速与减速增伤', attack='伤害、攻速与双暴', counter='反击与叠加防御', shield='开局护盾', survival='治疗、净化与生存', fervor='狂热与团队支援', unverified='其他与新卡')


def category(text):
    if re.search(r'每\s*第?\s*\d+\s*次.{0,40}(?:全体|所有敌人|所有目标)', text): return 'bounce'
    if '必定暴击' in text or '必暴' in text: return 'critical'
    if '普攻' in text and '冻结' in text: return 'freeze'
    if '反应' in text or '消耗状态' in text: return 'reaction'
    if re.search(r'(?:带状态|有状态|异常状态|负面状态).{0,20}(?:伤害|增伤)', text): return 'statusDamage'
    if '标记' in text or '印记' in text: return 'mark'
    if re.search(r'低血|低于|生命.{0,8}(?:少于|以下)', text) and re.search(r'伤害|增伤', text): return 'execute'
    if '普攻' in text and re.search('点燃|燃烧', text): return 'burn'
    if '普攻' in text and re.search('带电|触电', text): return 'lightning'
    if '普攻' in text and '中毒' in text: return 'poison'
    if '减速' in text: return 'slow'
    if '反击' in text or re.search('每承受.{0,12}防御', text): return 'counter'
    if re.search(r'伤害|攻速|攻击速度|暴击', text): return 'attack'
    if '护盾' in text: return 'shield'
    if '狂热' in text or '热度' in text: return 'fervor'
    return 'survival'


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--receipts', type=Path, nargs='+', required=True)
    p.add_argument('--images', type=Path, required=True)
    p.add_argument('--output', type=Path, default=ROOT/'shared/card-catalog.json')
    args = p.parse_args()
    definitions = {}
    for path in args.receipts:
        for card in json.loads(path.read_text(encoding='utf-8-sig')):
            definitions[card['id']] = card
    cards = {}
    for ident, item in definitions.items():
        desc, name = item.get('descZh', ''), item.get('nameZh', '')
        if not re.search('[\u3400-\u9fff]', desc) or not re.search('[\u3400-\u9fff]', name):
            raise ValueError(f'Missing Chinese translation: {ident}')
        badge = 'umbral' in item
        cards[ident] = dict(id=ident, family=item['familia'], category=category(desc), name=name, description=desc,
                            kind='badge' if badge else 'relic', rarity='系列徽章' if badge else dict(comun='普通',rara='稀有',epica='史诗',legendaria='传说').get(item.get('rareza'), '遗物'),
                            threshold=int(item['umbral']) if badge else 0, core='CONGELAN' in item['desc'].upper(), verified=True, source='local-receipt-and-game-locale')
    # One previously translated observed relic remains in the project's dictionary.
    prior = json.loads((ROOT/'shared/game-text-zh.json').read_text(encoding='utf-8-sig'))['cards']
    if 'au_letania' not in cards and 'au_letania' in prior:
        item = prior['au_letania']
        cards['au_letania'] = dict(id='au_letania',family='aurora',category=category(item['description']),name=item['name'],description=item['description'],kind='relic',rarity='遗物',threshold=0,core=False,verified=True,source='existing-observed-dictionary')
    for image in sorted(args.images.glob('*.png')):
        ident = image.stem
        if ident not in cards and re.match(r'^(es|fu|br|po|um|ac|au)[246]?_', ident):
            family = PREFIXES[ident[:2]]
            cards[ident] = dict(id=ident, family=family, category='unverified', name=FAMILIES[family]+'徽章（待核实）',description='已核对本地卡图，尚未取得含名称与效果的可靠回执。中文名称、效果与解锁门槛待核实，不推测数值。', kind='badge',rarity='资料待核实',threshold=None,core=False,verified=False,source='local-image-only')
    for card in cards.values():
        if not (args.images/(card['id']+'.png')).exists(): raise ValueError('Missing art: '+card['id'])
        card['image'] = '/cards/'+card['id']+'.png'
    data = dict(schemaVersion=1, families=[dict(id=k,name=v) for k,v in FAMILIES.items()], categories=[dict(id=k,name=v) for k,v in CATEGORIES.items()],
                coverage=dict(localCards=len(cards), verified=sum(c['verified'] for c in cards.values()), pending=sum(not c['verified'] for c in cards.values()), extractedImages=len(list(args.images.glob('*.png'))), scope='本地七系列及教义卡图清单；服务端可能新增卡牌，不宣称服务端全集'),
                cards=sorted(cards.values(),key=lambda c:(list(FAMILIES).index(c['family']),list(CATEGORIES).index(c['category']),c['id'])))
    args.output.parent.mkdir(parents=True, exist_ok=True)
    temp=args.output.with_suffix('.json.tmp');temp.write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf-8');temp.replace(args.output)
    print(json.dumps(data['coverage'],ensure_ascii=False))


if __name__ == '__main__': main()
