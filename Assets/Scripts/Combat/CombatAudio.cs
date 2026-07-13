using UnityEngine;

namespace Swordman2.Combat
{
    public sealed class CombatAudio : MonoBehaviour
    {
        public const float PairChainWindow = 1.5f;
        public const float PairPredictionLead = 1f;
        public const float Perfect4StartOffset = 0.3f;
        private const int SourcePoolSize = 8;

        private readonly AudioSource[] sources = new AudioSource[SourcePoolSize];
        private readonly AudioClip[] pairClips = new AudioClip[3];
        private AudioClip normalHitClip;
        private float lastPairTime = float.NegativeInfinity;
        private int pairChainIndex;
        private int nextSource;

        public int PairChainIndex => pairChainIndex;
        public string LastPlayedClipName { get; private set; } = string.Empty;

        public void Initialize()
        {
            pairClips[0] = LoadClip("Audio/PerfectParry4");
            pairClips[1] = LoadClip("Audio/PerfectParry5");
            pairClips[2] = LoadClip("Audio/PerfectParry6");
            normalHitClip = LoadClip("Audio/NormalHit");

            for (int i = 0; i < sources.Length; i++)
            {
                AudioSource source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 0f;
                source.volume = 0.85f;
                sources[i] = source;
            }
        }

        public void PlayActionPair(float predictedPairTime)
        {
            if (predictedPairTime - lastPairTime <= PairChainWindow)
                pairChainIndex = (pairChainIndex + 1) % pairClips.Length;
            else
                pairChainIndex = 0;

            lastPairTime = predictedPairTime;
            float startOffset = pairChainIndex == 0 ? Perfect4StartOffset : 0f;
            PlayOverlapping(pairClips[pairChainIndex], startOffset);
        }

        public void PlayNormalHit()
        {
            PlayOverlapping(normalHitClip);
        }

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
            AudioClip clip = Resources.Load<AudioClip>(resourcePath);
            if (clip == null)
                Debug.LogError($"无法加载战斗音效：Resources/{resourcePath}");
            return clip;
        }
    }
}
