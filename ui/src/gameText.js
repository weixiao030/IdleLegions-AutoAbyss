import catalog from '../../shared/game-text-zh.json' with {type:'json'};

const chinese = text => /[\u3400-\u9fff]/.test(text || '');
const escape = text => text.replace(/[.*+?^${}()|[\]\\]/g,'\\$&');
const terms = catalog.terms || {};
const termPattern = new RegExp(`(?<![\\p{L}\\p{N}_])(?:${Object.keys(terms).sort((a,b)=>b.length-a.length).map(escape).join('|')})(?![\\p{L}\\p{N}_])`,'gu');
export function gameText(text = '') {
  if (catalog.texts[text]) return catalog.texts[text];
  if (!text.startsWith('§')) return text.replace(termPattern, match => terms[match]);
  const [template, ...args] = text.slice(1).split('§');
  return args.reduce((result, arg) => {
    const fields = arg.startsWith('¦') ? arg.slice(1).split('¦') : [];
    const value = fields.length >= 2 ? gameText(fields[0]).replace('%s', gameText(fields[1])) : gameText(fields.length === 1 ? fields[0] : arg);
    return result.replace('%s', () => value);
  }, gameText(template));
}
const group = kind => kind === 'relic' ? 'cards' : `${kind}s`;
export const gameName = (kind, id, original = '') => {
  if (kind === 'relic' && catalog[group(kind)]?.[id]?.name) return catalog[group(kind)][id].name;
  if (chinese(original)) return gameText(original);
  const translated = catalog[group(kind)]?.[id]?.name || gameText(original || id || '未知');
  return kind === 'relic' && !chinese(translated) ? '新发现卡牌' : translated;
};
export const gameDescription = (kind, id, original = '', profession = '') => {
  const translated = gameText(original || (kind === 'skill' ? catalog.skillsByProfession?.[profession]?.[id]?.description : '') || catalog[group(kind)]?.[id]?.[kind === 'reaction' ? 'desc' : 'description'] || '');
  return kind === 'relic' && !chinese(translated) ? '效果资料待核实' : translated;
};
export const cardLabel = card => gameName('relic', card?.id, card?.nombreZh || card?.nameZh || card?.nombre || card?.name || card?.texto || '');
export const cardDescription = card => gameDescription('relic', card?.id, card?.descZh || card?.desc || '');
