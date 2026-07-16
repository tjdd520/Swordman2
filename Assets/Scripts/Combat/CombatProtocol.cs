using System;

namespace Swordman2.Combat
{
    [Serializable]
    public struct CombatInputCommand
    {
        public long tick;
        public int playerIndex;
        public float moveX;
        public float moveY;
        public string attackAction;

        public CombatInputCommand(long commandTick, int index, float horizontal, float vertical,
            string action = null)
        {
            tick = commandTick;
            playerIndex = index;
            moveX = horizontal;
            moveY = vertical;
            attackAction = action;
        }
    }

    public enum CombatEventType
    {
        AttackStarted,
        ActionPairPredicted,
        ActionPairResolved,
        NormalHit,
        ArmoredHit,
        InterruptedHit,
        Rebound
    }

    [Serializable]
    public sealed class CombatEvent
    {
        public long tick;
        public int sequence;
        public CombatEventType type;
        public int sourcePlayer;
        public int targetPlayer;
        public string actionId;
        public string pairId;
        public SlashSide side;
        public float predictedTimeSeconds;
        public string message;
    }

    [Serializable]
    public sealed class FighterSnapshot
    {
        public int playerIndex;
        public float positionX;
        public float positionZ;
        public float facingX;
        public float facingZ;
        public float moveX;
        public float moveY;
        public FighterMode mode;
        public AttackRuntime currentAttack;
        public float health;
        public float stance;
        public float delayEffectFrames;
        public bool usesTemporaryVitalScale;
        public SlashSide nextSlashSide;
        public float lockedElapsedFrames;
        public float lockedDurationFrames;
        public float freeElapsedFrames;
        public string bufferedAction;
        public float inputBufferElapsedFrames;
        public string lockedActionId;
        public SlashSide lockedActionSide;
        public float lockedAnimationStartFrame;

        public FighterSnapshot Clone(CombatCatalogData catalog)
        {
            return new FighterSnapshot
            {
                playerIndex = playerIndex,
                positionX = positionX,
                positionZ = positionZ,
                facingX = facingX,
                facingZ = facingZ,
                moveX = moveX,
                moveY = moveY,
                mode = mode,
                currentAttack = currentAttack?.Clone(catalog),
                health = health,
                stance = stance,
                delayEffectFrames = delayEffectFrames,
                usesTemporaryVitalScale = usesTemporaryVitalScale,
                nextSlashSide = nextSlashSide,
                lockedElapsedFrames = lockedElapsedFrames,
                lockedDurationFrames = lockedDurationFrames,
                freeElapsedFrames = freeElapsedFrames,
                bufferedAction = bufferedAction,
                inputBufferElapsedFrames = inputBufferElapsedFrames,
                lockedActionId = lockedActionId,
                lockedActionSide = lockedActionSide,
                lockedAnimationStartFrame = lockedAnimationStartFrame
            };
        }
    }

    [Serializable]
    public sealed class CombatSnapshot
    {
        public long tick;
        public FighterSnapshot playerOne;
        public FighterSnapshot playerTwo;
        public string lastEvent;

        public CombatSnapshot Clone(CombatCatalogData catalog)
        {
            return new CombatSnapshot
            {
                tick = tick,
                playerOne = playerOne?.Clone(catalog),
                playerTwo = playerTwo?.Clone(catalog),
                lastEvent = lastEvent
            };
        }
    }
}
