using System.Collections.Generic;
using UnityEngine;

namespace Swordman2.Combat
{
    public sealed class FighterController
    {
        private readonly List<string> inputBuffer = new();
        private readonly CharacterController characterController;
        private readonly FighterAnimationPlayer animationPlayer;
        private readonly CombatCatalogData catalog;
        private readonly CombatSettings settings;
        private readonly CommonAnimationData animations;
        private SlashSide nextSlashSide = SlashSide.RightToLeft;
        private float lockedElapsedFrames;
        private float lockedDurationFrames;
        private float freeElapsedFrames;
        private float inputBufferElapsedFrames;
        private Vector2 movementInput;

        public readonly GameObject Root;
        public readonly int PlayerIndex;
        public FighterController Opponent { get; set; }
        public FighterMode Mode { get; private set; } = FighterMode.Free;
        public AttackRuntime CurrentAttack { get; private set; }
        public float Health { get; private set; }
        public float Stance { get; private set; }
        public float DelayEffectFrames { get; private set; }
        public bool UsesTemporaryVitalScale { get; private set; }
        public float MaximumHealth => settings.maxHealth;
        public float MaximumStance => settings.maxStance;
        public float TemporaryVitalLimit => settings.temporaryVitalLimit;
        public float HealthDisplayMaximum => UsesTemporaryVitalScale ? TemporaryVitalLimit : MaximumHealth;
        public float StanceDisplayMaximum => UsesTemporaryVitalScale ? TemporaryVitalLimit : MaximumStance;
        public float EffectTime => DelayEffectFrames / settings.logicFrameRate;
        public int BufferedInputCount => inputBuffer.Count;
        public string CurrentAnimation => animationPlayer.CurrentName;
        public float MoveSpeed { get; set; }

        public Vector3 Position => Root.transform.position;
        public Vector3 Facing
        {
            get
            {
                Vector3 forward = Root.transform.forward;
                forward.y = 0f;
                return forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;
            }
        }

        public FighterController(int playerIndex, Vector3 position, Color tint, GameObject modelPrefab,
            AnimationClip[] clips, CombatCatalogData combatCatalog)
        {
            PlayerIndex = playerIndex;
            catalog = combatCatalog;
            settings = catalog.settings;
            animations = catalog.commonAnimations;
            Health = settings.maxHealth;
            Stance = settings.maxStance;
            MoveSpeed = settings.moveSpeed;

            Root = new GameObject($"Player_{playerIndex}");
            Root.transform.position = position;
            characterController = Root.AddComponent<CharacterController>();
            characterController.height = 1.9f;
            characterController.radius = 0.34f;
            characterController.center = new Vector3(0f, 0.95f, 0f);
            characterController.skinWidth = 0.03f;
            characterController.minMoveDistance = 0f;

            GameObject visual;
            if (modelPrefab != null)
            {
                visual = Object.Instantiate(modelPrefab, Root.transform);
                visual.name = "SwordsmanVisual";
                visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 180f, 0f));
            }
            else
            {
                visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                visual.name = "MissingModelFallback";
                visual.transform.SetParent(Root.transform, false);
                visual.transform.localPosition = Vector3.up;
            }

            TintVisual(visual, tint);
            Animator animator = visual.GetComponentInChildren<Animator>();
            if (animator == null) animator = visual.AddComponent<Animator>();
            animationPlayer = new FighterAnimationPlayer(animator, clips);
            animationPlayer.Play(animations.idle, true, 1f, 0f, 0.01f, true);
        }

        public void SetMovementInput(Vector2 input) => movementInput = Vector2.ClampMagnitude(input, 1f);

        public void Teleport(Vector3 position)
        {
            bool wasEnabled = characterController.enabled;
            characterController.enabled = false;
            Root.transform.position = position;
            characterController.enabled = wasEnabled;
            Physics.SyncTransforms();
        }

        public void SubmitAttack(string actionId)
        {
            if (catalog.GetAttack(actionId) == null)
            {
                Debug.LogError($"P{PlayerIndex} 尝试执行不存在的动作：{actionId}");
                return;
            }
            if (Mode == FighterMode.Free && TryStartAttack(actionId))
            {
                ClearInputBuffer();
                return;
            }
            inputBuffer.Clear();
            inputBuffer.Add(actionId);
            inputBufferElapsedFrames = 0f;
        }

        public void UpdateFacing()
        {
            if (Opponent == null) return;
            Vector3 toOpponent = Opponent.Position - Position;
            toOpponent.y = 0f;
            if (toOpponent.sqrMagnitude > 0.0001f)
                Root.transform.rotation = Quaternion.LookRotation(toOpponent.normalized, Vector3.up);
        }

        public void UpdateMovement(float deltaTime)
        {
            if (Mode != FighterMode.Free || Opponent == null)
            {
                PlayFreeAnimation(Vector2.zero);
                return;
            }
            Vector3 forward = Opponent.Position - Position;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Facing;
            else forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 velocity = (forward * movementInput.y + right * movementInput.x) * MoveSpeed;
            characterController.Move(velocity * deltaTime);
            PlayFreeAnimation(movementInput);
        }

        public void Advance(float deltaTime)
        {
            animationPlayer.Tick(deltaTime);
            float deltaFrames = deltaTime * settings.logicFrameRate;
            AdvanceInputBuffer(deltaFrames);
            if (DelayEffectFrames > 0f)
                DelayEffectFrames = Mathf.Max(0f, DelayEffectFrames - deltaFrames);

            if (Mode == FighterMode.Attack && CurrentAttack != null)
            {
                CurrentAttack.PreviousPhase = CurrentAttack.Phase;
                CurrentAttack.ElapsedFrames += deltaFrames * CurrentAttack.RuntimeSpeed;
                animationPlayer.SetCurrentTime(CurrentAttack.SourceAnimationTime);
                if (CurrentAttack.Phase == AttackPhase.Finished) FinishLockedAction();
                return;
            }

            if (Mode == FighterMode.Rebound || Mode == FighterMode.Hit)
            {
                lockedElapsedFrames += deltaFrames;
                if (lockedElapsedFrames >= lockedDurationFrames) FinishLockedAction();
                return;
            }

            freeElapsedFrames += deltaFrames;
            if (freeElapsedFrames >= settings.stanceRecoveryDelayFrames && Stance < settings.maxStance)
            {
                float ratePerFrame = settings.maxStance / Mathf.Max(1, settings.stanceRecoveryDurationFrames);
                Stance = Mathf.Min(settings.maxStance, Stance + ratePerFrame * deltaFrames);
            }
            TryStartLatestBufferedInput();
        }

        public bool IsInRangeOf(FighterController target, float radius)
        {
            Vector3 delta = target.Position - Position;
            delta.y = 0f;
            if (delta.sqrMagnitude > radius * radius) return false;
            if (delta.sqrMagnitude < 0.0001f) return true;
            return Vector3.Dot(Facing, delta.normalized) >= 0f;
        }

        public void MarkAttackSettled()
        {
            if (CurrentAttack != null) CurrentAttack.Settled = true;
        }

        public void EnterRebound(float pairSpeedScale = 1f)
        {
            if (Mode != FighterMode.Attack || CurrentAttack == null) return;
            pairSpeedScale = Mathf.Max(0.01f, pairSpeedScale);
            AttackRuntime attack = CurrentAttack;
            attack.Settled = true;
            string clip = attack.Definition.ReboundClip(attack.Side);
            AttackAnimationData source = attack.Definition.animation;
            float sourceStart = Mathf.Max(0f, source.reboundEntryFrame - source.sourceStartFrame) / source.sourceFrameRate;
            float sourceEnd = Mathf.Max(sourceStart, source.sourceEndFrame - source.sourceStartFrame) / source.sourceFrameRate;
            float sourceRemaining = Mathf.Max(0.001f, sourceEnd - sourceStart);
            float sourceTotal = Mathf.Max(0.001f,
                (source.sourceEndFrame - source.sourceStartFrame) / source.sourceFrameRate);
            lockedDurationFrames = attack.TotalFrames * (sourceRemaining / sourceTotal) / pairSpeedScale;
            float durationSeconds = lockedDurationFrames / settings.logicFrameRate;
            float speed = animationPlayer.HasClip(clip) ? sourceRemaining / durationSeconds : 1f;
            float clipDuration = animationPlayer.ClipDuration(clip);
            float normalizedStart = clipDuration > 0f ? sourceStart / clipDuration : 0f;
            animationPlayer.Play(clip, false, speed, normalizedStart,
                attack.Definition.blendSeconds, true);
            CurrentAttack = null;
            Mode = FighterMode.Rebound;
            lockedElapsedFrames = 0f;
            freeElapsedFrames = 0f;
        }

        public void ReceiveInterruptingHit(int damage)
        {
            Health = Mathf.Max(0f, Health - damage);
            CurrentAttack = null;
            ClearInputBuffer();
            Mode = FighterMode.Hit;
            lockedElapsedFrames = 0f;
            lockedDurationFrames = settings.hitReactionFrames;
            freeElapsedFrames = 0f;
            float duration = lockedDurationFrames / settings.logicFrameRate;
            float speed = animationPlayer.PlaybackSpeedForDuration(animations.hitReaction, duration);
            animationPlayer.Play(animations.hitReaction, false, speed, 0f, animations.hitBlendSeconds, true);
        }

        public void ReceiveArmoredHit(int damage)
        {
            Health = Mathf.Max(0f, Health - damage);
        }

        public void ApplyPairDamage(int damage) => Health = Mathf.Max(0f, Health - damage);

        public void ApplyPairContinuationSpeed(float speedScale)
        {
            if (Mode != FighterMode.Attack || CurrentAttack == null) return;
            CurrentAttack.RuntimeSpeed *= Mathf.Max(0.01f, speedScale);
        }

        public void ApplyDelayEffect(int frames)
        {
            DelayEffectFrames = Mathf.Max(DelayEffectFrames, Mathf.Max(0, frames));
        }

        public void RestoreVitals()
        {
            Health = settings.maxHealth;
            Stance = settings.maxStance;
            UsesTemporaryVitalScale = false;
        }

        public void SetTemporaryVitals(float health, float stance)
        {
            Health = Mathf.Clamp(health, 0f, settings.temporaryVitalLimit);
            Stance = Mathf.Clamp(stance, 0f, settings.temporaryVitalLimit);
            UsesTemporaryVitalScale = true;
        }

        public string DebugState() => Mode == FighterMode.Attack && CurrentAttack != null
            ? $"{CurrentAttack.Definition.id}-{CurrentAttack.Phase}"
            : Mode.ToString();

        public void Dispose() => animationPlayer.Dispose();

        private bool TryStartAttack(string actionId)
        {
            if (Mode != FighterMode.Free) return false;
            AttackDefinition definition = catalog.GetAttack(actionId);
            if (definition == null || Stance + 0.0001f < definition.stanceCost) return false;

            Stance -= definition.stanceCost;
            SlashSide side = nextSlashSide;
            nextSlashSide = nextSlashSide == SlashSide.RightToLeft ? SlashSide.LeftToRight : SlashSide.RightToLeft;
            int delayFrames = Mathf.RoundToInt(DelayEffectFrames);
            DelayEffectFrames = 0f;
            CurrentAttack = new AttackRuntime(definition, side, settings.logicFrameRate,
                delayFrames, settings.delayEffectAttackScale);
            Mode = FighterMode.Attack;
            freeElapsedFrames = 0f;
            string successClip = definition.SuccessClip(side);
            animationPlayer.Play(successClip, false, 0f, 0f, definition.blendSeconds, true);
            animationPlayer.SetCurrentTime(CurrentAttack.SourceAnimationTime);
            return true;
        }

        private void TryStartLatestBufferedInput()
        {
            if (Mode != FighterMode.Free || inputBuffer.Count == 0) return;
            string latest = inputBuffer[^1];
            if (!TryStartAttack(latest)) return;
            ClearInputBuffer();
        }

        private void AdvanceInputBuffer(float deltaFrames)
        {
            if (inputBuffer.Count == 0) return;
            inputBufferElapsedFrames += deltaFrames;
            if (inputBufferElapsedFrames >= settings.inputBufferFrames) ClearInputBuffer();
        }

        private void ClearInputBuffer()
        {
            inputBuffer.Clear();
            inputBufferElapsedFrames = 0f;
        }

        private void FinishLockedAction()
        {
            Mode = FighterMode.Free;
            CurrentAttack = null;
            lockedElapsedFrames = 0f;
            lockedDurationFrames = 0f;
            freeElapsedFrames = 0f;
            animationPlayer.Play(animations.idle, true, 1f, 0f, animations.idleBlendSeconds, true);
            TryStartLatestBufferedInput();
        }

        private void PlayFreeAnimation(Vector2 input)
        {
            if (Mode != FighterMode.Free) return;
            if (input.sqrMagnitude < 0.001f)
            {
                animationPlayer.Play(animations.idle, true, 1f);
                return;
            }
            string clip;
            if (Mathf.Abs(input.y) >= Mathf.Abs(input.x))
                clip = input.y >= 0f ? animations.walkForward : animations.walkBackward;
            else
                clip = input.x < 0f ? animations.walkLeft : animations.walkRight;
            animationPlayer.Play(clip, true, MoveSpeed / Mathf.Max(0.01f, animations.walkReferenceSpeed),
                0f, animations.locomotionBlendSeconds);
        }

        private static void TintVisual(GameObject visual, Color tint)
        {
            foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                MaterialPropertyBlock block = new();
                renderer.GetPropertyBlock(block);
                block.SetColor("_BaseColor", tint);
                block.SetColor("_Color", tint);
                renderer.SetPropertyBlock(block);
            }
        }
    }
}
