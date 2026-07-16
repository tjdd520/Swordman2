using UnityEngine;

namespace Swordman2.Combat
{
    public sealed class CombatAudio : MonoBehaviour
    {
        private AudioSource[] sources;
        private AudioClip[] pairClips;
        private AudioClip normalHitClip;
        private CombatAudioData data;
        private float lastPairTime = float.NegativeInfinity;
        private int pairChainIndex;
        private int nextSource;

        public int PairChainIndex => pairChainIndex;
        public string LastPlayedClipName { get; private set; } = string.Empty;

        public void Initialize(CombatAudioData audioData)
        {
            data = audioData;
            pairClips = new AudioClip[data.pairSequence.Length];
            for (int i = 0; i < pairClips.Length; i++) pairClips[i] = LoadClip(data.pairSequence[i]);
            normalHitClip = LoadClip(data.normalHit);
            sources = new AudioSource[Mathf.Max(1, data.sourcePoolSize)];
            for (int i = 0; i < sources.Length; i++)
            {
                AudioSource source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 0f;
                source.volume = data.volume;
                sources[i] = source;
            }
        }

        public void PlayActionPair(float predictedPairTime)
        {
            if (pairClips.Length == 0) return;
            pairChainIndex = predictedPairTime - lastPairTime <= data.pairChainWindowSeconds
                ? (pairChainIndex + 1) % pairClips.Length
                : 0;
            lastPairTime = predictedPairTime;
            float offset = pairChainIndex == 0 ? data.firstPairStartOffsetSeconds : 0f;
            PlayOverlapping(pairClips[pairChainIndex], offset);
        }

        public void PlayNormalHit() => PlayOverlapping(normalHitClip);

        private void PlayOverlapping(AudioClip clip, float startOffsetSeconds = 0f)
        {
            if (clip == null) return;
            AudioSource source = FindAvailableSource();
            source.clip = clip;
            source.time = Mathf.Clamp(startOffsetSeconds, 0f, Mathf.Max(0f, clip.length - 0.01f));
            source.Play();
            LastPlayedClipName = clip.name;
        }

        private AudioSource FindAvailableSource()
        {
            for (int offset = 0; offset < sources.Length; offset++)
            {
                int index = (nextSource + offset) % sources.Length;
                if (!sources[index].isPlaying)
                {
                    nextSource = (index + 1) % sources.Length;
                    return sources[index];
                }
            }
            AudioSource fallback = sources[nextSource];
            nextSource = (nextSource + 1) % sources.Length;
            return fallback;
        }

        private static AudioClip LoadClip(string resourcePath)
        {
            if (string.IsNullOrWhiteSpace(resourcePath)) return null;
            AudioClip clip = Resources.Load<AudioClip>(resourcePath);
            if (clip == null) Debug.LogError($"无法加载战斗音效：Resources/{resourcePath}");
            return clip;
        }
    }
}
