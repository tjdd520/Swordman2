using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Swordman2.Combat
{
    [Serializable]
    public sealed class CombatCatalogData
    {
        public CombatSettings settings = new();
        public CommonAnimationData commonAnimations = new();
        public CombatControls controls = new();
        public CombatAudioData audio = new();
        public AttackDefinition[] attacks = Array.Empty<AttackDefinition>();
        public ActionPairDefinition[] actionPairs = Array.Empty<ActionPairDefinition>();

        [NonSerialized] private Dictionary<string, AttackDefinition> attackLookup;
        [NonSerialized] private Dictionary<string, ActionPairDefinition> pairLookup;

        public AttackDefinition GetAttack(string id)
        {
            EnsureLookups();
            attackLookup.TryGetValue(id ?? string.Empty, out AttackDefinition attack);
            return attack;
        }

        public ActionPairDefinition GetPair(string firstAction, string secondAction, out bool swapped)
        {
            EnsureLookups();
            swapped = string.Compare(firstAction, secondAction, StringComparison.OrdinalIgnoreCase) > 0;
            string left = swapped ? secondAction : firstAction;
            string right = swapped ? firstAction : secondAction;
            pairLookup.TryGetValue(PairKey(left, right), out ActionPairDefinition pair);
            return pair;
        }

        public void RebuildLookups()
        {
            attackLookup = null;
            pairLookup = null;
            EnsureLookups();
        }

        private void EnsureLookups()
        {
            if (attackLookup != null) return;
            attackLookup = new Dictionary<string, AttackDefinition>(StringComparer.OrdinalIgnoreCase);
            pairLookup = new Dictionary<string, ActionPairDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (AttackDefinition attack in attacks ?? Array.Empty<AttackDefinition>())
                if (attack != null && !string.IsNullOrWhiteSpace(attack.id)) attackLookup[attack.id] = attack;
            foreach (ActionPairDefinition pair in actionPairs ?? Array.Empty<ActionPairDefinition>())
                if (pair != null) pairLookup[PairKey(pair.firstAction, pair.secondAction)] = pair;
        }

        private static string PairKey(string left, string right) => $"{left?.Trim()}::{right?.Trim()}";
    }

    [Serializable]
    public sealed class CombatSettings
    {
        public int logicFrameRate = 120;
        public float maxHealth = 5f;
        public float maxStance = 5f;
        public float temporaryVitalLimit = 50f;
        public float moveSpeed = 1.2f;
        public int inputBufferFrames = 48;
        public int stanceRecoveryDelayFrames = 36;
        public int stanceRecoveryDurationFrames = 180;
        public int hitReactionFrames = 46;
        public float delayEffectAttackScale = 2f;
    }

    [Serializable]
    public sealed class CommonAnimationData
    {
        public string idle = "Idle_TwoHand_Sword";
        public string walkForward = "Walk_Forward";
        public string walkBackward = "Walk_Backward";
        public string walkLeft = "Walk_Left";
        public string walkRight = "Walk_Right";
        public string hitReaction = "Hit_Reaction_Front";
        public float walkReferenceSpeed = 0.68f;
        public float locomotionBlendSeconds = 0.12f;
        public float idleBlendSeconds = 0.08f;
        public float hitBlendSeconds = 0.06f;
    }

    [Serializable]
    public sealed class CombatControls
    {
        public PlayerControls playerOne = new();
        public PlayerControls playerTwo = new();
        public SystemControls system = new();
    }

    [Serializable]
    public sealed class PlayerControls
    {
        public string moveLeft;
        public string moveRight;
        public string moveDown;
        public string moveUp;
        public AttackBinding[] attacks = Array.Empty<AttackBinding>();
    }

    [Serializable]
    public sealed class AttackBinding
    {
        public string action;
        public string key;
    }

    [Serializable]
    public sealed class SystemControls
    {
        public string pause = "P";
        public string help = "U";
        public string restoreVitals = "I";
        public string adjustVitals = "O";
    }

    [Serializable]
    public sealed class CombatAudioData
    {
        public string normalHit = "Audio/NormalHit";
        public string[] pairSequence = Array.Empty<string>();
        public float pairChainWindowSeconds = 1.5f;
        public float pairPredictionLeadSeconds = 0.4f;
        public float volume = 0.85f;
    }

    [Serializable]
    public sealed class AttackDefinition
    {
        public string id;
        public string displayName;
        public float stanceCost;
        public int normalDamage;
        public int startupPoise = -1;
        public int windupFrames = 14;
        public int activeFrames = 23;
        public int recoveryFrames = 23;
        public float radius = 2.1f;
        public float blendSeconds = 0.08f;
        public AttackAnimationData animation = new();

        public int TotalFrames => windupFrames + activeFrames + recoveryFrames;

        public string SuccessClip(SlashSide side) => side == SlashSide.RightToLeft
            ? animation.successRightToLeft
            : animation.successLeftToRight;

        public string ReboundClip(SlashSide side) => side == SlashSide.RightToLeft
            ? animation.reboundRightToLeft
            : animation.reboundLeftToRight;
    }

    [Serializable]
    public sealed class AttackAnimationData
    {
        public string successRightToLeft;
        public string successLeftToRight;
        public string reboundRightToLeft;
        public string reboundLeftToRight;
        public float sourceFrameRate = 30f;
        public int sourceStartFrame = 1;
        public int sourceActiveStartFrame = 18;
        public int sourceActiveEndFrame = 38;
        public int sourceEndFrame = 52;
    }

    public enum PairParticipantResult { Continue, Rebound }

    [Serializable]
    public sealed class ActionPairDefinition
    {
        public string firstAction;
        public string secondAction;
        public string displayName;
        public PairParticipantData first = new();
        public PairParticipantData second = new();
    }

    [Serializable]
    public sealed class PairParticipantData
    {
        public string result = "Rebound";
        public float speedScale = 1f;
        public int damage;
        public int nextAttackDelayFrames;

        public PairParticipantResult Result => string.Equals(result, "Continue", StringComparison.OrdinalIgnoreCase)
            ? PairParticipantResult.Continue
            : PairParticipantResult.Rebound;
    }

    public static class CombatCatalogLoader
    {
        public const string ResourcePath = "CombatData/combat_catalog";

        public static bool TryLoad(out CombatCatalogData catalog)
        {
            catalog = null;
            TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                Debug.LogError($"找不到战斗数据：Assets/Resources/{ResourcePath}.json");
                return false;
            }

            try
            {
                catalog = JsonUtility.FromJson<CombatCatalogData>(asset.text);
            }
            catch (Exception exception)
            {
                Debug.LogError($"战斗 JSON 无法解析：{exception.Message}");
                return false;
            }

            string errors = Validate(catalog);
            if (errors.Length == 0)
            {
                catalog.RebuildLookups();
                return true;
            }

            Debug.LogError("战斗数据校验失败，停止初始化：\n" + errors);
            catalog = null;
            return false;
        }

        public static string Validate(CombatCatalogData catalog)
        {
            StringBuilder errors = new();
            if (catalog == null) return "- JSON 根对象为空";
            if (catalog.settings == null || catalog.settings.logicFrameRate <= 0)
                errors.AppendLine("- settings.logicFrameRate 必须大于 0");
            if (catalog.settings != null && (catalog.settings.maxHealth <= 0f || catalog.settings.maxStance <= 0f))
                errors.AppendLine("- 默认生命和架势上限必须大于 0");

            Dictionary<string, AttackDefinition> attacks = new(StringComparer.OrdinalIgnoreCase);
            foreach (AttackDefinition attack in catalog.attacks ?? Array.Empty<AttackDefinition>())
            {
                if (attack == null || string.IsNullOrWhiteSpace(attack.id))
                {
                    errors.AppendLine("- 存在空动作 ID");
                    continue;
                }
                if (!attacks.TryAdd(attack.id, attack)) errors.AppendLine($"- 动作 ID 重复：{attack.id}");
                if (attack.windupFrames <= 0 || attack.activeFrames <= 0 || attack.recoveryFrames <= 0)
                    errors.AppendLine($"- 动作 {attack.id} 的三个逻辑阶段帧数都必须大于 0");
                if (attack.startupPoise < 0)
                    errors.AppendLine($"- 动作 {attack.id} 必须配置非负整数 startupPoise");
                if (attack.radius <= 0f || attack.stanceCost < 0f || attack.normalDamage < 0)
                    errors.AppendLine($"- 动作 {attack.id} 的范围、架势消耗或伤害无效");
                ValidateAnimation(attack, errors);
            }
            if (attacks.Count == 0) errors.AppendLine("- 至少需要一个动作");

            ValidateControls(catalog.controls?.playerOne, "P1", attacks, errors);
            ValidateControls(catalog.controls?.playerTwo, "P2", attacks, errors);
            ValidateSystemControls(catalog.controls?.system, errors);

            HashSet<string> pairs = new(StringComparer.OrdinalIgnoreCase);
            foreach (ActionPairDefinition pair in catalog.actionPairs ?? Array.Empty<ActionPairDefinition>())
            {
                if (pair == null) continue;
                if (!attacks.ContainsKey(pair.firstAction ?? string.Empty) || !attacks.ContainsKey(pair.secondAction ?? string.Empty))
                    errors.AppendLine($"- 动作对 {pair?.firstAction}+{pair?.secondAction} 引用了不存在的动作");
                if (string.Compare(pair.firstAction, pair.secondAction, StringComparison.OrdinalIgnoreCase) > 0)
                    errors.AppendLine($"- 动作对必须按动作 ID 排序：{pair.firstAction}+{pair.secondAction}");
                string key = $"{pair.firstAction}::{pair.secondAction}";
                if (!pairs.Add(key)) errors.AppendLine($"- 动作对重复：{pair.firstAction}+{pair.secondAction}");
                ValidateParticipant(pair.first, pair, "first", errors);
                ValidateParticipant(pair.second, pair, "second", errors);
            }

            List<string> ids = new(attacks.Keys);
            ids.Sort(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < ids.Count; i++)
                for (int j = i; j < ids.Count; j++)
                    if (!pairs.Contains($"{ids[i]}::{ids[j]}")) errors.AppendLine($"- 缺少动作对：{ids[i]}+{ids[j]}");
            return errors.ToString().TrimEnd();
        }

        private static void ValidateAnimation(AttackDefinition attack, StringBuilder errors)
        {
            AttackAnimationData animation = attack.animation;
            if (animation == null || animation.sourceFrameRate <= 0f)
            {
                errors.AppendLine($"- 动作 {attack.id} 缺少有效动画分段数据");
                return;
            }
            if (!(animation.sourceStartFrame < animation.sourceActiveStartFrame &&
                  animation.sourceActiveStartFrame < animation.sourceActiveEndFrame &&
                  animation.sourceActiveEndFrame < animation.sourceEndFrame))
                errors.AppendLine($"- 动作 {attack.id} 的动画源分段必须依次递增");
        }

        private static void ValidateControls(PlayerControls controls, string label,
            Dictionary<string, AttackDefinition> attacks, StringBuilder errors)
        {
            if (controls == null)
            {
                errors.AppendLine($"- 缺少 {label} 控制配置");
                return;
            }
            ValidateKey(controls.moveLeft, $"{label}.moveLeft", errors);
            ValidateKey(controls.moveRight, $"{label}.moveRight", errors);
            ValidateKey(controls.moveDown, $"{label}.moveDown", errors);
            ValidateKey(controls.moveUp, $"{label}.moveUp", errors);
            HashSet<string> bound = new(StringComparer.OrdinalIgnoreCase);
            foreach (AttackBinding binding in controls.attacks ?? Array.Empty<AttackBinding>())
            {
                if (binding == null || !attacks.ContainsKey(binding.action ?? string.Empty))
                    errors.AppendLine($"- {label} 攻击键引用了不存在的动作：{binding?.action}");
                else if (!bound.Add(binding.action)) errors.AppendLine($"- {label} 重复绑定动作：{binding.action}");
                ValidateKey(binding?.key, $"{label}.{binding?.action}", errors);
            }
            foreach (string id in attacks.Keys)
                if (!bound.Contains(id)) errors.AppendLine($"- {label} 没有绑定动作：{id}");
        }

        private static void ValidateSystemControls(SystemControls controls, StringBuilder errors)
        {
            if (controls == null) { errors.AppendLine("- 缺少系统键配置"); return; }
            ValidateKey(controls.pause, "system.pause", errors);
            ValidateKey(controls.help, "system.help", errors);
            ValidateKey(controls.restoreVitals, "system.restoreVitals", errors);
            ValidateKey(controls.adjustVitals, "system.adjustVitals", errors);
        }

        private static void ValidateKey(string value, string field, StringBuilder errors)
        {
            if (!Enum.TryParse(value, true, out Key key) || key == Key.None)
                errors.AppendLine($"- 键位 {field} 无效：{value}");
        }

        private static void ValidateParticipant(PairParticipantData participant, ActionPairDefinition pair,
            string field, StringBuilder errors)
        {
            if (participant == null) { errors.AppendLine($"- 动作对 {pair.firstAction}+{pair.secondAction} 缺少 {field}"); return; }
            if (!string.Equals(participant.result, "Continue", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(participant.result, "Rebound", StringComparison.OrdinalIgnoreCase))
                errors.AppendLine($"- 动作对 {pair.firstAction}+{pair.secondAction} 的 {field}.result 只能是 Continue 或 Rebound");
            if (participant.speedScale <= 0f || participant.damage < 0 || participant.nextAttackDelayFrames < 0)
                errors.AppendLine($"- 动作对 {pair.firstAction}+{pair.secondAction} 的 {field} 数值无效");
        }
    }
}
