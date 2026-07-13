using System;
using UnityEngine;

namespace Swordman2.Combat
{
    public enum AttackKind { A, B, C }
    public enum SlashSide { RightToLeft, LeftToRight }
    public enum FighterMode { Free, Attack, Rebound, Hit }
    public enum AttackPhase { None, Windup, Active, Recovery, Finished }

    public static class PairActionSpeed
    {
        // 动作对成立后的速度倍率。1 = 保持当前速度，数值越大越快。
        public const float AA = 0.48f;
        public const float AB_A = 0.40f;
        public const float AB_B = 0.3f;
        public const float AC_A = 0.45f;
        public const float AC_C = 0.5f;
        public const float BB = 0.3f;
        public const float BC_B = 0.35f;
        public const float BC_C = 0.4f;
        public const float CC = 0.6f;

        public static float Get(AttackKind own, AttackKind opponent)
        {
            if (own == AttackKind.A && opponent == AttackKind.A) return AA;
            if (own == AttackKind.A && opponent == AttackKind.B) return AB_A;
            if (own == AttackKind.B && opponent == AttackKind.A) return AB_B;
            if (own == AttackKind.A && opponent == AttackKind.C) return AC_A;
            if (own == AttackKind.C && opponent == AttackKind.A) return AC_C;
            if (own == AttackKind.B && opponent == AttackKind.B) return BB;
            if (own == AttackKind.B && opponent == AttackKind.C) return BC_B;
            if (own == AttackKind.C && opponent == AttackKind.B) return BC_C;
            return CC;
        }
    }

    [Serializable]
    public sealed class AttackDefinition
    {
        public const float FramesPerSecond = 30f;
        public const float GlobalActionSpeed = 3.5f;

        public AttackKind Kind;
        public string DisplayName;
        public float StanceCost;
        public int NormalDamage;
        public int TotalFrames;
        public int ActiveStartFrame;
        public int ActiveEndFrame;
        public int ReboundEntryFrame;
        public float BlendSeconds;
        public float Radius;

        public float BaseDuration => TotalFrames / FramesPerSecond / GlobalActionSpeed;
        public float ActiveStart => ActiveStartFrame / FramesPerSecond / GlobalActionSpeed;
        public float ActiveEnd => ActiveEndFrame / FramesPerSecond / GlobalActionSpeed;
        public float ReboundNormalizedTime => Mathf.Clamp01((ReboundEntryFrame - 1f) / (TotalFrames - 1f));

        public static AttackDefinition Create(AttackKind kind)
        {
            switch (kind)
            {
                case AttackKind.A:
                    return New(kind, "轻击", 1f, 1);
                case AttackKind.B:
                    return New(kind, "重击", 2f, 3);
                default:
                    return New(kind, "挑飞", 1f, 1);
            }
        }

        private static AttackDefinition New(AttackKind kind, string name, float stance, int damage)
        {
            return new AttackDefinition
            {
                Kind = kind,
                DisplayName = name,
                StanceCost = stance,
                NormalDamage = damage,
                TotalFrames = 52,
                ActiveStartFrame = 12,
                ActiveEndFrame = 32,
                ReboundEntryFrame = 21,
                BlendSeconds = 0.08f,
                Radius = 2.1f
            };
        }

        public string SuccessClip(SlashSide side)
        {
            if (Kind == AttackKind.B)
                return "Attack_Horizontal_Success";
            return side == SlashSide.RightToLeft ? "Attack_RtoL_Success" : "Attack_LtoR_Success";
        }

        public string ReboundClip(SlashSide side)
        {
            if (Kind == AttackKind.B)
                return "Attack_Horizontal_Blocked";
            return side == SlashSide.RightToLeft ? "Attack_RtoL_Blocked" : "Attack_LtoR_Blocked";
        }
    }

    public sealed class AttackRuntime
    {
        public readonly AttackDefinition Definition;
        public readonly SlashSide Side;
        public readonly float TimeScale;
        public float Elapsed;
        public AttackPhase PreviousPhase;
        public bool HadTemporalOverlap;
        public bool HadMutualRangeOverlap;
        public bool TargetWasInRange;
        public bool Settled;
        public bool PairAudioPlayed;
        public float RuntimeSpeed = 1f;

        public AttackRuntime(AttackDefinition definition, SlashSide side, float timeScale)
        {
            Definition = definition;
            Side = side;
            TimeScale = timeScale;
            PreviousPhase = AttackPhase.Windup;
        }

        public float ActualDuration => Definition.BaseDuration * TimeScale;
        public float ActualActiveStart => Definition.ActiveStart * TimeScale;
        public float ActualActiveEnd => Definition.ActiveEnd * TimeScale;
        public float BaseTime => TimeScale <= 0f ? 0f : Elapsed / TimeScale;

        public AttackPhase Phase
        {
            get
            {
                float time = BaseTime;
                if (time < Definition.ActiveStart) return AttackPhase.Windup;
                if (time <= Definition.ActiveEnd) return AttackPhase.Active;
                if (time < Definition.BaseDuration) return AttackPhase.Recovery;
                return AttackPhase.Finished;
            }
        }
    }
}
