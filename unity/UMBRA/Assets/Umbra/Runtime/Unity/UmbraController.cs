using System;
using Umbra.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Umbra.Unity
{
    /// <summary>Connects the deterministic game, Unity presentation and platform-local saves.</summary>
    public sealed class UmbraController : MonoBehaviour
    {
        public Simulation Game { get; private set; }
        public ProgressStore Progress { get; private set; }
        public bool SoundEnabled { get; private set; }
        public bool ReducedMotion { get; private set; }
        public string Feedback { get; private set; }
        public Vector2 TouchMovement { get; set; }

        private Camera gameCamera;
        private UmbraRenderer scene;
        private UmbraHud hud;
        private AudioSource audioSource;
        private AudioClip attackCue, hitCue, rewardCue;
        private bool dashRequested, pendingSave;
        private string runId;
        private int lastKills, screenWidth, screenHeight;
        private float lastHp;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindObjectOfType<UmbraController>() != null) return;
            new GameObject("UMBRA Runtime").AddComponent<UmbraController>();
        }

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            Progress = new ProgressStore();
            Feedback = Progress.Status;
            try { ReducedMotion = PlayerPrefs.GetInt("umbra.unity.reduced-motion", 0) == 1; }
            catch (Exception) { ReducedMotion = false; }
            Game = new Simulation();
            Game.Ended += FinishRun;
            Game.Notice += OnNotice;

            gameCamera = Camera.main;
            if (gameCamera == null)
            {
                var cameraObject = new GameObject("UMBRA Camera");
                cameraObject.transform.SetParent(transform);
                gameCamera = cameraObject.AddComponent<Camera>();
                cameraObject.tag = "MainCamera";
            }
            gameCamera.transform.position = new Vector3(0, 0, -10);
            gameCamera.orthographic = true;
            gameCamera.clearFlags = CameraClearFlags.SolidColor;
            gameCamera.backgroundColor = new Color32(16, 25, 28, 255);
            gameCamera.rect = new Rect(0, .105f, 1, .79f);
            if (FindObjectOfType<AudioListener>() == null) gameCamera.gameObject.AddComponent<AudioListener>();
            ConfigureCamera();

            if (FindObjectOfType<EventSystem>() == null)
            {
                var events = new GameObject("UMBRA UI Events");
                events.transform.SetParent(transform);
                events.AddComponent<EventSystem>();
                events.AddComponent<StandaloneInputModule>();
            }
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0;
            audioSource.volume = .18f;
            attackCue = CreateTone("Luz", 420, .065f);
            hitCue = CreateTone("Impacto", 110, .12f);
            rewardCue = CreateTone("Reliquia", 660, .18f);
            scene = gameObject.AddComponent<UmbraRenderer>();
            scene.Initialize(Game, gameCamera);
            scene.ReducedMotion = ReducedMotion;
            hud = gameObject.AddComponent<UmbraHud>();
            hud.Initialize(this);
        }

        private void Update()
        {
            if (screenWidth != Screen.width || screenHeight != Screen.height) ConfigureCamera();
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (hud.IsAuxiliaryMenuOpen) hud.CloseAuxiliaryMenu();
                else TogglePause();
            }
            bool menuInput = hud.IsAuxiliaryMenuOpen;
            GameObject selected = EventSystem.current == null ? null : EventSystem.current.currentSelectedGameObject;
            bool keyboardUI = selected != null && selected.GetComponent<Selectable>() != null;
            float horizontal = TouchMovement.x, vertical = -TouchMovement.y;
            if (!keyboardUI)
            {
                horizontal += (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1 : 0)
                    - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1 : 0);
                vertical += (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1 : 0)
                    - (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1 : 0);
            }
            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            bool manual = Input.GetMouseButton(0) && Input.touchCount == 0 && !overUI;
            Vector3 world = gameCamera.ScreenToWorldPoint(Input.mousePosition);
            bool dash = dashRequested || (!keyboardUI && Input.GetKeyDown(KeyCode.Space));
            dashRequested = false;
            if (menuInput && Game.State == RunState.Playing) Game.Pause();
            Game.Step(Time.unscaledDeltaTime, horizontal, vertical, dash,
                world.x * 48 + 480, 300 - world.y * 48, manual);
            if (Game.Kills > lastKills) PlayCue(attackCue);
            else if (Game.Player.Hp < lastHp) PlayCue(hitCue);
            lastKills = Game.Kills;
            lastHp = Game.Player.Hp;
            scene.RenderFrame(Game.Elapsed);
        }

        public void StartRun()
        {
            if (Game.State == RunState.Playing || Game.State == RunState.Reward) return;
            if (Game.State == RunState.Paused) { Game.Resume(); return; }
            runId = Guid.NewGuid().ToString("N");
            TouchMovement = Vector2.zero;
            lastKills = 0;
            Game.StartRun(Progress.Profile.Data.Vitality, Progress.Profile.Data.Power);
            lastHp = Game.Player.Hp;
        }

        public void TogglePause()
        {
            if (Game == null) return;
            if (Game.State == RunState.Playing) { Game.Pause(); TouchMovement = Vector2.zero; }
            else if (Game.State == RunState.Paused) Game.Resume();
        }

        public void SelectRelic(string id)
        {
            if (Game.ChooseRelic(id)) PlayCue(rewardCue);
        }

        public void Purchase(UpgradeKind kind)
        {
            string message;
            if (!Progress.Profile.TryUpgrade(kind, out message)) { Feedback = message; return; }
            Feedback = message;
            Persist();
        }

        public void RetrySave() { Persist(); }
        public void RequestDash() { dashRequested = true; }
        public void SetSound(bool enabled) { SoundEnabled = enabled; if (enabled) PlayCue(rewardCue); }

        public void SetReducedMotion(bool enabled)
        {
            ReducedMotion = enabled;
            scene.ReducedMotion = enabled;
            try { PlayerPrefs.SetInt("umbra.unity.reduced-motion", enabled ? 1 : 0); PlayerPrefs.Save(); }
            catch (Exception) { Feedback = "El ajuste de movimiento se conservará solo durante esta sesión."; }
        }

        private void FinishRun(bool won)
        {
            TouchMovement = Vector2.zero;
            if (!Progress.Profile.RecordRun(runId, won, Game.Room, Game.Kills, Game.Elapsed, Game.Shards))
            {
                Feedback = "Esta incursión ya fue registrada o contiene datos inválidos. No se duplicaron sus fragmentos.";
                return;
            }
            Feedback = (won ? "La cripta vuelve a ver el amanecer. " : "La luz regresará al faro. ") + "+" + Game.Shards + " fragmentos.";
            Persist();
            PlayCue(won ? rewardCue : hitCue);
        }

        private void Persist()
        {
            string message;
            pendingSave = !Progress.Save(out message);
            if (pendingSave) Feedback = message;
            else if (Feedback != null && Feedback.Contains("guardar")) Feedback = message;
        }

        private void OnNotice(string message) { Feedback = message; }

        private void ConfigureCamera()
        {
            screenWidth = Screen.width;
            screenHeight = Screen.height;
            float aspect = screenWidth / Mathf.Max(1f, screenHeight * .79f);
            gameCamera.aspect = aspect;
            gameCamera.orthographicSize = Mathf.Max(6.55f, 10.2f / aspect);
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused && Game != null) { Game.Pause(); TouchMovement = Vector2.zero; }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && Game != null) { Game.Pause(); TouchMovement = Vector2.zero; }
            if (paused && pendingSave) Persist();
        }

        private void OnApplicationQuit() { if (pendingSave) Persist(); }

        private void PlayCue(AudioClip clip)
        {
            if (SoundEnabled && audioSource != null && clip != null) audioSource.PlayOneShot(clip);
        }

        private static AudioClip CreateTone(string name, float frequency, float seconds)
        {
            const int rate = 22050;
            int count = Mathf.CeilToInt(seconds * rate);
            var samples = new float[count];
            for (int i = 0; i < count; i++)
            {
                float envelope = Mathf.Min(1, i / (rate * .008f)) * (1 - i / (float)count);
                samples[i] = Mathf.Sin(2 * Mathf.PI * frequency * i / rate) * envelope * .6f;
            }
            var clip = AudioClip.Create(name, count, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private void OnDestroy()
        {
            if (Game != null) { Game.Ended -= FinishRun; Game.Notice -= OnNotice; }
            if (attackCue != null) Destroy(attackCue);
            if (hitCue != null) Destroy(hitCue);
            if (rewardCue != null) Destroy(rewardCue);
        }
    }
}
