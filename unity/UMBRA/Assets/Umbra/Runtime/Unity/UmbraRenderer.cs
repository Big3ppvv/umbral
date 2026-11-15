using System;
using System.Collections.Generic;
using Umbra.Core;
using UnityEngine;

namespace Umbra.Unity
{
    /// <summary>
    /// Built-in-pipeline, dependency-free pixel scene. The simulation remains the only
    /// source of collision and combat state; these pooled objects are visual only.
    /// </summary>
    public sealed class UmbraRenderer : MonoBehaviour
    {
        private const float PixelsPerUnit = 48f;
        private static readonly Color32 Ink = Hex("10191c");
        private static readonly Color32 Brass = Hex("d5b476");
        private static readonly Color32 Teal = Hex("7dbcb5");
        private static readonly Color32 Bone = Hex("e9dcc0");
        private static readonly Color32 Ember = Hex("d77b5e");

        private sealed class Actor
        {
            internal GameObject Root;
            internal SpriteRenderer Body, Shadow, Glow, HealthBack, HealthFill, Warning;
        }

        private sealed class LightSprite
        {
            internal Transform Root;
            internal SpriteRenderer Flame, Glow;
            internal float Phase;
        }

        private Simulation game;
        private Camera sceneCamera;
        private Transform scenery;
        private SpriteRenderer floorRenderer;
        private Texture2D floorTexture;
        private Sprite floorSprite;
        private Material spriteMaterial;
        private Actor player;
        private readonly List<Actor> enemyPool = new List<Actor>();
        private readonly List<Actor> projectilePool = new List<Actor>();
        private readonly List<SpriteRenderer> obstaclePool = new List<SpriteRenderer>();
        private readonly List<LightSprite> torches = new List<LightSprite>();
        private readonly List<Texture2D> textures = new List<Texture2D>();
        private readonly List<Sprite> sprites = new List<Sprite>();
        private readonly Sprite[] enemies = new Sprite[4];
        private Sprite guardian, shadeShadow, softGlow, shotLight, shotDark, pillar, tomb;
        private Sprite whitePixel, flame, warningRing, portal;
        private SpriteRenderer portalRenderer, portalGlow;
        private int renderedRoom = -1;
        private string renderedSeed;
        private bool renderedIdle = true;
        private float previousPlayerX, previousPlayerY;
        private bool playerPositionKnown;
        private bool initialized;

        public bool ReducedMotion { get; set; }

        public static Vector3 ToWorld(float x, float y)
        {
            return new Vector3((x - 480f) / PixelsPerUnit, (300f - y) / PixelsPerUnit, 0f);
        }

        public void Initialize(Simulation simulation, Camera camera)
        {
            if (initialized) return;
            if (simulation == null) throw new ArgumentNullException("simulation");
            if (camera == null) throw new ArgumentNullException("camera");
            game = simulation;
            sceneCamera = camera;
            sceneCamera.backgroundColor = Ink;
            sceneCamera.clearFlags = CameraClearFlags.SolidColor;
            sceneCamera.orthographic = true;
            sceneCamera.transform.position = new Vector3(0f, 0f, -10f);
            sceneCamera.transform.rotation = Quaternion.identity;

            var preservedMaterial = Resources.Load<Material>("UmbraSprite");
            if (preservedMaterial != null)
                spriteMaterial = new Material(preservedMaterial) { name = "UMBRA pixel material" };
            else
            {
                var shader = Shader.Find("Sprites/Default");
                if (shader != null) spriteMaterial = new Material(shader) { name = "UMBRA pixel material" };
            }
            scenery = new GameObject("UMBRA scenery").transform;
            scenery.SetParent(transform, false);
            BuildAtlas();
            floorRenderer = MakeRenderer("Crypt floor", scenery, null, -100);
            player = MakeActor("Keeper", guardian, scenery, true);
            portalRenderer = MakeRenderer("Northern gate", scenery, portal, -30);
            portalRenderer.transform.position = ToWorld(480f, 100f);
            portalGlow = MakeRenderer("Gate glow", scenery, softGlow, -31);
            portalGlow.transform.position = ToWorld(480f, 105f);
            portalGlow.transform.localScale = Vector3.one * 2.6f;
            portalGlow.color = new Color(.49f, .74f, .71f, .18f);
            CreateTorches();
            initialized = true;
            RenderFrame(0f);
        }

        public void RenderFrame(float visualTime)
        {
            if (!initialized || game == null) return;
            if (renderedRoom != game.Room || renderedSeed != game.Seed || (renderedIdle && game.State != RunState.Idle))
            {
                renderedRoom = game.Room;
                renderedSeed = game.Seed;
                BuildFloor();
                SyncObstacles();
            }
            renderedIdle = game.State == RunState.Idle;

            float time = ReducedMotion ? 0f : visualTime;
            var p = game.Player;
            bool walking = game.State == RunState.Playing && playerPositionKnown &&
                (Mathf.Abs(p.X - previousPlayerX) + Mathf.Abs(p.Y - previousPlayerY) > .01f);
            previousPlayerX = p.X;
            previousPlayerY = p.Y;
            playerPositionKnown = true;
            float bob = walking && !ReducedMotion ? Mathf.Round(Mathf.Sin(time * 14f)) : 0f;
            player.Root.transform.position = ToWorld(p.X, p.Y + 10f);
            player.Body.transform.localPosition = new Vector3(0f, bob / PixelsPerUnit, 0f);
            player.Body.flipX = p.FacingX < -.1f;
            player.Body.sortingOrder = Sort(p.Y) + 1;
            player.Shadow.sortingOrder = Sort(p.Y) - 1;
            player.Glow.sortingOrder = Sort(p.Y) - 2;
            player.Body.color = p.Invincible > 0f && p.DashRemaining <= 0f && !ReducedMotion
                ? new Color(1f, 1f, 1f, .65f + .35f * Mathf.Abs(Mathf.Sin(time * 22f))) : Color.white;
            player.Glow.color = new Color(.49f, .74f, .71f, p.DashRemaining > 0f ? .48f : .24f);
            player.Glow.transform.localScale = Vector3.one * (p.DashRemaining > 0f ? 1.8f : 1.2f);

            while (enemyPool.Count < game.Enemies.Count) enemyPool.Add(MakeActor("Shadow", enemies[0], scenery, true));
            for (int i = 0; i < enemyPool.Count; i++)
            {
                Actor actor = enemyPool[i];
                bool visible = i < game.Enemies.Count;
                if (actor.Root.activeSelf != visible) actor.Root.SetActive(visible);
                if (!visible) continue;
                EnemyState e = game.Enemies[i];
                actor.Body.sprite = enemies[(int)e.Kind];
                float hover = e.Kind == EnemyKind.Wisp && !ReducedMotion ? Mathf.Round(Mathf.Sin(time * 4f + e.Phase) * 3f) : 0f;
                actor.Root.transform.position = ToWorld(e.X, e.Y + e.Radius * .65f);
                actor.Body.transform.localPosition = new Vector3(0f, hover / PixelsPerUnit, 0f);
                actor.Body.flipX = e.FacingX < 0f;
                actor.Body.color = e.Flash > 0f ? new Color(1.5f, 1.4f, 1.2f, 1f) : e.Slow > 0f ? new Color(.64f, .91f, .95f, 1f) : Color.white;
                int order = Sort(e.Y);
                actor.Body.sortingOrder = order;
                actor.Shadow.sortingOrder = order - 2;
                actor.Glow.sortingOrder = order - 3;
                actor.Shadow.transform.localScale = Vector3.one * (e.Kind == EnemyKind.Boss ? 2.25f : e.Kind == EnemyKind.Brute ? 1.45f : 1f);
                actor.Glow.enabled = e.Kind == EnemyKind.Wisp || e.Kind == EnemyKind.Boss;
                actor.Glow.color = e.Kind == EnemyKind.Boss ? new Color(.77f, .34f, .26f, .18f) : new Color(.65f, .63f, .86f, .2f);
                actor.Glow.transform.localScale = Vector3.one * (e.Kind == EnemyKind.Boss ? 2.6f : 1.05f);
                bool healthVisible = e.Hp < e.MaxHp || e.Kind == EnemyKind.Boss;
                actor.HealthBack.enabled = actor.HealthFill.enabled = healthVisible;
                if (healthVisible)
                {
                    float width = e.Kind == EnemyKind.Boss ? 68f : e.Kind == EnemyKind.Brute ? 31f : 24f;
                    float height = e.Kind == EnemyKind.Boss ? 88f : e.Kind == EnemyKind.Brute ? 58f : 44f;
                    actor.HealthBack.transform.localPosition = new Vector3(-width * .5f / PixelsPerUnit, height / PixelsPerUnit, 0f);
                    actor.HealthFill.transform.localPosition = actor.HealthBack.transform.localPosition;
                    actor.HealthBack.transform.localScale = new Vector3(width, 3f, 1f);
                    actor.HealthFill.transform.localScale = new Vector3(width * Mathf.Clamp01(e.Hp / Mathf.Max(1f, e.MaxHp)), 3f, 1f);
                    actor.HealthBack.sortingOrder = order + 4;
                    actor.HealthFill.sortingOrder = order + 5;
                }
                actor.Warning.enabled = e.Kind == EnemyKind.Brute && (e.Telegraph > 0f || (e.Timer < .5f && e.Charging <= 0f));
                actor.Warning.sortingOrder = order - 1;
                actor.Warning.color = new Color(.84f, .48f, .37f, ReducedMotion ? .8f : .55f + .3f * Mathf.Abs(Mathf.Sin(time * 16f)));
            }

            while (projectilePool.Count < game.Projectiles.Count) projectilePool.Add(MakeActor("Projectile", shotLight, scenery, false));
            for (int i = 0; i < projectilePool.Count; i++)
            {
                Actor actor = projectilePool[i];
                bool visible = i < game.Projectiles.Count;
                if (actor.Root.activeSelf != visible) actor.Root.SetActive(visible);
                if (!visible) continue;
                ProjectileState shot = game.Projectiles[i];
                actor.Root.transform.position = ToWorld(shot.X, shot.Y);
                actor.Body.sprite = shot.Enemy ? shotDark : shotLight;
                actor.Body.transform.localScale = Vector3.one * Mathf.Max(.5f, shot.Radius / 4f);
                actor.Body.sortingOrder = 900;
                actor.Glow.sortingOrder = 899;
                actor.Glow.transform.localScale = Vector3.one * (shot.Enemy ? .48f : .4f);
                actor.Glow.color = shot.Enemy ? new Color(.84f, .48f, .37f, .23f) : new Color(.49f, .74f, .71f, .28f);
            }

            bool exitOpen = game.State == RunState.Reward || game.State == RunState.Won;
            portalRenderer.color = exitOpen ? Color.white : new Color(.55f, .65f, .58f, .58f);
            portalGlow.color = new Color(.49f, .74f, .71f, exitOpen ? .42f : .1f);
            for (int i = 0; i < torches.Count; i++)
            {
                LightSprite torch = torches[i];
                float flicker = ReducedMotion ? 1f : 1f + Mathf.Sin(time * 8.5f + torch.Phase) * .09f;
                torch.Flame.transform.localScale = new Vector3(1f, flicker, 1f);
                torch.Glow.color = new Color(.84f, .57f, .29f, .19f * flicker);
            }
        }

        private static int Sort(float y) { return 100 + Mathf.RoundToInt(y); }

        private SpriteRenderer MakeRenderer(string objectName, Transform parent, Sprite sprite, int order)
        {
            var child = new GameObject(objectName);
            child.transform.SetParent(parent, false);
            var renderer = child.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = order;
            if (spriteMaterial != null) renderer.sharedMaterial = spriteMaterial;
            return renderer;
        }

        private Actor MakeActor(string objectName, Sprite bodySprite, Transform parent, bool withShadow)
        {
            var actor = new Actor { Root = new GameObject(objectName) };
            actor.Root.transform.SetParent(parent, false);
            actor.Body = MakeRenderer("Body", actor.Root.transform, bodySprite, 100);
            actor.Glow = MakeRenderer("Light", actor.Root.transform, softGlow, 98);
            actor.Glow.transform.localPosition = new Vector3(0f, .22f, 0f);
            actor.Shadow = MakeRenderer("Ground shadow", actor.Root.transform, shadeShadow, 99);
            actor.Shadow.enabled = withShadow;
            actor.HealthBack = MakeRenderer("Vitality track", actor.Root.transform, whitePixel, 104);
            actor.HealthBack.color = new Color(.04f, .08f, .08f, .9f);
            actor.HealthFill = MakeRenderer("Vitality", actor.Root.transform, whitePixel, 105);
            actor.HealthFill.color = Ember;
            actor.HealthBack.enabled = actor.HealthFill.enabled = false;
            actor.Warning = MakeRenderer("Charge warning", actor.Root.transform, warningRing, 99);
            actor.Warning.transform.localPosition = new Vector3(0f, .08f, 0f);
            actor.Warning.enabled = false;
            return actor;
        }

        private void SyncObstacles()
        {
            while (obstaclePool.Count < game.Obstacles.Count)
                obstaclePool.Add(MakeRenderer("Crypt architecture", scenery, pillar, 200));
            for (int i = 0; i < obstaclePool.Count; i++)
            {
                var renderer = obstaclePool[i];
                bool active = i < game.Obstacles.Count;
                renderer.gameObject.SetActive(active);
                if (!active) continue;
                var obstacle = game.Obstacles[i];
                renderer.sprite = obstacle.Variant % 2 == 0 ? pillar : tomb;
                renderer.transform.position = ToWorld(obstacle.X + obstacle.Width * .5f, obstacle.Y + obstacle.Height);
                renderer.sortingOrder = Sort(obstacle.Y + obstacle.Height * .7f);
            }
        }

        private void CreateTorches()
        {
            float[,] places = { {156, 89}, {354, 89}, {606, 89}, {804, 89}, {53, 291}, {907, 291}, {161, 551}, {799, 551} };
            for (int i = 0; i < places.GetLength(0); i++)
            {
                var root = new GameObject("Warm torch").transform;
                root.SetParent(scenery, false);
                root.position = ToWorld(places[i, 0], places[i, 1]);
                var light = new LightSprite { Root = root, Phase = i * 1.73f };
                light.Flame = MakeRenderer("Flame", root, flame, -10);
                light.Glow = MakeRenderer("Torch halo", root, softGlow, -12);
                light.Glow.transform.localScale = Vector3.one * 2.5f;
                light.Glow.transform.localPosition = new Vector3(0f, .2f, 0f);
                torches.Add(light);
            }
        }

        private void BuildFloor()
        {
            const int width = 480, height = 300;
            var paint = new Pixels(width, height);
            var rng = new System.Random(StableHash(game.Seed) ^ game.Room * 6151);
            paint.Fill(Hex("10191c"));
            paint.Rect(22, 30, 436, 253, Hex("131d1e"));
            for (int y = 49; y < 265; y += 24)
            {
                for (int x = 34; x < 447; x += 24)
                {
                    int shade = rng.Next(0, 5);
                    Color32 tile = new Color32((byte)(34 + shade), (byte)(46 + shade), (byte)(42 + shade), 255);
                    paint.Rect(x, y, 23, 23, tile);
                    paint.Rect(x, y, 23, 1, Hex("34413a"));
                    paint.Rect(x, y, 1, 23, Hex("2e3c35"));
                    paint.Rect(x + 2, y + 21, 21, 1, Hex("1c2925"));
                    if (rng.NextDouble() > .48)
                    {
                        int crackX = x + rng.Next(5, 17), crackY = y + rng.Next(4, 15);
                        paint.Rect(crackX, crackY, 1, 4, Hex("192922"));
                        paint.Rect(crackX + 1, crackY + 3, 3, 1, Hex("192922"));
                        paint.Rect(crackX + 3, crackY + 4, 1, 3, Hex("192922"));
                    }
                    if (rng.NextDouble() > .8)
                    {
                        int mossX = x + rng.Next(1, 16), mossY = y + rng.Next(1, 16);
                        paint.Rect(mossX, mossY, 5, 2, Hex("3b4b35"));
                        paint.Rect(mossX + 2, mossY - 1, 3, 4, Hex("33442f"));
                    }
                }
            }
            // Weathered central ritual stones remain deliberately quiet beneath combat.
            paint.Ring(240, 161, 49, 30, 1, Hex("405044"));
            paint.Ring(240, 161, 42, 25, 1, Hex("34463b"));
            paint.Line(205, 161, 275, 161, Hex("36483d"));
            paint.Line(240, 139, 240, 183, Hex("36483d"));
            for (int r = 0; r < 8; r++)
            {
                float angle = r * Mathf.PI / 4f;
                int rx = 240 + Mathf.RoundToInt(Mathf.Cos(angle) * 48f);
                int ry = 161 + Mathf.RoundToInt(Mathf.Sin(angle) * 30f);
                paint.Rect(rx - 1, ry - 2, 2, 4, Hex("5c6350"));
            }

            // Layered masonry makes the upper wall taller than the floor collision edge.
            for (int row = 0; row < 3; row++)
            {
                for (int x = 22; x < 458; x += 31)
                {
                    int offset = row % 2 == 0 ? 0 : -15;
                    int y = 27 + row * 9;
                    paint.Rect(x + offset, y, 29, 8, Hex(row == 0 ? "334039" : "2a3631"));
                    paint.Rect(x + offset, y, 29, 1, Hex("4a5547"));
                }
            }
            paint.Rect(22, 52, 436, 3, Hex("0c1615"));
            paint.Rect(22, 55, 436, 2, Hex("354138"));
            paint.Rect(22, 55, 12, 210, Hex("28362f"));
            paint.Rect(446, 55, 12, 210, Hex("28362f"));
            for (int y = 58; y < 263; y += 16)
            {
                paint.Rect(23, y, 9, 1, Hex("4a5547"));
                paint.Rect(448, y, 9, 1, Hex("4a5547"));
                paint.Rect(32, y, 3, 15, Hex("14221d"));
                paint.Rect(445, y, 3, 15, Hex("14221d"));
            }
            paint.Rect(22, 264, 436, 3, Hex("0c1615"));
            for (int x = 22; x < 458; x += 31)
            {
                paint.Rect(x, 267, 29, 12, Hex("344137"));
                paint.Rect(x, 267, 29, 2, Hex("515d4c"));
                paint.Rect(x, 279, 29, 3, Hex("17231e"));
            }
            // Recessed alcoves, north doorway, and creeping ivy.
            for (int x = 96; x < 433; x += 96)
            {
                paint.Rect(x, 31, 18, 20, Hex("0f1a19"));
                paint.Rect(x + 2, 32, 14, 17, Hex("17231e"));
                paint.Rect(x - 2, 49, 22, 3, Hex("4b5647"));
                paint.Rect(x + 5, 29, 8, 2, Hex("495344"));
            }
            paint.Rect(223, 26, 34, 30, Hex("10191c"));
            paint.Rect(220, 30, 3, 27, Hex("64654e"));
            paint.Rect(257, 30, 3, 27, Hex("64654e"));
            paint.Rect(225, 23, 30, 3, Hex("64654e"));
            paint.Rect(228, 20, 24, 3, Hex("4d5746"));
            for (int side = 0; side < 2; side++)
            {
                int x = side == 0 ? 42 : 436;
                for (int j = 0; j < 54; j++)
                {
                    int y = 54 + j * 3;
                    int leafX = x + (int)(Math.Sin(j * .7) * 4);
                    paint.Rect(leafX, y, 2, 4, Hex("354831"));
                    if (j % 3 == 0) paint.Rect(leafX - 3, y + 2, 7, 2, Hex("44573a"));
                }
            }
            // Small remains and stone dust, all outside the central spawn.
            for (int i = 0; i < 85; i++)
            {
                int x = rng.Next(39, 441), y = rng.Next(60, 257);
                paint.Rect(x, y, rng.Next(1, 3), 1, i % 7 == 0 ? Hex("76765c") : Hex("4a5543"));
            }
            for (int i = 0; i < 4; i++)
            {
                int x = 51 + i * 115, y = i % 2 == 0 ? 243 : 70;
                paint.Ellipse(x, y, 3, 2, Hex("79765f"));
                paint.Rect(x - 2, y, 1, 1, Hex("18211e"));
                paint.Rect(x + 1, y, 1, 1, Hex("18211e"));
                paint.Rect(x - 1, y + 2, 2, 1, Hex("79765f"));
            }
            if (floorSprite != null) Destroy(floorSprite);
            if (floorTexture != null) Destroy(floorTexture);
            floorTexture = paint.Texture("UMBRA crypt floor");
            floorSprite = Sprite.Create(floorTexture, new Rect(0, 0, width, height), new Vector2(.5f, .5f), 24f);
            floorSprite.name = "UMBRA crypt floor";
            floorRenderer.sprite = floorSprite;
        }

        private void BuildAtlas()
        {
            var keeper = new Pixels(34, 44);
            keeper.Rect(10, 35, 6, 8, Hex("111d1b")); keeper.Rect(23, 35, 5, 8, Hex("111d1b"));
            keeper.Rect(8, 19, 21, 20, Hex("253f3d")); keeper.Rect(6, 25, 25, 15, Hex("365752"));
            keeper.Rect(10, 22, 7, 16, Hex("648d83")); keeper.Rect(8, 31, 5, 10, Hex("648d83"));
            keeper.Rect(18, 24, 10, 14, Hex("3a5e57")); keeper.Rect(16, 21, 3, 17, Hex("789b8a"));
            keeper.Rect(11, 9, 16, 14, Hex("192d2b")); keeper.Rect(9, 9, 20, 7, Hex("5e8c80"));
            keeper.Rect(12, 5, 13, 6, Hex("74a095")); keeper.Rect(10, 9, 4, 10, Hex("648d83"));
            keeper.Rect(13, 14, 3, 2, Bone); keeper.Rect(22, 14, 3, 2, Bone);
            keeper.Rect(13, 20, 11, 4, Hex("172b29")); keeper.Rect(27, 23, 4, 4, Brass);
            keeper.Rect(29, 5, 2, 27, Brass); keeper.Rect(28, 3, 4, 19, Hex("b7d4be"));
            keeper.Rect(26, 22, 8, 2, Brass); keeper.Rect(2, 26, 7, 10, Hex("b19356"));
            keeper.Rect(3, 27, 5, 7, Teal); keeper.Rect(4, 28, 2, 4, Hex("d9f0d7"));
            keeper.Rect(4, 23, 3, 3, Brass);
            guardian = Register(keeper, "Light keeper", true);

            var shade = new Pixels(30, 36);
            shade.Rect(9, 5, 13, 8, Hex("40414e")); shade.Rect(6, 11, 19, 17, Hex("282e38"));
            shade.Rect(3, 20, 25, 10, Hex("212931")); shade.Rect(6, 27, 20, 6, Hex("1b252d"));
            shade.Rect(4, 31, 6, 4, Hex("2f3740")); shade.Rect(19, 31, 7, 3, Hex("2f3740"));
            shade.Rect(10, 13, 4, 3, Ember); shade.Rect(19, 13, 4, 3, Ember);
            shade.Rect(11, 22, 12, 1, Hex("42414c")); shade.Rect(0, 21, 5, 3, Hex("4f4a55"));
            enemies[(int)EnemyKind.Shade] = Register(shade, "Wandering shade", true);

            var wisp = new Pixels(30, 38);
            wisp.Rect(12, 4, 7, 3, Hex("b5b2c8")); wisp.Rect(8, 8, 15, 6, Hex("8b89a6"));
            wisp.Rect(5, 13, 21, 12, Hex("605d7f")); wisp.Rect(8, 23, 15, 7, Hex("45445e"));
            wisp.Rect(11, 29, 9, 5, Hex("34374b")); wisp.Rect(15, 33, 3, 3, Hex("252e3d"));
            wisp.Rect(9, 16, 4, 3, Hex("e3d9e3")); wisp.Rect(19, 16, 4, 3, Hex("e3d9e3"));
            wisp.Rect(3, 14, 4, 8, Hex("8b89a6")); wisp.Rect(24, 11, 3, 8, Hex("8b89a6"));
            enemies[(int)EnemyKind.Wisp] = Register(wisp, "Will-o-wisp", true);

            var brute = new Pixels(46, 52);
            brute.Rect(10, 42, 9, 9, Hex("242d27")); brute.Rect(29, 42, 9, 9, Hex("242d27"));
            brute.Rect(7, 21, 33, 23, Hex("5b6150")); brute.Rect(4, 23, 9, 18, Hex("74775e"));
            brute.Rect(35, 22, 9, 19, Hex("74775e")); brute.Rect(12, 20, 7, 21, Hex("797c62"));
            brute.Rect(17, 4, 17, 17, Hex("6a705a")); brute.Rect(14, 7, 23, 7, Hex("8a8a69"));
            brute.Rect(18, 12, 5, 3, Ember); brute.Rect(29, 12, 5, 3, Ember);
            brute.Rect(21, 19, 9, 2, Hex("1f2c24")); brute.Rect(17, 29, 15, 2, Hex("333f31"));
            brute.Rect(27, 25, 2, 14, Hex("333f31")); brute.Rect(0, 34, 7, 9, Hex("545b49"));
            enemies[(int)EnemyKind.Brute] = Register(brute, "Stone breaker", true);

            var boss = new Pixels(80, 82);
            boss.Rect(17, 66, 15, 15, Hex("1a2028")); boss.Rect(50, 66, 15, 15, Hex("1a2028"));
            boss.Rect(14, 34, 52, 37, Hex("34313a")); boss.Rect(9, 43, 62, 24, Hex("262831"));
            boss.Rect(3, 37, 14, 27, Hex("52434a")); boss.Rect(64, 35, 13, 29, Hex("52434a"));
            boss.Rect(21, 38, 9, 29, Hex("5c4447")); boss.Rect(50, 38, 9, 29, Hex("5c4447"));
            boss.Rect(27, 20, 28, 20, Hex("6b5552")); boss.Rect(22, 14, 38, 15, Hex("45404a"));
            boss.Rect(27, 25, 8, 4, Ember); boss.Rect(46, 25, 8, 4, Ember);
            boss.Rect(31, 34, 19, 4, Hex("18232a")); boss.Rect(24, 10, 35, 5, Brass);
            boss.Rect(25, 4, 5, 9, Brass); boss.Rect(38, 0, 6, 13, Brass); boss.Rect(53, 4, 5, 9, Brass);
            boss.Rect(36, 45, 10, 14, Hex("aa6653")); boss.Rect(39, 48, 4, 7, Hex("edbe87"));
            boss.Rect(0, 62, 9, 9, Hex("a08a6b")); boss.Rect(71, 61, 9, 9, Hex("a08a6b"));
            enemies[(int)EnemyKind.Boss] = Register(boss, "The Devourer", true);

            var column = new Pixels(58, 78);
            column.Ellipse(29, 72, 27, 5, Hex("12201a")); column.Rect(5, 64, 48, 8, Hex("4e5948"));
            column.Rect(8, 59, 42, 7, Hex("72745a")); column.Rect(13, 15, 32, 44, Hex("4e5a4a"));
            column.Rect(14, 15, 6, 44, Hex("73785d")); column.Rect(22, 18, 3, 39, Hex("364635"));
            column.Rect(35, 18, 5, 39, Hex("364635")); column.Rect(9, 10, 40, 8, Hex("7c7c61"));
            column.Rect(6, 6, 46, 5, Hex("5f6953")); column.Rect(11, 3, 36, 4, Hex("808066"));
            column.Rect(8, 11, 40, 1, Hex("a19b76")); column.Rect(11, 62, 38, 1, Hex("a19b76"));
            column.Rect(33, 12, 2, 14, Hex("2d3b2f")); column.Rect(30, 25, 5, 2, Hex("2d3b2f"));
            column.Rect(9, 57, 11, 3, Hex("536741")); column.Rect(5, 65, 9, 2, Hex("536741"));
            pillar = Register(column, "Old pillar", true);

            var coffin = new Pixels(58, 68);
            coffin.Ellipse(29, 62, 28, 5, Hex("12201a")); coffin.Rect(3, 33, 52, 28, Hex("434e40"));
            coffin.Rect(5, 25, 48, 30, Hex("68705a")); coffin.Rect(9, 20, 40, 29, Hex("7e8064"));
            coffin.Rect(12, 23, 34, 23, Hex("5d6752")); coffin.Rect(27, 25, 4, 18, Hex("a39b72"));
            coffin.Rect(21, 31, 16, 3, Hex("a39b72")); coffin.Rect(5, 53, 48, 3, Hex("313e31"));
            coffin.Rect(7, 26, 3, 24, Hex("929173")); coffin.Rect(46, 29, 3, 17, Hex("3e503b"));
            coffin.Rect(3, 52, 11, 5, Hex("3a5131")); tomb = Register(coffin, "Stone sarcophagus", true);

            var shadow = new Pixels(40, 14);
            shadow.Ellipse(20, 7, 19, 6, new Color32(4, 10, 10, 100));
            shadeShadow = Register(shadow, "Ground shadow", false);

            var glow = new Pixels(96, 96);
            for (int y = 0; y < 96; y++) for (int x = 0; x < 96; x++)
            {
                float distance = Mathf.Sqrt((x - 47.5f) * (x - 47.5f) + (y - 47.5f) * (y - 47.5f)) / 47.5f;
                byte alpha = (byte)(Mathf.Pow(Mathf.Clamp01(1f - distance), 2f) * 255f);
                glow.Set(x, y, new Color32(255, 255, 255, alpha));
            }
            softGlow = Register(glow, "Soft pixel glow", false);

            var arrow = new Pixels(12, 12); arrow.Ellipse(6, 6, 4, 4, Teal); arrow.Rect(4, 4, 5, 5, Bone);
            arrow.Rect(5, 5, 3, 3, Hex("e8f5de")); shotLight = Register(arrow, "Beacon bolt", false);
            var hostile = new Pixels(12, 12); hostile.Ellipse(6, 6, 5, 5, Hex("6c3c41"));
            hostile.Ellipse(6, 6, 3, 3, Ember); hostile.Rect(5, 5, 3, 3, Hex("f0b77f"));
            shotDark = Register(hostile, "Shadow bolt", false);
            var pixel = new Pixels(1, 1); pixel.Set(0, 0, new Color32(255, 255, 255, 255));
            whitePixel = Register(pixel, "White pixel", false, new Vector2(0f, .5f));

            var torch = new Pixels(14, 30);
            torch.Rect(5, 19, 4, 10, Hex("65543b")); torch.Rect(3, 17, 8, 5, Brass);
            torch.Rect(4, 6, 7, 12, Ember); torch.Rect(6, 2, 4, 15, Hex("e5ad61"));
            torch.Rect(5, 9, 3, 9, Hex("f4d69a")); flame = Register(torch, "Torch flame", true);
            var ring = new Pixels(80, 40); ring.Ring(40, 20, 38, 17, 1, new Color32(255, 255, 255, 255));
            warningRing = Register(ring, "Charge tell", false);
            var gate = new Pixels(64, 60);
            gate.Rect(5, 8, 5, 49, Brass); gate.Rect(54, 8, 5, 49, Brass); gate.Rect(10, 4, 44, 5, Brass);
            gate.Rect(16, 0, 32, 4, Hex("8f936a")); gate.Rect(10, 9, 44, 48, Hex("132a29"));
            for (int x = 16; x < 53; x += 8) gate.Rect(x, 12, 2, 43, Hex("386057"));
            gate.Rect(29, 22, 6, 17, Teal); gate.Rect(25, 27, 14, 5, Teal);
            gate.Rect(5, 56, 54, 3, Hex("78816a")); portal = Register(gate, "The northern gate", true);
        }

        private Sprite Register(Pixels pixels, string spriteName, bool foot, Vector2? customPivot = null)
        {
            var texture = pixels.Texture(spriteName);
            textures.Add(texture);
            var sprite = Sprite.Create(texture, new Rect(0, 0, pixels.Width, pixels.Height),
                customPivot ?? (foot ? new Vector2(.5f, 0f) : new Vector2(.5f, .5f)), PixelsPerUnit);
            sprite.name = spriteName;
            sprites.Add(sprite);
            return sprite;
        }

        private static Color32 Hex(string hex)
        {
            return new Color32(Convert.ToByte(hex.Substring(0, 2), 16), Convert.ToByte(hex.Substring(2, 2), 16), Convert.ToByte(hex.Substring(4, 2), 16), 255);
        }

        private static int StableHash(string value)
        {
            unchecked
            {
                uint hash = 2166136261;
                if (value != null) for (int i = 0; i < value.Length; i++) hash = (hash ^ value[i]) * 16777619;
                return (int)hash;
            }
        }

        private void OnDestroy()
        {
            if (scenery != null) Destroy(scenery.gameObject);
            if (floorSprite != null) Destroy(floorSprite);
            if (floorTexture != null) Destroy(floorTexture);
            for (int i = 0; i < sprites.Count; i++) if (sprites[i] != null) Destroy(sprites[i]);
            for (int i = 0; i < textures.Count; i++) if (textures[i] != null) Destroy(textures[i]);
            if (spriteMaterial != null) Destroy(spriteMaterial);
        }

        /// <summary>A top-left-origin raster painter used only while making room/art textures.</summary>
        private sealed class Pixels
        {
            internal readonly int Width, Height;
            private readonly Color32[] data;
            internal Pixels(int width, int height) { Width = width; Height = height; data = new Color32[width * height]; }
            internal void Fill(Color32 color) { for (int i = 0; i < data.Length; i++) data[i] = color; }
            internal void Set(int x, int y, Color32 color)
            {
                if (x >= 0 && x < Width && y >= 0 && y < Height) data[(Height - 1 - y) * Width + x] = color;
            }
            internal void Rect(int x, int y, int width, int height, Color32 color)
            {
                int right = Math.Min(Width, x + width), bottom = Math.Min(Height, y + height);
                for (int py = Math.Max(0, y); py < bottom; py++) for (int px = Math.Max(0, x); px < right; px++) Set(px, py, color);
            }
            internal void Ellipse(int cx, int cy, int rx, int ry, Color32 color)
            {
                for (int y = -ry; y <= ry; y++) for (int x = -rx; x <= rx; x++)
                    if ((float)x * x / (rx * rx) + (float)y * y / (ry * ry) <= 1f) Set(cx + x, cy + y, color);
            }
            internal void Ring(int cx, int cy, int rx, int ry, int thickness, Color32 color)
            {
                int innerX = Math.Max(1, rx - thickness), innerY = Math.Max(1, ry - thickness);
                for (int y = -ry; y <= ry; y++) for (int x = -rx; x <= rx; x++)
                {
                    float outer = (float)x * x / (rx * rx) + (float)y * y / (ry * ry);
                    float inner = (float)x * x / (innerX * innerX) + (float)y * y / (innerY * innerY);
                    if (outer <= 1f && inner >= 1f) Set(cx + x, cy + y, color);
                }
            }
            internal void Line(int x0, int y0, int x1, int y1, Color32 color)
            {
                int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
                int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
                int error = dx + dy;
                while (true)
                {
                    Set(x0, y0, color);
                    if (x0 == x1 && y0 == y1) break;
                    int doubled = 2 * error;
                    if (doubled >= dy) { error += dy; x0 += sx; }
                    if (doubled <= dx) { error += dx; y0 += sy; }
                }
            }
            internal Texture2D Texture(string textureName)
            {
                var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false)
                {
                    name = textureName,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                texture.SetPixels32(data);
                texture.Apply(false, true);
                return texture;
            }
        }
    }
}
