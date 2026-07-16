using System;
using System.Collections.Generic;

namespace Swordman2.Combat
{
    public sealed class CombatSimulation
    {
        private const SlashSide DefaultSlashSide = SlashSide.RightToLeft;
        private readonly CombatCatalogData catalog;
        private readonly CombatSettings settings;
        private readonly List<CombatEvent> events = new();
        private FighterSnapshot playerOne;
        private FighterSnapshot playerTwo;
        private string lastEvent = "战斗开始";

        public long Tick { get; private set; }
        public IReadOnlyList<CombatEvent> Events => events;

        public CombatSimulation(CombatCatalogData combatCatalog,
            float playerOneX, float playerOneZ, float playerTwoX, float playerTwoZ)
        {
            catalog = combatCatalog ?? throw new ArgumentNullException(nameof(combatCatalog));
            settings = catalog.settings;
            playerOne = CreateFighter(1, playerOneX, playerOneZ);
            playerTwo = CreateFighter(2, playerTwoX, playerTwoZ);
            RecalculateFacing();
        }

        public CombatSnapshot Step(CombatInputCommand firstCommand, CombatInputCommand secondCommand)
        {
            long nextTick = Tick + 1;
            ValidateCommand(firstCommand, nextTick, 1);
            ValidateCommand(secondCommand, nextTick, 2);
            Tick = nextTick;
            events.Clear();
            ApplyCommand(playerOne, firstCommand);
            ApplyCommand(playerTwo, secondCommand);
            RecalculateFacing();
            AdvanceMovement();
            RecalculateFacing();
            AdvanceFighter(playerOne);
            AdvanceFighter(playerTwo);
            ResolveCombat();
            return CaptureSnapshot();
        }

        public CombatSnapshot CaptureSnapshot()
        {
            return new CombatSnapshot
            {
                tick = Tick,
                playerOne = playerOne.Clone(catalog),
                playerTwo = playerTwo.Clone(catalog),
                lastEvent = lastEvent
            };
        }

        public void ApplySnapshot(CombatSnapshot snapshot)
        {
            if (snapshot?.playerOne == null || snapshot.playerTwo == null)
                throw new ArgumentException("战斗快照缺少玩家状态", nameof(snapshot));
            Tick = snapshot.tick;
            playerOne = snapshot.playerOne.Clone(catalog);
            playerTwo = snapshot.playerTwo.Clone(catalog);
            lastEvent = snapshot.lastEvent ?? string.Empty;
            events.Clear();
        }

        public void Teleport(int playerIndex, float x, float z)
        {
            FighterSnapshot fighter = GetFighter(playerIndex);
            fighter.positionX = x;
            fighter.positionZ = z;
            RecalculateFacing();
        }

        public void RecalculateFacing()
        {
            SetFacingToward(playerOne, playerTwo);
            SetFacingToward(playerTwo, playerOne);
        }

        public void RestoreVitals(int playerIndex)
        {
            FighterSnapshot fighter = GetFighter(playerIndex);
            fighter.health = settings.maxHealth;
            fighter.stance = settings.maxStance;
            fighter.usesTemporaryVitalScale = false;
        }

        public void SetTemporaryVitals(int playerIndex, float health, float stance)
        {
            FighterSnapshot fighter = GetFighter(playerIndex);
            fighter.health = Clamp(health, 0f, settings.temporaryVitalLimit);
            fighter.stance = Clamp(stance, 0f, settings.temporaryVitalLimit);
            fighter.usesTemporaryVitalScale = true;
        }

        private FighterSnapshot CreateFighter(int index, float x, float z)
        {
            return new FighterSnapshot
            {
                playerIndex = index,
                positionX = x,
                positionZ = z,
                facingZ = 1f,
                mode = FighterMode.Free,
                health = settings.maxHealth,
                stance = settings.maxStance,
                nextSlashSide = DefaultSlashSide
            };
        }

        private void ApplyCommand(FighterSnapshot fighter, CombatInputCommand command)
        {
            Normalize(command.moveX, command.moveY, out fighter.moveX, out fighter.moveY);
            if (!string.IsNullOrWhiteSpace(command.attackAction))
                SubmitAttack(fighter, command.attackAction);
        }

        private void SubmitAttack(FighterSnapshot fighter, string actionId)
        {
            if (catalog.GetAttack(actionId) == null) return;
            if (fighter.mode == FighterMode.Free && TryStartAttack(fighter, actionId))
            {
                ClearInputBuffer(fighter);
                return;
            }
            fighter.bufferedAction = actionId;
            fighter.inputBufferElapsedFrames = 0f;
        }

        private bool TryStartAttack(FighterSnapshot fighter, string actionId)
        {
            if (fighter.mode != FighterMode.Free) return false;
            AttackDefinition definition = catalog.GetAttack(actionId);
            if (definition == null || fighter.stance + 0.0001f < definition.stanceCost) return false;

            fighter.stance -= definition.stanceCost;
            SlashSide side = fighter.nextSlashSide;
            fighter.nextSlashSide = side == SlashSide.RightToLeft
                ? SlashSide.LeftToRight
                : SlashSide.RightToLeft;
            int delayFrames = (int)Math.Round(fighter.delayEffectFrames);
            fighter.delayEffectFrames = 0f;
            fighter.currentAttack = new AttackRuntime(definition, side, settings.logicFrameRate,
                delayFrames, settings.delayEffectAttackScale);
            fighter.mode = FighterMode.Attack;
            fighter.freeElapsedFrames = 0f;
            Emit(new CombatEvent
            {
                tick = Tick,
                type = CombatEventType.AttackStarted,
                sourcePlayer = fighter.playerIndex,
                actionId = actionId,
                side = side
            });
            return true;
        }

        private void AdvanceMovement()
        {
            float step = 1f / settings.logicFrameRate;
            MoveFighter(playerOne, step);
            MoveFighter(playerTwo, step);
        }

        private void MoveFighter(FighterSnapshot fighter, float step)
        {
            if (fighter.mode != FighterMode.Free) return;
            float rightX = fighter.facingZ;
            float rightZ = -fighter.facingX;
            float velocityX = fighter.facingX * fighter.moveY + rightX * fighter.moveX;
            float velocityZ = fighter.facingZ * fighter.moveY + rightZ * fighter.moveX;
            fighter.positionX += velocityX * settings.moveSpeed * step;
            fighter.positionZ += velocityZ * settings.moveSpeed * step;
        }

        private void AdvanceFighter(FighterSnapshot fighter)
        {
            AdvanceInputBuffer(fighter);
            if (fighter.delayEffectFrames > 0f)
                fighter.delayEffectFrames = Math.Max(0f, fighter.delayEffectFrames - 1f);

            if (fighter.mode == FighterMode.Attack && fighter.currentAttack != null)
            {
                fighter.currentAttack.PreviousPhase = fighter.currentAttack.Phase;
                fighter.currentAttack.ElapsedFrames += fighter.currentAttack.RuntimeSpeed;
                if (fighter.currentAttack.Phase == AttackPhase.Finished) FinishLockedAction(fighter);
                return;
            }

            if (fighter.mode == FighterMode.Rebound || fighter.mode == FighterMode.Hit)
            {
                fighter.lockedElapsedFrames += 1f;
                if (fighter.lockedElapsedFrames >= fighter.lockedDurationFrames) FinishLockedAction(fighter);
                return;
            }

            fighter.freeElapsedFrames += 1f;
            if (fighter.freeElapsedFrames >= settings.stanceRecoveryDelayFrames && fighter.stance < settings.maxStance)
            {
                float ratePerFrame = settings.maxStance / Math.Max(1, settings.stanceRecoveryDurationFrames);
                fighter.stance = Math.Min(settings.maxStance, fighter.stance + ratePerFrame);
            }
            TryStartLatestBufferedInput(fighter);
        }

        private void AdvanceInputBuffer(FighterSnapshot fighter)
        {
            if (string.IsNullOrWhiteSpace(fighter.bufferedAction)) return;
            fighter.inputBufferElapsedFrames += 1f;
            if (fighter.inputBufferElapsedFrames >= settings.inputBufferFrames) ClearInputBuffer(fighter);
        }

        private bool TryStartLatestBufferedInput(FighterSnapshot fighter)
        {
            if (fighter.mode != FighterMode.Free || string.IsNullOrWhiteSpace(fighter.bufferedAction)) return false;
            string action = fighter.bufferedAction;
            if (!TryStartAttack(fighter, action)) return false;
            ClearInputBuffer(fighter);
            return true;
        }

        private static void ClearInputBuffer(FighterSnapshot fighter)
        {
            fighter.bufferedAction = null;
            fighter.inputBufferElapsedFrames = 0f;
        }

        private void FinishLockedAction(FighterSnapshot fighter)
        {
            bool canContinueHandSequence = fighter.mode == FighterMode.Attack;
            fighter.mode = FighterMode.Free;
            fighter.currentAttack = null;
            fighter.lockedElapsedFrames = 0f;
            fighter.lockedDurationFrames = 0f;
            fighter.lockedActionId = null;
            fighter.lockedAnimationStartFrame = 0f;
            fighter.freeElapsedFrames = 0f;
            if (!canContinueHandSequence) fighter.nextSlashSide = DefaultSlashSide;
            if (!TryStartLatestBufferedInput(fighter)) fighter.nextSlashSide = DefaultSlashSide;
        }

        private void ResolveCombat()
        {
            AttackRuntime first = playerOne.currentAttack;
            AttackRuntime second = playerTwo.currentAttack;
            TryPredictActionPair(first, second);
            bool firstActive = first != null && first.Phase == AttackPhase.Active;
            bool secondActive = second != null && second.Phase == AttackPhase.Active;

            if (firstActive && IsInRange(playerOne, playerTwo, first.Definition.radius))
                first.TargetWasInRange = true;
            if (secondActive && IsInRange(playerTwo, playerOne, second.Definition.radius))
                second.TargetWasInRange = true;

            if (firstActive && secondActive)
            {
                first.HadTemporalOverlap = true;
                second.HadTemporalOverlap = true;
                bool mutualRange = IsInRange(playerOne, playerTwo, first.Definition.radius) &&
                                   IsInRange(playerTwo, playerOne, second.Definition.radius);
                if (mutualRange)
                {
                    first.HadMutualRangeOverlap = true;
                    second.HadMutualRangeOverlap = true;
                    if (!first.Settled && !second.Settled) ResolveActionPair(first, second);
                }
            }

            ResolveExpiredActiveWindow(playerOne, playerTwo);
            ResolveExpiredActiveWindow(playerTwo, playerOne);
        }

        private void TryPredictActionPair(AttackRuntime first, AttackRuntime second)
        {
            if (first == null || second == null || first.Settled || second.Settled ||
                first.PairAudioPlayed || second.PairAudioPlayed) return;
            float firstStartRemaining = first.ActualActiveStart - first.Elapsed;
            float secondStartRemaining = second.ActualActiveStart - second.Elapsed;
            float overlapStart = Math.Max(firstStartRemaining, secondStartRemaining);
            float firstEndRemaining = first.ActualActiveEnd - first.Elapsed;
            float secondEndRemaining = second.ActualActiveEnd - second.Elapsed;
            float overlapEnd = Math.Min(firstEndRemaining, secondEndRemaining);
            float timeUntilOverlap = Math.Max(0f, overlapStart);
            if (overlapEnd < timeUntilOverlap || timeUntilOverlap > catalog.audio.pairPredictionLeadSeconds) return;
            if (!IsInRange(playerOne, playerTwo, first.Definition.radius) ||
                !IsInRange(playerTwo, playerOne, second.Definition.radius)) return;
            EmitPairPrediction(first, second, Tick / (float)settings.logicFrameRate + timeUntilOverlap);
        }

        private void EmitPairPrediction(AttackRuntime first, AttackRuntime second, float predictedTimeSeconds)
        {
            if (first.PairAudioPlayed || second.PairAudioPlayed) return;
            first.PairAudioPlayed = true;
            second.PairAudioPlayed = true;
            Emit(new CombatEvent
            {
                tick = Tick,
                type = CombatEventType.ActionPairPredicted,
                sourcePlayer = 1,
                targetPlayer = 2,
                pairId = PairId(first.Definition.id, second.Definition.id),
                predictedTimeSeconds = predictedTimeSeconds
            });
        }

        private void ResolveActionPair(AttackRuntime firstAttack, AttackRuntime secondAttack)
        {
            EmitPairPrediction(firstAttack, secondAttack, Tick / (float)settings.logicFrameRate);
            ActionPairDefinition pair = catalog.GetPair(firstAttack.Definition.id,
                secondAttack.Definition.id, out bool swapped);
            if (pair == null)
            {
                firstAttack.Settled = true;
                secondAttack.Settled = true;
                return;
            }

            PairParticipantData playerOneData = swapped ? pair.second : pair.first;
            PairParticipantData playerTwoData = swapped ? pair.first : pair.second;
            firstAttack.Settled = true;
            secondAttack.Settled = true;
            ApplyPairValues(playerOne, playerOneData);
            ApplyPairValues(playerTwo, playerTwoData);
            ApplyPairResult(playerOne, playerOneData);
            ApplyPairResult(playerTwo, playerTwoData);
            lastEvent = $"{pair.displayName}：{ParticipantEvent(playerOne, playerOneData)}；{ParticipantEvent(playerTwo, playerTwoData)}";
            Emit(new CombatEvent
            {
                tick = Tick,
                type = CombatEventType.ActionPairResolved,
                sourcePlayer = 1,
                targetPlayer = 2,
                pairId = PairId(pair.firstAction, pair.secondAction),
                message = lastEvent
            });
        }

        private void ApplyPairValues(FighterSnapshot fighter, PairParticipantData data)
        {
            if (data.damage > 0) fighter.health = Math.Max(0f, fighter.health - data.damage);
            if (data.nextAttackDelayFrames > 0)
                fighter.delayEffectFrames = Math.Max(fighter.delayEffectFrames, data.nextAttackDelayFrames);
        }

        private void ApplyPairResult(FighterSnapshot fighter, PairParticipantData data)
        {
            if (data.Result == PairParticipantResult.Continue)
            {
                if (fighter.mode == FighterMode.Attack && fighter.currentAttack != null)
                    fighter.currentAttack.RuntimeSpeed *= Math.Max(0.01f, data.speedScale);
                return;
            }
            EnterRebound(fighter, data.speedScale);
        }

        private void EnterRebound(FighterSnapshot fighter, float pairSpeedScale)
        {
            if (fighter.mode != FighterMode.Attack || fighter.currentAttack == null) return;
            pairSpeedScale = Math.Max(0.01f, pairSpeedScale);
            AttackRuntime attack = fighter.currentAttack;
            AttackAnimationData source = attack.Definition.animation;
            float currentSourceFrame = source.sourceStartFrame +
                                       attack.SourceAnimationTime * source.sourceFrameRate;
            float animationStartFrame = Clamp(currentSourceFrame,
                source.sourceStartFrame, source.sourceEndFrame);
            float sourceRemainingFrames = Math.Max(0.001f,
                source.sourceEndFrame - animationStartFrame);
            float sourceTotalFrames = Math.Max(0.001f,
                source.sourceEndFrame - source.sourceStartFrame);
            fighter.lockedDurationFrames = attack.TotalFrames *
                                           (sourceRemainingFrames / sourceTotalFrames) /
                                           pairSpeedScale;
            fighter.lockedActionId = attack.Definition.id;
            fighter.lockedActionSide = attack.Side;
            fighter.lockedAnimationStartFrame = animationStartFrame;
            fighter.currentAttack = null;
            fighter.mode = FighterMode.Rebound;
            fighter.nextSlashSide = DefaultSlashSide;
            fighter.lockedElapsedFrames = 0f;
            fighter.freeElapsedFrames = 0f;
            Emit(new CombatEvent
            {
                tick = Tick,
                type = CombatEventType.Rebound,
                sourcePlayer = fighter.playerIndex,
                actionId = fighter.lockedActionId,
                side = fighter.lockedActionSide
            });
        }

        private void ResolveExpiredActiveWindow(FighterSnapshot attacker, FighterSnapshot target)
        {
            AttackRuntime attack = attacker.currentAttack;
            if (attack == null || attack.Settled) return;
            bool justEnded = attack.PreviousPhase == AttackPhase.Active && attack.Phase != AttackPhase.Active;
            if (!justEnded) return;
            attack.Settled = true;
            if (!attack.HadTemporalOverlap && attack.TargetWasInRange)
            {
                AttackRuntime targetAttack = target.currentAttack;
                bool targetWasInWindup = target.mode == FighterMode.Attack && targetAttack != null &&
                                         targetAttack.PreviousPhase == AttackPhase.Windup;
                bool poiseProtected = targetWasInWindup &&
                                      targetAttack.Definition.startupPoise > attack.Definition.startupPoise;
                target.health = Math.Max(0f, target.health - attack.Definition.normalDamage);
                if (poiseProtected)
                {
                    lastEvent = $"P{attacker.playerIndex} {attack.Definition.displayName}命中 P{target.playerIndex}，后手以更高出手韧性继续动作";
                    Emit(HitEvent(CombatEventType.ArmoredHit, attacker, target, attack, lastEvent));
                }
                else
                {
                    InterruptWithHit(target);
                    lastEvent = $"P{attacker.playerIndex} {attack.Definition.displayName}普通命中并打断 P{target.playerIndex}";
                    Emit(HitEvent(CombatEventType.InterruptedHit, attacker, target, attack, lastEvent));
                }
                Emit(HitEvent(CombatEventType.NormalHit, attacker, target, attack, null));
            }
            else
            {
                lastEvent = attack.HadTemporalOverlap
                    ? "有效期有重叠但未满足双方攻击范围，双方均不普通命中"
                    : $"P{attacker.playerIndex} 攻击落空";
            }
        }

        private void InterruptWithHit(FighterSnapshot target)
        {
            target.currentAttack = null;
            ClearInputBuffer(target);
            target.mode = FighterMode.Hit;
            target.nextSlashSide = DefaultSlashSide;
            target.lockedElapsedFrames = 0f;
            target.lockedDurationFrames = settings.hitReactionFrames;
            target.lockedActionId = null;
            target.lockedAnimationStartFrame = 0f;
            target.freeElapsedFrames = 0f;
        }

        private CombatEvent HitEvent(CombatEventType type, FighterSnapshot attacker, FighterSnapshot target,
            AttackRuntime attack, string message)
        {
            return new CombatEvent
            {
                tick = Tick,
                type = type,
                sourcePlayer = attacker.playerIndex,
                targetPlayer = target.playerIndex,
                actionId = attack.Definition.id,
                side = attack.Side,
                message = message
            };
        }

        private void Emit(CombatEvent combatEvent)
        {
            combatEvent.tick = Tick;
            combatEvent.sequence = events.Count;
            events.Add(combatEvent);
        }

        private static string ParticipantEvent(FighterSnapshot fighter, PairParticipantData data)
        {
            string result = data.Result == PairParticipantResult.Continue ? "继续动作" : "弹回";
            if (data.damage > 0) result += $"、伤害 {data.damage}";
            if (data.nextAttackDelayFrames > 0) result += $"、延迟条 +{data.nextAttackDelayFrames}帧";
            return $"P{fighter.playerIndex} {result}";
        }

        private bool IsInRange(FighterSnapshot attacker, FighterSnapshot target, float radius)
        {
            float x = target.positionX - attacker.positionX;
            float z = target.positionZ - attacker.positionZ;
            float distanceSquared = x * x + z * z;
            if (distanceSquared > radius * radius) return false;
            if (distanceSquared < 0.0001f) return true;
            float inverseDistance = 1f / (float)Math.Sqrt(distanceSquared);
            return attacker.facingX * x * inverseDistance + attacker.facingZ * z * inverseDistance >= 0f;
        }

        private static void SetFacingToward(FighterSnapshot fighter, FighterSnapshot opponent)
        {
            float x = opponent.positionX - fighter.positionX;
            float z = opponent.positionZ - fighter.positionZ;
            Normalize(x, z, out float facingX, out float facingZ);
            if (Math.Abs(facingX) + Math.Abs(facingZ) < 0.0001f) return;
            fighter.facingX = facingX;
            fighter.facingZ = facingZ;
        }

        private FighterSnapshot GetFighter(int playerIndex)
        {
            return playerIndex == 1 ? playerOne : playerIndex == 2
                ? playerTwo
                : throw new ArgumentOutOfRangeException(nameof(playerIndex));
        }

        private static void ValidateCommand(CombatInputCommand command, long expectedTick, int expectedPlayer)
        {
            if (command.tick != expectedTick || command.playerIndex != expectedPlayer)
                throw new InvalidOperationException(
                    $"输入命令与模拟步不匹配：期望 P{expectedPlayer} Tick {expectedTick}，" +
                    $"收到 P{command.playerIndex} Tick {command.tick}");
        }

        private static string PairId(string first, string second) =>
            string.Compare(first, second, StringComparison.OrdinalIgnoreCase) <= 0
                ? first + "+" + second
                : second + "+" + first;

        private static void Normalize(float x, float y, out float normalizedX, out float normalizedY)
        {
            float lengthSquared = x * x + y * y;
            if (lengthSquared <= 1f)
            {
                normalizedX = x;
                normalizedY = y;
                return;
            }
            float inverseLength = 1f / (float)Math.Sqrt(lengthSquared);
            normalizedX = x * inverseLength;
            normalizedY = y * inverseLength;
        }

        private static float Clamp(float value, float minimum, float maximum) =>
            Math.Max(minimum, Math.Min(maximum, value));
    }
}
