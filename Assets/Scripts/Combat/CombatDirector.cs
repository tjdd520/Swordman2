using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Swordman2.Combat
{
    public sealed class CombatDirector : MonoBehaviour
    {
        public const int BCPairDamage = 3;
        public const float PairEffectDuration = 0.3f;
        private const float SimulationStep = 1f / 120f;
        private float accumulator;
        private Vector2 playerOneMove;
        private Vector2 playerTwoMove;
        private CombatAudio combatAudio;

        public FighterController PlayerOne { get; private set; }
        public FighterController PlayerTwo { get; private set; }
        public string LastEvent { get; private set; } = "战斗开始";

        public void Initialize(FighterController playerOne, FighterController playerTwo, CombatAudio audio)
        {
            PlayerOne = playerOne;
            PlayerTwo = playerTwo;
            combatAudio = audio;
            playerOne.Opponent = playerTwo;
            playerTwo.Opponent = playerOne;
            playerOne.UpdateFacing();
            playerTwo.UpdateFacing();
        }

        private void Update()
        {
            if (PlayerOne == null || PlayerTwo == null) return;
            if (Time.timeScale <= 0f) return;
            ReadInput();
            accumulator = Mathf.Min(accumulator + Time.deltaTime, 0.1f);
            while (accumulator >= SimulationStep)
            {
                Simulate(SimulationStep);
                accumulator -= SimulationStep;
            }
        }

        private void ReadInput()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                playerOneMove = playerTwoMove = Vector2.zero;
                return;
            }

            playerOneMove = ReadMove(keyboard.aKey, keyboard.dKey, keyboard.sKey, keyboard.wKey);
            playerTwoMove = ReadMove(keyboard.leftArrowKey, keyboard.rightArrowKey,
                keyboard.downArrowKey, keyboard.upArrowKey);

            if (keyboard.fKey.wasPressedThisFrame) PlayerOne.SubmitAttack(AttackKind.A);
            if (keyboard.gKey.wasPressedThisFrame) PlayerOne.SubmitAttack(AttackKind.B);
            if (keyboard.hKey.wasPressedThisFrame) PlayerOne.SubmitAttack(AttackKind.C);
            if (keyboard.commaKey.wasPressedThisFrame) PlayerTwo.SubmitAttack(AttackKind.A);
            if (keyboard.periodKey.wasPressedThisFrame) PlayerTwo.SubmitAttack(AttackKind.B);
            if (keyboard.slashKey.wasPressedThisFrame) PlayerTwo.SubmitAttack(AttackKind.C);
        }

        private void Simulate(float deltaTime)
        {
            PlayerOne.SetMovementInput(playerOneMove);
            PlayerTwo.SetMovementInput(playerTwoMove);
            PlayerOne.UpdateFacing();
            PlayerTwo.UpdateFacing();
            PlayerOne.UpdateMovement(deltaTime);
            PlayerTwo.UpdateMovement(deltaTime);
            PlayerOne.UpdateFacing();
            PlayerTwo.UpdateFacing();

            PlayerOne.Advance(deltaTime);
            PlayerTwo.Advance(deltaTime);
            ResolveCombat();
        }

        private void ResolveCombat()
        {
            AttackRuntime first = PlayerOne.CurrentAttack;
            AttackRuntime second = PlayerTwo.CurrentAttack;

            TryPlayPredictedPairAudio(first, second);

            bool firstActive = first != null && first.Phase == AttackPhase.Active;
            bool secondActive = second != null && second.Phase == AttackPhase.Active;

            if (firstActive && PlayerOne.IsInRangeOf(PlayerTwo, first.Definition.Radius))
                first.TargetWasInRange = true;
            if (secondActive && PlayerTwo.IsInRangeOf(PlayerOne, second.Definition.Radius))
                second.TargetWasInRange = true;

            if (firstActive && secondActive)
            {
                first.HadTemporalOverlap = true;
                second.HadTemporalOverlap = true;
                bool mutualRange = PlayerOne.IsInRangeOf(PlayerTwo, first.Definition.Radius) &&
                                   PlayerTwo.IsInRangeOf(PlayerOne, second.Definition.Radius);
                if (mutualRange)
                {
                    first.HadMutualRangeOverlap = true;
                    second.HadMutualRangeOverlap = true;
                    if (!first.Settled && !second.Settled)
                        ResolveActionPair(first.Definition.Kind, second.Definition.Kind);
                }
            }

            ResolveExpiredActiveWindow(PlayerOne, PlayerTwo);
            ResolveExpiredActiveWindow(PlayerTwo, PlayerOne);
        }

        private void ResolveExpiredActiveWindow(FighterController attacker, FighterController target)
        {
            AttackRuntime attack = attacker.CurrentAttack;
            if (attack == null || attack.Settled) return;
            bool justEnded = attack.PreviousPhase == AttackPhase.Active && attack.Phase != AttackPhase.Active;
            if (!justEnded) return;

            if (!attack.HadTemporalOverlap && attack.TargetWasInRange)
            {
                attacker.MarkAttackSettled();
                target.ReceiveNormalHit(attack.Definition.NormalDamage);
                combatAudio?.PlayNormalHit();
                LastEvent = $"P{attacker.PlayerIndex} {attack.Definition.DisplayName}普通命中 P{target.PlayerIndex}";
            }
            else
            {
                attacker.MarkAttackSettled();
                LastEvent = attack.HadTemporalOverlap
                    ? "有效期有重叠但未满足双方攻击范围，双方均不普通命中"
                    : $"P{attacker.PlayerIndex} 攻击落空";
            }
        }

        private void ResolveActionPair(AttackKind first, AttackKind second)
        {
            PlayPairAudioIfNeeded(PlayerOne.CurrentAttack, PlayerTwo.CurrentAttack, Time.time);
            float firstPairSpeed = PairActionSpeed.Get(first, second);
            float secondPairSpeed = PairActionSpeed.Get(second, first);
            // 先确定完整结果，再同时应用，避免玩家脚本顺序影响结果。
            if ((first == AttackKind.B && second == AttackKind.C) ||
                (first == AttackKind.C && second == AttackKind.B))
            {
                FighterController heavy = first == AttackKind.B ? PlayerOne : PlayerTwo;
                FighterController launcher = first == AttackKind.C ? PlayerOne : PlayerTwo;
                heavy.MarkAttackSettled();
                launcher.MarkAttackSettled();
                launcher.ApplyPairDamage(BCPairDamage);
                launcher.ApplyEffect(PairEffectDuration);
                heavy.ApplyPairContinuationSpeed(first == AttackKind.B ? firstPairSpeed : secondPairSpeed);
                launcher.EnterRebound(first == AttackKind.C ? firstPairSpeed : secondPairSpeed);
                LastEvent = $"B对C：P{heavy.PlayerIndex}横扫成功，P{launcher.PlayerIndex}血量-{BCPairDamage}并获得{PairEffectDuration:0.0}秒效果";
                return;
            }

            bool aAgainstC = (first == AttackKind.A && second == AttackKind.C) ||
                             (first == AttackKind.C && second == AttackKind.A);
            if (aAgainstC)
            {
                FighterController light = first == AttackKind.A ? PlayerOne : PlayerTwo;
                light.ApplyEffect(PairEffectDuration);
            }

            PlayerOne.MarkAttackSettled();
            PlayerTwo.MarkAttackSettled();
            PlayerOne.EnterRebound(firstPairSpeed);
            PlayerTwo.EnterRebound(secondPairSpeed);
            LastEvent = aAgainstC
                ? $"A对C：双方弹回，P{(first == AttackKind.A ? 1 : 2)}获得0.3秒效果"
                : $"{first}对{second}：双方弹回";
        }

        private void TryPlayPredictedPairAudio(AttackRuntime first, AttackRuntime second)
        {
            if (combatAudio == null || first == null || second == null ||
                first.Settled || second.Settled || first.PairAudioPlayed || second.PairAudioPlayed)
                return;

            float firstStartRemaining = first.ActualActiveStart - first.Elapsed;
            float secondStartRemaining = second.ActualActiveStart - second.Elapsed;
            float overlapStart = Mathf.Max(firstStartRemaining, secondStartRemaining);

            float firstEndRemaining = first.ActualActiveEnd - first.Elapsed;
            float secondEndRemaining = second.ActualActiveEnd - second.Elapsed;
            float overlapEnd = Mathf.Min(firstEndRemaining, secondEndRemaining);

            float timeUntilOverlap = Mathf.Max(0f, overlapStart);
            bool hasFutureOverlap = overlapEnd >= timeUntilOverlap;
            if (!hasFutureOverlap || timeUntilOverlap > CombatAudio.PairPredictionLead)
                return;

            // 攻击期间双方不能移动，因此当前互相处于范围内也代表预测时刻仍在范围内。
            bool mutualRange = PlayerOne.IsInRangeOf(PlayerTwo, first.Definition.Radius) &&
                               PlayerTwo.IsInRangeOf(PlayerOne, second.Definition.Radius);
            if (!mutualRange) return;

            PlayPairAudioIfNeeded(first, second, Time.time + timeUntilOverlap);
        }

        private void PlayPairAudioIfNeeded(AttackRuntime first, AttackRuntime second, float pairTime)
        {
            if (combatAudio == null || first == null || second == null ||
                first.PairAudioPlayed || second.PairAudioPlayed)
                return;

            first.PairAudioPlayed = true;
            second.PairAudioPlayed = true;
            combatAudio.PlayActionPair(pairTime);
        }

        private static Vector2 ReadMove(KeyControl left, KeyControl right, KeyControl down, KeyControl up)
        {
            float x = (right.isPressed ? 1f : 0f) - (left.isPressed ? 1f : 0f);
            float y = (up.isPressed ? 1f : 0f) - (down.isPressed ? 1f : 0f);
            return Vector2.ClampMagnitude(new Vector2(x, y), 1f);
        }

        private void OnDestroy()
        {
            PlayerOne?.Dispose();
            PlayerTwo?.Dispose();
        }
    }
}
