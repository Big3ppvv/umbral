import { Game, type GameSnapshot, type Upgrade } from './engine.js';
import { ProfileStore, type RunRecord } from './profile.js';

const $ = <T extends HTMLElement = HTMLElement>(selector: string): T => {
  const element = document.querySelector<T>(selector);
  if (!element) throw new Error(`Falta un elemento de la interfaz: ${selector}`);
  return element;
};
const text = (selector: string, value: string | number): void => { $(selector).textContent = String(value); };
const formatTime = (seconds: number): string => `${Math.floor(seconds / 60).toString().padStart(2, '0')}:${Math.floor(seconds % 60).toString().padStart(2, '0')}`;
const icon = (name: string): string => `<svg aria-hidden="true"><use href="#i-${name}"/></svg>`;
const glyphs: Record<string, string> = { flame: '♧', heart: '♡', wind: '≋', clock: '◷', sparkles: '✧', snowflake: '❄', shield: '⬡', leaf: '❧', moon: '☽', sword: '†', droplet: '♢' };
const rooms = ['El umbral', 'Los ecos', 'El osario', 'La vigilia', 'El abismo', 'El devorador'];
const store = new ProfileStore();
const canvas = $<HTMLCanvasElement>('#game-canvas');
const startButton = $<HTMLButtonElement>('#start-button');
const pauseButton = $<HTMLButtonElement>('#pause-button');
const overlay = $('#game-overlay');
const rewardDialog = $<HTMLDialogElement>('#reward-dialog');
const helpDialog = $<HTMLDialogElement>('#help-dialog');
let currentView = 'play';
let ready = false;
let soundEnabled = false;
let runId = '';
let lastStatus = '';
let relicKey = '';
let toastTimer: ReturnType<typeof setTimeout>;
let pendingRuns: RunRecord[] = [];
let saving: Promise<void> | null = null;
let purchasing = false;
let uncertainPurchase = false;
let helpWasPlaying = false;

function notice(message: string, error = false): void {
  const toast = $('#notice');
  clearTimeout(toastTimer);
  toast.textContent = message;
  toast.classList.toggle('error', error);
  toast.classList.add('visible');
  toastTimer = setTimeout(() => toast.classList.remove('visible'), 4200);
}

function queueKey(): string { return `umbra.pending.${store.mode === 'server' ? 'server' : 'local'}`; }
function saveQueue(): void {
  try { localStorage.setItem(queueKey(), JSON.stringify(pendingRuns)); } catch { /* Session queue remains playable. */ }
}

function renderProfile(): void {
  const p = store.profile;
  text('#wallet', p.shards);
  text('#record-runs', p.runs);
  text('#record-wins', p.wins);
  text('#record-room', p.bestRoom ? `${p.bestRoom} / 6` : '—');
  text('#record-time', p.bestTime === null ? '—' : formatTime(p.bestTime));
  text('#record-hint', p.runs ? 'Cada regreso alimenta el faro.' : 'Tu historia empieza en el umbral.');
  const mode = store.mode === 'server' ? 'Guardado en Python · SQLite' : store.mode === 'local' ? 'Progreso guardado en este navegador' : 'Progreso solo en esta sesión';
  text('#storage-status', pendingRuns.length ? `${pendingRuns.length} ${pendingRuns.length === 1 ? 'partida pendiente' : 'partidas pendientes'} de guardar` : mode);
  for (const kind of ['vitality', 'power'] as const) {
    const level = p[kind];
    const cost = 15 * (level + 1);
    text(`#${kind}-level`, `Nivel ${level} / 5`);
    text(`#${kind}-effect`, kind === 'vitality' ? `+${level * 10} de vitalidad` : `+${level * 2} de poder`);
    $(`#${kind}-track`).innerHTML = Array.from({ length: 5 }, (_, i) => `<span class="${i < level ? 'unlocked' : ''}"></span>`).join('');
    const button = $<HTMLButtonElement>(`#upgrade-${kind}`);
    button.disabled = !ready || purchasing || uncertainPurchase || pendingRuns.length > 0 || level >= 5 || p.shards < cost;
    button.textContent = purchasing ? 'Guardando mejora…' : level >= 5 ? 'Mejora completada' : `Mejorar · ${cost} fragmentos`;
    text(`#${kind}-reason`, uncertainPurchase ? 'Recarga la página para comprobar tu compra.' : pendingRuns.length ? 'Guarda tus partidas pendientes para mejorar.' : level >= 5 ? 'La luz ha alcanzado su máximo.' : p.shards < cost ? `Te faltan ${cost - p.shards} fragmentos.` : 'Se aplicará en la próxima incursión.');
  }
}

function renderGame(s: GameSnapshot): void {
  const hp = s.status === 'idle' ? 100 + store.profile.vitality * 10 : s.hp;
  const maxHp = s.status === 'idle' ? hp : s.maxHp;
  text('#hp-text', `${hp} / ${maxHp}`);
  $('#hp-fill').style.width = `${Math.max(0, hp / maxHp * 100)}%`;
  $('#hp-fill').style.backgroundColor = hp < maxHp * .3 ? 'var(--color-danger)' : 'var(--color-secondary)';
  const health = $('#hp-bar');
  health.setAttribute('aria-valuemax', String(maxHp));
  health.setAttribute('aria-valuenow', String(Math.max(0, hp)));
  text('#damage-value', s.status === 'idle' ? 22 + store.profile.power * 2 : s.damage);
  text('#dash-value', s.dashCooldown > 0 ? `${s.dashCooldown.toFixed(1)} s` : 'Listo');
  text('#time-value', formatTime(s.time));
  text('#kills-value', s.kills);
  text('#coins-value', s.coins);
  text('#room-label', `SALA ${String(s.room).padStart(2, '0')} / 06`);
  text('#dungeon-title', s.status === 'idle' ? 'La cripta olvidada' : rooms[s.room - 1]);
  text('#seed-display', s.status === 'idle' ? 'MUNDO PROCEDURAL' : `SEMILLA · ${s.seed}`);
  text('#game-status', s.status === 'playing' ? `${s.enemies} ${s.enemies === 1 ? 'sombra acecha' : 'sombras acechan'}` : ({ idle: 'El umbral espera', paused: 'La luz espera tu regreso', reward: 'La sala ha sido purificada', dead: 'Tu luz regresará al faro', won: 'El amanecer vuelve a la cripta' }[s.status] ?? ''));
  pauseButton.disabled = !['playing', 'paused'].includes(s.status);
  pauseButton.setAttribute('aria-label', s.status === 'paused' ? 'Continuar partida' : 'Pausar partida');
  pauseButton.title = s.status === 'paused' ? 'Continuar partida' : 'Pausar partida';
  document.querySelectorAll('#room-route li').forEach((node, i) => {
    node.classList.toggle('current', i + 1 === s.room);
    node.classList.toggle('complete', i + 1 < s.room || s.status === 'won');
    if (i + 1 === s.room) node.setAttribute('aria-current', 'step'); else node.removeAttribute('aria-current');
  });
  const key = s.relics.map(r => r.id).join(',');
  if (key !== relicKey) {
    relicKey = key;
    const list = $('#relic-list');
    list.replaceChildren();
    for (const relic of s.relics) {
      const item = document.createElement('span');
      item.className = 'relic-token';
      item.textContent = glyphs[relic.icon] ?? '◇';
      item.title = `${relic.name}: ${relic.description}`;
      item.setAttribute('aria-label', item.title);
      item.tabIndex = 0;
      list.append(item);
    }
    for (let i = s.relics.length; i < 3; i++) {
      const item = document.createElement('span'); item.className = 'empty-relic'; item.textContent = '◇'; list.append(item);
    }
    text('#relic-count', s.relics.length);
    text('#relic-hint', s.relics.length ? s.relics[s.relics.length - 1].name : 'La cripta guarda sus secretos.');
  }
  if (lastStatus !== s.status) {
    lastStatus = s.status;
    overlay.dataset.status = s.status;
    overlay.classList.toggle('hidden', ['playing', 'reward'].includes(s.status));
    if (s.status === 'paused') {
      text('#overlay-eyebrow', 'UN MOMENTO JUNTO A LA LUZ');
      $('#overlay-title').innerHTML = 'Respira.<br>El faro espera.';
      text('#overlay-description', 'La oscuridad puede esperar un poco.');
      startButton.innerHTML = `Continuar incursión ${icon('arrow')}`;
      text('#overlay-note', 'Pulsa ESC para continuar');
    }
    if (s.status === 'playing') canvas.focus({ preventScroll: true });
  }
}

function reward(choices: Upgrade[]): void {
  const container = $('#reward-choices');
  container.replaceChildren();
  for (const choice of choices) {
    const button = document.createElement('button');
    button.className = 'reward-choice';
    const symbol = document.createElement('span'); symbol.className = 'relic-icon'; symbol.textContent = glyphs[choice.icon] ?? '◇'; symbol.setAttribute('aria-hidden', 'true');
    const title = document.createElement('strong'); title.textContent = choice.name;
    const description = document.createElement('small'); description.textContent = choice.description;
    const rarity = document.createElement('span'); rarity.className = 'rarity'; rarity.textContent = choice.rarity;
    button.append(symbol, title, description, rarity);
    button.addEventListener('click', () => { rewardDialog.close(); game.chooseUpgrade(choice.id); });
    container.append(button);
  }
  rewardDialog.showModal();
}

async function flushSaves(): Promise<void> {
  if (saving) return saving;
  saving = (async () => {
    $<HTMLButtonElement>('#retry-save').disabled = true;
    try {
      while (pendingRuns.length) {
        await store.recordRun(pendingRuns[0]);
        pendingRuns.shift();
        saveQueue();
      }
      $('#save-error').hidden = true;
    } catch (error) {
      $('#save-error').hidden = false;
      text('#save-error span', error instanceof Error ? error.message : 'No se pudo guardar. Reintenta cuando vuelva la conexión.');
    } finally {
      $<HTMLButtonElement>('#retry-save').disabled = false;
      renderProfile();
    }
  })();
  await saving;
  saving = null;
}

function endRun(won: boolean, snapshot: GameSnapshot): void {
  renderGame(snapshot);
  text('#overlay-eyebrow', won ? 'LA OSCURIDAD HA CEDIDO' : 'TODA LUZ ENCUENTRA EL CAMINO');
  $('#overlay-title').innerHTML = won ? 'El amanecer<br>es tuyo.' : 'La luz cae.<br>Pero no se pierde.';
  text('#overlay-description', `${snapshot.kills} sombras · ${formatTime(snapshot.time)} · sala ${snapshot.room} de 6`);
  startButton.innerHTML = `Volver a intentarlo ${icon('arrow')}`;
  text('#overlay-note', `+${snapshot.coins} fragmentos para el Refugio${won ? ' · Cripta completada' : ''}`);
  pendingRuns.push({ id: runId, won, room: snapshot.room, kills: snapshot.kills, duration: snapshot.time, shards: Math.min(1000, snapshot.coins) });
  saveQueue();
  void flushSaves();
  startButton.focus({ preventScroll: true });
}

const game = new Game(canvas, { onUpdate: renderGame, onReward: reward, onEnd: endRun, onNotice: message => notice(message) });
renderGame(game.snapshot());
startButton.disabled = true;

startButton.addEventListener('click', () => {
  if (!ready) return;
  if (game.snapshot().status === 'paused') { game.resume(); return; }
  runId = typeof crypto.randomUUID === 'function' ? crypto.randomUUID() : `umbra_${Date.now()}_${Math.random().toString(36).slice(2)}`;
  game.start({ vitality: store.profile.vitality, power: store.profile.power });
});

function togglePause(): void {
  if (game.snapshot().status === 'playing') game.pause();
  else if (game.snapshot().status === 'paused' && currentView === 'play') game.resume();
}
pauseButton.addEventListener('click', togglePause);
window.addEventListener('keydown', event => {
  if (event.isComposing || event.repeat || event.key !== 'Escape' || helpDialog.open || rewardDialog.open) return;
  event.preventDefault();
  togglePause();
});
rewardDialog.addEventListener('cancel', event => event.preventDefault());
$('#sound-button').addEventListener('click', () => {
  soundEnabled = !soundEnabled;
  game.setSound(soundEnabled);
  const button = $('#sound-button');
  button.setAttribute('aria-pressed', String(soundEnabled));
  button.setAttribute('aria-label', soundEnabled ? 'Desactivar sonido' : 'Activar sonido');
  button.title = soundEnabled ? 'Desactivar sonido' : 'Activar sonido';
  button.innerHTML = icon(soundEnabled ? 'volume' : 'mute');
  notice(soundEnabled ? 'Sonidos de la cripta activados.' : 'Sonido desactivado.');
});
$('#help-button').addEventListener('click', () => {
  helpWasPlaying = game.snapshot().status === 'playing';
  game.pause();
  helpDialog.showModal();
});
helpDialog.querySelectorAll('.dialog-close').forEach(button => button.addEventListener('click', () => helpDialog.close()));
helpDialog.addEventListener('close', () => { if (helpWasPlaying && currentView === 'play') game.resume(); helpWasPlaying = false; });

function setView(view: string): void {
  if (!['play', 'codex', 'refuge'].includes(view)) return;
  if (view !== 'play') game.pause();
  currentView = view;
  document.querySelectorAll<HTMLElement>('.view').forEach(node => { node.hidden = node.id !== `view-${view}`; });
  document.querySelectorAll<HTMLElement>('[data-view]').forEach(button => {
    const active = button.dataset.view === view;
    button.classList.toggle('active', active);
    if (active) button.setAttribute('aria-current', 'page'); else button.removeAttribute('aria-current');
  });
  text('#page-title', view === 'play' ? 'Que la luz no se apague.' : view === 'codex' ? 'Lo que la cripta susurra.' : 'Aquí, la luz permanece.');
  text('#page-subtitle', view === 'play' ? 'Una cripta cambiante. Una vida a la vez. Algo que conservar.' : view === 'codex' ? 'Conocimiento para cuando las antorchas se apaguen.' : 'Todo lo que has ganado sigue contigo.');
  document.title = `UMBRA · ${view === 'play' ? 'El último faro' : view === 'codex' ? 'Códice' : 'Refugio'}`;
  if (view === 'refuge') renderProfile();
}
document.querySelectorAll<HTMLElement>('[data-view]').forEach(button => button.addEventListener('click', () => setView(button.dataset.view!)));
document.querySelectorAll<HTMLElement>('[data-go]').forEach(button => button.addEventListener('click', () => { setView(button.dataset.go!); $('#page-title').scrollIntoView({ block: 'start', behavior: 'instant' }); }));
$('#retry-save').addEventListener('click', () => { void flushSaves(); });

for (const kind of ['vitality', 'power'] as const) {
  $(`#upgrade-${kind}`).addEventListener('click', async () => {
    if (purchasing || uncertainPurchase) return;
    purchasing = true;
    renderProfile();
    try {
      await store.upgrade(kind);
      notice(`${kind === 'vitality' ? 'Corazón del faro' : 'Filo de aurora'} mejorado. Te espera en tu próxima incursión.`);
    } catch (error) {
      const message = error instanceof Error ? error.message : 'No se pudo guardar la mejora.';
      if (store.mode === 'server') uncertainPurchase = true;
      notice(message, true);
    } finally {
      purchasing = false;
      renderProfile();
      if (game.snapshot().status === 'idle') renderGame(game.snapshot());
    }
  });
}

document.querySelectorAll<HTMLButtonElement>('[data-input]').forEach(button => {
  const release = (): void => { game.setInput(button.dataset.input!, false); button.classList.remove('pressed'); };
  button.addEventListener('pointerdown', event => {
    event.preventDefault();
    button.setPointerCapture(event.pointerId);
    game.setInput(button.dataset.input!, true);
    button.classList.add('pressed');
  });
  button.addEventListener('pointerup', release);
  button.addEventListener('pointercancel', release);
  button.addEventListener('lostpointercapture', release);
  button.addEventListener('contextmenu', event => event.preventDefault());
});

function populateCodex(): void {
  const creatures = [
    ['♟', 'SOMBRA ERRANTE', 'El acechador', 'Te sigue sin descanso. Mantén la distancia y evita quedarte entre una sombra y un muro.'],
    ['✧', 'ESPÍRITU A DISTANCIA', 'El fuego fatuo', 'Se mantiene lejos y dispara orbes. Muévete en diagonal; sus proyectiles dejan espacio para esquivar.'],
    ['♜', 'GUARDIÁN DE PIEDRA', 'El quebrantador', 'Se detiene antes de cargar. La señal roja anuncia su embestida: cambia de dirección.'],
    ['♛', 'JEFE · SALA 6', 'El Devorador', 'Lanza anillos de oscuridad y convoca sombras. Conserva tu esquiva para cruzar sus proyectiles.'],
  ];
  const relics = [
    ['†', 'PODER', 'Brasa antigua', 'Añade 8 de daño a tus ataques. Combínala con Eco del faro para multiplicar tu fuerza.'],
    ['☽', 'MOVILIDAD', 'Velo de niebla', 'Reduce un 25 % la recarga de la esquiva. Un instante de invulnerabilidad puede cambiar tu destino.'],
    ['❧', 'RECUPERACIÓN', 'Raíz carmesí', 'Recupera 3 de vitalidad por enemigo derrotado. También encontrarás curación y vida máxima.'],
    ['✧', 'COMBINACIONES', 'Tu propia luz', 'Once reliquias pueden aparecer entre salas. Sus efectos se combinan y duran hasta terminar la incursión.'],
  ];
  for (const [selector, items] of [['#enemy-codex', creatures], ['#relic-codex', relics]] as const) {
    for (const [symbol, category, title, description] of items) {
      const card = document.createElement('article'); card.className = 'panel codex-card';
      const mark = document.createElement('div'); mark.className = 'codex-icon'; mark.textContent = symbol; mark.setAttribute('aria-hidden', 'true');
      const label = document.createElement('div'); label.className = 'eyebrow'; label.textContent = category;
      const heading = document.createElement('h3'); heading.textContent = title;
      const body = document.createElement('p'); body.textContent = description;
      card.append(mark, label, heading, body); $(selector).append(card);
    }
  }
}
populateCodex();

async function initialize(): Promise<void> {
  await store.init();
  try {
    const pending: unknown = JSON.parse(localStorage.getItem(queueKey()) ?? '[]');
    if (Array.isArray(pending)) {
      pendingRuns = pending.filter((run): run is RunRecord => typeof run === 'object' && run !== null && typeof run.id === 'string' && /^[a-zA-Z0-9_-]{8,80}$/.test(run.id) && typeof run.won === 'boolean' && Number.isFinite(run.duration) && run.duration >= 0 && run.duration <= 86400 && Number.isInteger(run.room) && run.room >= 1 && run.room <= 6 && (!run.won || run.room === 6) && Number.isInteger(run.kills) && run.kills >= 0 && run.kills <= 100000 && Number.isInteger(run.shards) && run.shards >= 0 && run.shards <= 1000);
      if (pendingRuns.length !== pending.length) { saveQueue(); notice('Se han descartado datos de partidas dañados.', true); }
    }
  } catch { /* Storage is optional; the session remains playable. */ }
  ready = true;
  startButton.disabled = false;
  renderProfile();
  renderGame(game.snapshot());
  if (pendingRuns.length) void flushSaves();
}
void initialize();
