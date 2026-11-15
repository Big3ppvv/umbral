using System;
using System.Collections.Generic;
using Umbra.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Umbra.Unity
{
    /// <summary>Runtime UGUI shell. Simulation and persistence stay in the controller.</summary>
    public sealed class UmbraHud : MonoBehaviour
    {
        private enum AuxiliaryMenu { None, Help, Refuge }

        private static readonly Color Slate = Hex(0x10191c);
        private static readonly Color Surface = Hex(0x172225);
        private static readonly Color Brass = Hex(0xd5b476);
        private static readonly Color Parchment = Hex(0xe9dcc0);
        private static readonly Color Teal = Hex(0x7dbcb5);
        private static readonly Color Muted = Hex(0xa0aea7);
        private static readonly Color Danger = Hex(0xd77b5e);
        private static readonly string[] Rooms = { "El umbral", "Los ecos", "El osario", "La vigilia", "El abismo", "El Devorador" };

        private UmbraController controller;
        private Font font;
        private RectTransform canvasRect, header, footer, mainCard, rewardCard, helpCard, refugeCard;
        private CanvasGroup headerActions;
        private Text brand, tagline, roomLabel, seedLabel, hpLabel, statsLabel, controlsLabel;
        private Text mainEyebrow, mainTitle, mainSummary, mainFeedback;
        private Text refugeWallet, refugeHistory, refugeFeedback, helpFeedback;
        private Text vitalityLevel, vitalityReason, powerLevel, powerReason;
        private Image hpFill;
        private RectTransform hpTrack;
        private GameObject mainLayer, rewardLayer, helpLayer, refugeLayer, activeLayer, touchLayer;
        private Button mainAction, vitalityButton, powerButton, helpClose, refugeClose;
        private Text mainActionLabel, soundLabel, motionLabel;
        private readonly Button[] choices = new Button[3];
        private readonly Text[] choiceTitle = new Text[3], choiceDescription = new Text[3], choiceRarity = new Text[3];
        private readonly string[] choiceIds = new string[3];
        private readonly RectTransform[] choiceRects = new RectTransform[3];
        private RectTransform headerRoom, headerSeed, headerBrand, headerTagline;
        private RectTransform footerStats, footerControls, footerHpLabel;
        private RectTransform touchPad, touchDash;
        private AuxiliaryMenu auxiliary;
        private float nextRefresh;
        private float lastWidth = -1, lastHeight = -1;
        private bool up, down, left, right;
        private bool initialized;
        private string previousChoiceKey = "";

        public bool IsAuxiliaryMenuOpen { get { return auxiliary != AuxiliaryMenu.None; } }

        public void Initialize(UmbraController owner)
        {
            if (initialized) return;
            controller = owner;
#if UNITY_2023_1_OR_NEWER
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
#else
            font = Resources.GetBuiltinResource<Font>("Arial.ttf");
#endif
            var root = new GameObject("UMBRA interface", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 800);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = .5f;
            canvasRect = root.GetComponent<RectTransform>();
            BuildChrome();
            BuildMainMenu();
            BuildRewards();
            BuildHelp();
            BuildRefuge();
            BuildTouchControls();
            initialized = true;
            Refresh();
        }

        public void CloseAuxiliaryMenu()
        {
            auxiliary = AuxiliaryMenu.None;
            ClearTouch();
            Refresh();
        }

        private void Update()
        {
            if (!initialized) return;
            RefreshLayout();
            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + .1f;
                Refresh();
            }
            if (Input.GetKeyDown(KeyCode.Tab)) CycleFocus(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
        }

        private void BuildChrome()
        {
            header = Panel(canvasRect, "Header", WithAlpha(Slate, .97f), false);
            Place(header, new Vector2(0, 1), new Vector2(0, 1), 0, 0, 0, 96);
            header.anchorMax = Vector2.one;
            header.sizeDelta = new Vector2(0, 96);
            brand = Label(header, "UMBRA", 32, Brass, TextAnchor.UpperLeft);
            headerBrand = brand.rectTransform;
            Place(headerBrand, new Vector2(0, 1), new Vector2(0, 1), 26, -13, 220, 42);
            tagline = Label(header, "EL ÚLTIMO FARO", 10, Muted, TextAnchor.UpperLeft);
            headerTagline = tagline.rectTransform;
            Place(headerTagline, new Vector2(0, 1), new Vector2(0, 1), 28, -57, 230, 20);
            roomLabel = Label(header, "LA CRIPTA OLVIDADA", 18, Parchment, TextAnchor.MiddleCenter);
            headerRoom = roomLabel.rectTransform;
            seedLabel = Label(header, "SEIS SALAS · UNA ÚLTIMA LUZ", 11, Muted, TextAnchor.MiddleCenter);
            headerSeed = seedLabel.rectTransform;
            var tools = Panel(header, "Actions", Color.clear, false);
            tools.gameObject.AddComponent<CanvasGroup>();
            headerActions = tools.GetComponent<CanvasGroup>();
            Place(tools, new Vector2(1, 1), new Vector2(1, 1), -20, -19, 250, 46);
            MakeButton(tools, "Pausa", -88, 0, 76, 42, delegate { controller.TogglePause(); Refresh(); }, false);
            MakeButton(tools, "Refugio", 0, 0, 84, 42, delegate { OpenAuxiliary(AuxiliaryMenu.Refuge); }, false);
            MakeButton(tools, "Guía", 88, 0, 76, 42, delegate { OpenAuxiliary(AuxiliaryMenu.Help); }, false);

            footer = Panel(canvasRect, "Footer", WithAlpha(Slate, .97f), false);
            Place(footer, Vector2.zero, Vector2.zero, 0, 0, 0, 102);
            footer.anchorMax = new Vector2(1, 0);
            footer.sizeDelta = new Vector2(0, 102);
            hpLabel = Label(footer, "VITALIDAD  100 / 100", 14, Teal, TextAnchor.MiddleLeft);
            footerHpLabel = hpLabel.rectTransform;
            hpTrack = Panel(footer, "Vitality track", Hex(0x33433f), false);
            var fill = Panel(hpTrack, "Vitality", Teal, false);
            Fill(fill);
            hpFill = fill.GetComponent<Image>();
            statsLabel = Label(footer, "00:00 · 0 sombras · 0 fragmentos", 16, Parchment, TextAnchor.MiddleRight);
            footerStats = statsLabel.rectTransform;
            controlsLabel = Label(footer, "WASD / flechas: moverse    ·    Ratón + clic: apuntar    ·    ESPACIO: esquivar    ·    ESC: pausa", 12, Muted, TextAnchor.MiddleCenter);
            footerControls = controlsLabel.rectTransform;
        }

        private void BuildMainMenu()
        {
            mainLayer = ModalLayer("Run menu", out mainCard, 660, 454);
            mainEyebrow = CardLabel(mainCard, "EL FARO TE HA ELEGIDO", 11, Brass, 174, 560, 26);
            mainTitle = CardLabel(mainCard, "Una luz.\nOtra oportunidad.", 41, Parchment, 96, 560, 102);
            mainSummary = CardLabel(mainCard, "Desciende. Encuentra reliquias. Regresa más fuerte.", 15, Muted, 14, 550, 54);
            mainAction = MakeButton(mainCard, "Comenzar incursión", 0, -54, 300, 50, delegate
            {
                if (controller.Game.State == RunState.Paused) controller.TogglePause();
                else controller.StartRun();
                Refresh();
            }, true);
            mainActionLabel = mainAction.GetComponentInChildren<Text>();
            MakeButton(mainCard, "Refugio y mejoras", -132, -121, 230, 42, delegate { OpenAuxiliary(AuxiliaryMenu.Refuge); }, false);
            MakeButton(mainCard, "Cómo jugar", 132, -121, 230, 42, delegate { OpenAuxiliary(AuxiliaryMenu.Help); }, false);
            mainFeedback = CardLabel(mainCard, "", 12, Muted, -178, 570, 48);
        }

        private void BuildRewards()
        {
            rewardLayer = ModalLayer("Relic choice", out rewardCard, 840, 424);
            CardLabel(rewardCard, "UNA SALA MÁS CERCA DEL AMANECER", 10, Brass, 165, 740, 26);
            CardLabel(rewardCard, "La luz toma otra forma.", 32, Parchment, 118, 740, 48);
            CardLabel(rewardCard, "Elige una reliquia para esta incursión.", 14, Muted, 72, 740, 30);
            for (int i = 0; i < 3; i++)
            {
                int slot = i;
                var button = MakeButton(rewardCard, "", (i - 1) * 250, -55, 232, 224, delegate
                {
                    if (!string.IsNullOrEmpty(choiceIds[slot])) controller.SelectRelic(choiceIds[slot]);
                    Refresh();
                }, false);
                choices[i] = button;
                choiceRects[i] = button.GetComponent<RectTransform>();
                button.GetComponentInChildren<Text>().gameObject.SetActive(false);
                choiceRarity[i] = CardLabel(choiceRects[i], "RELIQUIA", 10, Teal, 79, 200, 25);
                choiceTitle[i] = CardLabel(choiceRects[i], "", 22, Parchment, 29, 200, 62);
                choiceDescription[i] = CardLabel(choiceRects[i], "", 14, Muted, -49, 194, 80);
            }
            CardLabel(rewardCard, "Recuperas 15 de vitalidad al avanzar a la siguiente sala.", 11, Muted, -184, 740, 24);
        }

        private void BuildHelp()
        {
            helpLayer = ModalLayer("Help", out helpCard, 730, 650);
            CardLabel(helpCard, "GUÍA DEL PORTADOR", 11, Brass, 275, 640, 24);
            CardLabel(helpCard, "Protege tu última luz.", 32, Parchment, 231, 640, 48);
            var instructions = Label(helpCard,
                "MOVERSE\nWASD o flechas. En móvil, usa los botones en pantalla.\n\n" +
                "ATACAR\nDisparas automáticamente a la sombra más cercana. Mantén clic para dirigir tus disparos hacia el ratón.\n\n" +
                "ESQUIVAR\nEspacio te impulsa y te vuelve invulnerable un instante. Evita las cargas rojas y los proyectiles del jefe.\n\n" +
                "CRECER\nLimpia seis salas y derrota al Devorador. Elige reliquias entre salas; conserva los fragmentos para mejorar en el Refugio.",
                15, Muted, TextAnchor.UpperLeft);
            Place(instructions.rectTransform, new Vector2(.5f, .5f), new Vector2(.5f, 1), 0, 188, 630, 328);
            var sound = MakeButton(helpCard, "Sonido: desactivado", -158, -175, 288, 44, delegate { controller.SetSound(!controller.SoundEnabled); Refresh(); }, false);
            soundLabel = sound.GetComponentInChildren<Text>();
            var motion = MakeButton(helpCard, "Movimiento reducido: no", 158, -175, 288, 44, delegate { controller.SetReducedMotion(!controller.ReducedMotion); Refresh(); }, false);
            motionLabel = motion.GetComponentInChildren<Text>();
            helpClose = MakeButton(helpCard, "Volver a la cripta", 0, -241, 280, 46, CloseAuxiliaryMenu, true);
            helpFeedback = CardLabel(helpCard, "", 11, Muted, -290, 630, 42);
        }

        private void BuildRefuge()
        {
            refugeLayer = ModalLayer("Refuge", out refugeCard, 790, 594);
            CardLabel(refugeCard, "DONDE LA LUZ PERDURA", 10, Brass, 250, 710, 26);
            CardLabel(refugeCard, "Una chispa basta para volver.", 31, Parchment, 206, 710, 46);
            refugeWallet = CardLabel(refugeCard, "0 fragmentos de luz", 18, Teal, 157, 710, 36);

            var vitality = Panel(refugeCard, "Corazón del faro", WithAlpha(Slate, .8f), true);
            Place(vitality, new Vector2(.5f, .5f), new Vector2(.5f, .5f), -177, -1, 326, 242);
            CardLabel(vitality, "Corazón del faro", 24, Parchment, 83, 290, 42);
            CardLabel(vitality, "+10 de vitalidad máxima por nivel", 14, Muted, 38, 292, 38);
            vitalityLevel = CardLabel(vitality, "Nivel 0 / 5", 14, Teal, -2, 292, 30);
            vitalityButton = MakeButton(vitality, "Mejorar", 0, -46, 282, 44, delegate { controller.Purchase(UpgradeKind.Vitality); Refresh(); }, false);
            vitalityReason = CardLabel(vitality, "", 11, Muted, -93, 292, 38);

            var power = Panel(refugeCard, "Filo de aurora", WithAlpha(Slate, .8f), true);
            Place(power, new Vector2(.5f, .5f), new Vector2(.5f, .5f), 177, -1, 326, 242);
            CardLabel(power, "Filo de aurora", 24, Parchment, 83, 290, 42);
            CardLabel(power, "+2 de poder de ataque por nivel", 14, Muted, 38, 292, 38);
            powerLevel = CardLabel(power, "Nivel 0 / 5", 14, Brass, -2, 292, 30);
            powerButton = MakeButton(power, "Mejorar", 0, -46, 282, 44, delegate { controller.Purchase(UpgradeKind.Power); Refresh(); }, false);
            powerReason = CardLabel(power, "", 11, Muted, -93, 292, 38);

            refugeHistory = CardLabel(refugeCard, "", 13, Muted, -151, 700, 38);
            refugeClose = MakeButton(refugeCard, "Volver a la cripta", -144, -209, 270, 46, CloseAuxiliaryMenu, true);
            MakeButton(refugeCard, "Guardar progreso", 160, -209, 242, 46, delegate { controller.RetrySave(); Refresh(); }, false);
            refugeFeedback = CardLabel(refugeCard, "", 11, Muted, -262, 710, 44);
        }

        private void BuildTouchControls()
        {
            touchLayer = new GameObject("Touch controls", typeof(RectTransform));
            touchLayer.transform.SetParent(canvasRect, false);
            Fill(touchLayer.GetComponent<RectTransform>());
            touchPad = Panel(touchLayer.transform, "Movement pad", Color.clear, false);
            Place(touchPad, Vector2.zero, Vector2.zero, 22, 120, 260, 56);
            AddHeldButton(touchPad, "↑", -96, delegate(bool held) { up = held; UpdateTouchMovement(); });
            AddHeldButton(touchPad, "←", -32, delegate(bool held) { left = held; UpdateTouchMovement(); });
            AddHeldButton(touchPad, "↓", 32, delegate(bool held) { down = held; UpdateTouchMovement(); });
            AddHeldButton(touchPad, "→", 96, delegate(bool held) { right = held; UpdateTouchMovement(); });
            var dash = MakeButton(touchLayer.transform, "Esquivar", 0, 0, 142, 56, delegate { }, false);
            touchDash = dash.GetComponent<RectTransform>();
            Place(touchDash, new Vector2(1, 0), new Vector2(1, 0), -22, 120, 142, 56);
            var hold = dash.gameObject.AddComponent<UmbraHeldButton>();
            hold.HeldChanged = delegate(bool held) { if (held) controller.RequestDash(); };
        }

        private void AddHeldButton(Transform parent, string label, float x, Action<bool> changed)
        {
            var button = MakeButton(parent, label, x, 0, 56, 56, delegate { }, false);
            button.GetComponentInChildren<Text>().fontSize = 25;
            button.gameObject.AddComponent<UmbraHeldButton>().HeldChanged = changed;
        }

        private void UpdateTouchMovement()
        {
            if (controller == null) return;
            var movement = new Vector2((right ? 1 : 0) - (left ? 1 : 0), (up ? 1 : 0) - (down ? 1 : 0));
            controller.TouchMovement = movement.sqrMagnitude > 1 ? movement.normalized : movement;
        }

        private void ClearTouch()
        {
            up = down = left = right = false;
            if (controller != null) controller.TouchMovement = Vector2.zero;
        }

        private void OpenAuxiliary(AuxiliaryMenu target)
        {
            controller.Game.Pause();
            auxiliary = target;
            ClearTouch();
            Refresh();
        }

        private void Refresh()
        {
            if (!initialized || controller == null || controller.Game == null || controller.Progress == null) return;
            var game = controller.Game;
            var profile = controller.Progress.Profile.Data;
            var state = game.State;
            bool idle = state == RunState.Idle;
            float maximum = idle ? 100 + profile.Vitality * 10 : game.Player.MaxHp;
            float health = idle ? maximum : Mathf.Max(0, game.Player.Hp);
            hpLabel.text = "VITALIDAD  " + Mathf.CeilToInt(health) + " / " + Mathf.CeilToInt(maximum);
            hpFill.rectTransform.anchorMax = new Vector2(maximum <= 0 ? 0 : Mathf.Clamp01(health / maximum), 1);
            hpFill.color = health < maximum * .3f ? Danger : Teal;
            roomLabel.text = idle ? "LA CRIPTA OLVIDADA" : "SALA " + game.Room.ToString("00") + " / 06  ·  " + Rooms[Mathf.Clamp(game.Room - 1, 0, 5)];
            seedLabel.text = idle ? "SEIS SALAS · UNA ÚLTIMA LUZ" : "SEMILLA " + game.Seed;
            statsLabel.text = Clock(game.Elapsed) + "  ·  " + game.Kills + " sombras  ·  " + game.Shards + " fragmentos";
            mainFeedback.text = controller.Feedback;
            refugeFeedback.text = controller.Feedback;
            helpFeedback.text = controller.Feedback;
            soundLabel.text = "Sonido: " + (controller.SoundEnabled ? "activado" : "desactivado");
            motionLabel.text = "Movimiento reducido: " + (controller.ReducedMotion ? "sí" : "no");
            refugeWallet.text = profile.Shards + " fragmentos de luz";
            refugeHistory.text = profile.Runs + " incursiones  ·  " + profile.Wins + " victorias  ·  Mejor sala: " + profile.BestRoom + " / 6" + (profile.BestTime >= 0 ? "  ·  Mejor victoria: " + Clock(profile.BestTime) : "");
            UpdateUpgrade(vitalityButton, vitalityLevel, vitalityReason, profile.Vitality, profile.Shards, "+" + profile.Vitality * 10 + " de vitalidad");
            UpdateUpgrade(powerButton, powerLevel, powerReason, profile.Power, profile.Shards, "+" + profile.Power * 2 + " de poder");

            GameObject desired;
            Button first;
            if (auxiliary == AuxiliaryMenu.Help) { desired = helpLayer; first = helpClose; }
            else if (auxiliary == AuxiliaryMenu.Refuge) { desired = refugeLayer; first = refugeClose; }
            else if (state == RunState.Reward)
            {
                desired = rewardLayer;
                first = choices[0];
                RefreshChoices();
            }
            else if (state != RunState.Playing)
            {
                desired = mainLayer;
                first = mainAction;
                if (state == RunState.Paused)
                {
                    mainEyebrow.text = "UN MOMENTO JUNTO A LA LUZ";
                    mainTitle.text = "Respira.\nEl faro espera.";
                    mainSummary.text = "La oscuridad puede esperar.\nPulsa ESC o continúa cuando estés listo.";
                    mainActionLabel.text = "Continuar incursión";
                }
                else if (state == RunState.Dead || state == RunState.Won)
                {
                    bool won = state == RunState.Won;
                    mainEyebrow.text = won ? "LA OSCURIDAD HA CEDIDO" : "TODA LUZ ENCUENTRA EL CAMINO";
                    mainTitle.text = won ? "El amanecer\nes tuyo." : "La luz cae.\nPero no se pierde.";
                    mainSummary.text = game.Kills + " sombras  ·  " + Clock(game.Elapsed) + "  ·  sala " + game.Room + " de 6\n+" + game.Shards + " fragmentos para el Refugio";
                    mainActionLabel.text = "Volver a intentarlo";
                }
                else
                {
                    mainEyebrow.text = "EL FARO TE HA ELEGIDO";
                    mainTitle.text = "Una luz.\nOtra oportunidad.";
                    mainSummary.text = "Desciende. Encuentra reliquias. Regresa más fuerte.\nUna cripta diferente en cada intento.";
                    mainActionLabel.text = "Comenzar incursión";
                }
            }
            else { desired = null; first = null; }
            SetVisibleLayer(desired, first);
            bool showTouch = state == RunState.Playing && auxiliary == AuxiliaryMenu.None && (Application.isMobilePlatform || Input.touchSupported);
            if (touchLayer.activeSelf != showTouch)
            {
                touchLayer.SetActive(showTouch);
                if (!showTouch) ClearTouch();
            }
        }

        private void UpdateUpgrade(Button button, Text levelLabel, Text reason, int level, int shards, string effect)
        {
            int cost = 15 * (level + 1);
            levelLabel.text = "Nivel " + level + " / 5  ·  " + effect;
            button.interactable = level < 5 && shards >= cost;
            button.GetComponentInChildren<Text>().text = level >= 5 ? "Mejora completada" : "Mejorar · " + cost + " fragmentos";
            reason.text = level >= 5 ? "La luz ha alcanzado su máximo." : shards < cost ? "Te faltan " + (cost - shards) + " fragmentos." : "Se aplicará en tu próxima incursión.";
        }

        private void RefreshChoices()
        {
            var available = controller.Game.Choices;
            string key = controller.Game.Room.ToString();
            for (int i = 0; i < available.Count; i++) key += ":" + available[i].Id;
            if (key == previousChoiceKey) return;
            previousChoiceKey = key;
            for (int i = 0; i < 3; i++)
            {
                bool exists = i < available.Count;
                choices[i].gameObject.SetActive(exists);
                choiceIds[i] = exists ? available[i].Id : null;
                if (!exists) continue;
                choiceTitle[i].text = available[i].Name;
                choiceDescription[i].text = available[i].Description;
                choiceRarity[i].text = available[i].Rarity.ToUpperInvariant();
            }
        }

        private void SetVisibleLayer(GameObject desired, Button first)
        {
            if (activeLayer == desired) return;
            mainLayer.SetActive(desired == mainLayer);
            rewardLayer.SetActive(desired == rewardLayer);
            helpLayer.SetActive(desired == helpLayer);
            refugeLayer.SetActive(desired == refugeLayer);
            activeLayer = desired;
            headerActions.interactable = desired == null;
            headerActions.blocksRaycasts = desired == null;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(first == null ? null : first.gameObject);
            ClearTouch();
        }

        private void RefreshLayout()
        {
            float width = canvasRect.rect.width;
            float height = canvasRect.rect.height;
            if (width < 80 || height < 80) return;
            if (Mathf.Abs(width - lastWidth) < 1 && Mathf.Abs(height - lastHeight) < 1) return;
            lastWidth = width;
            lastHeight = height;
            bool narrow = width < 920;
            float headerHeight = narrow ? 128 : 96;
            float footerHeight = narrow ? 124 : 102;
            header.sizeDelta = new Vector2(0, headerHeight);
            footer.sizeDelta = new Vector2(0, footerHeight);
            brand.fontSize = narrow ? 28 : 32;
            tagline.gameObject.SetActive(!narrow);
            Place(headerRoom, narrow ? new Vector2(0, 1) : new Vector2(.5f, 1), narrow ? new Vector2(0, 1) : new Vector2(.5f, 1), narrow ? 26 : 0, narrow ? -73 : -19, narrow ? width - 52 : 450, 30);
            roomLabel.alignment = narrow ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter;
            Place(headerSeed, narrow ? new Vector2(0, 1) : new Vector2(.5f, 1), narrow ? new Vector2(0, 1) : new Vector2(.5f, 1), narrow ? 26 : 0, narrow ? -103 : -50, narrow ? width - 52 : 450, 22);
            seedLabel.alignment = narrow ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter;
            Place(footerHpLabel, Vector2.zero, Vector2.zero, 26, narrow ? 73 : 54, 266, 26);
            Place(hpTrack, Vector2.zero, Vector2.zero, 26, narrow ? 63 : 43, 258, 5);
            Place(footerStats, new Vector2(1, 0), new Vector2(1, 0), -26, narrow ? 73 : 48, Mathf.Max(240, width - 360), narrow ? 40 : 32);
            statsLabel.fontSize = narrow ? 13 : 16;
            Place(footerControls, new Vector2(.5f, 0), new Vector2(.5f, 0), 0, 12, width - 40, 30);
            controlsLabel.fontSize = narrow ? 10 : 12;
            controlsLabel.text = narrow ? "WASD / flechas: moverse · ESPACIO: esquivar · ESC: pausa\nAtaque automático; mantén clic para apuntar." : "WASD / flechas: moverse    ·    Ratón + clic: apuntar    ·    ESPACIO: esquivar    ·    ESC: pausa";

            float mainWidth = Mathf.Min(660, width - 32);
            mainCard.sizeDelta = new Vector2(mainWidth, 454);
            FitCardTexts(mainCard, mainWidth - 44);
            FitWideChildren(mainCard, 660, mainWidth);
            mainTitle.fontSize = mainWidth < 550 ? 34 : 41;
            bool stackedRewards = width < 900;
            rewardCard.sizeDelta = new Vector2(Mathf.Min(840, width - 32), stackedRewards ? 620 : 424);
            FitCardTexts(rewardCard, rewardCard.sizeDelta.x - 40);
            if (stackedRewards)
            {
                SetCardY(rewardCard, 0, 262);
                SetCardY(rewardCard, 1, 222);
                SetCardY(rewardCard, 2, 180);
                for (int i = 0; i < 3; i++)
                {
                    choiceRects[i].anchoredPosition = new Vector2(0, 77 - i * 123);
                    choiceRects[i].sizeDelta = new Vector2(rewardCard.sizeDelta.x - 44, 110);
                    Place(choiceRarity[i].rectTransform, new Vector2(0, .5f), new Vector2(0, .5f), 18, 31, 110, 22);
                    Place(choiceTitle[i].rectTransform, new Vector2(0, .5f), new Vector2(0, .5f), 18, 5, choiceRects[i].sizeDelta.x - 36, 30);
                    Place(choiceDescription[i].rectTransform, new Vector2(0, .5f), new Vector2(0, .5f), 18, -29, choiceRects[i].sizeDelta.x - 36, 38);
                    choiceTitle[i].fontSize = 21;
                    choiceDescription[i].fontSize = 12;
                }
                SetCardY(rewardCard, 6, -268);
            }
            else
            {
                SetCardY(rewardCard, 0, 165);
                SetCardY(rewardCard, 1, 118);
                SetCardY(rewardCard, 2, 72);
                for (int i = 0; i < 3; i++)
                {
                    Place(choiceRects[i], new Vector2(.5f, .5f), new Vector2(.5f, .5f), (i - 1) * 250, -55, 232, 224);
                    Place(choiceRarity[i].rectTransform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), 0, 79, 200, 25);
                    Place(choiceTitle[i].rectTransform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), 0, 29, 200, 62);
                    Place(choiceDescription[i].rectTransform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), 0, -49, 194, 80);
                    choiceTitle[i].fontSize = 22;
                    choiceDescription[i].fontSize = 14;
                }
                SetCardY(rewardCard, 6, -184);
            }
            helpCard.sizeDelta = new Vector2(Mathf.Min(730, width - 32), 650);
            refugeCard.sizeDelta = new Vector2(Mathf.Min(790, width - 32), 594);
            FitCardTexts(helpCard, helpCard.sizeDelta.x - 44);
            FitCardTexts(refugeCard, refugeCard.sizeDelta.x - 44);
            FitWideChildren(helpCard, 650, helpCard.sizeDelta.x);
            FitWideChildren(refugeCard, 790, refugeCard.sizeDelta.x);
            FitViewportHeight(mainCard, height);
            FitViewportHeight(rewardCard, height);
            FitViewportHeight(helpCard, height);
            FitViewportHeight(refugeCard, height);
            touchPad.anchoredPosition = new Vector2(22, footerHeight + 18);
            touchDash.anchoredPosition = new Vector2(-22, footerHeight + 18);
        }

        private static void FitViewportHeight(RectTransform card, float height)
        {
            float scale = Mathf.Min(1, (height - 32) / card.sizeDelta.y);
            card.localScale = new Vector3(scale, scale, 1);
        }

        private static void FitWideChildren(RectTransform card, float referenceWidth, float actualWidth)
        {
            float ratio = Mathf.Min(1, (actualWidth - 40) / (referenceWidth - 40));
            foreach (RectTransform child in card)
            {
                if (child.GetComponent<Text>() != null) continue;
                if (child.GetComponent<Button>() != null || child.GetComponent<Image>() != null)
                {
                    // Anchored controls retain documented reference positions on repeated resizes.
                    var original = child.GetComponent<UmbraOriginalRect>();
                    if (original == null)
                    {
                        original = child.gameObject.AddComponent<UmbraOriginalRect>();
                        original.Position = child.anchoredPosition;
                        original.Size = child.sizeDelta;
                    }
                    child.anchoredPosition = new Vector2(original.Position.x * ratio, original.Position.y);
                    child.sizeDelta = new Vector2(original.Size.x * ratio, original.Size.y);
                    foreach (var label in child.GetComponentsInChildren<Text>(true))
                    {
                        var size = label.rectTransform.sizeDelta;
                        label.rectTransform.sizeDelta = new Vector2(Mathf.Min(size.x, child.sizeDelta.x - 24), size.y);
                        label.resizeTextForBestFit = true;
                        label.resizeTextMinSize = 10;
                        label.resizeTextMaxSize = label.fontSize;
                    }
                }
            }
        }

        private static void SetCardY(RectTransform card, int childIndex, float y)
        {
            if (childIndex >= card.childCount) return;
            var child = card.GetChild(childIndex) as RectTransform;
            if (child == null) return;
            child.anchoredPosition = new Vector2(child.anchoredPosition.x, y);
        }

        private static void FitCardTexts(RectTransform card, float width)
        {
            foreach (RectTransform child in card)
            {
                var label = child.GetComponent<Text>();
                if (label == null) continue;
                var size = child.sizeDelta;
                child.sizeDelta = new Vector2(width, size.y);
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = Mathf.Min(11, label.fontSize);
                label.resizeTextMaxSize = label.fontSize;
            }
        }

        private void CycleFocus(bool backwards)
        {
            Transform scope = activeLayer != null ? activeLayer.transform : headerActions.transform;
            var all = scope.GetComponentsInChildren<Selectable>(false);
            var usable = new List<Selectable>();
            foreach (var selectable in all) if (selectable.IsInteractable() && selectable.isActiveAndEnabled) usable.Add(selectable);
            if (usable.Count == 0 || EventSystem.current == null) return;
            int index = -1;
            var selected = EventSystem.current.currentSelectedGameObject;
            for (int i = 0; i < usable.Count; i++) if (usable[i].gameObject == selected) index = i;
            index = index < 0 ? (backwards ? usable.Count - 1 : 0) : (index + (backwards ? -1 : 1) + usable.Count) % usable.Count;
            EventSystem.current.SetSelectedGameObject(usable[index].gameObject);
        }

        private GameObject ModalLayer(string name, out RectTransform card, float width, float height)
        {
            var layer = Panel(canvasRect, name, WithAlpha(Slate, .84f), false);
            Fill(layer);
            layer.GetComponent<Image>().raycastTarget = true;
            card = Panel(layer, name + " card", Surface, true);
            Place(card, new Vector2(.5f, .5f), new Vector2(.5f, .5f), 0, 0, width, height);
            layer.gameObject.SetActive(false);
            return layer.gameObject;
        }

        private RectTransform Panel(Transform parent, string name, Color color, bool outline)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            if (outline)
            {
                var border = go.AddComponent<Outline>();
                border.effectColor = WithAlpha(Brass, .36f);
                border.effectDistance = new Vector2(1, -1);
            }
            return go.GetComponent<RectTransform>();
        }

        private Text Label(Transform parent, string content, int size, Color color, TextAnchor alignment)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var label = go.GetComponent<Text>();
            label.font = font;
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.text = content;
            label.supportRichText = false;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.lineSpacing = 1.1f;
            return label;
        }

        private Text CardLabel(RectTransform card, string content, int size, Color color, float y, float width, float height)
        {
            var text = Label(card, content, size, color, TextAnchor.MiddleCenter);
            Place(text.rectTransform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), 0, y, width, height);
            return text;
        }

        private Button MakeButton(Transform parent, string label, float x, float y, float width, float height, Action clicked, bool primary)
        {
            var rect = Panel(parent, label.Length == 0 ? "Relic button" : label, primary ? Brass : Hex(0x22302e), !primary);
            Place(rect, new Vector2(.5f, .5f), new Vector2(.5f, .5f), x, y, width, height);
            var image = rect.GetComponent<Image>();
            image.raycastTarget = true;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.16f, 1.16f, 1.12f, 1);
            colors.pressedColor = new Color(.82f, .82f, .78f, 1);
            colors.selectedColor = new Color(1.2f, 1.2f, 1.13f, 1);
            colors.disabledColor = new Color(.5f, .5f, .5f, .6f);
            colors.fadeDuration = .08f;
            button.colors = colors;
            var focusBorder = rect.GetComponent<Outline>();
            if (focusBorder == null)
            {
                focusBorder = rect.gameObject.AddComponent<Outline>();
                focusBorder.effectColor = Color.clear;
                focusBorder.effectDistance = new Vector2(2, -2);
            }
            var focus = rect.gameObject.AddComponent<UmbraButtonFocus>();
            focus.Border = focusBorder;
            focus.Normal = focusBorder.effectColor;
            focus.Selected = Brass;
            var caption = Label(rect, label, 15, primary ? Slate : Parchment, TextAnchor.MiddleCenter);
            Fill(caption.rectTransform);
            caption.rectTransform.offsetMin = new Vector2(10, 3);
            caption.rectTransform.offsetMax = new Vector2(-10, -3);
            button.onClick.AddListener(delegate { clicked(); });
            return button;
        }

        private static void Fill(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 pivot, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static Color Hex(uint value)
        {
            return new Color(((value >> 16) & 255) / 255f, ((value >> 8) & 255) / 255f, (value & 255) / 255f, 1);
        }

        private static Color WithAlpha(Color color, float alpha) { color.a = alpha; return color; }

        private static string Clock(float seconds)
        {
            int value = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return (value / 60).ToString("00") + ":" + (value % 60).ToString("00");
        }

        private void OnDisable() { ClearTouch(); }
    }

    /// <summary>Held touch input supports several simultaneous pointers and always releases on cancellation.</summary>
    public sealed class UmbraHeldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public Action<bool> HeldChanged;
        private readonly HashSet<int> pointers = new HashSet<int>();

        public void OnPointerDown(PointerEventData data)
        {
            bool previouslyHeld = pointers.Count > 0;
            pointers.Add(data.pointerId);
            if (!previouslyHeld && HeldChanged != null) HeldChanged(true);
        }

        public void OnPointerUp(PointerEventData data) { Release(data.pointerId); }
        public void OnPointerExit(PointerEventData data) { Release(data.pointerId); }

        private void Release(int pointerId)
        {
            bool previouslyHeld = pointers.Count > 0;
            pointers.Remove(pointerId);
            if (previouslyHeld && pointers.Count == 0 && HeldChanged != null) HeldChanged(false);
        }

        private void OnDisable()
        {
            if (pointers.Count > 0 && HeldChanged != null) HeldChanged(false);
            pointers.Clear();
        }
    }

    /// <summary>Reference geometry used when a modal is resized more than once.</summary>
    public sealed class UmbraOriginalRect : MonoBehaviour
    {
        public Vector2 Position;
        public Vector2 Size;
    }

    /// <summary>Keyboard selection uses an explicit brass outline as well as color feedback.</summary>
    public sealed class UmbraButtonFocus : MonoBehaviour, ISelectHandler, IDeselectHandler
    {
        public Outline Border;
        public Color Normal, Selected;
        public void OnSelect(BaseEventData data) { if (Border != null) Border.effectColor = Selected; }
        public void OnDeselect(BaseEventData data) { if (Border != null) Border.effectColor = Normal; }
        private void OnDisable() { if (Border != null) Border.effectColor = Normal; }
    }
}
