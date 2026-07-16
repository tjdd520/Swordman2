using UnityEngine;

namespace Swordman2.Combat
{
    public sealed class FighterController
    {
        private readonly CharacterController characterController;
        private readonly FighterAnimationPlayer animationPlayer;
        private readonly CombatCatalogData catalog;
        private readonly CombatSettings settings;
        private readonly CommonAnimationData animations;
        private CombatDirector director;
        private FighterSnapshot snapshot;

        public readonly GameObject Root;
        public readonly int PlayerIndex;
        public FighterController Opponent { get; set; }
        public FighterMode Mode => snapshot?.mode ?? FighterMode.Free;
        public AttackRuntime CurrentAttack => snapshot?.currentAttack;
        public float Health => snapshot?.health ?? settings.maxHealth;
        public float Stance => snapshot?.stance ?? settings.maxStance;
        public float DelayEffectFrames => snapshot?.delayEffectFrames ?? 0f;
        public bool UsesTemporaryVitalScale => snapshot?.usesTemporaryVitalScale ?? false;
        public float MaximumHealth => settings.maxHealth;
        public float MaximumStance => settings.maxStance;
        public float TemporaryVitalLimit => settings.temporaryVitalLimit;
        public float HealthDisplayMaximum => UsesTemporaryVitalScale ? TemporaryVitalLimit : MaximumHealth;
        public float StanceDisplayMaximum => UsesTemporaryVitalScale ? TemporaryVitalLimit : MaximumStance;
        public float EffectTime => DelayEffectFrames / settings.logicFrameRate;
        public int BufferedInputCount => string.IsNullOrWhiteSpace(snapshot?.bufferedAction) ? 0 : 1;
        public string CurrentAnimation => animationPlayer.CurrentName;
        public float MoveSpeed => settings.moveSpeed;
        public Vector3 Position => snapshot == null
            ? Root.transform.position
            : new Vector3(snapshot.positionX, Root.transform.position.y, snapshot.positionZ);
        public Vector3 Facing => snapshot == null
            ? Root.transform.forward
            : new Vector3(snapshot.facingX, 0f, snapshot.facingZ).normalized;

        public FighterController(int playerIndex, Vector3 position, Color tint, GameObject modelPrefab,
            AnimationClip[] clips, CombatCatalogData combatCatalog)
        {
            PlayerIndex = playerIndex;
            catalog = combatCatalog;
            settings = catalog.settings;
            animations = catalog.commonAnimations;

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

        internal void Bind(CombatDirector owner, FighterSnapshot initialSnapshot)
        {
            director = owner;
            ApplySnapshot(initialSnapshot, 0f);
        }

        internal void ApplySnapshot(FighterSnapshot next, float deltaTime)
        {
            if (next == null) return;
            FighterMode previousMode = snapshot?.mode ?? FighterMode.Free;
            string previousAction = snapshot?.currentAttack?.actionId;
            SlashSide previousSide = snapshot?.currentAttack?.side ?? SlashSide.RightToLeft;
            string previousLockedAction = snapshot?.lockedActionId;
            SlashSide previousLockedSide = snapshot?.lockedActionSide ?? SlashSide.RightToLeft;
            float previousLockedStartFrame = snapshot?.lockedAnimationStartFrame ?? 0f;
            animationPlayer.Tick(deltaTime);
            snapshot = next;
            snapshot.currentAttack?.Bind(catalog.GetAttack(snapshot.currentAttack.actionId));
            ApplyTransform();

            if (snapshot.mode == FighterMode.Attack && snapshot.currentAttack != null)
            {
                bool changedAttack = previousMode != FighterMode.Attack ||
                                     !string.Equals(previousAction, snapshot.currentAttack.actionId,
                                         System.StringComparison.OrdinalIgnoreCase) ||
                                     previousSide != snapshot.currentAttack.side;
                if (changedAttack)
                {
                    AttackDefinition definition = snapshot.currentAttack.Definition;
                    animationPlayer.Play(definition.SuccessClip(snapshot.currentAttack.Side), false,
                        0f, 0f, definition.blendSeconds, true);
                }
                animationPlayer.SetCurrentTime(snapshot.currentAttack.SourceAnimationTime);
                return;
            }

            if (snapshot.mode == FighterMode.Rebound)
            {
                bool changedRebound = previousMode != FighterMode.Rebound ||
                                      !string.Equals(previousLockedAction, snapshot.lockedActionId,
                                          System.StringComparison.OrdinalIgnoreCase) ||
                                      previousLockedSide != snapshot.lockedActionSide ||
                                      !Mathf.Approximately(previousLockedStartFrame,
                                          snapshot.lockedAnimationStartFrame);
                if (changedRebound) PlayRebound();
                return;
            }

            if (snapshot.mode == FighterMode.Hit)
            {
                if (previousMode != FighterMode.Hit) PlayHit();
                return;
            }

            PlayFreeAnimation(snapshot.moveX, snapshot.moveY);
        }

        public void SubmitAttack(string actionId) => director?.QueueAttack(PlayerIndex, actionId);

        public void Teleport(Vector3 position) => director?.Teleport(PlayerIndex, position);

        public void UpdateFacing() => director?.RefreshFacing();

        public void RestoreVitals() => director?.RestoreVitals(PlayerIndex);

        public void SetTemporaryVitals(float health, float stance) =>
            director?.SetTemporaryVitals(PlayerIndex, health, stance);

        public bool IsInRangeOf(FighterController target, float radius)
        {
            if (target == null) return false;
            Vector3 delta = target.Position - Position;
            delta.y = 0f;
            if (delta.sqrMagnitude > radius * radius) return false;
            if (delta.sqrMagnitude < 0.0001f) return true;
            return Vector3.Dot(Facing, delta.normalized) >= 0f;
        }

        public string DebugState() => Mode == FighterMode.Attack && CurrentAttack != null
            ? $"{CurrentAttack.Definition.id}-{CurrentAttack.Phase}"
            : Mode.ToString();

        public void Dispose() => animationPlayer.Dispose();

        private void ApplyTransform()
        {
            Vector3 position = new Vector3(snapshot.positionX, Root.transform.position.y, snapshot.positionZ);
            Vector3 facing = new Vector3(snapshot.facingX, 0f, snapshot.facingZ);
            Quaternion rotation = facing.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(facing.normalized, Vector3.up)
                : Root.transform.rotation;
            bool wasEnabled = characterController.enabled;
            characterController.enabled = false;
            Root.transform.SetPositionAndRotation(position, rotation);
            characterController.enabled = wasEnabled;
        }

        private void PlayRebound()
        {
            AttackDefinition definition = catalog.GetAttack(snapshot.lockedActionId);
            if (definition == null) return;
            AttackAnimationData source = definition.animation;
            float animationStartFrame = Mathf.Clamp(snapshot.lockedAnimationStartFrame,
                source.sourceStartFrame, source.sourceEndFrame);
            float sourceStart = Mathf.Max(0f, animationStartFrame - source.sourceStartFrame) /
                                source.sourceFrameRate;
            float sourceEnd = Mathf.Max(sourceStart, source.sourceEndFrame - source.sourceStartFrame) /
                              source.sourceFrameRate;
            float sourceRemaining = Mathf.Max(0.001f, sourceEnd - sourceStart);
            float durationSeconds = snapshot.lockedDurationFrames / settings.logicFrameRate;
            string clip = definition.ReboundClip(snapshot.lockedActionSide);
            float speed = animationPlayer.HasClip(clip) ? sourceRemaining / Mathf.Max(0.001f, durationSeconds) : 1f;
            float clipDuration = animationPlayer.ClipDuration(clip);
            float normalizedStart = clipDuration > 0f ? sourceStart / clipDuration : 0f;
            animationPlayer.Play(clip, false, speed, normalizedStart, definition.blendSeconds, true);
        }

        private void PlayHit()
        {
            float duration = snapshot.lockedDurationFrames / settings.logicFrameRate;
            float speed = animationPlayer.PlaybackSpeedForDuration(animations.hitReaction, duration);
            animationPlayer.Play(animations.hitReaction, false, speed, 0f, animations.hitBlendSeconds, true);
        }

        private void PlayFreeAnimation(float horizontal, float vertical)
        {
            if (horizontal * horizontal + vertical * vertical < 0.001f)
            {
                animationPlayer.Play(animations.idle, true, 1f);
                return;
            }
            string clip;
            if (Mathf.Abs(vertical) >= Mathf.Abs(horizontal))
                clip = vertical >= 0f ? animations.walkForward : animations.walkBackward;
            else
                clip = horizontal < 0f ? animations.walkLeft : animations.walkRight;
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
