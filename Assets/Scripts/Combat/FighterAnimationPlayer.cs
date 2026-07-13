using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Swordman2.Combat
{
    public sealed class FighterAnimationPlayer : IDisposable
    {
        private readonly Dictionary<string, AnimationClip> clips = new(StringComparer.OrdinalIgnoreCase);
        private readonly PlayableGraph graph;
        private readonly AnimationMixerPlayable mixer;
        private AnimationClipPlayable[] slots = new AnimationClipPlayable[2];
        private bool[] valid = new bool[2];
        private int currentSlot;
        private int previousSlot = -1;
        private float blendDuration;
        private float blendElapsed;
        private bool loopCurrent;
        private string currentName;

        public string CurrentName => currentName;

        public FighterAnimationPlayer(Animator animator, IEnumerable<AnimationClip> sourceClips)
        {
            foreach (AnimationClip clip in sourceClips)
            {
                string cleanName = CleanName(clip.name);
                clips[cleanName] = clip;
            }

            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            graph = PlayableGraph.Create($"{animator.gameObject.name}_AnimationGraph");
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            mixer = AnimationMixerPlayable.Create(graph, 2);
            AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, "Character Animation", animator);
            output.SetSourcePlayable(mixer);
            graph.Play();
        }

        public bool HasClip(string clipName) => clips.ContainsKey(clipName);

        public float PlaybackSpeedForDuration(string clipName, float desiredDuration)
        {
            if (!clips.TryGetValue(clipName, out AnimationClip clip) || desiredDuration <= 0.0001f)
                return 1f;
            return clip.length / desiredDuration;
        }

        public void MultiplyCurrentSpeed(float multiplier)
        {
            if (!valid[currentSlot]) return;
            slots[currentSlot].SetSpeed(slots[currentSlot].GetSpeed() * Mathf.Max(0.01f, multiplier));
        }

        public void Play(string clipName, bool loop, float speed = 1f, float normalizedStart = 0f, float blend = 0.08f, bool force = false)
        {
            if (!clips.TryGetValue(clipName, out AnimationClip clip))
            {
                Debug.LogWarning($"找不到动画片段：{clipName}");
                return;
            }

            if (!force && string.Equals(currentName, clipName, StringComparison.OrdinalIgnoreCase))
            {
                if (valid[currentSlot]) slots[currentSlot].SetSpeed(speed);
                loopCurrent = loop;
                return;
            }

            int nextSlot = valid[currentSlot] ? 1 - currentSlot : currentSlot;
            if (valid[nextSlot])
            {
                graph.Disconnect(mixer, nextSlot);
                slots[nextSlot].Destroy();
                valid[nextSlot] = false;
            }

            AnimationClipPlayable playable = AnimationClipPlayable.Create(graph, clip);
            playable.SetApplyFootIK(false);
            playable.SetApplyPlayableIK(false);
            playable.SetTime(Mathf.Clamp01(normalizedStart) * clip.length);
            playable.SetSpeed(speed);
            graph.Connect(playable, 0, mixer, nextSlot);
            mixer.SetInputWeight(nextSlot, valid[currentSlot] ? 0f : 1f);

            previousSlot = valid[currentSlot] ? currentSlot : -1;
            currentSlot = nextSlot;
            slots[currentSlot] = playable;
            valid[currentSlot] = true;
            blendDuration = Mathf.Max(0.001f, blend);
            blendElapsed = 0f;
            loopCurrent = loop;
            currentName = clipName;
        }

        public void Tick(float deltaTime)
        {
            if (!valid[currentSlot]) return;

            if (loopCurrent)
            {
                double length = slots[currentSlot].GetAnimationClip().length;
                double time = slots[currentSlot].GetTime();
                if (length > 0d && time >= length)
                    slots[currentSlot].SetTime(time % length);
            }

            if (previousSlot < 0) return;
            blendElapsed += deltaTime;
            float t = Mathf.Clamp01(blendElapsed / blendDuration);
            mixer.SetInputWeight(previousSlot, 1f - t);
            mixer.SetInputWeight(currentSlot, t);
            if (t < 1f) return;

            graph.Disconnect(mixer, previousSlot);
            slots[previousSlot].Destroy();
            valid[previousSlot] = false;
            previousSlot = -1;
        }

        public void Dispose()
        {
            if (graph.IsValid()) graph.Destroy();
        }

        private static string CleanName(string name)
        {
            int separator = name.LastIndexOf('|');
            return separator >= 0 ? name[(separator + 1)..] : name;
        }
    }
}
