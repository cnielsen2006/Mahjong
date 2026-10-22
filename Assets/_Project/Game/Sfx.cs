using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Mahjong.Game
{
    /// <summary>Procedurally synthesised sound effects and a generative ambient music loop. No audio files.</summary>
    public sealed class Sfx : MonoBehaviour
    {
        const int Rate = 22050;
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        AudioSource[] voices;
        int nextVoice;
        AudioSource music;
        Task<float[]> musicTask;
        AudioClip musicClip;

        public float SoundVolume = 1f;
        public float MusicVolume = 0.3f;

        public void Init()
        {
            voices = new AudioSource[8];
            for (int i = 0; i < voices.Length; i++)
            {
                voices[i] = gameObject.AddComponent<AudioSource>();
                voices[i].playOnAwake = false;
                voices[i].spatialBlend = 0f;
            }
            music = gameObject.AddComponent<AudioSource>();
            music.loop = true; music.playOnAwake = false; music.spatialBlend = 0f;

            Make("select", Tone(0.09f, 0.002f, 40f, 740f, 0f, 0.5f));
            Make("click", Tone(0.04f, 0.001f, 90f, 520f, 0f, 0.35f));
            Make("match", Chime(new[] { 523.25f, 659.25f, 783.99f }, 0.09f, 0.55f));
            Make("nope", Tone(0.16f, 0.003f, 22f, 150f, 0.6f, 0.5f));
            Make("hint", Chime(new[] { 987.77f, 1318.5f }, 0.12f, 0.35f));
            Make("undo", Sweep(0.2f, 640f, 300f, 0.4f));
            Make("shuffle", Swish(0.55f));
            Make("stuck", Chime(new[] { 392f, 311.13f }, 0.18f, 0.4f));
            Make("win", Chime(new[] { 523.25f, 659.25f, 783.99f, 1046.5f, 1318.5f }, 0.13f, 0.6f));
            Make("page", Tone(0.06f, 0.002f, 55f, 440f, 0f, 0.25f));

            musicTask = Bg.Run(() => MakeMusic(0));
        }

        void Make(string name, float[] data)
        {
            var c = AudioClip.Create(name, data.Length, 1, Rate, false);
            c.SetData(data, 0);
            clips[name] = c;
        }

        public void Play(string name, float vol = 1f, float pitch = 1f)
        {
            if (voices == null || SoundVolume <= 0.001f) return;
            AudioClip c;
            if (!clips.TryGetValue(name, out c)) return;
            var v = voices[nextVoice++ % voices.Length];
            v.pitch = pitch;
            v.volume = vol * SoundVolume;
            v.clip = c;
            v.Play();
        }

        void Update()
        {
            if (musicTask != null && musicTask.IsCompleted)
            {
                if (!musicTask.IsFaulted)
                {
                    var data = musicTask.Result;
                    musicClip = AudioClip.Create("music", data.Length, 1, Rate, false);
                    musicClip.SetData(data, 0);
                    music.clip = musicClip;
                    music.Play();
                }
                musicTask = null;
            }
            if (music != null) music.volume = MusicVolume;
        }

        // ------------------------------------------------------------------ synthesis

        static float[] Tone(float dur, float attack, float decayRate, float freq, float noise, float gain)
        {
            int n = (int)(dur * Rate);
            var d = new float[n];
            var rnd = new System.Random(7);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Min(1f, t / Mathf.Max(attack, 0.0005f)) * Mathf.Exp(-t * decayRate);
                float s = Mathf.Sin(2 * Mathf.PI * freq * t) + 0.35f * Mathf.Sin(4 * Mathf.PI * freq * t);
                s += noise * ((float)rnd.NextDouble() * 2f - 1f);
                d[i] = s * env * gain * 0.6f;
            }
            return d;
        }

        static float[] Sweep(float dur, float f0, float f1, float gain)
        {
            int n = (int)(dur * Rate);
            var d = new float[n];
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate, k = i / (float)n;
                float f = Mathf.Lerp(f0, f1, k);
                phase += 2 * Mathf.PI * f / Rate;
                d[i] = Mathf.Sin(phase) * Mathf.Sin(Mathf.PI * k) * gain * 0.6f;
            }
            return d;
        }

        static float[] Chime(float[] notes, float spacing, float gain)
        {
            float tail = 0.55f;
            int n = (int)((spacing * notes.Length + tail) * Rate);
            var d = new float[n];
            for (int k = 0; k < notes.Length; k++)
            {
                int start = (int)(k * spacing * Rate);
                float f = notes[k];
                for (int i = start; i < n; i++)
                {
                    float t = (i - start) / (float)Rate;
                    float env = Mathf.Min(1f, t / 0.004f) * Mathf.Exp(-t * 6.5f);
                    if (env < 0.0008f) break;
                    float s = Mathf.Sin(2 * Mathf.PI * f * t) + 0.3f * Mathf.Sin(2 * Mathf.PI * f * 2.01f * t) + 0.12f * Mathf.Sin(2 * Mathf.PI * f * 3.02f * t);
                    d[i] += s * env * gain * 0.35f;
                }
            }
            return d;
        }

        static float[] Swish(float dur)
        {
            int n = (int)(dur * Rate);
            var d = new float[n];
            var rnd = new System.Random(11);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float k = i / (float)n;
                float env = Mathf.Sin(Mathf.PI * k);
                float cutoff = Mathf.Lerp(0.05f, 0.45f, k);
                lp += ((float)rnd.NextDouble() * 2f - 1f - lp) * cutoff;
                d[i] = lp * env * 0.55f;
            }
            return d;
        }

        /// <summary>Slow pentatonic pad with sparse bell notes. Pure function of the seed, loops seamlessly.</summary>
        static float[] MakeMusic(int seed)
        {
            float[] scale = { 0f, 2f, 4f, 7f, 9f };       // C major pentatonic
            float root = 130.81f;                        // C3
            int bars = 12; float barLen = 4.0f;
            int n = (int)(bars * barLen * Rate);
            var d = new float[n];
            var rnd = new System.Random(1000 + seed);
            int[][] chords = { new[] { 0, 2, 4 }, new[] { 1, 3, 0 }, new[] { 2, 4, 1 }, new[] { 0, 3, 4 } };
            for (int b = 0; b < bars; b++)
            {
                var ch = chords[b % chords.Length];
                int start = (int)(b * barLen * Rate);
                int len = (int)((barLen + 1.5f) * Rate);
                foreach (int deg in ch)
                {
                    float f = root * Mathf.Pow(2f, scale[deg] / 12f);
                    for (int i = 0; i < len; i++)
                    {
                        int idx = (start + i) % n;
                        float t = i / (float)Rate;
                        float env = Mathf.Min(1f, t / 1.4f) * Mathf.Min(1f, (len - i) / (1.4f * Rate));
                        float s = Mathf.Sin(2 * Mathf.PI * f * t) * 0.6f + Mathf.Sin(2 * Mathf.PI * f * 2f * t) * 0.18f;
                        d[idx] += s * env * 0.07f;
                    }
                }
                // a couple of soft bell notes per bar
                for (int k = 0; k < 3; k++)
                {
                    if (rnd.NextDouble() < 0.35) continue;
                    int deg = rnd.Next(5);
                    float f = root * 4f * Mathf.Pow(2f, scale[deg] / 12f);
                    int bs = start + (int)((0.4f + k * 1.2f + (float)rnd.NextDouble() * 0.3f) * Rate);
                    int bl = (int)(2.2f * Rate);
                    for (int i = 0; i < bl; i++)
                    {
                        int idx = (bs + i) % n;
                        float t = i / (float)Rate;
                        float env = Mathf.Min(1f, t / 0.01f) * Mathf.Exp(-t * 2.4f);
                        d[idx] += Mathf.Sin(2 * Mathf.PI * f * t) * env * 0.05f;
                    }
                }
            }
            return d;
        }
    }
}
