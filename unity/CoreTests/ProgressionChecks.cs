using System;
using System.Collections.Generic;
using Umbra.Core;

namespace Umbra.Tests
{
    public static class ProgressionChecks
    {
        public static void Run()
        {
            var profile = new Progression();
            Check(Progression.IsValid(profile.Data), "A new profile must be valid.");
            Check(profile.Data.BestTime == -1, "A profile without victories has no best time.");
            Check(profile.RecordRun("progress_run_001", false, 3, 12, 80, 20), "A valid run must be recorded.");
            Check(!profile.RecordRun("progress_run_001", false, 3, 12, 80, 20), "A duplicate ID must not be recorded twice.");
            Check(profile.Data.Runs == 1 && profile.Data.Shards == 20 && profile.Data.BestRoom == 3,
                "A duplicate run must not award extra fragments or alter statistics.");

            string message;
            Check(profile.TryUpgrade(UpgradeKind.Vitality, out message), "An affordable upgrade must succeed.");
            Check(profile.Data.Vitality == 1 && profile.Data.Shards == 5, "The first upgrade must cost 15 fragments.");
            Check(!profile.TryUpgrade(UpgradeKind.Power, out message) && profile.Data.Shards == 5,
                "Insufficient funds must leave the balance unchanged.");
            Check(!profile.TryUpgrade((UpgradeKind)99, out message), "An unknown upgrade must be rejected.");

            int runs = profile.Data.Runs;
            Check(!profile.RecordRun("bad", false, 1, 0, 1, 0), "Short IDs must be rejected.");
            Check(!profile.RecordRun("invalid_id_1", true, 5, 0, 1, 0), "Victory requires the final room.");
            Check(!profile.RecordRun("invalid_id_2", false, 1, -1, 1, 0), "Negative kills must be rejected.");
            Check(!profile.RecordRun("invalid_id_3", false, 1, 0, float.NaN, 0), "NaN time must be rejected.");
            Check(!profile.RecordRun("invalid_id_4", false, 1, 0, float.PositiveInfinity, 0), "Infinite time must be rejected.");
            Check(!profile.RecordRun("invalid_id_5", false, 7, 0, 1, 0), "Out-of-range rooms must be rejected.");
            Check(!profile.RecordRun("invalid_id_6", false, 1, 0, 1, 1001), "Excessive fragment awards must be rejected.");
            Check(!profile.RecordRun("invalid_id_7", false, 1, 0, -1, 0), "Negative time must be rejected.");
            Check(profile.Data.Runs == runs && profile.Data.Shards == 5, "Rejected runs must not mutate the profile.");

            Check(profile.RecordRun("victory_run_001", true, 6, 50, 120, 100), "The first victory must be recorded.");
            Check(profile.RecordRun("victory_run_002", true, 6, 50, 90, 100), "A faster victory must be recorded.");
            Check(profile.RecordRun("victory_run_003", true, 6, 50, 180, 100), "A slower victory must be recorded.");
            Check(profile.Data.Wins == 3 && profile.Data.BestRoom == 6 && profile.Data.BestTime == 90,
                "Statistics must retain the fastest completed victory.");
            Check(Progression.IsValid(profile.Data), "Recorded and upgraded profiles must remain valid.");

            var rich = new Progression(new ProgressData { Shards = 1000 });
            for (int i = 0; i < Progression.MaxLevel; i++)
            {
                Check(Progression.Cost(i) == 15 * (i + 1), "Upgrade costs must increase by level.");
                Check(rich.TryUpgrade(UpgradeKind.Power, out message), "Each affordable level must be available.");
            }
            Check(rich.Data.Power == 5 && rich.Data.Shards == 775, "Five levels must cost 225 fragments.");
            Check(!rich.TryUpgrade(UpgradeKind.Power, out message) && rich.Data.Shards == 775,
                "The maximum level must not spend more fragments.");

            var source = new ProgressData
            {
                Version = 99, Shards = -10, Vitality = 100, Power = -5, Runs = -1, Wins = 9,
                BestRoom = 100, BestTime = float.NaN,
                CompletedRuns = new List<string> { "valid_run_01", "valid_run_01", "bad", null }
            };
            Check(!Progression.IsValid(source), "Malformed imports must fail strict save validation.");
            var sanitized = new Progression(source);
            Check(Progression.IsValid(sanitized.Data), "Constructor imports must sanitize to a valid profile.");
            Check(sanitized.Data.Version == 1 && sanitized.Data.Shards == 0
                && sanitized.Data.Vitality == 5 && sanitized.Data.Power == 0
                && sanitized.Data.Runs == 1 && sanitized.Data.Wins == 1
                && sanitized.Data.BestTime == 86400 && sanitized.Data.CompletedRuns.Count == 1,
                "Sanitization must clamp numeric values and deduplicate valid IDs.");
            sanitized.Data.CompletedRuns.Add("another_run_1");
            Check(source.CompletedRuns.Count == 4, "Imports must not share the caller's mutable ID list.");

            Check(!Progression.IsValid(null), "Null saves must fail validation.");
            Check(!Progression.IsValid(new ProgressData { CompletedRuns = null }), "Missing ID lists must fail validation.");
            Check(!Progression.IsValid(new ProgressData { BestTime = 0 }), "Profiles without victories cannot claim a best time.");
            Check(!Progression.IsValid(new ProgressData { Runs = 2, BestRoom = 1, CompletedRuns = new List<string> { "repeat_run_1", "repeat_run_1" } }),
                "Duplicate IDs must fail strict save validation.");
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Progression check failed: " + message);
        }
    }
}
