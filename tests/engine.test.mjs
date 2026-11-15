import test from 'node:test';
import assert from 'node:assert/strict';
import { Game } from '../dist/assets/engine.js';

// A canvas substitute keeps simulation tests independent of graphics/browser drivers.
const noop = () => {};
const context = new Proxy({}, {
  get(target, key) {
    if (key === 'createRadialGradient') return () => ({ addColorStop: noop });
    return target[key] ?? noop;
  },
  set(target, key, value) { target[key] = value; return true; },
});
const canvas = () => ({
  width: 0, height: 0, getContext: () => context, addEventListener: noop,
  getBoundingClientRect: () => ({ left: 0, top: 0, width: 960, height: 600 }),
});
globalThis.window = { matchMedia: () => ({ matches: false }), addEventListener: noop };
globalThis.document = { hidden: false, createElement: canvas, addEventListener: noop };
globalThis.requestAnimationFrame = () => 1;
globalThis.cancelAnimationFrame = noop;

function setup() {
  const events = { updates: [], rewards: [], ends: [], notices: [] };
  const game = new Game(canvas(), {
    onUpdate: value => events.updates.push(value),
    onReward: value => events.rewards.push(value),
    onEnd: (won, value) => events.ends.push({ won, value }),
    onNotice: value => events.notices.push(value),
  });
  return { game, events };
}

test('idle preview and paused game preserve health and simulation time', () => {
  const { game } = setup();
  assert.equal(game.snapshot().room, 1);
  assert.equal(game.snapshot().status, 'idle');
  game.enemies[0].x = game.player.x;
  game.enemies[0].y = game.player.y;
  game.tick(1000);
  game.tick(900000);
  assert.equal(game.snapshot().hp, 100);
  assert.equal(game.snapshot().time, 0);
  game.start(undefined, 'CLOCK');
  game.tick(950000);
  assert.ok(game.snapshot().time <= .033, 'background gaps must not advance combat');
  game.pause();
  const paused = game.snapshot();
  game.tick(990000);
  assert.equal(game.snapshot().time, paused.time);
  assert.equal(game.snapshot().hp, paused.hp);
  game.resume();
  assert.equal(game.snapshot().status, 'playing');
  game.destroy();
});

test('same seed repeats room generation and permanent levels apply documented bonuses', () => {
  const { game } = setup();
  game.start({ vitality: 2, power: 3 }, 'TEST42');
  assert.equal(game.snapshot().maxHp, 120);
  assert.equal(game.snapshot().damage, 28);
  const layout = structuredClone(game.obstacles);
  const foes = game.enemies.map(({ x, y, kind }) => ({ x, y, kind }));
  game.start({ vitality: 2, power: 3 }, 'TEST42');
  assert.deepEqual(game.obstacles, layout);
  assert.deepEqual(game.enemies.map(({ x, y, kind }) => ({ x, y, kind })), foes);
  game.destroy();
});

test('auto-fire hits an enemy overlapping the player at 30 fps', () => {
  const { game } = setup();
  game.start(undefined, 'CONTACT');
  const enemy = game.createEnemy('shade', game.player.x, game.player.y);
  enemy.speed = 0;
  game.enemies = [enemy];
  game.pendingSpawns = [];
  game.spawnGrace = 0;
  game.shotTimer = 0;
  game.player.invincible = Infinity;
  for (let i = 0; i < 90 && game.status === 'playing'; i++) game.update(.033);
  assert.equal(game.snapshot().kills, 1);
  assert.equal(game.snapshot().status, 'reward');
  game.destroy();
});

test('room rewards reject unknown choices, heal, and advance only once', () => {
  const { game, events } = setup();
  game.start(undefined, 'REWARD');
  game.enemies = [];
  game.pendingSpawns = [];
  game.spawnGrace = 0;
  game.player.hp = 50;
  game.update(.016);
  assert.equal(events.rewards.length, 1);
  assert.equal(events.rewards[0].length, 3);
  game.chooseUpgrade('unknown');
  assert.equal(game.snapshot().room, 1);
  const choice = game.choices.find(value => !['heart', 'salve'].includes(value.id));
  assert.ok(choice);
  game.chooseUpgrade(choice.id);
  assert.equal(game.snapshot().room, 2);
  assert.equal(game.snapshot().hp, 65);
  assert.equal(game.snapshot().relics[0].id, choice.id);
  game.chooseUpgrade(choice.id);
  assert.equal(game.snapshot().room, 2);
  assert.equal(game.snapshot().relics.length, 1);
  game.destroy();
});

test('death callback fires once and terminal frames preserve the final result', () => {
  const { game, events } = setup();
  game.start(undefined, 'END');
  game.player.hp = 0;
  game.tick(1000);
  const time = game.snapshot().time;
  for (let i = 0; i < 20; i++) game.tick(2000 + i * 16);
  assert.equal(game.snapshot().status, 'dead');
  assert.equal(game.snapshot().time, time);
  assert.equal(events.ends.length, 1);
  assert.equal(events.ends[0].won, false);
  game.destroy();
});

for (const seed of ['UMBRA', 'TEST42', 'ALBA']) {
  test(`base character can clear all six rooms using ordinary movement and dash (${seed})`, () => {
    const { game, events } = setup();
    game.start(undefined, seed);
    // A simple circling controller uses the same input API as touch controls.
    // No invulnerability, stat changes, skipped enemies, or forced room transitions.
    for (let frame = 0; frame < 60 * 300 && !['dead', 'won'].includes(game.status); frame++) {
      if (game.status === 'reward') {
        const choice = game.choices.find(value => value.id === 'echo')
          || game.choices.find(value => value.id === 'ember')
          || game.choices.find(value => value.id === 'haste')
          || game.choices[0];
        game.chooseUpgrade(choice.id);
      } else {
        const angle = game.elapsed * .7;
        const dx = 480 + 110 * Math.cos(angle) - game.player.x;
        const dy = 330 + 105 * Math.sin(angle) - game.player.y;
        game.setInput('left', dx < -6);
        game.setInput('right', dx > 6);
        game.setInput('up', dy < -6);
        game.setInput('down', dy > 6);
        if (game.enemies.some(enemy => Math.hypot(enemy.x - game.player.x, enemy.y - game.player.y) < 50)) game.setInput('dash', true);
        game.update(1 / 60);
      }
    }
    assert.equal(game.snapshot().status, 'won', JSON.stringify(game.snapshot()));
    assert.equal(game.snapshot().room, 6);
    assert.equal(game.snapshot().kills, 77);
    assert.equal(game.snapshot().relics.length, 5);
    assert.equal(game.snapshot().enemies, 0);
    assert.equal(events.ends.length, 1);
    assert.equal(events.ends[0].won, true);
    assert.ok(game.snapshot().hp > 0);
    game.destroy();
  });
}
