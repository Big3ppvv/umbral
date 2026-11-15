using System;
using System.Collections.Generic;

namespace Umbra.Core
{
    /// <summary>Deterministic, renderer-independent UMBRA combat simulation (logical 960 x 600 world).</summary>
    public sealed class Simulation
    {
        private static readonly string[] RoomNames = { "El umbral", "Los ecos", "El osario", "La vigilia", "El abismo", "El devorador" };
        private static readonly Relic[] Catalog =
        {
            new Relic("ember", "Brasa antigua", "Tus proyectiles infligen 8 de daño adicional.", "flame", "Común"),
            new Relic("heart", "Corazón de ámbar", "+25 de vida máxima. Recuperas 25 de vida.", "heart", "Común"),
            new Relic("boots", "Paso del espectro", "Te mueves un 16 % más rápido.", "wind", "Común"),
            new Relic("haste", "Reloj sin tiempo", "Disparas un 18 % más rápido.", "clock", "Rara"),
            new Relic("echo", "Eco del faro", "Disparas un proyectil adicional en cada ataque.", "sparkles", "Épica"),
            new Relic("frost", "Lágrima lunar", "Tus impactos ralentizan a los enemigos.", "snowflake", "Rara"),
            new Relic("armor", "Pacto de piedra", "Recibes un 20 % menos de daño.", "shield", "Común"),
            new Relic("leech", "Raíz carmesí", "Recuperas 3 de vida al derrotar a un enemigo.", "leaf", "Rara"),
            new Relic("dash", "Velo de niebla", "La esquiva se recarga un 25 % más rápido.", "moon", "Común"),
            new Relic("pierce", "Aguja del alba", "Tus proyectiles atraviesan a otro enemigo.", "sword", "Rara"),
            new Relic("salve", "Agua del santuario", "Recuperas 50 de vida y ganas 5 de vida máxima.", "droplet", "Común")
        };

        private readonly List<EnemyState> enemies = new List<EnemyState>();
        private readonly List<ProjectileState> projectiles = new List<ProjectileState>();
        private readonly List<Obstacle> obstacles = new List<Obstacle>();
        private readonly List<Relic> relics = new List<Relic>();
        private readonly List<Relic> choices = new List<Relic>();
        private readonly List<EnemyKind> pending = new List<EnemyKind>();
        private RandomSource random;
        private int nextId, multishot = 1, piercing;
        private float spawnTimer, spawnGrace, shotTimer, dashPeriod = 1.8f, dashX, dashY;
        private float lastMoveX, lastMoveY = -1, armor = 1, leech;
        private bool frost;

        public RunState State { get; private set; }
        public PlayerState Player { get; private set; }
        public int Room { get; private set; }
        public int Kills { get; private set; }
        public int Shards { get; private set; }
        public float Elapsed { get; private set; }
        public float DashCooldown { get; private set; }
        public string Seed { get; private set; }
        public string RoomName { get { return RoomNames[Room - 1]; } }
        public IList<EnemyState> Enemies { get; private set; }
        public IList<ProjectileState> Projectiles { get; private set; }
        public IList<Obstacle> Obstacles { get; private set; }
        public IList<Relic> Relics { get; private set; }
        public IList<Relic> Choices { get; private set; }
        public int RemainingEnemies { get { return enemies.Count + pending.Count; } }

        public event Action Changed;
        public event Action<IReadOnlyList<Relic>> RewardOffered;
        public event Action<bool> Ended;
        public event Action<string> Notice;

        public Simulation()
        {
            Enemies = enemies.AsReadOnly();
            Projectiles = projectiles.AsReadOnly();
            Obstacles = obstacles.AsReadOnly();
            Relics = relics.AsReadOnly();
            Choices = choices.AsReadOnly();
            Player = new PlayerState();
            Room = 1;
            Seed = "UMBRA";
            random = new RandomSource(Seed);
            State = RunState.Idle;
            GenerateRoom(true);
        }

        public void StartRun(int vitality = 0, int power = 0, string seed = null)
        {
            Seed = string.IsNullOrEmpty(seed) ? Guid.NewGuid().ToString("N").Substring(0, 6).ToUpperInvariant() : seed.Substring(0, Math.Min(seed.Length, 32));
            random = new RandomSource(Seed);
            vitality = Math.Max(0, Math.Min(5, vitality));
            power = Math.Max(0, Math.Min(5, power));
            Player = new PlayerState { Hp = 100 + vitality * 10, MaxHp = 100 + vitality * 10, Damage = 22 + power * 2, Invincible = 1.5f };
            Room = 1;
            Kills = Shards = nextId = piercing = 0;
            Elapsed = DashCooldown = leech = 0;
            lastMoveX = dashX = dashY = 0;
            lastMoveY = -1;
            multishot = 1;
            armor = 1;
            dashPeriod = 1.8f;
            frost = false;
            relics.Clear();
            choices.Clear();
            State = RunState.Playing;
            GenerateRoom(false);
            NotifyNotice("Sala 1 · " + RoomNames[0]);
            NotifyChanged();
        }

        public void Pause()
        {
            if (State != RunState.Playing) return;
            State = RunState.Paused;
            NotifyChanged();
        }

        public void Resume()
        {
            if (State != RunState.Paused) return;
            State = RunState.Playing;
            NotifyChanged();
        }

        public bool ChooseRelic(string id)
        {
            if (State != RunState.Reward) return false;
            Relic selected = choices.Find(value => value.Id == id);
            if (selected == null) return false;
            switch (id)
            {
                case "ember": Player.Damage += 8; break;
                case "heart": Player.MaxHp += 25; Player.Hp = Math.Min(Player.MaxHp, Player.Hp + 25); break;
                case "boots": Player.Speed = Math.Min(300, Player.Speed * 1.16f); break;
                case "haste": Player.AttackInterval = Math.Max(.16f, Player.AttackInterval * .82f); break;
                case "echo": multishot = Math.Min(5, multishot + 1); break;
                case "frost": frost = true; break;
                case "armor": armor *= .8f; break;
                case "leech": leech += 3; break;
                case "dash": dashPeriod = Math.Max(.65f, dashPeriod * .75f); break;
                case "pierce": piercing++; break;
                case "salve": Player.MaxHp += 5; Player.Hp = Math.Min(Player.MaxHp, Player.Hp + 50); break;
                default: return false;
            }
            Player.Hp = Math.Min(Player.MaxHp, Player.Hp + 15);
            relics.Add(Copy(selected));
            choices.Clear();
            Room++;
            State = RunState.Playing;
            Player.Invincible = 1.5f;
            GenerateRoom(false);
            NotifyNotice("Sala " + Room + " · " + RoomName);
            NotifyChanged();
            return true;
        }

        public void Step(float dt, float moveX, float moveY, bool dashPressed, float aimX = 0, float aimY = 0, bool manualAim = false)
        {
            if (State != RunState.Playing || float.IsNaN(dt) || float.IsInfinity(dt) || dt <= 0) return;
            dt = Math.Min(.033f, dt);
            if (float.IsNaN(moveX) || float.IsInfinity(moveX)) moveX = 0;
            if (float.IsNaN(moveY) || float.IsInfinity(moveY)) moveY = 0;
            moveX = Clamp(moveX, -1, 1);
            moveY = Clamp(moveY, -1, 1);
            float length = Length(moveX, moveY);
            if (length > 1) { moveX /= length; moveY /= length; }
            if (length > 0) { lastMoveX = moveX / Math.Max(.0001f, Length(moveX, moveY)); lastMoveY = moveY / Math.Max(.0001f, Length(moveX, moveY)); }
            Elapsed += dt;
            spawnGrace = Math.Max(0, spawnGrace - dt);
            DashCooldown = Math.Max(0, DashCooldown - dt);
            Player.Invincible = Math.Max(0, Player.Invincible - dt);
            if (dashPressed && DashCooldown <= 0)
            {
                dashX = lastMoveX; dashY = lastMoveY;
                Player.DashRemaining = .19f;
                Player.Invincible = Math.Max(Player.Invincible, .28f);
                DashCooldown = dashPeriod;
            }
            float px = Player.X, py = Player.Y;
            if (Player.DashRemaining > 0)
            {
                Player.DashRemaining -= dt;
                Move(ref px, ref py, dashX * 640 * dt, dashY * 640 * dt, 12);
            }
            else Move(ref px, ref py, moveX * Player.Speed * dt, moveY * Player.Speed * dt, 12);
            Player.X = px; Player.Y = py;
            if (length > 0) { Player.FacingX = lastMoveX; Player.FacingY = lastMoveY; }

            spawnTimer -= dt;
            if (pending.Count > 0 && spawnTimer <= 0 && enemies.Count < (Room == 6 ? 6 : 7))
            {
                SpawnEnemy(pending[0]);
                pending.RemoveAt(0);
                spawnTimer = Room == 6 ? 4 : 1.45f;
            }
            shotTimer -= dt;
            if (shotTimer <= 0 && enemies.Count > 0 && spawnGrace <= 0)
                Fire(aimX, aimY, manualAim && !float.IsNaN(aimX) && !float.IsNaN(aimY) && !float.IsInfinity(aimX) && !float.IsInfinity(aimY));

            foreach (EnemyState enemy in enemies)
            {
                enemy.Flash = Math.Max(0, enemy.Flash - dt);
                enemy.Slow = Math.Max(0, enemy.Slow - dt);
                enemy.Timer -= dt;
                enemy.Telegraph = 0;
                float dx = Player.X - enemy.X, dy = Player.Y - enemy.Y;
                float d = Math.Max(1, Length(dx, dy));
                float factor = enemy.Slow > 0 ? .48f : 1;
                float vx = dx / d * enemy.Speed * factor, vy = dy / d * enemy.Speed * factor;
                if (enemy.Kind != EnemyKind.Brute) { enemy.FacingX = dx / d; enemy.FacingY = dy / d; }
                if (enemy.Kind == EnemyKind.Wisp)
                {
                    float direction = d < 150 ? -1 : d > 245 ? 1 : 0;
                    vx = dx / d * enemy.Speed * direction + (float)Math.Cos(Elapsed + enemy.Phase) * 20;
                    vy = dy / d * enemy.Speed * direction + (float)Math.Sin(Elapsed + enemy.Phase) * 20;
                    if (enemy.Timer <= 0 && spawnGrace <= 0)
                    {
                        EnemyShot(enemy, (float)Math.Atan2(dy, dx), 130 + Room * 9);
                        enemy.Timer = 2.2f + random.Float() * .9f;
                    }
                }
                else if (enemy.Kind == EnemyKind.Brute)
                {
                    if (enemy.Charging > 0)
                    {
                        enemy.Charging -= dt;
                        vx = enemy.FacingX * 300; vy = enemy.FacingY * 300;
                    }
                    else if (enemy.Timer < .5f)
                    {
                        vx = vy = 0;
                        enemy.Telegraph = Clamp(1 - enemy.Timer / .5f, 0, 1);
                        if (enemy.Timer <= 0)
                        {
                            enemy.Charging = .6f; enemy.FacingX = dx / d; enemy.FacingY = dy / d;
                            enemy.Timer = 3.4f;
                        }
                    }
                }
                else if (enemy.Kind == EnemyKind.Boss && enemy.Timer <= 0 && spawnGrace <= 0)
                {
                    int count = enemy.Hp < enemy.MaxHp / 2 ? 14 : 10;
                    float angle = (float)Math.Atan2(dy, dx);
                    for (int i = 0; i < count; i++) EnemyShot(enemy, angle + (float)Math.PI * 2 * i / count, 115);
                    EnemyShot(enemy, angle, 195);
                    enemy.Timer = enemy.Hp < enemy.MaxHp / 2 ? 1.8f : 2.6f;
                }
                foreach (EnemyState other in enemies)
                {
                    if (ReferenceEquals(enemy, other)) continue;
                    float apart = Distance(enemy.X, enemy.Y, other.X, other.Y);
                    if (apart < enemy.Radius + other.Radius + 5 && apart > .1f)
                    {
                        vx += (enemy.X - other.X) / apart * 32;
                        vy += (enemy.Y - other.Y) / apart * 32;
                    }
                }
                if (Collides(enemy.X + vx * .28f, enemy.Y + vy * .28f, enemy.Radius))
                {
                    int side = enemy.Id % 2 != 0 ? 1 : -1;
                    float originalX = vx; vx = -vy * side; vy = originalX * side;
                }
                if (spawnGrace <= 0) Move(ref enemy.X, ref enemy.Y, vx * dt, vy * dt, enemy.Radius);
                if (Distance(enemy.X, enemy.Y, Player.X, Player.Y) < enemy.Radius + 11 && spawnGrace <= 0)
                    Hurt(enemy.Kind == EnemyKind.Boss ? 22 : enemy.Kind == EnemyKind.Brute ? 18 : 11);
            }

            foreach (ProjectileState shot in projectiles)
            {
                float previousX = shot.X, previousY = shot.Y;
                shot.X += shot.Vx * dt; shot.Y += shot.Vy * dt; shot.Life -= dt;
                bool blocked = shot.X < 70 || shot.X > 890 || shot.Y < 102 || shot.Y > 526 || Collides(shot.X, shot.Y, shot.Radius);
                if (shot.Life <= 0) continue;
                if (shot.Enemy)
                {
                    if (SegmentDistance(Player.X, Player.Y, previousX, previousY, shot.X, shot.Y) < shot.Radius + 11)
                    { Hurt(shot.Damage); shot.Life = 0; }
                }
                else foreach (EnemyState enemy in enemies)
                {
                    if (enemy.Hp <= 0 || shot.Hit.Contains(enemy.Id) || SegmentDistance(enemy.X, enemy.Y, previousX, previousY, shot.X, shot.Y) >= shot.Radius + enemy.Radius) continue;
                    shot.Hit.Add(enemy.Id);
                    enemy.Hp -= shot.Damage;
                    enemy.Flash = .12f;
                    if (frost) enemy.Slow = 1.2f;
                    if (shot.Pierce-- <= 0) shot.Life = 0;
                    if (enemy.Hp <= 0)
                    {
                        Kills++;
                        Shards += enemy.Kind == EnemyKind.Boss ? 25 : enemy.Kind == EnemyKind.Brute ? 4 : 2;
                        if (Player.Hp > 0) Player.Hp = Math.Min(Player.MaxHp, Player.Hp + leech);
                    }
                    if (shot.Life <= 0) break;
                }
                // The swept path may hit a body before its endpoint enters masonry.
                if (blocked) shot.Life = 0;
            }
            projectiles.RemoveAll(value => value.Life <= 0);
            enemies.RemoveAll(value => value.Hp <= 0);
            if (Player.Hp <= 0) Finish(false);
            else if (enemies.Count == 0 && pending.Count == 0 && spawnGrace <= 0) ClearRoom();
        }

        private void GenerateRoom(bool preview)
        {
            obstacles.Clear(); enemies.Clear(); projectiles.Clear(); pending.Clear();
            int layout = preview ? 0 : random.Int(3);
            float[,] positions = layout == 0 ? new float[,] { { 234, 192 }, { 680, 192 }, { 234, 414 }, { 680, 414 } }
                : layout == 1 ? new float[,] { { 260, 224 }, { 650, 224 }, { 260, 407 }, { 650, 407 } }
                : new float[,] { { 208, 270 }, { 704, 270 }, { 328, 412 }, { 584, 160 } };
            for (int i = 0; i < 4; i++) obstacles.Add(new Obstacle(positions[i, 0], positions[i, 1], 44, 36, i % 2));
            Player.X = 480; Player.Y = 352; Player.DashRemaining = 0;
            spawnGrace = 1.2f; spawnTimer = .2f; shotTimer = .45f;
            if (preview)
            {
                enemies.Add(CreateEnemy(EnemyKind.Shade, 358, 205));
                enemies.Add(CreateEnemy(EnemyKind.Wisp, 732, 332));
                enemies.Add(CreateEnemy(EnemyKind.Brute, 220, 346));
                enemies.Add(CreateEnemy(EnemyKind.Shade, 628, 448));
                enemies.Add(CreateEnemy(EnemyKind.Shade, 694, 159));
                return;
            }
            if (Room == 6)
            {
                enemies.Add(CreateEnemy(EnemyKind.Boss, 480, 192));
                pending.AddRange(new[] { EnemyKind.Shade, EnemyKind.Shade, EnemyKind.Wisp, EnemyKind.Shade, EnemyKind.Brute, EnemyKind.Wisp });
                spawnTimer = 7;
            }
            else
            {
                int[] counts = { 0, 8, 11, 14, 17, 20 };
                int count = counts[Room];
                for (int i = 0; i < count; i++)
                {
                    float roll = random.Float();
                    pending.Add(Room >= 3 && roll > .76f ? EnemyKind.Brute : Room >= 2 && roll > .53f ? EnemyKind.Wisp : EnemyKind.Shade);
                }
                for (int i = 0; i < Math.Min(4, count); i++) { SpawnEnemy(pending[0]); pending.RemoveAt(0); }
            }
        }

        private EnemyState CreateEnemy(EnemyKind kind, float x, float y)
        {
            float hp = kind == EnemyKind.Boss ? 950 : kind == EnemyKind.Brute ? 86 + Room * 8 : kind == EnemyKind.Wisp ? 38 + Room * 5 : 32 + Room * 6;
            return new EnemyState
            {
                Id = nextId++, Kind = kind, X = x, Y = y, Hp = hp, MaxHp = hp,
                Radius = kind == EnemyKind.Boss ? 31 : kind == EnemyKind.Brute ? 19 : 13,
                Speed = kind == EnemyKind.Boss ? 28 : kind == EnemyKind.Brute ? 39 : kind == EnemyKind.Wisp ? 52 : 58 + Room * 3,
                Timer = 1.3f + random.Float() * 1.4f, Phase = random.Float() * (float)Math.PI * 2, FacingY = 1
            };
        }

        private void SpawnEnemy(EnemyKind kind)
        {
            float x = 120, y = 150;
            for (int i = 0; i < 30; i++)
            {
                int edge = random.Int(4);
                if (edge == 0) { x = 98 + random.Float() * 760; y = 128; }
                else if (edge == 1) { x = 860; y = 136 + random.Float() * 350; }
                else if (edge == 2) { x = 98 + random.Float() * 760; y = 498; }
                else { x = 100; y = 136 + random.Float() * 350; }
                if (Distance(x, y, Player.X, Player.Y) > 185 && !Collides(x, y, 24)) break;
            }
            enemies.Add(CreateEnemy(kind, x, y));
        }

        private void Fire(float aimX, float aimY, bool manual)
        {
            float targetX = aimX, targetY = aimY;
            if (!manual)
            {
                float nearest = float.MaxValue;
                foreach (EnemyState enemy in enemies)
                {
                    float d = Distance(enemy.X, enemy.Y, Player.X, Player.Y);
                    if (enemy.Hp > 0 && d < nearest) { nearest = d; targetX = enemy.X; targetY = enemy.Y; }
                }
                if (nearest == float.MaxValue) return;
            }
            float angle = (float)Math.Atan2(targetY - Player.Y, targetX - Player.X);
            Player.FacingX = (float)Math.Cos(angle); Player.FacingY = (float)Math.Sin(angle);
            for (int i = 0; i < multishot; i++)
            {
                float a = angle + (i - (multishot - 1) / 2f) * .14f;
                float dx = (float)Math.Cos(a), dy = (float)Math.Sin(a);
                projectiles.Add(new ProjectileState { X = Player.X + dx * 5, Y = Player.Y - 5 + dy * 5, Vx = dx * 470, Vy = dy * 470, Life = 2, Damage = Player.Damage, Radius = 5, Pierce = piercing });
            }
            shotTimer = Player.AttackInterval;
        }

        private void EnemyShot(EnemyState enemy, float angle, float speed)
        {
            float dx = (float)Math.Cos(angle), dy = (float)Math.Sin(angle);
            projectiles.Add(new ProjectileState { X = enemy.X + dx * (enemy.Radius + 8), Y = enemy.Y + dy * (enemy.Radius + 8), Vx = dx * speed, Vy = dy * speed, Life = 6, Damage = enemy.Kind == EnemyKind.Boss ? 14 : 10, Enemy = true, Radius = enemy.Kind == EnemyKind.Boss ? 6 : 5 });
        }

        private void Hurt(float damage)
        {
            if (Player.Invincible > 0 || Player.Hp <= 0) return;
            Player.Hp = Math.Max(0, Player.Hp - Math.Max(1, (float)Math.Round(damage * armor)));
            Player.Invincible = .8f;
        }

        private void ClearRoom()
        {
            projectiles.Clear();
            if (Room == 6) { Shards += 40; Finish(true); return; }
            State = RunState.Reward;
            Shards += 8;
            choices.Clear();
            List<Relic> pool = new List<Relic>();
            foreach (Relic relic in Catalog)
                if (!(relic.Id == "frost" && frost) && !(relic.Id == "echo" && multishot >= 5)) pool.Add(relic);
            for (int i = pool.Count - 1; i > 0; i--) { int j = random.Int(i + 1); Relic swap = pool[i]; pool[i] = pool[j]; pool[j] = swap; }
            for (int i = 0; i < 3; i++) choices.Add(Copy(pool[i]));
            NotifyChanged();
            Action<IReadOnlyList<Relic>> handler = RewardOffered;
            if (handler != null) handler(new List<Relic>(choices).AsReadOnly());
        }

        private void Finish(bool won)
        {
            if (State == RunState.Dead || State == RunState.Won) return;
            State = won ? RunState.Won : RunState.Dead;
            NotifyChanged();
            Action<bool> handler = Ended;
            if (handler != null) handler(won);
        }

        private bool Collides(float x, float y, float radius)
        {
            foreach (Obstacle obstacle in obstacles)
            {
                float nx = Clamp(x, obstacle.X - obstacle.Width / 2, obstacle.X + obstacle.Width / 2);
                float ny = Clamp(y, obstacle.Y - obstacle.Height / 2, obstacle.Y + obstacle.Height / 2);
                float dx = x - nx, dy = y - ny;
                if (dx * dx + dy * dy < radius * radius) return true;
            }
            return false;
        }

        private void Move(ref float x, ref float y, float dx, float dy, float radius)
        {
            float nextX = Clamp(x + dx, 70 + radius, 890 - radius);
            if (!Collides(nextX, y, radius)) x = nextX;
            float nextY = Clamp(y + dy, 102 + radius, 526 - radius);
            if (!Collides(x, nextY, radius)) y = nextY;
        }

        private void NotifyChanged() { Action handler = Changed; if (handler != null) handler(); }
        private void NotifyNotice(string text) { Action<string> handler = Notice; if (handler != null) handler(text); }
        private static Relic Copy(Relic relic) { return new Relic(relic.Id, relic.Name, relic.Description, relic.Icon, relic.Rarity); }
        private static float Clamp(float value, float min, float max) { return Math.Max(min, Math.Min(max, value)); }
        private static float Length(float x, float y) { return (float)Math.Sqrt(x * x + y * y); }
        private static float Distance(float x1, float y1, float x2, float y2) { return Length(x1 - x2, y1 - y2); }
        private static float SegmentDistance(float x, float y, float ax, float ay, float bx, float by)
        {
            float dx = bx - ax, dy = by - ay, squared = dx * dx + dy * dy;
            float t = squared > 0 ? Clamp(((x - ax) * dx + (y - ay) * dy) / squared, 0, 1) : 0;
            return Length(x - ax - t * dx, y - ay - t * dy);
        }

        private sealed class RandomSource
        {
            private uint value;
            public RandomSource(string seed)
            {
                unchecked { value = 2166136261; foreach (char c in seed) value = (value ^ c) * 16777619; }
            }
            private double Next()
            {
                unchecked
                {
                    value += 0x6d2b79f5;
                    uint n = value;
                    n = (n ^ n >> 15) * (n | 1);
                    n ^= n + (n ^ n >> 7) * (n | 61);
                    return (n ^ n >> 14) / 4294967296.0;
                }
            }
            public float Float() { return (float)Next(); }
            public int Int(int max) { return Math.Min(max - 1, (int)(Next() * max)); }
        }
    }
}
