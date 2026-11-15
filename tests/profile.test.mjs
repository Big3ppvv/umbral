import assert from 'node:assert/strict';
import { beforeEach, test } from 'node:test';
import { ProfileStore } from '../dist/assets/profile.js';

const storageKey = 'umbra.profile.v1';
let values;
const run = { id: 'profile_test_001', won: false, room: 3, kills: 9, duration: 80, shards: 20 };
const freshProfile = () => ({ version: 1, shards: 0, vitality: 0, power: 0, runs: 0, wins: 0, bestRoom: 0, bestTime: null });

beforeEach(() => {
  values = new Map();
  globalThis.localStorage = {
    getItem: key => values.get(key) ?? null,
    setItem: (key, value) => values.set(key, String(value)),
    removeItem: key => values.delete(key),
  };
  globalThis.fetch = async () => { throw new TypeError('API unavailable in static hosting'); };
});

test('local saves survive reloading and duplicate IDs cannot award twice', async () => {
  const first = new ProfileStore();
  await first.init();
  assert.equal(first.mode, 'local');
  await first.recordRun(run);
  await first.recordRun(run);
  assert.equal(first.profile.runs, 1);
  assert.equal(first.profile.shards, 20);
  assert.equal(first.profile.bestRoom, 3);
  assert.equal(first.profile.bestTime, null);

  const reloaded = new ProfileStore();
  await reloaded.init();
  await reloaded.recordRun(run);
  assert.deepEqual(reloaded.profile, first.profile);
  assert.deepEqual(JSON.parse(values.get(storageKey)).completedRuns, [run.id]);
});

test('upgrades use increasing costs and persist levels without spending twice', async () => {
  const store = new ProfileStore();
  await store.init();
  await store.recordRun({ ...run, shards: 1000 });
  for (let level = 1; level <= 5; level++) {
    await store.upgrade('vitality');
    assert.equal(store.profile.vitality, level);
  }
  assert.equal(store.profile.shards, 1000 - 15 * (1 + 2 + 3 + 4 + 5));
  const before = store.profile.shards;
  await assert.rejects(store.upgrade('vitality'), /nivel máximo/);
  assert.equal(store.profile.shards, before);

  const reloaded = new ProfileStore();
  await reloaded.init();
  assert.equal(reloaded.profile.vitality, 5);
  assert.equal(reloaded.profile.shards, before);
});

test('insufficient balance and invalid records cannot change the profile', async () => {
  const store = new ProfileStore();
  await store.init();
  await assert.rejects(store.upgrade('power'), /15 fragmentos/);
  for (const invalid of [
    { shards: -1 }, { shards: 1001 }, { kills: -1 }, { kills: 100001 },
    { duration: -1 }, { duration: Infinity }, { duration: 86401 },
    { room: 0 }, { room: 7 }, { won: true }, { id: 'short' },
  ]) {
    await assert.rejects(store.recordRun({ ...run, ...invalid }), /inválidos/);
  }
  assert.deepEqual(store.profile, freshProfile());
});

test('the fastest completed victory is retained and mutations are serialized', async () => {
  const store = new ProfileStore();
  await store.init();
  await Promise.all([
    store.recordRun({ ...run, id: 'victory_test_01', won: true, room: 6, duration: 120 }),
    store.recordRun({ ...run, id: 'victory_test_02', won: true, room: 6, duration: 90 }),
    store.recordRun({ ...run, id: 'victory_test_03', won: true, room: 6, duration: 180 }),
  ]);
  assert.equal(store.profile.runs, 3);
  assert.equal(store.profile.wins, 3);
  assert.equal(store.profile.shards, 60);
  assert.equal(store.profile.bestTime, 90);
});

test('corrupt or invalid saved data starts a valid fresh local save', async () => {
  for (const corrupt of [
    '{broken json',
    JSON.stringify({ version: 1, profile: { ...freshProfile(), power: 99 }, completedRuns: [] }),
    JSON.stringify({ version: 1, profile: freshProfile(), completedRuns: ['invalid'] }),
  ]) {
    values.set(storageKey, corrupt);
    const store = new ProfileStore();
    await store.init();
    assert.equal(store.mode, 'local');
    assert.deepEqual(store.profile, freshProfile());
    await store.recordRun(run);
    assert.equal(store.profile.runs, 1);
    assert.equal(JSON.parse(values.get(storageKey)).profile.shards, 20);
  }
});

test('blocked storage or a failed write keeps the session playable in memory', async () => {
  globalThis.localStorage = {
    getItem() { throw new Error('Blocked'); },
    setItem() { throw new Error('Blocked'); },
    removeItem() {},
  };
  const store = new ProfileStore();
  await store.init();
  assert.equal(store.mode, 'memory');
  await store.recordRun(run);
  await store.upgrade('power');
  assert.equal(store.profile.power, 1);
  assert.equal(store.profile.shards, 5);
});

test('server errors retain server mode and do not award a local fallback', async () => {
  const profile = { ...freshProfile(), shards: 21, vitality: 1, power: 1, runs: 1, bestRoom: 1 };
  globalThis.fetch = async () => ({ ok: true, json: async () => profile });
  const store = new ProfileStore();
  await store.init();
  assert.equal(store.mode, 'server');

  globalThis.fetch = async () => { throw new TypeError('Connection lost'); };
  await assert.rejects(store.recordRun(run), /Sin conexión/);
  assert.equal(store.mode, 'server');
  assert.deepEqual(store.profile, profile);
  assert.equal(values.has(storageKey), false);

  globalThis.fetch = async () => ({ ok: false, json: async () => ({ error: 'Saldo insuficiente.' }) });
  await assert.rejects(store.upgrade('power'), /Saldo insuficiente/);
  assert.deepEqual(store.profile, profile);
});

test('server POST uses the stable run ID and accepts the returned profile', async () => {
  const store = new ProfileStore();
  globalThis.fetch = async () => ({ ok: true, json: async () => freshProfile() });
  await store.init();
  const result = { ...freshProfile(), shards: run.shards, runs: 1, bestRoom: 3 };
  globalThis.fetch = async (path, options) => {
    assert.equal(path, '/api/run');
    assert.equal(options.method, 'POST');
    assert.equal(JSON.parse(options.body).id, run.id);
    return { ok: true, json: async () => result };
  };
  await store.recordRun(run);
  assert.deepEqual(store.profile, result);
});
