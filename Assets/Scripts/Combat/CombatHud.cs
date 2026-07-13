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
            if (keyboard != null)
            {
                if (keyboard.pKey.wasPressedThisFrame) SetPaused(!isPaused);
                if (keyboard.uKey.wasPressedThisFrame)
                {
                    helpPanel.SetActive(!helpPanel.activeSelf);
                    if (helpPanel.activeSelf) helpPanel.transform.SetAsLastSibling();
                }
                if (keyboard.iKey.wasPressedThisFrame) RestoreAllVitals();
                if (keyboard.oKey.wasPressedThisFrame) adjustmentVisible = !adjustmentVisible;
            }

            if (director == null) return;
            UpdatePlayer(left, director.PlayerOne);
            UpdatePlayer(right, director.PlayerTwo);
            eventText.text = director.LastEvent;
            if (helpPanel.activeSelf) helpText.text = BuildDynamicHelpText();
        }

        private static void UpdatePlayer(PlayerWidgets ui, FighterController fighter)
        {
            if (fighter == null) return;
            SetBarAmount(ui.Health, fighter.Health / fighter.HealthDisplayMaximum);
            SetBarAmount(ui.Stance, fighter.Stance / fighter.StanceDisplayMaximum);
            SetBarAmount(ui.Effect, fighter.EffectTime / CombatDirector.PairEffectDuration);
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
            pauseText.text = "游戏暂停\n\nP  继续游戏\nU  动态战斗信息\nI  双方生命与架势回满\nO  临时调节生命与架势";
            pausePanel.SetActive(false);
        }

        private string BuildDynamicHelpText()
        {
            AttackDefinition a = AttackDefinition.Create(AttackKind.A);
            AttackDefinition b = AttackDefinition.Create(AttackKind.B);
            AttackDefinition c = AttackDefinition.Create(AttackKind.C);
            StringBuilder text = new StringBuilder(1200);
            text.AppendLine(isPaused ? "动态战斗信息（当前：暂停）" : "动态战斗信息（当前：运行）");
            text.AppendLine($"全局动作速度 {AttackDefinition.GlobalActionSpeed:0.##}x　缓冲 {FighterController.InputBufferLifetime:0.##}s　" +
                            $"效果延时强度 {FighterController.EffectDelayStrength:0.##}x　音效提前 {CombatAudio.PairPredictionLead:0.##}s");
            text.AppendLine();
            AppendAttack(text, a);
            AppendAttack(text, b);
            AppendAttack(text, c);
            text.AppendLine();
            text.AppendLine($"AA {PairActionSpeed.AA:0.##}x　AB A:{PairActionSpeed.AB_A:0.##}x B:{PairActionSpeed.AB_B:0.##}x　AC A:{PairActionSpeed.AC_A:0.##}x C:{PairActionSpeed.AC_C:0.##}x");
            text.AppendLine($"BB {PairActionSpeed.BB:0.##}x　BC B:{PairActionSpeed.BC_B:0.##}x C:{PairActionSpeed.BC_C:0.##}x　CC {PairActionSpeed.CC:0.##}x");
            text.AppendLine($"B+C：C方损失 {CombatDirector.BCPairDamage} 血并获得 {CombatDirector.PairEffectDuration:0.##}s 效果；A+C：A方获得该效果；其他动作对双方弹回。");
            text.AppendLine("黄色效果条：剩余效果时间会延长下一次攻击。普通命中在有效期结束且确认没有重叠后结算。");
            text.AppendLine();
            AppendFighter(text, director?.PlayerOne, "P1");
            AppendFighter(text, director?.PlayerTwo, "P2");
            text.AppendLine("按键：P 暂停/继续　U 关闭信息　I 回满　O 临时数值面板");
            return text.ToString();
        }

        private static void AppendAttack(StringBuilder text, AttackDefinition attack)
        {
            text.AppendLine($"{attack.Kind} {attack.DisplayName}：架势 {attack.StanceCost:0.##}，普通伤害 {attack.NormalDamage}，" +
                            $"总时长 {attack.BaseDuration:0.###}s，有效期 {attack.ActiveStart:0.###}～{attack.ActiveEnd:0.###}s，范围 {attack.Radius:0.##}m");
        }

        private static void AppendFighter(StringBuilder text, FighterController fighter, string label)
        {
            if (fighter == null) return;
            text.AppendLine($"{label}：HP {fighter.Health:0.0}/{fighter.HealthDisplayMaximum:0.0}，架势 {fighter.Stance:0.0}/{fighter.StanceDisplayMaximum:0.0}，" +
                            $"效果 {fighter.EffectTime:0.00}s，状态 {fighter.DebugState()}，缓冲 {fighter.BufferedInputCount}");
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
            GUILayout.Label($"{label}　生命 {fighter.Health:0.0}/{FighterController.TemporaryVitalLimit:0}（临时上限）");
            float health = GUILayout.HorizontalSlider(fighter.Health, 0f, FighterController.TemporaryVitalLimit,
                GUILayout.Height(24f));
            GUILayout.Label($"{label}　架势 {fighter.Stance:0.0}/{FighterController.TemporaryVitalLimit:0}（临时上限）");
            float stance = GUILayout.HorizontalSlider(fighter.Stance, 0f, FighterController.TemporaryVitalLimit,
                GUILayout.Height(24f));
            fighter.SetTemporaryVitals(health, stance);
        }

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
