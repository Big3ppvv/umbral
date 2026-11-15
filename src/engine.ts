export interface Upgrade {
  id: string;
  name: string;
  description: string;
  icon: string;
  rarity: string;
}

export interface GameSnapshot {
  hp: number;
  maxHp: number;
  room: number;
  kills: number;
  coins: number;
  time: number;
  dashCooldown: number;
  status: string;
  damage: number;
  speed: number;
  attackRate: number;
  relics: Upgrade[];
  seed: string;
  enemies: number;
}

type Status = 'idle' | 'playing' | 'paused' | 'reward' | 'dead' | 'won';
type EnemyKind = 'shade' | 'wisp' | 'brute' | 'boss';
type Point = { x: number; y: number };
type Obstacle = Point & { w: number; h: number; variant: number };
type Enemy = Point & {
  id: number;
  kind: EnemyKind;
  hp: number;
  maxHp: number;
  radius: number;
  speed: number;
  timer: number;
  flash: number;
  slow: number;
  charging: number;
  chargeX: number;
  chargeY: number;
  phase: number;
};
type Shot = Point & {
  vx: number;
  vy: number;
  life: number;
  damage: number;
  enemy: boolean;
  radius: number;
  pierce: number;
  hit: Set<number>;
};
type Particle = Point & { vx: number; vy: number; life: number; max: number; color: string; size: number };
type DamageText = Point & { text: string; life: number; color: string };
type Callbacks = {
  onUpdate: (s: GameSnapshot) => void;
  onReward: (choices: Upgrade[]) => void;
  onEnd: (won: boolean, s: GameSnapshot) => void;
  onNotice: (text: string) => void;
};

const WIDTH = 960;
const HEIGHT = 600;
const BOUNDS = { left: 70, right: 890, top: 102, bottom: 526 };
const ROOM_NAMES = ['El umbral', 'Los ecos', 'El osario', 'La vigilia', 'El abismo', 'El devorador'];
const UPGRADES: Upgrade[] = [
  { id: 'ember', name: 'Brasa antigua', description: 'Tus proyectiles infligen 8 de daño adicional.', icon: 'flame', rarity: 'Común' },
  { id: 'heart', name: 'Corazón de ámbar', description: '+25 de vida máxima. Recuperas 25 de vida.', icon: 'heart', rarity: 'Común' },
  { id: 'boots', name: 'Paso del espectro', description: 'Te mueves un 16 % más rápido.', icon: 'wind', rarity: 'Común' },
  { id: 'haste', name: 'Reloj sin tiempo', description: 'Disparas un 18 % más rápido.', icon: 'clock', rarity: 'Rara' },
  { id: 'echo', name: 'Eco del faro', description: 'Disparas un proyectil adicional en cada ataque.', icon: 'sparkles', rarity: 'Épica' },
  { id: 'frost', name: 'Lágrima lunar', description: 'Tus impactos ralentizan a los enemigos.', icon: 'snowflake', rarity: 'Rara' },
  { id: 'armor', name: 'Pacto de piedra', description: 'Recibes un 20 % menos de daño.', icon: 'shield', rarity: 'Común' },
  { id: 'leech', name: 'Raíz carmesí', description: 'Recuperas 3 de vida al derrotar a un enemigo.', icon: 'leaf', rarity: 'Rara' },
  { id: 'dash', name: 'Velo de niebla', description: 'La esquiva se recarga un 25 % más rápido.', icon: 'moon', rarity: 'Común' },
  { id: 'pierce', name: 'Aguja del alba', description: 'Tus proyectiles atraviesan a otro enemigo.', icon: 'sword', rarity: 'Rara' },
  { id: 'salve', name: 'Agua del santuario', description: 'Recuperas 50 de vida y ganas 5 de vida máxima.', icon: 'droplet', rarity: 'Común' },
];

function seedNumber(seed: string): number {
  let n = 2166136261;
  for (let i = 0; i < seed.length; i++) n = Math.imul(n ^ seed.charCodeAt(i), 16777619);
  return n >>> 0;
}
function randomGenerator(seed: number): () => number {
  let value = seed;
  return () => {
    value += 0x6d2b79f5;
    let n = value;
    n = Math.imul(n ^ n >>> 15, n | 1);
    n ^= n + Math.imul(n ^ n >>> 7, n | 61);
    return ((n ^ n >>> 14) >>> 0) / 4294967296;
  };
}
const distance = (a: Point, b: Point): number => Math.hypot(a.x - b.x, a.y - b.y);
const clamp = (value: number, min: number, max: number): number => Math.max(min, Math.min(max, value));
const distanceToSegment = (point: Point, start: Point, end: Point): number => {
  const dx = end.x - start.x;
  const dy = end.y - start.y;
  const lengthSquared = dx * dx + dy * dy;
  const t = lengthSquared ? clamp(((point.x - start.x) * dx + (point.y - start.y) * dy) / lengthSquared, 0, 1) : 0;
  return Math.hypot(point.x - start.x - t * dx, point.y - start.y - t * dy);
};

/** Self-contained canvas simulation. The host owns menus and persistent progression. */
export class Game {
  private canvas: HTMLCanvasElement;
  private ctx: CanvasRenderingContext2D;
  private callbacks: Callbacks;
  private status: Status = 'idle';
  private player = { x: 480, y: 344, hp: 100, maxHp: 100, damage: 22, speed: 170, attackRate: .44, invincible: 0, dash: 0, dashX: 0, dashY: 0, facing: 1 };
  private room = 1;
  private kills = 0;
  private coins = 0;
  private elapsed = 0;
  private seed = 'UMBRA';
  private rng = randomGenerator(seedNumber('UMBRA'));
  private enemies: Enemy[] = [];
  private shots: Shot[] = [];
  private particles: Particle[] = [];
  private damageTexts: DamageText[] = [];
  private obstacles: Obstacle[] = [];
  private floor: HTMLCanvasElement;
  private relics: Upgrade[] = [];
  private choices: Upgrade[] = [];
  private pendingSpawns: EnemyKind[] = [];
  private spawnTimer = 0;
  private spawnGrace = 0;
  private shotTimer = 0;
  private dashCooldown = 0;
  private dashPeriod = 1.8;
  private multishot = 1;
  private piercing = 0;
  private armor = 1;
  private leech = 0;
  private frost = false;
  private nextId = 0;
  private keys = new Set<string>();
  private pointer = { x: 480, y: 250, down: false, active: false };
  private lastDirection = { x: 0, y: -1 };
  private frame = 0;
  private lastTime = 0;
  private uiTimer = 0;
  private shake = 0;
  private soundEnabled = false;
  private audio: AudioContext | null = null;
  private reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  private disposed = false;
  private abort = new AbortController();

  constructor(canvas: HTMLCanvasElement, callbacks: Callbacks) {
    this.canvas = canvas;
    this.callbacks = callbacks;
    canvas.width = WIDTH;
    canvas.height = HEIGHT;
    const ctx = canvas.getContext('2d');
    if (!ctx) throw new Error('Este navegador no admite Canvas 2D.');
    this.ctx = ctx;
    this.ctx.imageSmoothingEnabled = false;
    this.floor = document.createElement('canvas');
    this.floor.width = WIDTH;
    this.floor.height = HEIGHT;
    this.generateRoom(true);
    this.listen();
    this.frame = requestAnimationFrame(this.tick);
  }

  start(meta: { vitality: number; power: number } = { vitality: 0, power: 0 }, seed?: string): void {
    if (this.disposed) return;
    this.seed = (seed || Math.random().toString(36).slice(2, 8).toUpperCase()).slice(0, 32);
    this.rng = randomGenerator(seedNumber(this.seed));
    const vitality = clamp(Number(meta.vitality) || 0, 0, 20);
    const power = clamp(Number(meta.power) || 0, 0, 20);
    this.player = { x: 480, y: 344, hp: 100 + vitality * 10, maxHp: 100 + vitality * 10, damage: 22 + power * 2, speed: 170, attackRate: .44, invincible: 1.5, dash: 0, dashX: 0, dashY: 0, facing: 1 };
    this.room = 1;
    this.kills = this.coins = this.elapsed = this.shotTimer = this.dashCooldown = this.shake = 0;
    this.multishot = 1;
    this.piercing = this.leech = 0;
    this.armor = 1;
    this.dashPeriod = 1.8;
    this.frost = false;
    this.relics = [];
    this.choices = [];
    this.keys.clear();
    this.pointer.down = false;
    this.pointer.active = false;
    this.status = 'playing';
    this.generateRoom();
    this.callbacks.onNotice(`Sala 1 · ${ROOM_NAMES[0]}`);
    this.notify();
    if (this.soundEnabled) void this.audio?.resume();
  }

  pause(): void {
    if (this.status !== 'playing') return;
    this.status = 'paused';
    this.keys.clear();
    this.pointer.down = false;
    this.notify();
  }

  resume(): void {
    if (this.status !== 'paused') return;
    this.status = 'playing';
    this.lastTime = performance.now();
    this.notify();
  }

  chooseUpgrade(id: string): void {
    if (this.status !== 'reward') return;
    const chosen = this.choices.find(choice => choice.id === id);
    if (!chosen) return;
    switch (id) {
      case 'ember': this.player.damage += 8; break;
      case 'heart': this.player.maxHp += 25; this.player.hp = Math.min(this.player.maxHp, this.player.hp + 25); break;
      case 'boots': this.player.speed = Math.min(300, this.player.speed * 1.16); break;
      case 'haste': this.player.attackRate = Math.max(.16, this.player.attackRate * .82); break;
      case 'echo': this.multishot = Math.min(5, this.multishot + 1); break;
      case 'frost': this.frost = true; break;
      case 'armor': this.armor *= .8; break;
      case 'leech': this.leech += 3; break;
      case 'dash': this.dashPeriod = Math.max(.65, this.dashPeriod * .75); break;
      case 'pierce': this.piercing++; break;
      case 'salve': this.player.maxHp += 5; this.player.hp = Math.min(this.player.maxHp, this.player.hp + 50); break;
    }
    this.player.hp = Math.min(this.player.maxHp, this.player.hp + 15);
    this.relics.push({ ...chosen });
    this.choices = [];
    this.room++;
    this.status = 'playing';
    this.player.invincible = 1.5;
    this.generateRoom();
    this.callbacks.onNotice(`Sala ${this.room} · ${ROOM_NAMES[this.room - 1]}`);
    this.tone(550, .16, 'sine');
    this.notify();
  }

  setSound(enabled: boolean): void {
    this.soundEnabled = enabled;
    if (!enabled) return;
    try {
      this.audio ??= new AudioContext();
      void this.audio.resume().catch(() => {});
    } catch { this.soundEnabled = false; }
  }

  setInput(action: string, pressed: boolean): void {
    if (action === 'dash') {
      if (pressed) this.dash();
      return;
    }
    if (action === 'attack') {
      this.pointer.down = pressed;
      return;
    }
    const map: Record<string, string> = { up: 'w', down: 's', left: 'a', right: 'd' };
    const key = map[action];
    if (key) pressed ? this.keys.add(key) : this.keys.delete(key);
  }

  snapshot(): GameSnapshot {
    return {
      hp: Math.ceil(this.player.hp), maxHp: this.player.maxHp, room: this.room,
      kills: this.kills, coins: this.coins, time: this.elapsed,
      dashCooldown: this.dashCooldown, status: this.status,
      damage: this.player.damage, speed: Math.round(this.player.speed),
      attackRate: this.player.attackRate, relics: this.relics.map(r => ({ ...r })),
      seed: this.seed, enemies: this.enemies.length + this.pendingSpawns.length,
    };
  }

  destroy(): void {
    this.disposed = true;
    cancelAnimationFrame(this.frame);
    this.abort.abort();
    if (this.audio) void this.audio.close().catch(() => {});
  }

  private notify(): void { this.callbacks.onUpdate(this.snapshot()); }

  private listen(): void {
    const signal = this.abort.signal;
    window.addEventListener('keydown', event => {
      if (event.ctrlKey || event.metaKey || event.altKey) return;
      if (event.target instanceof Element && event.target.closest('input,textarea,select,button,a,summary,[role="button"],dialog')) return;
      const key = event.key.toLowerCase();
      if (this.status === 'playing' && [' ', 'arrowup', 'arrowdown', 'arrowleft', 'arrowright'].includes(key)) event.preventDefault();
      if (this.status !== 'playing') return;
      this.keys.add(key);
      if (key === ' ' && !event.repeat) this.dash();
    }, { signal });
    window.addEventListener('keyup', event => this.keys.delete(event.key.toLowerCase()), { signal });
    window.addEventListener('blur', () => this.pause(), { signal });
    document.addEventListener('visibilitychange', () => { if (document.hidden) this.pause(); }, { signal });
    this.canvas.addEventListener('pointermove', event => {
      const box = this.canvas.getBoundingClientRect();
      this.pointer.x = (event.clientX - box.left) / box.width * WIDTH;
      this.pointer.y = (event.clientY - box.top) / box.height * HEIGHT;
      this.pointer.active = event.pointerType !== 'touch';
    }, { signal });
    this.canvas.addEventListener('pointerdown', event => {
      if (this.status !== 'playing') return;
      if (event.pointerType === 'touch') return;
      this.pointer.down = true;
      this.pointer.active = true;
      const box = this.canvas.getBoundingClientRect();
      this.pointer.x = (event.clientX - box.left) / box.width * WIDTH;
      this.pointer.y = (event.clientY - box.top) / box.height * HEIGHT;
    }, { signal });
    window.addEventListener('pointerup', () => { this.pointer.down = false; }, { signal });
  }

  private tick = (now: number): void => {
    if (this.disposed) return;
    const dt = Math.min(.033, Math.max(0, (now - (this.lastTime || now)) / 1000));
    this.lastTime = now;
    if (this.status === 'playing') this.update(dt);
    this.draw(this.status === 'idle' ? 0 : this.elapsed);
    this.uiTimer += dt;
    if (this.uiTimer > .12) { this.uiTimer = 0; this.notify(); }
    this.frame = requestAnimationFrame(this.tick);
  };

  private generateRoom(preview = false): void {
    this.obstacles = [];
    // A broad central corridor remains clear in every layout.
    const layouts = [
      [{ x: 234, y: 192 }, { x: 680, y: 192 }, { x: 234, y: 414 }, { x: 680, y: 414 }],
      [{ x: 260, y: 224 }, { x: 650, y: 224 }, { x: 260, y: 407 }, { x: 650, y: 407 }],
      [{ x: 208, y: 270 }, { x: 704, y: 270 }, { x: 328, y: 412 }, { x: 584, y: 160 }],
    ];
    const layout = layouts[preview ? 0 : Math.floor(this.rng() * layouts.length)];
    this.obstacles = layout.map((point, i) => ({ ...point, w: 44, h: 36, variant: i % 2 }));
    this.buildFloor(preview ? randomGenerator(420) : this.rng);
    this.enemies = [];
    this.shots = [];
    this.particles = [];
    this.damageTexts = [];
    this.pendingSpawns = [];
    this.player.x = 480;
    this.player.y = 352;
    this.spawnGrace = 1.2;
    this.spawnTimer = .2;
    this.shotTimer = .45;
    if (preview) {
      this.enemies = [
        this.createEnemy('shade', 358, 205), this.createEnemy('wisp', 732, 332),
        this.createEnemy('brute', 220, 346), this.createEnemy('shade', 628, 448),
        this.createEnemy('shade', 694, 159),
      ];
      return;
    }
    if (this.room === 6) {
      this.enemies.push(this.createEnemy('boss', 480, 192));
      this.pendingSpawns = ['shade', 'shade', 'wisp', 'shade', 'brute', 'wisp'];
      this.spawnTimer = 7;
    } else {
      const count = [0, 8, 11, 14, 17, 20][this.room];
      for (let i = 0; i < count; i++) {
        const roll = this.rng();
        this.pendingSpawns.push(this.room >= 3 && roll > .76 ? 'brute' : this.room >= 2 && roll > .53 ? 'wisp' : 'shade');
      }
      for (let i = 0; i < Math.min(4, count); i++) this.spawnEnemy(this.pendingSpawns.shift()!);
    }
  }

  private createEnemy(kind: EnemyKind, x: number, y: number): Enemy {
    const hp = kind === 'boss' ? 950 : kind === 'brute' ? 86 + this.room * 8 : kind === 'wisp' ? 38 + this.room * 5 : 32 + this.room * 6;
    return { id: this.nextId++, x, y, kind, hp, maxHp: hp,
      radius: kind === 'boss' ? 31 : kind === 'brute' ? 19 : 13,
      speed: kind === 'boss' ? 28 : kind === 'brute' ? 39 : kind === 'wisp' ? 52 : 58 + this.room * 3,
      timer: 1.3 + this.rng() * 1.4, flash: 0, slow: 0, charging: 0, chargeX: 0, chargeY: 0, phase: this.rng() * Math.PI * 2 };
  }

  private spawnEnemy(kind: EnemyKind): void {
    let point = { x: 120, y: 150 };
    for (let i = 0; i < 30; i++) {
      const edge = Math.floor(this.rng() * 4);
      point = edge === 0 ? { x: 98 + this.rng() * 760, y: 128 } : edge === 1 ? { x: 860, y: 136 + this.rng() * 350 } : edge === 2 ? { x: 98 + this.rng() * 760, y: 498 } : { x: 100, y: 136 + this.rng() * 350 };
      if (distance(point, this.player) > 185 && !this.collides(point.x, point.y, 24)) break;
    }
    const foe = this.createEnemy(kind, point.x, point.y);
    this.enemies.push(foe);
    this.burst(foe.x, foe.y, '#678a83', 12, 45);
  }

  private dash(): void {
    if (this.status !== 'playing' || this.dashCooldown > 0) return;
    const dir = this.movement();
    const length = Math.hypot(dir.x, dir.y);
    const vector = length > 0 ? { x: dir.x / length, y: dir.y / length } : this.lastDirection;
    this.player.dashX = vector.x;
    this.player.dashY = vector.y;
    this.player.dash = .19;
    this.player.invincible = Math.max(this.player.invincible, .28);
    this.dashCooldown = this.dashPeriod;
    this.burst(this.player.x, this.player.y, '#7dbcb5', 18, 90);
    this.tone(240, .1, 'triangle');
    this.notify();
  }

  private movement(): Point {
    return { x: (this.keys.has('d') || this.keys.has('arrowright') ? 1 : 0) - (this.keys.has('a') || this.keys.has('arrowleft') ? 1 : 0), y: (this.keys.has('s') || this.keys.has('arrowdown') ? 1 : 0) - (this.keys.has('w') || this.keys.has('arrowup') ? 1 : 0) };
  }

  private collides(x: number, y: number, radius: number): boolean {
    return this.obstacles.some(o => {
      const nx = clamp(x, o.x - o.w / 2, o.x + o.w / 2);
      const ny = clamp(y, o.y - o.h / 2, o.y + o.h / 2);
      return (x - nx) ** 2 + (y - ny) ** 2 < radius * radius;
    });
  }

  private move(entity: Point, dx: number, dy: number, radius: number): void {
    const x = clamp(entity.x + dx, BOUNDS.left + radius, BOUNDS.right - radius);
    if (!this.collides(x, entity.y, radius)) entity.x = x;
    const y = clamp(entity.y + dy, BOUNDS.top + radius, BOUNDS.bottom - radius);
    if (!this.collides(entity.x, y, radius)) entity.y = y;
  }

  private update(dt: number): void {
    this.elapsed += dt;
    this.spawnGrace = Math.max(0, this.spawnGrace - dt);
    this.dashCooldown = Math.max(0, this.dashCooldown - dt);
    this.player.invincible = Math.max(0, this.player.invincible - dt);
    this.shake = Math.max(0, this.shake - dt * 20);
    const movement = this.movement();
    const magnitude = Math.hypot(movement.x, movement.y);
    if (magnitude > 0) {
      movement.x /= magnitude;
      movement.y /= magnitude;
      this.lastDirection = { ...movement };
      if (movement.x) this.player.facing = movement.x > 0 ? 1 : -1;
    }
    if (this.player.dash > 0) {
      this.player.dash -= dt;
      this.move(this.player, this.player.dashX * 640 * dt, this.player.dashY * 640 * dt, 12);
      this.particles.push({ x: this.player.x, y: this.player.y, vx: 0, vy: 0, life: .18, max: .18, color: '#7dbcb5', size: 12 });
    } else this.move(this.player, movement.x * this.player.speed * dt, movement.y * this.player.speed * dt, 12);

    this.spawnTimer -= dt;
    if (this.pendingSpawns.length && this.spawnTimer <= 0 && this.enemies.length < (this.room === 6 ? 6 : 7)) {
      this.spawnEnemy(this.pendingSpawns.shift()!);
      this.spawnTimer = this.room === 6 ? 4 : 1.45;
    }
    this.shotTimer -= dt;
    if (this.shotTimer <= 0 && this.enemies.length && this.spawnGrace <= 0) this.fire();

    for (const enemy of this.enemies) {
      enemy.flash = Math.max(0, enemy.flash - dt);
      enemy.slow = Math.max(0, enemy.slow - dt);
      enemy.timer -= dt;
      const dx = this.player.x - enemy.x;
      const dy = this.player.y - enemy.y;
      const d = Math.max(1, Math.hypot(dx, dy));
      const factor = enemy.slow > 0 ? .48 : 1;
      let vx = dx / d * enemy.speed * factor;
      let vy = dy / d * enemy.speed * factor;
      if (enemy.kind === 'wisp') {
        const direction = d < 150 ? -1 : d > 245 ? 1 : 0;
        vx = dx / d * enemy.speed * direction + Math.cos(this.elapsed + enemy.phase) * 20;
        vy = dy / d * enemy.speed * direction + Math.sin(this.elapsed + enemy.phase) * 20;
        if (enemy.timer <= 0 && this.spawnGrace <= 0) {
          this.enemyShot(enemy, Math.atan2(dy, dx), 130 + this.room * 9);
          enemy.timer = 2.2 + this.rng() * .9;
        }
      } else if (enemy.kind === 'brute') {
        if (enemy.charging > 0) {
          enemy.charging -= dt;
          vx = enemy.chargeX * 300;
          vy = enemy.chargeY * 300;
        } else if (enemy.timer < .5) {
          vx = vy = 0;
          if (enemy.timer <= 0) {
            enemy.charging = .6;
            enemy.chargeX = dx / d;
            enemy.chargeY = dy / d;
            enemy.timer = 3.4;
          }
        }
      } else if (enemy.kind === 'boss') {
        if (enemy.timer <= 0 && this.spawnGrace <= 0) {
          const count = enemy.hp < enemy.maxHp / 2 ? 14 : 10;
          const offset = Math.atan2(dy, dx);
          for (let i = 0; i < count; i++) this.enemyShot(enemy, offset + Math.PI * 2 * i / count, 115);
          this.enemyShot(enemy, offset, 195);
          enemy.timer = enemy.hp < enemy.maxHp / 2 ? 1.8 : 2.6;
          this.burst(enemy.x, enemy.y, '#d77b5e', 16, 70);
        }
      }
      // Separation prevents a single unreadable stack of enemies.
      for (const other of this.enemies) {
        if (other === enemy) continue;
        const apart = distance(enemy, other);
        if (apart < enemy.radius + other.radius + 5 && apart > .1) {
          vx += (enemy.x - other.x) / apart * 32;
          vy += (enemy.y - other.y) / apart * 32;
        }
      }
      // Tangential steering lets chasing enemies navigate columns.
      if (this.collides(enemy.x + vx * .28, enemy.y + vy * .28, enemy.radius)) {
        const side = enemy.id % 2 ? 1 : -1;
        const originalX = vx;
        vx = -vy * side;
        vy = originalX * side;
      }
      if (this.spawnGrace <= 0) this.move(enemy, vx * dt, vy * dt, enemy.radius);
      if (distance(enemy, this.player) < enemy.radius + 11 && this.spawnGrace <= 0) this.hurt(enemy.kind === 'boss' ? 22 : enemy.kind === 'brute' ? 18 : 11);
    }

    for (const shot of this.shots) {
      const previous = { x: shot.x, y: shot.y };
      shot.x += shot.vx * dt;
      shot.y += shot.vy * dt;
      shot.life -= dt;
      if (shot.x < BOUNDS.left || shot.x > BOUNDS.right || shot.y < BOUNDS.top || shot.y > BOUNDS.bottom || this.collides(shot.x, shot.y, shot.radius)) shot.life = 0;
      if (shot.life <= 0) continue;
      if (shot.enemy) {
        if (distanceToSegment(this.player, previous, shot) < shot.radius + 11) {
          this.hurt(shot.damage);
          shot.life = 0;
          this.burst(shot.x, shot.y, '#d77b5e', 5, 40);
        }
      } else for (const enemy of this.enemies) {
        if (enemy.hp <= 0 || shot.hit.has(enemy.id) || distanceToSegment(enemy, previous, shot) >= shot.radius + enemy.radius) continue;
        shot.hit.add(enemy.id);
        enemy.hp -= shot.damage;
        enemy.flash = .12;
        if (this.frost) enemy.slow = 1.2;
        this.burst(shot.x, shot.y, '#9fcfc4', 7, 70);
        this.damageTexts.push({ x: enemy.x, y: enemy.y - enemy.radius - 8, text: String(Math.round(shot.damage)), life: .65, color: '#e9dcc0' });
        if (shot.pierce-- <= 0) shot.life = 0;
        if (enemy.hp <= 0) {
          this.kills++;
          this.coins += enemy.kind === 'boss' ? 25 : enemy.kind === 'brute' ? 4 : 2;
          this.player.hp = Math.min(this.player.maxHp, this.player.hp + this.leech);
          this.burst(enemy.x, enemy.y, enemy.kind === 'boss' ? '#d5b476' : '#718d80', enemy.kind === 'boss' ? 60 : 15, 100);
          this.tone(enemy.kind === 'boss' ? 130 : 95, .07, 'triangle');
        }
        if (shot.life <= 0) break;
      }
    }
    this.shots = this.shots.filter(s => s.life > 0);
    this.enemies = this.enemies.filter(e => e.hp > 0);
    for (const particle of this.particles) {
      particle.x += particle.vx * dt;
      particle.y += particle.vy * dt;
      particle.vx *= Math.pow(.08, dt);
      particle.vy *= Math.pow(.08, dt);
      particle.life -= dt;
    }
    this.particles = this.particles.filter(p => p.life > 0).slice(-450);
    for (const item of this.damageTexts) { item.y -= dt * 22; item.life -= dt; }
    this.damageTexts = this.damageTexts.filter(t => t.life > 0);
    if (this.player.hp <= 0) {
      this.status = 'dead';
      this.keys.clear();
      this.notify();
      this.callbacks.onEnd(false, this.snapshot());
    } else if (!this.enemies.length && !this.pendingSpawns.length && this.spawnGrace <= 0) this.clearRoom();
  }

  private fire(): void {
    let target: Point | undefined;
    if (this.pointer.down && this.pointer.active) target = this.pointer;
    else {
      let nearest = Infinity;
      for (const enemy of this.enemies) {
        const d = distance(enemy, this.player);
        if (enemy.hp > 0 && d < nearest) { nearest = d; target = enemy; }
      }
    }
    if (!target) return;
    const angle = Math.atan2(target.y - this.player.y, target.x - this.player.x);
    this.player.facing = Math.cos(angle) >= 0 ? 1 : -1;
    for (let i = 0; i < this.multishot; i++) {
      const a = angle + (i - (this.multishot - 1) / 2) * .14;
      this.shots.push({ x: this.player.x + Math.cos(a) * 5, y: this.player.y - 5 + Math.sin(a) * 5, vx: Math.cos(a) * 470, vy: Math.sin(a) * 470, life: 2, damage: this.player.damage, enemy: false, radius: 5, pierce: this.piercing, hit: new Set() });
    }
    this.shotTimer = this.player.attackRate;
    this.tone(500 + this.rng() * 80, .035, 'sine');
  }

  private enemyShot(enemy: Enemy, angle: number, speed: number): void {
    this.shots.push({ x: enemy.x + Math.cos(angle) * (enemy.radius + 8), y: enemy.y + Math.sin(angle) * (enemy.radius + 8), vx: Math.cos(angle) * speed, vy: Math.sin(angle) * speed, life: 6, damage: enemy.kind === 'boss' ? 14 : 10, enemy: true, radius: enemy.kind === 'boss' ? 6 : 5, pierce: 0, hit: new Set() });
  }

  private hurt(damage: number): void {
    if (this.player.invincible > 0 || this.player.hp <= 0) return;
    const actual = Math.max(1, Math.round(damage * this.armor));
    this.player.hp = Math.max(0, this.player.hp - actual);
    this.player.invincible = .8;
    this.shake = this.reducedMotion ? 0 : 4;
    this.burst(this.player.x, this.player.y, '#d77b5e', 14, 80);
    this.damageTexts.push({ x: this.player.x, y: this.player.y - 25, text: `−${actual}`, life: .7, color: '#e9a28b' });
    this.tone(70, .14, 'sawtooth');
    this.notify();
  }

  private clearRoom(): void {
    this.shots = [];
    this.keys.clear();
    this.pointer.down = false;
    if (this.room === 6) {
      this.status = 'won';
      this.coins += 40;
      this.notify();
      this.callbacks.onEnd(true, this.snapshot());
      return;
    }
    this.status = 'reward';
    this.coins += 8;
    const pool = UPGRADES.filter(u => !(u.id === 'frost' && this.frost) && !(u.id === 'echo' && this.multishot >= 5));
    for (let i = pool.length - 1; i > 0; i--) {
      const j = Math.floor(this.rng() * (i + 1));
      [pool[i], pool[j]] = [pool[j], pool[i]];
    }
    this.choices = pool.slice(0, 3).map(u => ({ ...u }));
    this.notify();
    this.callbacks.onReward(this.choices);
    this.tone(660, .25, 'sine');
  }

  private burst(x: number, y: number, color: string, count: number, speed: number): void {
    if (this.reducedMotion) count = Math.ceil(count / 3);
    for (let i = 0; i < count; i++) {
      const angle = this.rng() * Math.PI * 2;
      const velocity = speed * (.3 + this.rng());
      const life = .2 + this.rng() * .45;
      this.particles.push({ x, y, vx: Math.cos(angle) * velocity, vy: Math.sin(angle) * velocity, life, max: life, color, size: 2 + Math.floor(this.rng() * 3) });
    }
  }

  private tone(frequency: number, duration: number, type: OscillatorType): void {
    if (!this.soundEnabled || !this.audio || this.audio.state !== 'running') return;
    try {
      const osc = this.audio.createOscillator();
      const gain = this.audio.createGain();
      osc.type = type;
      osc.frequency.setValueAtTime(frequency, this.audio.currentTime);
      osc.frequency.exponentialRampToValueAtTime(Math.max(25, frequency * .5), this.audio.currentTime + duration);
      gain.gain.setValueAtTime(.025, this.audio.currentTime);
      gain.gain.exponentialRampToValueAtTime(.0001, this.audio.currentTime + duration);
      osc.connect(gain);
      gain.connect(this.audio.destination);
      osc.start();
      osc.stop(this.audio.currentTime + duration);
    } catch { /* Audio can be interrupted when the browser backgrounds a tab. */ }
  }

  private buildFloor(rng: () => number): void {
    const ctx = this.floor.getContext('2d')!;
    ctx.fillStyle = '#10191c';
    ctx.fillRect(0, 0, WIDTH, HEIGHT);
    ctx.fillStyle = '#182320';
    ctx.fillRect(43, 62, 874, 498);
    ctx.fillStyle = '#2b3831';
    ctx.fillRect(56, 85, 848, 458);
    ctx.fillStyle = '#0d1618';
    ctx.fillRect(65, 95, 830, 440);
    const colors = ['#25302c', '#27322e', '#29342e', '#24302d', '#28332f', '#2a352f'];
    for (let row = 0; row < 18; row++) {
      for (let col = 0; col < 28; col++) {
        const x = 70 + col * 30;
        const y = 102 + row * 24;
        ctx.fillStyle = colors[Math.floor(rng() * colors.length)];
        ctx.fillRect(x + 1, y + 1, 28, 22);
        ctx.fillStyle = rng() > .5 ? '#344036' : '#202b28';
        ctx.fillRect(x + 2, y + 1, 26, 1);
        if (rng() > .58) {
          ctx.fillStyle = '#3a4436';
          ctx.globalAlpha = .4;
          ctx.fillRect(x + 4 + Math.floor(rng() * 17), y + 5 + Math.floor(rng() * 12), 3, 2);
          ctx.globalAlpha = 1;
        }
        if (rng() > .88) {
          ctx.strokeStyle = '#172522';
          ctx.lineWidth = 1;
          ctx.beginPath();
          ctx.moveTo(x + 12, y + 2);
          ctx.lineTo(x + 9, y + 8);
          ctx.lineTo(x + 15, y + 14);
          ctx.lineTo(x + 12, y + 23);
          ctx.stroke();
        }
      }
    }
    // A worn processional aisle connects the hero to the sealed northern door.
    ctx.fillStyle = '#354035';
    ctx.globalAlpha = .32;
    ctx.fillRect(436, 103, 88, 421);
    ctx.globalAlpha = 1;
    ctx.strokeStyle = '#6a7050';
    ctx.lineWidth = 1;
    ctx.globalAlpha = .22;
    ctx.strokeRect(101.5, 132.5, 757, 363);
    ctx.strokeRect(106.5, 137.5, 747, 353);
    ctx.beginPath();
    ctx.arc(480, 326, 105, 0, Math.PI * 2);
    ctx.stroke();
    ctx.beginPath();
    ctx.arc(480, 326, 93, 0, Math.PI * 2);
    ctx.stroke();
    for (let i = 0; i < 8; i++) {
      const a = i * Math.PI / 4;
      ctx.beginPath();
      ctx.moveTo(480 + Math.cos(a) * 85, 326 + Math.sin(a) * 85);
      ctx.lineTo(480 + Math.cos(a) * 114, 326 + Math.sin(a) * 114);
      ctx.stroke();
    }
    ctx.globalAlpha = 1;
    // The raised masonry has a lit upper edge and a heavy lower shadow.
    for (let x = 44; x < 918; x += 46) {
      this.floorStone(ctx, x, 58, 44, 30);
      this.floorStone(ctx, x, 535, 44, 29);
    }
    for (let y = 88; y < 537; y += 36) {
      this.floorStone(ctx, 40, y, 28, 34);
      this.floorStone(ctx, 893, y, 28, 34);
    }
    ctx.fillStyle = '#0b1213';
    ctx.fillRect(39, 564, 883, 11);
    ctx.fillStyle = '#101a18';
    ctx.fillRect(69, 526, 824, 9);
    // Shattered steps and a recessed doorway.
    ctx.fillStyle = '#111c1c';
    ctx.fillRect(423, 24, 114, 75);
    ctx.fillStyle = '#3e4a3c';
    ctx.fillRect(414, 40, 10, 60);
    ctx.fillRect(536, 40, 10, 60);
    ctx.fillRect(425, 28, 110, 10);
    ctx.fillRect(435, 20, 90, 10);
    ctx.fillStyle = '#5b6249';
    ctx.fillRect(414, 40, 3, 60);
    ctx.fillRect(536, 40, 3, 60);
    ctx.fillRect(435, 20, 90, 3);
    ctx.fillStyle = '#0b1516';
    ctx.fillRect(432, 42, 96, 56);
    for (let i = 0; i < 4; i++) {
      ctx.fillStyle = i % 2 ? '#36443a' : '#3d493d';
      ctx.fillRect(427 - i * 5, 88 + i * 8, 106 + i * 10, 7);
      ctx.fillStyle = '#1b2825';
      ctx.fillRect(427 - i * 5, 94 + i * 8, 106 + i * 10, 2);
    }
    ctx.fillStyle = '#7b8760';
    ctx.globalAlpha = .5;
    for (let i = 0; i < 10; i++) ctx.fillRect(445 + i * 7, 46, 2, 39);
    ctx.globalAlpha = 1;
    // Broken rocks and moss stay cosmetic and never block movement.
    for (let i = 0; i < 54; i++) {
      const x = 86 + rng() * 787;
      const y = 125 + rng() * 386;
      if (Math.abs(x - 480) < 90 && Math.abs(y - 345) < 90) continue;
      const color = rng() > .45 ? '#4a5140' : '#1d2927';
      ctx.fillStyle = '#19231f';
      ctx.fillRect(x + 1, y + 4, 10, 4);
      ctx.fillStyle = color;
      ctx.fillRect(x, y, 5 + rng() * 6, 4);
      ctx.fillRect(x + 3, y - 2, 4, 3);
    }
    for (const [x, y] of [[86, 126], [874, 126], [83, 491], [874, 487], [151, 105], [782, 523]]) {
      this.vine(ctx, x, y, rng);
    }
    // Quiet corner ornaments give the crypt a specific architectural language.
    ctx.strokeStyle = '#7d7751';
    ctx.globalAlpha = .36;
    for (const [x, y, sx, sy] of [[121, 152, 1, 1], [839, 152, -1, 1], [121, 476, 1, -1], [839, 476, -1, -1]]) {
      ctx.beginPath();
      ctx.moveTo(x, y + sy * 23);
      ctx.lineTo(x, y);
      ctx.lineTo(x + sx * 27, y);
      ctx.moveTo(x + sx * 6, y + sy * 17);
      ctx.lineTo(x + sx * 6, y + sy * 6);
      ctx.lineTo(x + sx * 19, y + sy * 6);
      ctx.stroke();
    }
    ctx.globalAlpha = 1;
  }

  private floorStone(ctx: CanvasRenderingContext2D, x: number, y: number, w: number, h: number): void {
    ctx.fillStyle = '#111d1b';
    ctx.fillRect(x, y, w, h);
    ctx.fillStyle = '#344038';
    ctx.fillRect(x + 2, y + 2, w - 3, h - 5);
    ctx.fillStyle = '#48513f';
    ctx.fillRect(x + 2, y + 2, w - 3, 2);
    ctx.fillStyle = '#28352e';
    ctx.fillRect(x + 2, y + 6, w - 3, h - 10);
    ctx.fillStyle = '#1d2a25';
    ctx.fillRect(x + w - 4, y + 5, 2, h - 7);
  }

  private vine(ctx: CanvasRenderingContext2D, x: number, y: number, rng: () => number): void {
    ctx.fillStyle = '#3b5039';
    for (let i = 0; i < 24; i++) {
      const px = x + (rng() - .5) * 40;
      const py = y + (rng() - .5) * 35;
      ctx.globalAlpha = .5 + rng() * .5;
      ctx.fillRect(px, py, 3 + rng() * 5, 3);
      ctx.fillRect(px + 2, py - 3, 3, 3);
    }
    ctx.globalAlpha = 1;
  }

  private draw(time: number): void {
    const ctx = this.ctx;
    ctx.save();
    ctx.clearRect(0, 0, WIDTH, HEIGHT);
    if (this.shake > 0) ctx.translate(Math.sin(time * 83) * this.shake, Math.cos(time * 91) * this.shake);
    ctx.drawImage(this.floor, 0, 0);
    this.drawLight(480, 76, 105, '#609389', .16);
    // Door sigil, extinguished until the crypt is freed.
    ctx.save();
    ctx.translate(480, 67);
    ctx.strokeStyle = this.status === 'won' ? '#d5b476' : '#7dbcb5';
    ctx.globalAlpha = .65;
    ctx.lineWidth = 1.5;
    ctx.beginPath();
    ctx.moveTo(0, -13);
    ctx.lineTo(9, 0);
    ctx.lineTo(0, 13);
    ctx.lineTo(-9, 0);
    ctx.closePath();
    ctx.stroke();
    ctx.beginPath();
    ctx.moveTo(0, -18);
    ctx.lineTo(0, 18);
    ctx.stroke();
    ctx.restore();
    for (const [x, y] of [[156, 77], [354, 77], [606, 77], [804, 77], [53, 291], [907, 291], [161, 548], [799, 548]]) this.drawTorch(x, y, time);
    for (const enemy of this.enemies) {
      if (enemy.kind === 'wisp') this.drawLight(enemy.x, enemy.y - 10, 65, '#7e9f96', .13);
      if (enemy.kind === 'boss') this.drawLight(enemy.x, enemy.y - 15, 120, '#c27451', .12);
      if (enemy.kind === 'brute' && enemy.timer < .5 && enemy.charging <= 0) {
        ctx.save();
        ctx.globalAlpha = .5;
        ctx.strokeStyle = '#d77b5e';
        ctx.setLineDash([6, 8]);
        ctx.lineWidth = 12;
        ctx.beginPath();
        ctx.moveTo(enemy.x, enemy.y);
        const a = Math.atan2(this.player.y - enemy.y, this.player.x - enemy.x);
        ctx.lineTo(enemy.x + Math.cos(a) * 200, enemy.y + Math.sin(a) * 200);
        ctx.stroke();
        ctx.restore();
      }
    }
    this.drawLight(this.player.x, this.player.y - 12, 118, '#7dbcb5', .12);
    const items: Array<{ y: number; draw: () => void }> = this.obstacles.map(o => ({ y: o.y + o.h / 2, draw: () => this.drawColumn(o) }));
    for (const enemy of this.enemies) items.push({ y: enemy.y, draw: () => this.drawEnemy(enemy, time) });
    items.push({ y: this.player.y, draw: () => this.drawPlayer(time) });
    items.sort((a, b) => a.y - b.y);
    for (const item of items) item.draw();
    for (const shot of this.shots) {
      const angle = Math.atan2(shot.vy, shot.vx);
      ctx.save();
      ctx.translate(shot.x, shot.y);
      ctx.rotate(angle);
      ctx.fillStyle = shot.enemy ? '#d77b5e' : '#7dbcb5';
      ctx.globalAlpha = .22;
      ctx.fillRect(-15, -shot.radius - 2, 22, shot.radius * 2 + 4);
      ctx.globalAlpha = 1;
      ctx.fillStyle = shot.enemy ? '#f0b494' : '#d5eee1';
      ctx.fillRect(-5, -shot.radius / 2, 10, shot.radius);
      ctx.fillStyle = shot.enemy ? '#d77b5e' : '#87cbbd';
      ctx.fillRect(-10, -shot.radius / 2 + 1, 5, shot.radius - 2);
      ctx.restore();
    }
    for (const p of this.particles) {
      ctx.globalAlpha = clamp(p.life / p.max, 0, 1);
      ctx.fillStyle = p.color;
      ctx.fillRect(Math.round(p.x), Math.round(p.y), p.size, p.size);
    }
    ctx.globalAlpha = 1;
    for (const item of this.damageTexts) {
      ctx.globalAlpha = Math.min(1, item.life * 3);
      ctx.fillStyle = '#13201c';
      ctx.font = 'bold 13px monospace';
      ctx.textAlign = 'center';
      ctx.fillText(item.text, item.x + 1, item.y + 1);
      ctx.fillStyle = item.color;
      ctx.fillText(item.text, item.x, item.y);
    }
    ctx.globalAlpha = 1;
    // Sparse dust has no effect on gameplay.
    ctx.fillStyle = '#b2b487';
    for (let i = 0; i < 20; i++) {
      const x = 96 + (i * 127.43 + time * (i % 2 ? 2 : -2) + 780) % 780;
      const y = 118 + (i * 59.17 + Math.sin(time * .45 + i) * 10) % 385;
      ctx.globalAlpha = .08 + (Math.sin(time + i * 3) + 1) * .08;
      ctx.fillRect(x, y, 2, 2);
    }
    ctx.globalAlpha = 1;
    const vignette = ctx.createRadialGradient(480, 308, 175, 480, 308, 495);
    vignette.addColorStop(0, 'rgba(8,16,17,0)');
    vignette.addColorStop(.75, 'rgba(8,16,17,.15)');
    vignette.addColorStop(1, 'rgba(8,16,17,.72)');
    ctx.fillStyle = vignette;
    ctx.fillRect(0, 0, WIDTH, HEIGHT);
    if (this.status === 'playing' && this.room === 6) this.drawBossBar();
    if (this.pointer.down && this.pointer.active && this.status === 'playing') {
      ctx.strokeStyle = '#d5b476';
      ctx.lineWidth = 1;
      ctx.beginPath();
      ctx.arc(this.pointer.x, this.pointer.y, 9, 0, Math.PI * 2);
      ctx.moveTo(this.pointer.x - 14, this.pointer.y);
      ctx.lineTo(this.pointer.x - 5, this.pointer.y);
      ctx.moveTo(this.pointer.x + 5, this.pointer.y);
      ctx.lineTo(this.pointer.x + 14, this.pointer.y);
      ctx.stroke();
    }
    if (this.status === 'dead') {
      ctx.fillStyle = 'rgba(15,21,23,.55)';
      ctx.fillRect(0, 0, WIDTH, HEIGHT);
    }
    ctx.restore();
  }

  private drawLight(x: number, y: number, radius: number, color: string, alpha: number): void {
    const gradient = this.ctx.createRadialGradient(x, y, 0, x, y, radius);
    gradient.addColorStop(0, color);
    gradient.addColorStop(1, 'rgba(0,0,0,0)');
    this.ctx.save();
    this.ctx.globalCompositeOperation = 'screen';
    this.ctx.globalAlpha = alpha;
    this.ctx.fillStyle = gradient;
    this.ctx.fillRect(x - radius, y - radius, radius * 2, radius * 2);
    this.ctx.restore();
  }

  private drawTorch(x: number, y: number, time: number): void {
    const ctx = this.ctx;
    const flicker = this.reducedMotion ? 0 : Math.sin(time * 8 + x) * 2;
    this.drawLight(x, y - 6, 82 + flicker, '#d3a055', .22);
    ctx.fillStyle = '#0c1615';
    ctx.fillRect(x - 5, y - 2, 12, 19);
    ctx.fillStyle = '#65553c';
    ctx.fillRect(x - 3, y - 2, 6, 13);
    ctx.fillStyle = '#9c8050';
    ctx.fillRect(x - 6, y - 3, 12, 3);
    ctx.fillStyle = '#aa673e';
    ctx.fillRect(x - 5, y - 11, 10, 9);
    ctx.fillRect(x - 2, y - 17 - flicker, 4, 14 + flicker);
    ctx.fillStyle = '#dfb774';
    ctx.fillRect(x - 3, y - 12, 6, 9);
    ctx.fillStyle = '#f1dbaa';
    ctx.fillRect(x - 1, y - 10, 3, 7);
  }

  private drawColumn(o: Obstacle): void {
    const ctx = this.ctx;
    const x = Math.round(o.x);
    const y = Math.round(o.y);
    ctx.fillStyle = 'rgba(8,17,15,.52)';
    ctx.beginPath();
    ctx.ellipse(x + 7, y + 16, 37, 18, -.15, 0, Math.PI * 2);
    ctx.fill();
    ctx.fillStyle = '#1a2722';
    ctx.fillRect(x - 28, y - 3, 56, 24);
    ctx.fillStyle = '#4b5441';
    ctx.fillRect(x - 27, y - 8, 54, 20);
    ctx.fillStyle = '#60664b';
    ctx.fillRect(x - 26, y - 8, 52, 3);
    ctx.fillStyle = '#313e31';
    ctx.fillRect(x - 26, y + 7, 52, 5);
    const height = o.variant === 0 ? 64 : 40;
    ctx.fillStyle = '#354434';
    ctx.fillRect(x - 18, y - height, 36, height + 2);
    ctx.fillStyle = '#465440';
    ctx.fillRect(x - 17, y - height, 10, height);
    ctx.fillStyle = '#536047';
    ctx.fillRect(x - 15, y - height + 3, 3, height - 6);
    ctx.fillStyle = '#2d3b2d';
    ctx.fillRect(x + 7, y - height, 9, height);
    ctx.fillStyle = '#202e25';
    ctx.fillRect(x - 5, y - height + 6, 2, height - 10);
    ctx.fillRect(x + 3, y - height + 6, 2, height - 10);
    ctx.fillStyle = '#58634a';
    ctx.fillRect(x - 24, y - height - 8, 48, 11);
    ctx.fillStyle = '#768064';
    ctx.fillRect(x - 24, y - height - 8, 48, 3);
    ctx.fillStyle = '#2c3c2e';
    ctx.fillRect(x - 26, y - height - 1, 52, 5);
    ctx.fillStyle = '#6a7652';
    for (let i = 0; i < 6; i++) {
      ctx.fillRect(x - 23 + i * 5, y - height - 11 + i % 2 * 2, 5, 4);
      if (i < 4) ctx.fillRect(x - 20 + i * 2, y - height + i * 6, 4, 5);
    }
    if (o.variant === 1) {
      ctx.fillStyle = '#29382b';
      ctx.fillRect(x + 6, y - height - 9, 18, 6);
      ctx.fillRect(x + 13, y - height - 13, 9, 4);
      ctx.fillStyle = '#7b8160';
      ctx.fillRect(x - 18, y - height - 9, 15, 3);
    }
  }

  private sprite(map: string[], colors: Record<string, string>, x: number, y: number, size: number, flash = false): void {
    const ctx = this.ctx;
    for (let row = 0; row < map.length; row++) {
      for (let col = 0; col < map[row].length; col++) {
        const color = colors[map[row][col]];
        if (!color) continue;
        ctx.fillStyle = flash ? '#e9dcc0' : color;
        ctx.fillRect(Math.round(x + col * size), Math.round(y + row * size), size, size);
      }
    }
  }

  private drawPlayer(time: number): void {
    const ctx = this.ctx;
    const x = Math.round(this.player.x);
    const y = Math.round(this.player.y);
    const moving = this.movement();
    const bob = this.reducedMotion || this.status !== 'playing' || !(moving.x || moving.y) ? 0 : Math.round(Math.sin(time * 15) * 2);
    ctx.fillStyle = 'rgba(5,14,14,.55)';
    ctx.beginPath();
    ctx.ellipse(x, y + 6, 21, 8, 0, 0, Math.PI * 2);
    ctx.fill();
    if (this.player.invincible > 0 && this.status === 'playing') {
      ctx.strokeStyle = '#9ac6b5';
      ctx.globalAlpha = .28 + Math.sin(time * 20) * .15;
      ctx.lineWidth = 2;
      ctx.beginPath();
      ctx.ellipse(x, y - 16, 24, 31, 0, 0, Math.PI * 2);
      ctx.stroke();
      ctx.globalAlpha = 1;
    }
    ctx.save();
    ctx.translate(x, y + bob);
    ctx.scale(this.player.facing, 1);
    this.sprite([
      '....TTTT....',
      '...TLLLLT...',
      '..TLMMMMMT..',
      '..TMMDDDMT..',
      '..TMDEEDMT..',
      '...TDDDMT...',
      '...TMMMT....',
      '..TLLLLMT...',
      '.TLLCLLMMT..',
      '.TLCLCLMMT..',
      '.TLCLCLMMT..',
      'TLLCLCLMMMT.',
      'TLLCLCLLMMT.',
      'TLLLLLLMMMT.',
      '..BB..BB....',
      '..BB..BB....',
    ], { T: '#163b38', L: '#679e8d', M: '#315c53', D: '#152525', E: '#e8ddaa', C: '#8ab7a0', B: '#293a32' }, -18, -45, 3);
    // A short brass wand carries the last light of the lighthouse.
    ctx.fillStyle = '#6e5b3e';
    ctx.fillRect(17, -26, 4, 24);
    ctx.fillStyle = '#c3a36a';
    ctx.fillRect(14, -29, 10, 4);
    ctx.fillRect(16, -38, 6, 9);
    ctx.fillStyle = '#a0d2bb';
    ctx.fillRect(17, -42, 4, 10);
    ctx.fillStyle = '#e4ecd5';
    ctx.fillRect(18, -40, 3, 6);
    ctx.restore();
    this.drawLight(x + this.player.facing * 19, y - 36 + bob, 31, '#a4dfbc', .3);
  }

  private drawEnemy(enemy: Enemy, time: number): void {
    const ctx = this.ctx;
    const x = Math.round(enemy.x);
    const y = Math.round(enemy.y);
    const bob = this.reducedMotion ? 0 : Math.round(Math.sin(time * 5 + enemy.phase) * (enemy.kind === 'wisp' ? 4 : 1));
    ctx.fillStyle = 'rgba(6,13,14,.6)';
    ctx.beginPath();
    ctx.ellipse(x, y + 5, enemy.radius * 1.25, enemy.radius * .45, 0, 0, Math.PI * 2);
    ctx.fill();
    if (enemy.kind === 'shade') {
      this.sprite([
        '....KKKK....',
        '...KMMMMK...',
        '..KMMMMMMK..',
        '..KMDDDDMK..',
        '..KDEDD EDK.',
        '..KDDDDDDK..',
        '.KMMMMMMMMK.',
        'KMMMSMMMMMMK',
        'KMMSMMMMMMMK',
        '.KMSSMMMMMK.',
        '..KMMKMMMK..',
        '..KK..KKK...',
      ].map(row => row.replace(' ', 'D')), { K: '#192027', M: '#444747', D: '#1b2428', E: '#d59776', S: '#606353' }, x - 18, y - 30 + bob, 3, enemy.flash > 0);
    } else if (enemy.kind === 'wisp') {
      this.sprite([
        '.....LL.....',
        '....LCCL....',
        '...LCCMML...',
        '..LCCMMMML..',
        '.LCCDDDDMML.',
        '.LCDWDDWDML.',
        '.LCDDDDDDML.',
        '..LMMMMMML..',
        '...LMMMML...',
        '..LMM..MML..',
        '.LM......ML.',
        '.L........L.',
      ], { L: '#3c675f', C: '#77978a', M: '#4c796c', D: '#24443e', W: '#e4c689' }, x - 18, y - 37 + bob, 3, enemy.flash > 0);
    } else if (enemy.kind === 'brute') {
      this.sprite([
        '....AAAAAA....',
        '...AHHHHHHA...',
        '...AHHHHHHA...',
        '...ADDDDDDA...',
        '...ADERREDA...',
        '...ADDDDDDA...',
        '..AAAABBAAAA..',
        '.AHHHABBAHHHA.',
        '.AHHHABBAHHHA.',
        '.AHHHAAAAHHHA.',
        '.AAHHHAAHHHAA.',
        '..AAHHAAHHAA..',
        '...BBBBBBBB...',
        '...BBB..BBB...',
        '...BBB..BBB...',
      ], { A: '#313c34', H: '#65705a', D: '#202a26', E: '#d58c64', R: '#bd7152', B: '#2e352c' }, x - 21, y - 40 + bob, 3, enemy.flash > 0);
      ctx.fillStyle = enemy.timer < .5 ? '#d77b5e' : '#8a8a69';
      ctx.fillRect(x + 23, y - 26, 7, 27);
      ctx.fillRect(x + 18, y - 29, 17, 12);
      ctx.fillStyle = '#3a4534';
      ctx.fillRect(x + 26, y - 15, 3, 21);
    } else {
      this.sprite([
        '....G...........G....',
        '....GG.........GG....',
        '.....GGBBBBBBBGG.....',
        '....BGGGGGGGGGGGB....',
        '...BBHHHHHHHHHHHBB...',
        '...BHHHHHHHHHHHHHB...',
        '...BHDDDDDDDDDDDHB...',
        '...BHDREEEDREEEDHB...',
        '...BHDDDDDDDDDDDHB...',
        '....BHHDDDDDDDHHB....',
        '...BHHHHHHHHHHHHHB...',
        '..BBHGGGHHHHHGGGHBB..',
        '.BBHHHGGHHHHHGGHHHBB.',
        'BBHHHHGGHHHHHGGHHHHBB',
        'BBHHHHGGHHHHHGGHHHHBB',
        'BBHHHHHHHGGGHHHHHHHBB',
        '.BBHHHHHHGGGHHHHHHBB.',
        '..BBHHHHHGGGHHHHHBB..',
        '...BBHHHHGGGHHHHBB...',
        '...BBHHHHHHHHHHHBB...',
        '....BBHHHHHHHHHBB....',
        '....BBHHHBBBHHHBB....',
        '....BBBBBB.BBBBBB....',
      ], { G: '#a79659', B: '#202b2b', H: '#49554b', D: '#172421', R: '#d3865c', E: '#f0c891' }, x - 32, y - 61 + bob, 3, enemy.flash > 0);
      ctx.fillStyle = '#433c2c';
      ctx.fillRect(x + 39, y - 52, 6, 55);
      ctx.fillStyle = '#b49b60';
      ctx.fillRect(x + 34, y - 58, 16, 5);
      ctx.fillRect(x + 38, y - 78, 8, 20);
      ctx.fillStyle = '#d4c18a';
      ctx.fillRect(x + 40, y - 75, 3, 15);
    }
    if (enemy.slow > 0) {
      ctx.strokeStyle = '#91c9c5';
      ctx.lineWidth = 1;
      ctx.beginPath();
      ctx.ellipse(x, y + 3, enemy.radius + 3, 7, 0, 0, Math.PI * 2);
      ctx.stroke();
    }
    if (enemy.hp < enemy.maxHp && enemy.kind !== 'boss') {
      const width = enemy.kind === 'brute' ? 36 : 27;
      ctx.fillStyle = '#13201b';
      ctx.fillRect(x - width / 2 - 1, y - (enemy.kind === 'brute' ? 48 : 42), width + 2, 5);
      ctx.fillStyle = '#c09569';
      ctx.fillRect(x - width / 2, y - (enemy.kind === 'brute' ? 47 : 41), width * Math.max(0, enemy.hp / enemy.maxHp), 3);
    }
  }

  private drawBossBar(): void {
    const boss = this.enemies.find(e => e.kind === 'boss');
    if (!boss) return;
    const ctx = this.ctx;
    ctx.fillStyle = 'rgba(13,22,22,.9)';
    ctx.fillRect(324, 13, 312, 36);
    ctx.textAlign = 'center';
    ctx.font = '11px monospace';
    ctx.fillStyle = '#d5b476';
    ctx.fillText('EL DEVORADOR', 480, 28);
    ctx.fillStyle = '#3f3c30';
    ctx.fillRect(340, 36, 280, 4);
    ctx.fillStyle = '#d77b5e';
    ctx.fillRect(340, 36, 280 * Math.max(0, boss.hp / boss.maxHp), 4);
  }
}
