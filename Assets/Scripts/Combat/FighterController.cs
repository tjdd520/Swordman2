using System.Collections.Generic;
using UnityEngine;

namespace Swordman2.Combat
{
    public sealed class FighterController
    {
        public const float MaxHealth = 5f;
        public const float MaxStance = 5f;
        public const float TemporaryVitalLimit = 50f;
        public const float EffectDelayStrength = 2f;
        private const float StanceRecoveryDelay = 0.3f;
        private const float StanceRecoveryRate = 5f / 1.5f;
        public const float InputBufferLifetime = 0.4f;
        private const float HitDuration = 40f / AttackDefinition.FramesPerSecond /
                                          AttackDefinition.GlobalActionSpeed;

        private readonly List<AttackKind> inputBuffer = new();
        private readonly CharacterController characterController;
        private readonly FighterAnimationPlayer animationPlayer;
        private SlashSide nextSlashSide = SlashSide.RightToLeft;
        private float lockedElapsed;
        private float lockedDuration;
        private float freeElapsed;
        private float inputBufferElapsed;
        private Vector2 movementInput;

        public readonly GameObject Root;
        public readonly int PlayerIndex;
        public FighterController Opponent { get; set; }
        public FighterMode Mode { get; private set; } = FighterMode.Free;
        public AttackRuntime CurrentAttack { get; private set; }
        public float Health { get; private set; } = MaxHealth;
        public float Stance { get; private set; } = MaxStance;
        public float EffectTime { get; private set; }
        public bool UsesTemporaryVitalScale { get; private set; }
        public float HealthDisplayMaximum => UsesTemporaryVitalScale ? TemporaryVitalLimit : MaxHealth;
        public float StanceDisplayMaximum => UsesTemporaryVitalScale ? TemporaryVitalLimit : MaxStance;
        public int BufferedInputCount => inputBuffer.Count;
        public string CurrentAnimation => animationPlayer.CurrentName;
        public float MoveSpeed { get; set; } = 1.20f;

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

        public FighterController(int playerIndex, Vector3 position, Color tint, GameObject modelPrefab, AnimationClip[] clips)
        {
            PlayerIndex = playerIndex;
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
                // Blender 模型的视觉前向与 Unity 战斗根节点相反。
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
            animationPlayer.Play("Idle_TwoHand_Sword", true, 1f, 0f, 0.01f, true);
        }

        public void SetMovementInput(Vector2 input)
        {
            movementInput = Vector2.ClampMagnitude(input, 1f);
        }

        public void Teleport(Vector3 position)
        {
            bool wasEnabled = characterController.enabled;
            characterController.enabled = false;
            Root.transform.position = position;
            characterController.enabled = wasEnabled;
            Physics.SyncTransforms();
        }

        public void SubmitAttack(AttackKind kind)
        {
            if (Mode == FighterMode.Free && TryStartAttack(kind))
            {
                ClearInputBuffer();
                return;
            }

            // 缓冲只保留最新攻击；新输入会替换旧输入并重新开始 0.4 秒有效期。
            inputBuffer.Clear();
            inputBuffer.Add(kind);
            inputBufferElapsed = 0f;
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
            AdvanceInputBuffer(deltaTime);

            if (Mode == FighterMode.Attack && CurrentAttack != null)
            {
                CurrentAttack.PreviousPhase = CurrentAttack.Phase;
                CurrentAttack.Elapsed += deltaTime * CurrentAttack.RuntimeSpeed;
                if (CurrentAttack.Phase == AttackPhase.Finished)
                    FinishLockedAction();
                return;
            }

            if (Mode == FighterMode.Rebound || Mode == FighterMode.Hit)
            {
                lockedElapsed += deltaTime;
                if (lockedElapsed >= lockedDuration)
                    FinishLockedAction();
                return;
            }

            if (EffectTime > 0f)
                EffectTime = Mathf.Max(0f, EffectTime - deltaTime);

            freeElapsed += deltaTime;
            if (freeElapsed >= StanceRecoveryDelay && Stance < MaxStance)
                Stance = Mathf.Min(MaxStance, Stance + StanceRecoveryRate * deltaTime);

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

        public void EnterRebound(float pairSpeedMultiplier = 1f)
        {
            if (Mode != FighterMode.Attack || CurrentAttack == null) return;
            pairSpeedMultiplier = Mathf.Max(0.01f, pairSpeedMultiplier);
            AttackRuntime attack = CurrentAttack;
            attack.Settled = true;
            string clip = attack.Definition.ReboundClip(attack.Side);
            float remainingFraction = 1f - attack.Definition.ReboundNormalizedTime;
            float speed = animationPlayer.PlaybackSpeedForDuration(clip,
                attack.Definition.BaseDuration * attack.TimeScale) * pairSpeedMultiplier;
            animationPlayer.Play(clip, false, speed, attack.Definition.ReboundNormalizedTime,
                attack.Definition.BlendSeconds, true);
            CurrentAttack = null;
            Mode = FighterMode.Rebound;
            lockedElapsed = 0f;
            lockedDuration = attack.Definition.BaseDuration * remainingFraction * attack.TimeScale /
                             pairSpeedMultiplier;
            freeElapsed = 0f;
        }

        public void ReceiveNormalHit(int damage)
        {
            Health = Mathf.Max(0f, Health - damage);
            CurrentAttack = null;
            Mode = FighterMode.Hit;
            lockedElapsed = 0f;
            lockedDuration = HitDuration;
            freeElapsed = 0f;
            float hitSpeed = animationPlayer.PlaybackSpeedForDuration("Hit_Reaction_Front", HitDuration);
            animationPlayer.Play("Hit_Reaction_Front", false, hitSpeed, 0f, 0.06f, true);
        }

        public void ApplyPairDamage(int damage)
        {
            Health = Mathf.Max(0f, Health - damage);
        }

        public void ApplyPairContinuationSpeed(float pairSpeedMultiplier)
        {
            if (Mode != FighterMode.Attack || CurrentAttack == null) return;
            pairSpeedMultiplier = Mathf.Max(0.01f, pairSpeedMultiplier);
            CurrentAttack.RuntimeSpeed *= pairSpeedMultiplier;
            animationPlayer.MultiplyCurrentSpeed(pairSpeedMultiplier);
        }

        public void ApplyEffect(float seconds)
        {
            EffectTime = Mathf.Max(0f, seconds);
        }

        public void RestoreVitals()
        {
            Health = MaxHealth;
            Stance = MaxStance;
            UsesTemporaryVitalScale = false;
        }

        public void SetTemporaryVitals(float health, float stance)
        {
            Health = Mathf.Clamp(health, 0f, TemporaryVitalLimit);
            Stance = Mathf.Clamp(stance, 0f, TemporaryVitalLimit);
            UsesTemporaryVitalScale = true;
        }

        public string DebugState()
        {
            if (Mode == FighterMode.Attack && CurrentAttack != null)
                return $"{CurrentAttack.Definition.Kind}-{CurrentAttack.Phase}";
            return Mode.ToString();
        }

        public void Dispose()
        {
            animationPlayer.Dispose();
        }

        private bool TryStartAttack(AttackKind kind)
        {
            if (Mode != FighterMode.Free) return false;
            AttackDefinition definition = AttackDefinition.Create(kind);
            if (Stance + 0.0001f < definition.StanceCost) return false;

            Stance -= definition.StanceCost;
            SlashSide side = nextSlashSide;
            if (kind != AttackKind.B)
                nextSlashSide = nextSlashSide == SlashSide.RightToLeft ? SlashSide.LeftToRight : SlashSide.RightToLeft;

            float effect = EffectTime;
            EffectTime = 0f;
            float delayedDuration = definition.BaseDuration + effect * EffectDelayStrength;
            float timeScale = delayedDuration / definition.BaseDuration;
            CurrentAttack = new AttackRuntime(definition, side, timeScale);
            Mode = FighterMode.Attack;
            freeElapsed = 0f;
            string successClip = definition.SuccessClip(side);
            float playbackSpeed = animationPlayer.PlaybackSpeedForDuration(successClip,
                definition.BaseDuration * timeScale);
            animationPlayer.Play(successClip, false, playbackSpeed, 0f,
                definition.BlendSeconds, true);
            return true;
        }

        private void TryStartLatestBufferedInput()
        {
            if (Mode != FighterMode.Free || inputBuffer.Count == 0) return;
            AttackKind latest = inputBuffer[^1];
            if (!TryStartAttack(latest)) return;
            ClearInputBuffer();
        }

        private void AdvanceInputBuffer(float deltaTime)
        {
            if (inputBuffer.Count == 0) return;
            inputBufferElapsed += deltaTime;
            if (inputBufferElapsed >= InputBufferLifetime)
                ClearInputBuffer();
        }

        private void ClearInputBuffer()
        {
            inputBuffer.Clear();
            inputBufferElapsed = 0f;
        }

        private void FinishLockedAction()
        {
            Mode = FighterMode.Free;
            CurrentAttack = null;
            lockedElapsed = 0f;
            lockedDuration = 0f;
            freeElapsed = 0f;
            animationPlayer.Play("Idle_TwoHand_Sword", true, 1f, 0f, 0.08f, true);
            TryStartLatestBufferedInput();
        }

        private void PlayFreeAnimation(Vector2 input)
        {
            if (Mode != FighterMode.Free) return;
            if (input.sqrMagnitude < 0.001f)
            {
                animationPlayer.Play("Idle_TwoHand_Sword", true, 1f);
                return;
            }

            string clip;
            if (Mathf.Abs(input.y) >= Mathf.Abs(input.x))
                clip = input.y >= 0f ? "Walk_Forward" : "Walk_Backward";
            else
                clip = input.x < 0f ? "Walk_Left" : "Walk_Right";
            // 0.68 m/s 是原始步幅在 30 FPS 下的匹配速度；提高位移时同步加速动画。
            animationPlayer.Play(clip, true, MoveSpeed / 0.68f, 0f, 0.12f);
        }

        private static void TintVisual(GameObject visual, Color tint)
        {
            foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                MaterialPropertyBlock block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                block.SetColor("_BaseColor", tint);
                block.SetColor("_Color", tint);
                renderer.SetPropertyBlock(block);
            }
        }
    }
}
