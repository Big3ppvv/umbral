using System;
using System.Collections.Generic;

namespace Umbra.Core
{
    [Serializable]
    public sealed class ProgressData
    {
        public int Version = 1;
        public int Shards;
        public int Vitality;
        public int Power;
        public int Runs;
        public int Wins;
        public int BestRoom;
        public float BestTime = -1;
        public List<string> CompletedRuns = new List<string>();
    }

    public sealed class Progression
    {
        public const int MaxLevel = 5;
        private const int MaxCounter = 1000000000;
        public ProgressData Data { get; private set; }

        // Core callers may import a partly populated object. The persistence
        // adapter calls IsValid first, so corrupt saved data is never rewritten.
        public Progression(ProgressData data = null)
        {
            Data = Sanitize(data ?? new ProgressData());
        }

        public static int Cost(int level)
        {
            if (level < 0 || level > MaxLevel)
                throw new ArgumentOutOfRangeException("level");
            return 15 * (level + 1);
        }

        // False means invalid input or an already recorded ID. Neither case
        // mutates the profile, so retrying the same finished run is safe.
        public bool RecordRun(string id, bool won, int room, int kills, float duration, int shards)
        {
            if (!ValidId(id) || room < 1 || room > 6 || (won && room != 6)
                || kills < 0 || kills > 100000 || !Finite(duration)
                || duration < 0 || duration > 86400 || shards < 0 || shards > 1000)
                return false;
            if (!IsValid(Data) || Data.CompletedRuns.Contains(id)) return false;

            Data.CompletedRuns.Add(id);
            Data.Shards = (int)Math.Min(MaxCounter, (long)Data.Shards + shards);
            Data.Runs = Math.Min(MaxCounter, Data.Runs + 1);
            if (won)
            {
                Data.Wins = Math.Min(Data.Runs, Data.Wins + 1);
                Data.BestTime = Data.BestTime < 0 ? duration : Math.Min(Data.BestTime, duration);
            }
            Data.BestRoom = Math.Max(Data.BestRoom, room);
            return true;
        }

        public bool TryUpgrade(UpgradeKind kind, out string message)
        {
            if (kind != UpgradeKind.Vitality && kind != UpgradeKind.Power)
            {
                message = "Esa mejora no existe.";
                return false;
            }
            if (!IsValid(Data))
            {
                message = "El perfil contiene datos inválidos. No se ha gastado ningún fragmento.";
                return false;
            }
            int level = kind == UpgradeKind.Vitality ? Data.Vitality : Data.Power;
            if (level >= MaxLevel)
            {
                message = "Esta mejora ya está al nivel máximo.";
                return false;
            }
            int cost = Cost(level);
            if (Data.Shards < cost)
            {
                message = "Necesitas " + cost + " fragmentos para esta mejora.";
                return false;
            }

            Data.Shards -= cost;
            if (kind == UpgradeKind.Vitality) Data.Vitality++;
            else Data.Power++;
            message = kind == UpgradeKind.Vitality
                ? "Corazón del faro mejorado. Se aplicará en la próxima incursión."
                : "Filo de aurora mejorado. Se aplicará en la próxima incursión.";
            return true;
        }

        public static bool IsValid(ProgressData data)
        {
            if (data == null || data.Version != 1
                || data.Shards < 0 || data.Shards > MaxCounter
                || data.Vitality < 0 || data.Vitality > MaxLevel
                || data.Power < 0 || data.Power > MaxLevel
                || data.Runs < 0 || data.Runs > MaxCounter
                || data.Wins < 0 || data.Wins > data.Runs
                || data.BestRoom < 0 || data.BestRoom > 6
                || !Finite(data.BestTime)
                || data.CompletedRuns == null || data.CompletedRuns.Count > data.Runs)
                return false;
            if (data.Runs == 0 && data.BestRoom != 0) return false;
            if (data.Runs > 0 && data.BestRoom == 0) return false;
            if (data.Wins == 0 && data.BestTime != -1) return false;
            if (data.Wins > 0 && (data.BestRoom != 6 || data.BestTime < 0 || data.BestTime > 86400))
                return false;

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in data.CompletedRuns)
                if (!ValidId(id) || !ids.Add(id)) return false;
            return true;
        }

        private static ProgressData Sanitize(ProgressData source)
        {
            var result = new ProgressData
            {
                Shards = Clamp(source.Shards, 0, MaxCounter),
                Vitality = Clamp(source.Vitality, 0, MaxLevel),
                Power = Clamp(source.Power, 0, MaxLevel),
                Runs = Clamp(source.Runs, 0, MaxCounter),
                BestRoom = Clamp(source.BestRoom, 0, 6)
            };
            var ids = new HashSet<string>(StringComparer.Ordinal);
            if (source.CompletedRuns != null)
            {
                foreach (string id in source.CompletedRuns)
                    if (ValidId(id) && ids.Add(id)) result.CompletedRuns.Add(id);
            }
            result.Runs = Math.Max(result.Runs, result.CompletedRuns.Count);
            result.Wins = Clamp(source.Wins, 0, result.Runs);
            if (result.Runs == 0) result.BestRoom = 0;
            else result.BestRoom = Math.Max(1, result.BestRoom);
            if (result.Wins > 0)
            {
                result.BestRoom = 6;
                result.BestTime = Finite(source.BestTime) && source.BestTime >= 0
                    ? Math.Min(86400, source.BestTime) : 86400;
            }
            return result;
        }

        private static bool ValidId(string id)
        {
            if (id == null || id.Length < 8 || id.Length > 80) return false;
            foreach (char c in id)
                if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z')
                    && !(c >= '0' && c <= '9') && c != '_' && c != '-') return false;
            return true;
        }

        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private static int Clamp(int value, int min, int max) { return Math.Max(min, Math.Min(max, value)); }
    }
}
