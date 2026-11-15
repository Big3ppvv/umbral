using System;
using Umbra.Core;

namespace Umbra.Tests
{
    public static class SimulationChecks
    {
        private static int passed;
        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Simulation: " + message);
            passed++;
        }

        public static void Run()
        {
            PreviewAndClock();
            SeedsAndMeta();
            ContactShots();
            ColumnContactShots();
            RewardsAndTerminal();
            FullRun("UMBRA");
            FullRun("TEST42");
            FullRun("ALBA");
            Console.WriteLine("PASS SimulationChecks: " + passed + " assertions, including 3 complete base-stat victories.");
        }

        private static void PreviewAndClock()
        {
            Simulation simulation = new Simulation();
            Check(simulation.State == RunState.Idle && simulation.Room == 1 && simulation.Enemies.Count == 5, "initial crypt preview");
            simulation.Enemies[0].X = simulation.Player.X;
            simulation.Enemies[0].Y = simulation.Player.Y;
            simulation.Step(60, 1, 1, true);
            Check(simulation.Player.Hp == 100 && simulation.Elapsed == 0, "idle does not simulate combat");
            simulation.Pause(); simulation.Resume();
            Check(simulation.State == RunState.Idle, "pause/resume cannot start an idle run");
            simulation.StartRun(0, 0, "CLOCK");
            simulation.Step(float.NaN, 1, 0, false);
            simulation.Step(float.PositiveInfinity, 1, 0, false);
            simulation.Step(-1, 1, 0, false);
            Check(simulation.Elapsed == 0, "invalid time deltas are ignored");
            float x = simulation.Player.X;
            simulation.Step(60, 1, 0, false);
            Check(Math.Abs(simulation.Elapsed - .033f) < .00001f, "background delta is capped");
            Check(simulation.Player.X - x < 6, "background delta cannot teleport player");
            simulation.Pause();
            float elapsed = simulation.Elapsed, hp = simulation.Player.Hp;
            simulation.Step(1, -1, 0, true);
            Check(simulation.Elapsed == elapsed && simulation.Player.Hp == hp, "paused time and health freeze");
            simulation.Resume();
            Check(simulation.State == RunState.Playing, "resume returns to combat");
            simulation.Step(.016f, float.NaN, float.PositiveInfinity, false, float.NaN, 1, true);
            Check(!float.IsNaN(simulation.Player.X) && !float.IsNaN(simulation.Player.Y), "invalid input cannot poison simulation coordinates");
        }

        private static void SeedsAndMeta()
        {
            Simulation simulation = new Simulation();
            simulation.StartRun(2, 3, "TEST42");
            Check(simulation.Player.MaxHp == 120 && simulation.Player.Damage == 28, "meta bonuses are +10HP and +2damage per level");
            float[] layout = new float[simulation.Obstacles.Count * 2];
            for (int i = 0; i < simulation.Obstacles.Count; i++) { layout[i * 2] = simulation.Obstacles[i].X; layout[i * 2 + 1] = simulation.Obstacles[i].Y; }
            float enemyX = simulation.Enemies[0].X, enemyY = simulation.Enemies[0].Y;
            simulation.StartRun(2, 3, "TEST42");
            for (int i = 0; i < simulation.Obstacles.Count; i++)
                Check(layout[i * 2] == simulation.Obstacles[i].X && layout[i * 2 + 1] == simulation.Obstacles[i].Y, "seed repeats obstacle " + i);
            Check(enemyX == simulation.Enemies[0].X && enemyY == simulation.Enemies[0].Y, "seed repeats enemy positions");
            simulation.StartRun(999, 999, "CAP");
            Check(simulation.Player.MaxHp == 150 && simulation.Player.Damage == 32, "meta upgrade levels cap at five");
            Check(simulation.Enemies.IsReadOnly && simulation.Choices.IsReadOnly, "collection views cannot mutate core ownership");
        }

        private static void ContactShots()
        {
            Simulation simulation = new Simulation();
            simulation.StartRun(0, 0, "CONTACT");
            EnemyState enemy = simulation.Enemies[0];
            enemy.X = simulation.Player.X;
            enemy.Y = simulation.Player.Y;
            enemy.Speed = 0;
            enemy.Hp = 22;
            // Isolate shot geometry from incoming damage in this regression only.
            simulation.Player.Invincible = 1000;
            for (int i = 0; i < 90 && simulation.Kills == 0; i++) simulation.Step(.033f, 0, 0, false);
            Check(simulation.Kills > 0 && enemy.Hp <= 0, "swept auto-fire can hit overlapping foes at 30fps");
        }

        private static void ColumnContactShots()
        {
            Simulation simulation = new Simulation();
            simulation.StartRun(0, 0, "COLUMN");
            Obstacle column = simulation.Obstacles[0];
            simulation.Player.X = column.X - column.Width / 2 - 13;
            simulation.Player.Y = column.Y;
            simulation.Player.Invincible = 1000;
            EnemyState enemy = simulation.Enemies[0];
            enemy.X = simulation.Player.X; enemy.Y = simulation.Player.Y;
            enemy.Speed = 0; enemy.Hp = 22;
            for (int i = 1; i < simulation.Enemies.Count; i++) { simulation.Enemies[i].X = 850; simulation.Enemies[i].Y = 480; }
            for (int i = 0; i < 90 && enemy.Hp > 0; i++) simulation.Step(.033f, 0, 0, false);
            Check(enemy.Hp <= 0, "column-adjacent contact hit resolves before projectile expires on masonry");
        }

        private static void RewardsAndTerminal()
        {
            Simulation simulation = new Simulation();
            int rewardEvents = 0, endEvents = 0;
            simulation.RewardOffered += list => { Check(list.Count == 3, "reward offers three choices"); rewardEvents++; };
            simulation.Ended += won => { Check(!won, "death reports loss"); endEvents++; };
            simulation.StartRun(0, 0, "REWARD");
            Check(!simulation.ChooseRelic("ember"), "relic cannot be selected during combat");
            for (int i = 0; i < 60 * 120 && simulation.State == RunState.Playing; i++) CircleStep(simulation);
            Check(simulation.State == RunState.Reward && rewardEvents == 1, "clearing room enters reward once");
            Check(!simulation.ChooseRelic("invalid") && simulation.Room == 1, "unknown reward cannot advance");
            Relic selected = null;
            foreach (Relic value in simulation.Choices) if (value.Id != "heart" && value.Id != "salve") { selected = value; break; }
            Check(selected != null, "offer includes a non-healing relic");
            simulation.Player.Hp = 50;
            Check(simulation.ChooseRelic(selected.Id), "valid reward accepted");
            Check(simulation.Room == 2 && simulation.Relics.Count == 1 && simulation.Player.Hp == 65, "reward heals 15 and advances room");
            Check(!simulation.ChooseRelic(selected.Id) && simulation.Room == 2, "duplicate choice cannot advance twice");
            simulation.Player.Hp = 0;
            simulation.Step(.016f, 0, 0, false);
            float elapsed = simulation.Elapsed;
            for (int i = 0; i < 30; i++) simulation.Step(.016f, 0, 0, false);
            Check(simulation.State == RunState.Dead && endEvents == 1, "death event fires once");
            Check(simulation.Elapsed == elapsed, "terminal result remains frozen");
        }

        private static void FullRun(string seed)
        {
            Simulation simulation = new Simulation();
            int endEvents = 0;
            simulation.Ended += won => { Check(won, seed + " reports victory"); endEvents++; };
            simulation.StartRun(0, 0, seed);
            // Same input route as the actual Unity keyboard/touch host. No stat,
            // enemy, invulnerability, room, or health edits in these full runs.
            for (int frame = 0; frame < 60 * 300 && simulation.State != RunState.Won && simulation.State != RunState.Dead; frame++)
            {
                if (simulation.State == RunState.Reward)
                {
                    Relic selected = null;
                    string[] preference = { "echo", "ember", "haste" };
                    foreach (string id in preference)
                    {
                        foreach (Relic value in simulation.Choices) if (value.Id == id) { selected = value; break; }
                        if (selected != null) break;
                    }
                    simulation.ChooseRelic((selected ?? simulation.Choices[0]).Id);
                }
                else CircleStep(simulation);
            }
            Check(simulation.State == RunState.Won, seed + " reaches victory (room " + simulation.Room + ", hp " + simulation.Player.Hp + ", kills " + simulation.Kills + ", seconds " + simulation.Elapsed + ")");
            Check(simulation.Room == 6 && simulation.Kills == 77 && simulation.RemainingEnemies == 0, seed + " defeats all room waves and boss");
            Check(simulation.Relics.Count == 5 && simulation.Player.Hp > 0 && endEvents == 1, seed + " records five relics and one final result");
            Console.WriteLine("  " + seed + ": won in " + Math.Round(simulation.Elapsed) + "s; " + Math.Round(simulation.Player.Hp) + "HP; " + simulation.Shards + " fragments.");
        }

        private static void CircleStep(Simulation simulation)
        {
            double angle = simulation.Elapsed * .7;
            float dx = 480 + 110 * (float)Math.Cos(angle) - simulation.Player.X;
            float dy = 330 + 105 * (float)Math.Sin(angle) - simulation.Player.Y;
            bool dash = false;
            foreach (EnemyState enemy in simulation.Enemies)
                if (Math.Sqrt(Math.Pow(enemy.X - simulation.Player.X, 2) + Math.Pow(enemy.Y - simulation.Player.Y, 2)) < 50) { dash = true; break; }
            simulation.Step(1f / 60, dx < -6 ? -1 : dx > 6 ? 1 : 0, dy < -6 ? -1 : dy > 6 ? 1 : 0, dash);
        }
    }
}
