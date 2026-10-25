/**
 * The Games tab's arrange logic (web/src/components/game/organize.ts) — the fixtures behind
 * tests/run-organize-tests.ps1, which compiles the module with the console's own tsc and points
 * this at the output:
 *
 *   node tests/organize-fixtures.cjs .verify/organize
 *
 * It is all pure view logic (grouping, sorting, the rolling date buckets), so it runs headless. The
 * module graph reaches react through gameState -> syncAll — a store hook the pure slice never calls —
 * which is stubbed below rather than installed, so no node_modules is needed beyond tsc itself.
 */

const path = require('node:path');
const Module = require('node:module');

const load = Module._load;
Module._load = function (request, ...rest) {
  if (request === 'react') return { useSyncExternalStore: () => undefined };
  return load.call(this, request, ...rest);
};

const dir = process.argv[2];
if (!dir) {
  console.error('usage: node organize-fixtures.cjs <compiled-dir>');
  process.exit(2);
}
const { organize, sortGames, DEFAULT_ORGANIZE } = require(path.join(dir, 'components', 'game', 'organize.js'));
const { standing } = require(path.join(dir, 'components', 'game', 'gameState.js'));
const { groupTitles } = require(path.join(dir, 'components', 'game', 'titles.js'));

let pass = 0, fail = 0;
function check(name, actual, expected) {
  const a = JSON.stringify(actual), e = JSON.stringify(expected);
  if (a === e) { console.log(`PASS: ${name}`); pass++; }
  else { console.log(`FAIL: ${name}\n      got      ${a}\n      expected ${e}`); fail++; }
}

function mk(name, opts = {}) {
  return {
    game: {
      id: opts.id ?? name,
      name,
      enabled: opts.enabled ?? true,
      release: opts.release ?? null,
      emulators: opts.emulators ?? null,
      titleId: opts.titleId ?? null,
      titleName: opts.titleName ?? null,
      titleLead: opts.titleLead ?? false,
      titleEdition: opts.titleEdition ?? null,
    },
    emulator: opts.emulator ?? null,
    head: opts.head ?? null,
    lease: null,
    hasOpenConflict: opts.conflict ?? false,
    totalStorageBytes: opts.size ?? 0,
    machineIds: opts.machineIds ?? null,
    sources: opts.sources ?? null,
  };
}

const at = days => new Date(Date.now() - days * 86400e3).toISOString();
const names = entries => entries.map(e => e.game.name);
const layout = groups => groups.map(g => [g.label, g.subGroups.map(sg => [sg.label, names(sg.entries)])]);

// ---- sorting: the default and the direction flips ----------------------------------------------
const mixed = [
  mk('Big', { size: 1000, head: { createdAt: at(1), machineId: 'm1' } }),
  mk('Never', { size: 0 }),
  mk('Small', { size: 10, head: { createdAt: at(2), machineId: 'm2' } }),
];
check('default state is None + Name', DEFAULT_ORGANIZE, { groupBy: 'none', sortBy: 'name', dir: 1 });
check('None grouping is one unlabelled group, in name order',
  layout(organize([...mixed].reverse(), DEFAULT_ORGANIZE, { machines: [], problems: new Set() })),
  [['', [['', ['Big', 'Never', 'Small']]]]]);
check('Name, Z-A flips', names(sortGames(mixed, 'name', -1)), ['Small', 'Never', 'Big']);
check('Size, smallest first: a no-saves game stays last (the PR review fix)',
  names(sortGames(mixed, 'size', 1)), ['Small', 'Big', 'Never']);
check('Size, largest first: a no-saves game stays last',
  names(sortGames(mixed, 'size', -1)), ['Big', 'Small', 'Never']);
check('Recently synced, oldest first: a no-saves game stays last',
  names(sortGames(mixed, 'synced', 1)), ['Small', 'Big', 'Never']);
check('Recently synced, newest first: a no-saves game stays last',
  names(sortGames(mixed, 'synced', -1)), ['Big', 'Small', 'Never']);

// ---- the rolling date buckets -------------------------------------------------------------------
const dated = [
  mk('Today', { head: { createdAt: at(0), machineId: 'm' } }),
  mk('Yesterday', { head: { createdAt: at(1.5), machineId: 'm' } }),
  mk('ThisWeek', { head: { createdAt: at(3), machineId: 'm' } }),
  mk('DaySeven', { head: { createdAt: at(7.2), machineId: 'm' } }),
  mk('ThisMonth', { head: { createdAt: at(20), machineId: 'm' } }),
  mk('Earlier', { head: { createdAt: at(40), machineId: 'm' } }),
  mk('Fresh', {}),
];
const buckets = organize(dated, { groupBy: 'synced', sortBy: 'name', dir: 1 }, { machines: [], problems: new Set() });
check('Date synced: rolling buckets, Never synced last',
  layout(buckets),
  [['Today', [['', ['Today']]]], ['Yesterday', [['', ['Yesterday']]]], ['Earlier this week', [['', ['ThisWeek']]]],
   ['This month', [['', ['DaySeven', 'ThisMonth']]]], ['Earlier', [['', ['Earlier']]]], ['Never synced', [['', ['Fresh']]]]]);

// ---- device: overlap on purpose, and the head uploader is a machine too -------------------------
const machines = [{ id: 'm1', name: 'Deck' }, { id: 'm2', name: 'PC' }];
const DeviceNowhere = mk('Nowhere', {});
const devGroups = organize([
  mk('Both', { machineIds: ['m1', 'm2'], head: { createdAt: at(1), machineId: 'm1' } }),
  mk('DeckOnly', { machineIds: ['m1'], head: { createdAt: at(2), machineId: 'm1' } }),
  mk('PCOnly', { machineIds: ['m2'], head: { createdAt: at(3), machineId: 'm2' } }),
  mk('HeadOnly', { head: { createdAt: at(4), machineId: 'm1' } }),
  DeviceNowhere,
], { groupBy: 'device', sortBy: 'name', dir: 1 }, { machines, problems: new Set() });
check('Device: a game on two machines appears under both, orphans under No machine yet',
  layout(devGroups),
  [['Deck', [['', ['Both', 'DeckOnly', 'HeadOnly']]]],
   ['PC', [['', ['Both', 'PCOnly']]]],
   ['No machine yet', [['', ['Nowhere']]]]]);

// ---- platform: PC first, emulator sub-headings --------------------------------------------------
const platGroups = organize([
  mk('Metroid', { release: { console: 'GameCube' }, emulators: ['Dolphin'], head: { createdAt: at(1), machineId: 'm' } }),
  mk('Metroid Prime', { release: { console: 'GameCube' }, emulators: ['PrimeHack'], head: { createdAt: at(1), machineId: 'm' } }),
  mk('Half-Life', { head: { createdAt: at(1), machineId: 'm' } }),
  mk('Chrono', { emulators: ['RetroArch'], head: { createdAt: at(1), machineId: 'm' } }),
], { groupBy: 'platform', sortBy: 'name', dir: 1 }, { machines: [], problems: new Set() });
check('Platform: PC first, a console through two emulators splits into sub-headings',
  layout(platGroups),
  [['PC', [['', ['Half-Life']]]], ['Emulator', [['', ['Chrono']]]],
   ['GameCube', [['Dolphin', ['Metroid']], ['PrimeHack', ['Metroid Prime']]]]]);

// ---- source: the known kinds in order, then No source recorded ----------------------------------
const srcGroups = organize([
  mk('S', { sources: [{ kind: 'steam', detail: 'Installed game' }], head: { createdAt: at(1), machineId: 'm' } }),
  mk('H', { sources: [{ kind: 'heroic', detail: 'Epic Games' }], head: { createdAt: at(1), machineId: 'm' } }),
  mk('P', { sources: [{ kind: 'playnite', detail: 'Playnite library' }], head: { createdAt: at(1), machineId: 'm' } }),
  mk('E', { sources: [{ kind: 'emulator', detail: 'RetroArch' }], head: { createdAt: at(1), machineId: 'm' } }),
  mk('Scan', { sources: [{ kind: 'save-folder', detail: 'Goldberg' }], head: { createdAt: at(1), machineId: 'm' } }),
  mk('Hand', { sources: [{ kind: 'manual', detail: 'savelocker add-game' }], head: { createdAt: at(1), machineId: 'm' } }),
  mk('Unknown', { sources: [{ kind: 'itch', detail: 'Butler' }], head: { createdAt: at(1), machineId: 'm' } }),
  mk('NoSource', { head: { createdAt: at(1), machineId: 'm' } }),
  mk('OnlyEmulators', { emulators: ['RetroArch'], head: { createdAt: at(1), machineId: 'm' } }),
], { groupBy: 'source', sortBy: 'name', dir: 1 }, { machines: [], problems: new Set() });
check('Source: Steam, Heroic, Playnite, Emulator, save-folder scan, added by hand, then unrecorded',
  layout(srcGroups),
  [['Steam', [['', ['S']]]], ['Heroic', [['', ['H']]]], ['Playnite', [['', ['P']]]],
   ['Emulator', [['', ['E', 'OnlyEmulators']]]], ['Save-folder scan', [['', ['Scan']]]],
   ['Added by hand', [['', ['Hand']]]], ['No source recorded', [['', ['NoSource', 'Unknown']]]]]);

// ---- status: the group order, and every heading agrees with the row's own dot --------------------
const problems = new Set(['Attention']);
const statusGames = [
  mk('Conflict', { conflict: true, head: { createdAt: at(1), machineId: 'm' } }),
  mk('Attention', { head: { createdAt: at(1), machineId: 'm' } }),
  mk('Synced', { head: { createdAt: at(1), machineId: 'm' } }),
  mk('Fresh', {}),
  mk('Disabled', { enabled: false, head: { createdAt: at(1), machineId: 'm' } }),
];
const stGroups = organize(statusGames, { groupBy: 'status', sortBy: 'name', dir: 1 }, { machines: [], problems });
check('Status: Conflict, Needs attention, Synced, No saves yet, Disabled',
  layout(stGroups),
  [['Conflict', [['', ['Conflict']]]], ['Needs attention', [['', ['Attention']]]],
   ['Synced', [['', ['Synced']]]], ['No saves yet', [['', ['Fresh']]]], ['Disabled', [['', ['Disabled']]]]]);
check('Status: every heading equals the row dot\'s own label',
  stGroups.every(g => g.subGroups[0].entries.every(s => standing(s, problems).label === g.label)), true);

// ---- titles (tasks/multi-emulator-games): one row per game, one tree per emulator ----------------------
const ds = { console: 'Nintendo DS', region: 'USA' }, wii = { console: 'Wii', region: 'USA' };
const trees = [
  mk('Metroid Prime - Hunters', { id: 'mph-md', titleId: 'T1', titleName: 'Metroid Prime - Hunters', titleLead: true, emulator: 'melonDS',
    release: ds, size: 100, machineIds: ['m1'], head: { createdAt: at(2), machineId: 'm1' } }),
  mk('Metroid Prime - Hunters (RetroArch)', { id: 'mph-ra', titleId: 'T1', titleName: 'Metroid Prime - Hunters', emulator: 'RetroArch',
    release: ds, size: 50, machineIds: ['m2'], head: { createdAt: at(0), machineId: 'm2' } }),
  mk('Metroid Prime Trilogy (PrimeHack)', { id: 'mpt-ph', titleId: 'T2', titleName: 'Metroid Prime Trilogy', emulator: 'PrimeHack',
    release: wii, conflict: true, head: { createdAt: at(1), machineId: 'm1' } }),
  mk('Metroid Prime Trilogy', { id: 'mpt-do', titleId: 'T2', titleName: 'Metroid Prime Trilogy', titleLead: true, emulator: 'Dolphin',
    release: wii, head: { createdAt: at(3), machineId: 'm1' } }),
  mk('Tomb Raider', { emulator: 'DuckStation', release: { console: 'PlayStation' }, head: { createdAt: at(1), machineId: 'm2' } }),
];
const titles = groupTitles(trees);
check('Titles: one row each, named without the emulator suffix', names(titles),
  ['Metroid Prime - Hunters', 'Metroid Prime Trilogy', 'Tomb Raider']);
check('Titles: trees ordered by emulator', titles.map(t => t.trees.map(x => x.emulator)),
  [['melonDS', 'RetroArch'], ['Dolphin', 'PrimeHack'], ['DuckStation']]);
check('Titles: size is the sum, last synced the newest, machines the union',
  [titles[0].totalStorageBytes, titles[0].head.machineId, titles[0].machineIds], [150, 'm2', ['m1', 'm2']]);
check("Titles: a conflict in one tree is the title's status", standing(titles[1], new Set()).label, 'Conflict');
check('Titles: a problem on one tree needs attention', standing(titles[0], new Set(['mph-ra'])).label, 'Needs attention');
check('Titles: a game with no title is its own, unchanged', titles[2].game.name === 'Tomb Raider' && titles[2].trees.length === 1, true);
check('Titles: Platform grouping lists a title under each emulator',
  layout(organize(titles, { groupBy: 'platform', sortBy: 'name', dir: 1 }, { machines: [], problems: new Set() })),
  [['Nintendo DS', [['melonDS', ['Metroid Prime - Hunters']], ['RetroArch', ['Metroid Prime - Hunters']]]],
   ['PlayStation', [['', ['Tomb Raider']]]],
   ['Wii', [['Dolphin', ['Metroid Prime Trilogy']], ['PrimeHack', ['Metroid Prime Trilogy']]]]]);
check("Titles: Device grouping is the union of the trees' machines",
  layout(organize(titles, { groupBy: 'device', sortBy: 'name', dir: 1 },
    { machines: [{ id: 'm1', name: 'WinTest' }, { id: 'm2', name: 'DeckTest' }], problems: new Set() })),
  [['WinTest', [['', ['Metroid Prime - Hunters', 'Metroid Prime Trilogy']]]], ['DeckTest', [['', ['Metroid Prime - Hunters', 'Tomb Raider']]]]]);

console.log(`\n${pass} passed, ${fail} failed`);
process.exit(fail > 0 ? 1 : 0);
