using UnityEngine;

namespace Swordman2.Combat
{
    public enum SlashSide { RightToLeft, LeftToRight }
    public enum FighterMode { Free, Attack, Rebound, Hit }
    public enum AttackPhase { None, Windup, Active, Recovery, Finished }

    public sealed class AttackRuntime
    {
        public readonly AttackDefinition Definition;
        public readonly SlashSide Side;
        public readonly float LogicFrameRate;
        public readonly float TimeScale;
        public float ElapsedFrames;
        public AttackPhase PreviousPhase;
        public bool HadTemporalOverlap;
        public bool HadMutualRangeOverlap;
        public bool TargetWasInRange;
        public bool Settled;
        public bool PairAudioPlayed;
        public float RuntimeSpeed = 1f;

        public AttackRuntime(AttackDefinition definition, SlashSide side, float logicFrameRate,
            int additionalDelayFrames, float delayScale)
        {
            Definition = definition;
            Side = side;
            LogicFrameRate = logicFrameRate;
            float scaledDelay = Mathf.Max(0, additionalDelayFrames) * Mathf.Max(0f, delayScale);
            TimeScale = definition.TotalFrames <= 0 ? 1f :
                (definition.TotalFrames + scaledDelay) / definition.TotalFrames;
            PreviousPhase = AttackPhase.Windup;
        }

        public float WindupFrames => Definition.windupFrames * TimeScale;
        public float ActiveEndFrames => (Definition.windupFrames + Definition.activeFrames) * TimeScale;
        public float TotalFrames => Definition.TotalFrames * TimeScale;
        public float Elapsed => ElapsedFrames / LogicFrameRate;
        public float ActualDuration => TotalFrames / LogicFrameRate;
        public float ActualActiveStart => WindupFrames / LogicFrameRate;
        public float ActualActiveEnd => ActiveEndFrames / LogicFrameRate;

        public AttackPhase Phase
        {
            get
            {
                if (ElapsedFrames < WindupFrames) return AttackPhase.Windup;
                if (ElapsedFrames < ActiveEndFrames) return AttackPhase.Active;
                if (ElapsedFrames < TotalFrames) return AttackPhase.Recovery;
                return AttackPhase.Finished;
            }
        }

        public float SourceAnimationTime
        {
            get
            {
                AttackAnimationData source = Definition.animation;
                float sourceFrame;
                if (Phase == AttackPhase.Windup)
                {
                    float progress = WindupFrames <= 0f ? 1f : ElapsedFrames / WindupFrames;
                    sourceFrame = Mathf.Lerp(source.sourceStartFrame, source.sourceActiveStartFrame, progress);
                }
                else if (Phase == AttackPhase.Active)
                {
                    float progress = Definition.activeFrames <= 0 ? 1f :
                        (ElapsedFrames - WindupFrames) / (Definition.activeFrames * TimeScale);
                    sourceFrame = Mathf.Lerp(source.sourceActiveStartFrame, source.sourceActiveEndFrame, progress);
                }
                else
                {
                    float recoveryStart = ActiveEndFrames;
                    float progress = Definition.recoveryFrames <= 0 ? 1f :
                        (ElapsedFrames - recoveryStart) / (Definition.recoveryFrames * TimeScale);
                    sourceFrame = Mathf.Lerp(source.sourceActiveEndFrame, source.sourceEndFrame, progress);
                }
                return Mathf.Max(0f, sourceFrame - source.sourceStartFrame) / source.sourceFrameRate;
            }
        }
    }
}
