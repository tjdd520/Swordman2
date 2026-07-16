using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Swordman2.Combat
{
    public sealed class CombatDirector : MonoBehaviour
    {
        private CombatCatalogData catalog;
        private float simulationStep;
        private float accumulator;
        private Vector2 playerOneMove;
        private Vector2 playerTwoMove;
        private CombatAudio combatAudio;

        public CombatCatalogData Catalog => catalog;
        public FighterController PlayerOne { get; private set; }
        public FighterController PlayerTwo { get; private set; }
        public string LastEvent { get; private set; } = "战斗开始";

        public void Initialize(FighterController playerOne, FighterController playerTwo,
            CombatAudio audio, CombatCatalogData combatCatalog)
        {
            PlayerOne = playerOne;
            PlayerTwo = playerTwo;
            combatAudio = audio;
            catalog = combatCatalog;
            simulationStep = 1f / catalog.settings.logicFrameRate;
            playerOne.Opponent = playerTwo;
            playerTwo.Opponent = playerOne;
            playerOne.UpdateFacing();
            playerTwo.UpdateFacing();
        }

        private void Update()
        {
            if (PlayerOne == null || PlayerTwo == null || Time.timeScale <= 0f) return;
            ReadInput();
            accumulator = Mathf.Min(accumulator + Time.deltaTime, 0.1f);
            while (accumulator >= simulationStep)
            {
                Simulate(simulationStep);
                accumulator -= simulationStep;
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
            playerOneMove = ReadMove(keyboard, catalog.controls.playerOne);
            playerTwoMove = ReadMove(keyboard, catalog.controls.playerTwo);
            ReadAttacks(keyboard, catalog.controls.playerOne, PlayerOne);
            ReadAttacks(keyboard, catalog.controls.playerTwo, PlayerTwo);
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

            if (firstActive && PlayerOne.IsInRangeOf(PlayerTwo, first.Definition.radius)) first.TargetWasInRange = true;
            if (secondActive && PlayerTwo.IsInRangeOf(PlayerOne, second.Definition.radius)) second.TargetWasInRange = true;
            if (firstActive && secondActive)
            {
                first.HadTemporalOverlap = true;
                second.HadTemporalOverlap = true;
                bool mutualRange = PlayerOne.IsInRangeOf(PlayerTwo, first.Definition.radius) &&
                                   PlayerTwo.IsInRangeOf(PlayerOne, second.Definition.radius);
                if (mutualRange)
                {
                    first.HadMutualRangeOverlap = true;
                    second.HadMutualRangeOverlap = true;
                    if (!first.Settled && !second.Settled) ResolveActionPair(first, second);
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
            attacker.MarkAttackSettled();
            if (!attack.HadTemporalOverlap && attack.TargetWasInRange)
            {
                AttackRuntime targetAttack = target.CurrentAttack;
                bool targetWasInWindup = target.Mode == FighterMode.Attack && targetAttack != null &&
                                         targetAttack.PreviousPhase == AttackPhase.Windup;
                bool poiseProtected = targetWasInWindup &&
                                      targetAttack.Definition.startupPoise > attack.Definition.startupPoise;
                if (poiseProtected)
                    target.ReceiveArmoredHit(attack.Definition.normalDamage);
                else
                    target.ReceiveInterruptingHit(attack.Definition.normalDamage);
                combatAudio?.PlayNormalHit();
                LastEvent = poiseProtected
                    ? $"P{attacker.PlayerIndex} {attack.Definition.displayName}命中 P{target.PlayerIndex}，后手以更高出手韧性继续动作"
                    : $"P{attacker.PlayerIndex} {attack.Definition.displayName}普通命中并打断 P{target.PlayerIndex}";
            }
            else
            {
                LastEvent = attack.HadTemporalOverlap
                    ? "有效期有重叠但未满足双方攻击范围，双方均不普通命中"
                    : $"P{attacker.PlayerIndex} 攻击落空";
            }
        }

        private void ResolveActionPair(AttackRuntime playerOneAttack, AttackRuntime playerTwoAttack)
        {
            PlayPairAudioIfNeeded(playerOneAttack, playerTwoAttack, Time.time);
            ActionPairDefinition pair = catalog.GetPair(playerOneAttack.Definition.id,
                playerTwoAttack.Definition.id, out bool swapped);
            if (pair == null)
            {
                Debug.LogError($"运行时缺少动作对：{playerOneAttack.Definition.id}+{playerTwoAttack.Definition.id}");
                PlayerOne.MarkAttackSettled();
                PlayerTwo.MarkAttackSettled();
                return;
            }

            PairParticipantData playerOneData = swapped ? pair.second : pair.first;
            PairParticipantData playerTwoData = swapped ? pair.first : pair.second;
            PlayerOne.MarkAttackSettled();
            PlayerTwo.MarkAttackSettled();
            ApplyPairValues(PlayerOne, playerOneData);
            ApplyPairValues(PlayerTwo, playerTwoData);
            ApplyPairResult(PlayerOne, playerOneData);
            ApplyPairResult(PlayerTwo, playerTwoData);
            LastEvent = BuildPairEvent(pair, PlayerOne, playerOneData, PlayerTwo, playerTwoData);
        }

        private static void ApplyPairValues(FighterController fighter, PairParticipantData data)
        {
            if (data.damage > 0) fighter.ApplyPairDamage(data.damage);
            if (data.nextAttackDelayFrames > 0) fighter.ApplyDelayEffect(data.nextAttackDelayFrames);
        }

        private static void ApplyPairResult(FighterController fighter, PairParticipantData data)
        {
            if (data.Result == PairParticipantResult.Continue)
                fighter.ApplyPairContinuationSpeed(data.speedScale);
            else
                fighter.EnterRebound(data.speedScale);
        }

        private static string BuildPairEvent(ActionPairDefinition pair, FighterController first,
            PairParticipantData firstData, FighterController second, PairParticipantData secondData)
        {
            string firstEffect = ParticipantEvent(first, firstData);
            string secondEffect = ParticipantEvent(second, secondData);
            return $"{pair.displayName}：{firstEffect}；{secondEffect}";
        }

        private static string ParticipantEvent(FighterController fighter, PairParticipantData data)
        {
            string result = data.Result == PairParticipantResult.Continue ? "继续动作" : "弹回";
            if (data.damage > 0) result += $"、伤害 {data.damage}";
            if (data.nextAttackDelayFrames > 0) result += $"、延迟条 +{data.nextAttackDelayFrames}帧";
            return $"P{fighter.PlayerIndex} {result}";
        }

        private void TryPlayPredictedPairAudio(AttackRuntime first, AttackRuntime second)
        {
            if (combatAudio == null || first == null || second == null || first.Settled || second.Settled ||
                first.PairAudioPlayed || second.PairAudioPlayed) return;
            float firstStartRemaining = first.ActualActiveStart - first.Elapsed;
            float secondStartRemaining = second.ActualActiveStart - second.Elapsed;
            float overlapStart = Mathf.Max(firstStartRemaining, secondStartRemaining);
            float firstEndRemaining = first.ActualActiveEnd - first.Elapsed;
            float secondEndRemaining = second.ActualActiveEnd - second.Elapsed;
            float overlapEnd = Mathf.Min(firstEndRemaining, secondEndRemaining);
            float timeUntilOverlap = Mathf.Max(0f, overlapStart);
            if (overlapEnd < timeUntilOverlap || timeUntilOverlap > catalog.audio.pairPredictionLeadSeconds) return;
            bool mutualRange = PlayerOne.IsInRangeOf(PlayerTwo, first.Definition.radius) &&
                               PlayerTwo.IsInRangeOf(PlayerOne, second.Definition.radius);
            if (mutualRange) PlayPairAudioIfNeeded(first, second, Time.time + timeUntilOverlap);
        }

        private void PlayPairAudioIfNeeded(AttackRuntime first, AttackRuntime second, float pairTime)
        {
            if (combatAudio == null || first == null || second == null ||
                first.PairAudioPlayed || second.PairAudioPlayed) return;
            first.PairAudioPlayed = true;
            second.PairAudioPlayed = true;
            combatAudio.PlayActionPair(pairTime);
        }

        private static Vector2 ReadMove(Keyboard keyboard, PlayerControls controls)
        {
            float x = (IsPressed(keyboard, controls.moveRight) ? 1f : 0f) -
                      (IsPressed(keyboard, controls.moveLeft) ? 1f : 0f);
            float y = (IsPressed(keyboard, controls.moveUp) ? 1f : 0f) -
                      (IsPressed(keyboard, controls.moveDown) ? 1f : 0f);
            return Vector2.ClampMagnitude(new Vector2(x, y), 1f);
        }

        private static void ReadAttacks(Keyboard keyboard, PlayerControls controls, FighterController fighter)
        {
            foreach (AttackBinding binding in controls.attacks)
                if (WasPressed(keyboard, binding.key)) fighter.SubmitAttack(binding.action);
        }

        private static bool IsPressed(Keyboard keyboard, string keyName) =>
            Enum.TryParse(keyName, true, out Key key) && keyboard[key].isPressed;

        private static bool WasPressed(Keyboard keyboard, string keyName) =>
            Enum.TryParse(keyName, true, out Key key) && keyboard[key].wasPressedThisFrame;

        private void OnDestroy()
        {
            PlayerOne?.Dispose();
            PlayerTwo?.Dispose();
        }
    }
}
