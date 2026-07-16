using System;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Swordman2.Combat
{
    public sealed class CombatHud : MonoBehaviour
    {
        private CombatDirector director;
        private PlayerWidgets left;
        private PlayerWidgets right;
        private Text eventText;
        private GameObject helpPanel;
        private Text helpText;
        private GameObject pausePanel;
        private bool isPaused;
        private bool adjustmentVisible;
        private Rect adjustmentWindow = new Rect(0f, 0f, 520f, 360f);

        public void Initialize(CombatDirector combatDirector)
        {
            director = combatDirector;
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;
            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);
            gameObject.AddComponent<GraphicRaycaster>();

            left = BuildPlayerPanel("P1", new Vector2(0f, 1f), new Vector2(0.5f, 1f),
                new Vector2(25f, -160f), new Vector2(-25f, -22f), TextAnchor.UpperLeft);
            right = BuildPlayerPanel("P2", new Vector2(0.5f, 1f), new Vector2(1f, 1f),
                new Vector2(25f, -160f), new Vector2(-25f, -22f), TextAnchor.UpperRight);

            eventText = CreateText("Event", transform, 24, TextAnchor.MiddleCenter);
            RectTransform eventRect = eventText.rectTransform;
            eventRect.anchorMin = new Vector2(0.2f, 0f);
            eventRect.anchorMax = new Vector2(0.8f, 0f);
            eventRect.pivot = new Vector2(0.5f, 0f);
            eventRect.anchoredPosition = new Vector2(0f, 18f);
            eventRect.sizeDelta = new Vector2(0f, 44f);
            eventText.color = new Color(1f, 0.92f, 0.62f);
            BuildHelpPanel();
            BuildPausePanel();
            SetPaused(false);
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && director?.Catalog != null)
            {
                SystemControls controls = director.Catalog.controls.system;
                if (WasPressed(keyboard, controls.pause)) SetPaused(!isPaused);
                if (WasPressed(keyboard, controls.help))
                {
                    helpPanel.SetActive(!helpPanel.activeSelf);
                    if (helpPanel.activeSelf) helpPanel.transform.SetAsLastSibling();
                }
                if (WasPressed(keyboard, controls.restoreVitals)) RestoreAllVitals();
                if (WasPressed(keyboard, controls.adjustVitals)) adjustmentVisible = !adjustmentVisible;
            }

            if (director == null) return;
            UpdatePlayer(left, director.PlayerOne);
            UpdatePlayer(right, director.PlayerTwo);
            eventText.text = director.LastEvent;
            if (helpPanel.activeSelf) helpText.text = BuildDynamicHelpText();
        }

        private void UpdatePlayer(PlayerWidgets ui, FighterController fighter)
        {
            if (fighter == null) return;
            SetBarAmount(ui.Health, fighter.Health / fighter.HealthDisplayMaximum);
            SetBarAmount(ui.Stance, fighter.Stance / fighter.StanceDisplayMaximum);
            float maximumEffect = 1f;
            foreach (ActionPairDefinition pair in director.Catalog.actionPairs)
                maximumEffect = Mathf.Max(maximumEffect,
                    pair.first.nextAttackDelayFrames, pair.second.nextAttackDelayFrames);
            SetBarAmount(ui.Effect, fighter.DelayEffectFrames / maximumEffect);
            ui.State.text = $"{ui.Label}  HP {fighter.Health:0.0}/{fighter.HealthDisplayMaximum:0}  " +
                            $"ST {fighter.Stance:0.0}/{fighter.StanceDisplayMaximum:0}\n" +
                            $"{fighter.DebugState()}  Buffer {fighter.BufferedInputCount}";
        }

        private PlayerWidgets BuildPlayerPanel(string label, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax, TextAnchor alignment)
        {
            GameObject panel = new GameObject(label + " Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(transform, false);
            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            panel.GetComponent<Image>().color = new Color(0.02f, 0.025f, 0.035f, 0.78f);

            Text state = CreateText(label + " State", panel.transform, 21, alignment);
            state.rectTransform.anchorMin = new Vector2(0f, 1f);
            state.rectTransform.anchorMax = new Vector2(1f, 1f);
            state.rectTransform.pivot = new Vector2(0.5f, 1f);
            state.rectTransform.anchoredPosition = new Vector2(0f, -8f);
            state.rectTransform.sizeDelta = new Vector2(-20f, 52f);

            return new PlayerWidgets
            {
                Label = label,
                State = state,
                Health = CreateBar("Health", panel.transform, -67f, new Color(0.82f, 0.12f, 0.12f)),
                Stance = CreateBar("Stance", panel.transform, -94f, new Color(0.18f, 0.72f, 0.95f)),
                Effect = CreateBar("Effect", panel.transform, -121f, new Color(0.95f, 0.72f, 0.12f))
            };
        }

        private static Image CreateBar(string name, Transform parent, float y, Color color)
        {
            GameObject background = new GameObject(name + " Background", typeof(RectTransform), typeof(Image));
            background.transform.SetParent(parent, false);
            RectTransform bg = background.GetComponent<RectTransform>();
            bg.anchorMin = new Vector2(0f, 1f);
            bg.anchorMax = new Vector2(1f, 1f);
            bg.pivot = new Vector2(0.5f, 1f);
            bg.anchoredPosition = new Vector2(0f, y);
            bg.sizeDelta = new Vector2(-20f, 18f);
            background.GetComponent<Image>().color = new Color(0.1f, 0.11f, 0.14f, 0.92f);

            GameObject fill = new GameObject(name + " Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(background.transform, false);
            RectTransform fr = fill.GetComponent<RectTransform>();
            fr.anchorMin = Vector2.zero;
            fr.anchorMax = Vector2.one;
            fr.offsetMin = new Vector2(2f, 2f);
            fr.offsetMax = new Vector2(-2f, -2f);
            Image image = fill.GetComponent<Image>();
            image.color = color;
            return image;
        }

        private static void SetBarAmount(Image image, float amount)
        {
            float normalized = Mathf.Clamp01(amount);
            RectTransform rect = image.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(normalized, 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            image.enabled = normalized > 0.0001f;
        }

        private static Text CreateText(string name, Transform parent, int size, TextAnchor alignment)
        {
            GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Text));
            obj.transform.SetParent(parent, false);
            Text text = obj.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.alignment = alignment;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private void BuildHelpPanel()
        {
            helpPanel = new GameObject("Attack Help", typeof(RectTransform), typeof(Image));
            helpPanel.transform.SetParent(transform, false);
            RectTransform panelRect = helpPanel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.18f, 0.17f);
            panelRect.anchorMax = new Vector2(0.82f, 0.83f);
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            helpPanel.GetComponent<Image>().color = new Color(0.015f, 0.02f, 0.03f, 0.94f);

            helpText = CreateText("Attack Help Text", helpPanel.transform, 19, TextAnchor.UpperLeft);
            RectTransform textRect = helpText.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(42f, 30f);
            textRect.offsetMax = new Vector2(-42f, -30f);
            helpText.lineSpacing = 1.05f;
            helpText.text = BuildDynamicHelpText();
            helpPanel.SetActive(false);
        }

        private void BuildPausePanel()
        {
            pausePanel = new GameObject("Pause Panel", typeof(RectTransform), typeof(Image));
            pausePanel.transform.SetParent(transform, false);
            RectTransform panelRect = pausePanel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            pausePanel.GetComponent<Image>().color = new Color(0.01f, 0.015f, 0.025f, 0.86f);

            Text pauseText = CreateText("Pause Text", pausePanel.transform, 44, TextAnchor.MiddleCenter);
            pauseText.rectTransform.anchorMin = new Vector2(0.2f, 0.25f);
            pauseText.rectTransform.anchorMax = new Vector2(0.8f, 0.75f);
            pauseText.rectTransform.offsetMin = Vector2.zero;
            pauseText.rectTransform.offsetMax = Vector2.zero;
            SystemControls controls = director.Catalog.controls.system;
            pauseText.text = $"游戏暂停\n\n{controls.pause}  继续游戏\n{controls.help}  动态战斗信息\n" +
                             $"{controls.restoreVitals}  双方生命与架势回满\n{controls.adjustVitals}  临时调节生命与架势";
            pausePanel.SetActive(false);
        }

        private string BuildDynamicHelpText()
        {
            CombatCatalogData catalog = director?.Catalog;
            if (catalog == null) return "战斗数据尚未加载。";
            StringBuilder text = new StringBuilder(1200);
            text.AppendLine(isPaused ? "动态战斗信息（当前：暂停）" : "动态战斗信息（当前：运行）");
            text.AppendLine($"逻辑帧率 {catalog.settings.logicFrameRate}Hz　缓冲 {catalog.settings.inputBufferFrames}帧　" +
                            $"延迟换算 {catalog.settings.delayEffectAttackScale:0.##}x　音效提前 {catalog.audio.pairPredictionLeadSeconds:0.##}s");
            text.AppendLine();
            foreach (AttackDefinition attack in catalog.attacks) AppendAttack(text, attack, catalog.settings.logicFrameRate);
            text.AppendLine();
            foreach (ActionPairDefinition pair in catalog.actionPairs) AppendPair(text, pair);
            text.AppendLine("黄色效果条：剩余逻辑帧会按延迟换算倍率延长下一次攻击；普通命中仅在有效期结束且确认没有重叠后结算。");
            text.AppendLine();
            AppendControls(text, "P1", catalog.controls.playerOne);
            AppendControls(text, "P2", catalog.controls.playerTwo);
            AppendFighter(text, director?.PlayerOne, "P1");
            AppendFighter(text, director?.PlayerTwo, "P2");
            SystemControls system = catalog.controls.system;
            text.AppendLine($"系统键：{system.pause} 暂停/继续　{system.help} 关闭信息　{system.restoreVitals} 回满　{system.adjustVitals} 临时数值面板");
            return text.ToString();
        }

        private static void AppendAttack(StringBuilder text, AttackDefinition attack, int frameRate)
        {
            text.AppendLine($"{attack.id} {attack.displayName}：架势 {attack.stanceCost:0.##}，伤害 {attack.normalDamage}，" +
                            $"前摇/有效/后摇 {attack.windupFrames}/{attack.activeFrames}/{attack.recoveryFrames}帧，" +
                            $"总时长 {(float)attack.TotalFrames / frameRate:0.###}s，范围 {attack.radius:0.##}m");
        }

        private static void AppendPair(StringBuilder text, ActionPairDefinition pair)
        {
            text.AppendLine($"{pair.displayName}：{PairSide(pair.firstAction, pair.first)}；{PairSide(pair.secondAction, pair.second)}");
        }

        private static string PairSide(string action, PairParticipantData data)
        {
            string value = $"{action} {(data.Result == PairParticipantResult.Continue ? "继续" : "弹回")} {data.speedScale:0.##}x";
            if (data.damage > 0) value += $" 伤害{data.damage}";
            if (data.nextAttackDelayFrames > 0) value += $" 延迟+{data.nextAttackDelayFrames}帧";
            return value;
        }

        private static void AppendControls(StringBuilder text, string label, PlayerControls controls)
        {
            text.Append($"{label} 移动 {controls.moveUp}/{controls.moveLeft}/{controls.moveDown}/{controls.moveRight}，攻击 ");
            foreach (AttackBinding binding in controls.attacks) text.Append($"{binding.action}:{binding.key} ");
            text.AppendLine();
        }

        private static void AppendFighter(StringBuilder text, FighterController fighter, string label)
        {
            if (fighter == null) return;
            text.AppendLine($"{label}：HP {fighter.Health:0.0}/{fighter.HealthDisplayMaximum:0.0}，架势 {fighter.Stance:0.0}/{fighter.StanceDisplayMaximum:0.0}，" +
                            $"延迟条 {fighter.DelayEffectFrames:0}帧，状态 {fighter.DebugState()}，缓冲 {fighter.BufferedInputCount}");
        }

        private void SetPaused(bool paused)
        {
            isPaused = paused;
            Time.timeScale = paused ? 0f : 1f;
            AudioListener.pause = paused;
            if (pausePanel != null)
            {
                pausePanel.SetActive(paused);
                if (paused) pausePanel.transform.SetAsLastSibling();
            }
        }

        private void RestoreAllVitals()
        {
            director?.PlayerOne?.RestoreVitals();
            director?.PlayerTwo?.RestoreVitals();
        }

        private void OnGUI()
        {
            if (!adjustmentVisible || director == null) return;
            adjustmentWindow.x = (Screen.width - adjustmentWindow.width) * 0.5f;
            adjustmentWindow.y = (Screen.height - adjustmentWindow.height) * 0.5f;
            adjustmentWindow = GUI.Window(48152, adjustmentWindow, DrawAdjustmentWindow,
                "临时数值调节（O 关闭）");
        }

        private void DrawAdjustmentWindow(int windowId)
        {
            GUILayout.Space(8f);
            DrawFighterAdjustment(director.PlayerOne, "P1");
            GUILayout.Space(18f);
            DrawFighterAdjustment(director.PlayerTwo, "P2");
            GUILayout.Space(14f);
            if (GUILayout.Button("双方生命与架势回满", GUILayout.Height(34f))) RestoreAllVitals();
            GUI.DragWindow(new Rect(0f, 0f, adjustmentWindow.width, 28f));
        }

        private static void DrawFighterAdjustment(FighterController fighter, string label)
        {
            if (fighter == null) return;
            GUILayout.Label($"{label}　生命 {fighter.Health:0.0}/{fighter.TemporaryVitalLimit:0}（临时上限）");
            float health = GUILayout.HorizontalSlider(fighter.Health, 0f, fighter.TemporaryVitalLimit,
                GUILayout.Height(24f));
            GUILayout.Label($"{label}　架势 {fighter.Stance:0.0}/{fighter.TemporaryVitalLimit:0}（临时上限）");
            float stance = GUILayout.HorizontalSlider(fighter.Stance, 0f, fighter.TemporaryVitalLimit,
                GUILayout.Height(24f));
            fighter.SetTemporaryVitals(health, stance);
        }

        private static bool WasPressed(Keyboard keyboard, string keyName) =>
            Enum.TryParse(keyName, true, out Key key) && keyboard[key].wasPressedThisFrame;

        private void OnDestroy()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }

        private sealed class PlayerWidgets
        {
            public string Label;
            public Text State;
            public Image Health;
            public Image Stance;
            public Image Effect;
        }
    }
}
