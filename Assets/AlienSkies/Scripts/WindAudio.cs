using UnityEngine;

namespace AlienSkies.Movement
{
    /// <summary>Procedural wind/whoosh layer (guardrail 3: speed needs a wind
    /// layer, no asset needed) — one looping low-passed-noise clip, presence
    /// and pitch driven by Motor's speed. Same asset-free doctrine as
    /// CombatFeedback.MakeBlip, applied to a loop instead of a one-shot.</summary>
    public sealed class WindAudio : MonoBehaviour
    {
        public PlayerMotor Motor;

        const int SampleRate = 44100;
        const float ClipSeconds = 2f;
        const int CrossfadeSamples = 2000;
        const float LowPassCoeff = 0.08f;
        const float NoiseAmplitude = 0.5f;

        const float VolumeLowSpeed = 8f;
        const float VolumeHighSpeed = 45f;
        const float MaxVolume = 0.5f;
        const float StopVolume = 0.01f;
        const float PitchLowSpeed = 8f;
        const float PitchHighSpeed = 60f;
        const float MinPitch = 0.8f;
        const float MaxPitch = 1.35f;

        AudioSource _source;
        AudioClip _clip;

        void Awake()
        {
            _source = gameObject.AddComponent<AudioSource>();
            _source.loop = true;
            _source.spatialBlend = 0f;
            _source.playOnAwake = false;
            _clip = MakeWindClip();
            _source.clip = _clip;
        }

        /// <summary>White noise through a one-pole low-pass, ~2s. The tail
        /// crossfades into a copy of the head so the loop point doesn't click.</summary>
        static AudioClip MakeWindClip()
        {
            int n = (int)(SampleRate * ClipSeconds);
            var data = new float[n];
            float s = 0f;
            for (int i = 0; i < n; i++)
            {
                float white = Random.Range(-1f, 1f);
                s += LowPassCoeff * (white - s);
                data[i] = s * NoiseAmplitude;
            }

            var head = new float[CrossfadeSamples];
            System.Array.Copy(data, 0, head, 0, CrossfadeSamples);
            for (int i = 0; i < CrossfadeSamples; i++)
            {
                float t = i / (float)CrossfadeSamples;
                int tail = n - CrossfadeSamples + i;
                data[tail] = Mathf.Lerp(data[tail], head[i], t);
            }

            // The crossfade morphs the tail INTO the head segment, but the
            // wrap still lands head-end -> data[0]: two decorrelated samples,
            // a soft tick every loop. Pin data[0] to the final sample and
            // ramp back into the real head over ~3ms (inaudible in noise).
            const int junctionRamp = 128;
            for (int i = 0; i < junctionRamp; i++)
                data[i] = Mathf.Lerp(data[n - 1], data[i], i / (float)junctionRamp);

            var clip = AudioClip.Create("wind", n, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        void OnDestroy()
        {
            if (_clip != null) Destroy(_clip);
        }

        void Update()
        {
            if (Motor == null) return;
            float speed = Motor.Core.Velocity.magnitude;

            float presence = Mathf.InverseLerp(VolumeLowSpeed, VolumeHighSpeed, speed);
            float volume = presence * presence * MaxVolume * FeelSettings.WindScale;
            _source.volume = volume;
            _source.pitch = Mathf.Lerp(MinPitch, MaxPitch,
                Mathf.InverseLerp(PitchLowSpeed, PitchHighSpeed, speed));

            if (volume < StopVolume)
            {
                if (_source.isPlaying) _source.Stop();
            }
            else if (!_source.isPlaying)
            {
                _source.Play();
            }
        }
    }
}
