import professions from '../../shared/professions.json';

export {professions};
export const names = Object.fromEntries(professions.map(p => [p.id, p.name]));
export const defaultFormation = ['berserker', 'mago', 'arquera', 'paladin'];
export const positionNames = ['前排 1 号位', '前排 2 号位', '后排 1 号位', '后排 2 号位'];
export const validFormation = value => Array.isArray(value) && value.length === 4
  && value.every(id => Object.hasOwn(names, id)) && new Set(value).size === 4;

// Choosing a profession already in the team swaps positions, without duplicates.
export function placeProfession(formation, index, id) {
  const next = [...formation];
  const previousIndex = next.indexOf(id);
  if (previousIndex >= 0) next[previousIndex] = next[index];
  next[index] = id;
  return next;
}
