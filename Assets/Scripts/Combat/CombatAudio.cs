using UnityEngine;

namespace Swordman2.Combat
{
    public sealed class CombatAudio : MonoBehaviour
    {
        private AudioSource source;
        private AudioClip[] pairClips;
        private AudioClip normalHitClip;
        private CombatAudioData data;
        private float lastPairTime = float.NegativeInfinity;
        private int pairChainIndex;

        public void Initialize(CombatAudioData audioData)
        {
            data = audioData;
            pairClips = new AudioClip[data.pairSequence.Length];
            for (int i = 0; i < pairClips.Length; i++)
                pairClips[i] = LoadClip(data.pairSequence[i]);
            normalHitClip = LoadClip(data.normalHit);

            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.volume = data.volume;
        }

        public void PlayActionPair(float predictedPairTime)
        {
            if (pairClips.Length == 0) return;
            pairChainIndex = predictedPairTime - lastPairTime <= data.pairChainWindowSeconds
                ? (pairChainIndex + 1) % pairClips.Length
                : 0;
            lastPairTime = predictedPairTime;
            Play(pairClips[pairChainIndex]);
        }

        public void PlayNormalHit() => Play(normalHitClip);

        private void Play(AudioClip clip)
        {
            if (clip != null) source.PlayOneShot(clip);
        }

        private static AudioClip LoadClip(string resourcePath)
        {
            if (string.IsNullOrWhiteSpace(resourcePath)) return null;
            AudioClip clip = Resources.Load<AudioClip>(resourcePath);
            if (clip == null)
                Debug.LogError($"无法加载战斗音效：Resources/{resourcePath}");
            else
                clip.LoadAudioData();
            return clip;
        }
    }
}
