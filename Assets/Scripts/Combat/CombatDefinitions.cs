using System;

namespace Swordman2.Combat
{
    public enum SlashSide { RightToLeft, LeftToRight }
    public enum FighterMode { Free, Attack, Rebound, Hit }
    public enum AttackPhase { None, Windup, Active, Recovery, Finished }

    [Serializable]
    public sealed class AttackRuntime
    {
        public string actionId;
        public SlashSide side;
        public float logicFrameRate;
        public float timeScale = 1f;
        public float elapsedFrames;
        public AttackPhase previousPhase = AttackPhase.Windup;
        public bool hadTemporalOverlap;
        public bool hadMutualRangeOverlap;
        public bool targetWasInRange;
        public bool settled;
        public bool pairAudioPlayed;
        public float runtimeSpeed = 1f;

        [NonSerialized] private AttackDefinition definition;

        public AttackDefinition Definition => definition;
        public SlashSide Side => side;
        public float LogicFrameRate => logicFrameRate;
        public float TimeScale => timeScale;
        public float ElapsedFrames { get => elapsedFrames; set => elapsedFrames = value; }
        public AttackPhase PreviousPhase { get => previousPhase; set => previousPhase = value; }
        public bool HadTemporalOverlap { get => hadTemporalOverlap; set => hadTemporalOverlap = value; }
        public bool HadMutualRangeOverlap { get => hadMutualRangeOverlap; set => hadMutualRangeOverlap = value; }
        public bool TargetWasInRange { get => targetWasInRange; set => targetWasInRange = value; }
        public bool Settled { get => settled; set => settled = value; }
        public bool PairAudioPlayed { get => pairAudioPlayed; set => pairAudioPlayed = value; }
        public float RuntimeSpeed { get => runtimeSpeed; set => runtimeSpeed = value; }

        public AttackRuntime(AttackDefinition attackDefinition, SlashSide slashSide, float frameRate,
            int additionalDelayFrames, float delayScale)
        {
            Bind(attackDefinition);
            actionId = attackDefinition.id;
            side = slashSide;
            logicFrameRate = frameRate;
            float scaledDelay = Math.Max(0, additionalDelayFrames) * Math.Max(0f, delayScale);
            timeScale = attackDefinition.TotalFrames <= 0 ? 1f :
                (attackDefinition.TotalFrames + scaledDelay) / attackDefinition.TotalFrames;
        }

        private AttackRuntime() { }

        public void Bind(AttackDefinition attackDefinition)
        {
            definition = attackDefinition ?? throw new ArgumentNullException(nameof(attackDefinition));
            if (string.IsNullOrWhiteSpace(actionId)) actionId = attackDefinition.id;
        }

        public AttackRuntime Clone(CombatCatalogData catalog)
        {
            AttackRuntime clone = new AttackRuntime
            {
                actionId = actionId,
                side = side,
                logicFrameRate = logicFrameRate,
                timeScale = timeScale,
                elapsedFrames = elapsedFrames,
                previousPhase = previousPhase,
                hadTemporalOverlap = hadTemporalOverlap,
                hadMutualRangeOverlap = hadMutualRangeOverlap,
                targetWasInRange = targetWasInRange,
                settled = settled,
                pairAudioPlayed = pairAudioPlayed,
                runtimeSpeed = runtimeSpeed
            };
            clone.Bind(catalog.GetAttack(actionId));
            return clone;
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
                    sourceFrame = Lerp(source.sourceStartFrame, source.sourceActiveStartFrame, progress);
                }
                else if (Phase == AttackPhase.Active)
                {
                    float progress = Definition.activeFrames <= 0 ? 1f :
                        (ElapsedFrames - WindupFrames) / (Definition.activeFrames * TimeScale);
                    sourceFrame = Lerp(source.sourceActiveStartFrame, source.sourceActiveEndFrame, progress);
                }
                else
                {
                    float progress = Definition.recoveryFrames <= 0 ? 1f :
                        (ElapsedFrames - ActiveEndFrames) / (Definition.recoveryFrames * TimeScale);
                    sourceFrame = Lerp(source.sourceActiveEndFrame, source.sourceEndFrame, progress);
                }
                return Math.Max(0f, sourceFrame - source.sourceStartFrame) / source.sourceFrameRate;
            }
        }

        private static float Lerp(float from, float to, float progress) => from + (to - from) * progress;
    }
}
