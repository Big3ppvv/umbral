using System;
using System.Collections.Generic;

namespace Umbra.Core
{
    public enum RunState { Idle, Playing, Paused, Reward, Dead, Won }
    public enum EnemyKind { Shade, Wisp, Brute, Boss }
    public enum UpgradeKind { Vitality, Power }

    [Serializable]
    public sealed class PlayerState
    {
        public float X = 480, Y = 344, Hp = 100, MaxHp = 100, Damage = 22;
        public float Speed = 170, AttackInterval = .44f, Invincible, DashRemaining;
        public float FacingX, FacingY = -1;
    }

    public sealed class EnemyState
    {
        public int Id;
        public EnemyKind Kind;
        public float X, Y, Hp, MaxHp, Radius, Speed, Timer, Slow, Flash, Charging;
        public float FacingX, FacingY, Phase, Telegraph;
    }

    public sealed class ProjectileState
    {
        public float X, Y, Vx, Vy, Radius, Damage, Life;
        public bool Enemy;
        public int Pierce;
        public readonly HashSet<int> Hit = new HashSet<int>();
    }

    public struct Obstacle
    {
        public float X, Y, Width, Height;
        public int Variant;
        public Obstacle(float x, float y, float width, float height, int variant)
        { X = x; Y = y; Width = width; Height = height; Variant = variant; }
    }

    [Serializable]
    public sealed class Relic
    {
        public string Id, Name, Description, Icon, Rarity;
        public Relic(string id, string name, string description, string icon, string rarity)
        { Id = id; Name = name; Description = description; Icon = icon; Rarity = rarity; }
    }
}
