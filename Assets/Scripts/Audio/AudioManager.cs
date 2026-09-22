using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using AlchemistsArsenal.Core;
using AlchemistsArsenal.Combat;

namespace AlchemistsArsenal.Audio
{
    public enum Sfx { Confirm, Deny, Tab, Seal, Detonate, Coin, Chime, Hurt }

    /// <summary>
    /// Fully procedural audio for the slice (DESIGN.md §5.6 / eval-audio-usability):
    /// two adaptive music beds (cozy shop / tense forest) that crossfade on phase
    /// change, a bank of synthesised SFX, and an "Animalese" speech synth. No audio
    /// asset files — everything is generated with <see cref="AudioClip.Create"/>.
    /// Volumes come live from <see cref="SettingsService"/>.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        private const int SampleRate = 44100;

        private AudioSource _musicA, _musicB, _sfx, _speech;
        private AudioSource _activeMusic;
        private readonly Dictionary<Sfx, AudioClip> _sfxCache = new Dictionary<Sfx, AudioClip>();
        private AudioClip _shopBed, _forestBed;
        private AudioClip _lullaby, _reveal, _resolution;
        private AlchemistsArsenal.Story.StoryMusic? _story;
        private bool _forest;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            _musicA = Child("MusicA", loop: true);
            _musicB = Child("MusicB", loop: true);
            _sfx = Child("Sfx", loop: false);
            _speech = Child("Speech", loop: false);
            _activeMusic = _musicA;

            _shopBed = BuildBed(cozy: true);
            _forestBed = BuildBed(cozy: false);
            _lullaby = BuildLullaby(major: false);
            _resolution = BuildLullaby(major: true);
            _reveal = BuildDrone();

            SettingsService.OnChanged += ApplyVolumes;
            ApplyVolumes();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            SettingsService.OnChanged -= ApplyVolumes;
            if (GameLoopManager.Instance != null) GameLoopManager.Instance.OnPhaseChanged -= OnPhase;
            BombProjectile2D.OnDetonatedGlobal -= OnDetonated;
        }

        private void Start()
        {
            if (GameLoopManager.Instance != null) GameLoopManager.Instance.OnPhaseChanged += OnPhase;
            BombProjectile2D.OnDetonatedGlobal += OnDetonated;
            PlayBed(forest: false);
        }

        private AudioSource Child(string name, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.loop = loop;
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            return s;
        }

        private void ApplyVolumes()
        {
            float m = SettingsService.MasterVolume;
            _musicA.volume = _musicA == _activeMusic ? m * SettingsService.MusicVolume : 0f;
            _musicB.volume = _musicB == _activeMusic ? m * SettingsService.MusicVolume : 0f;
            _sfx.volume = m * SettingsService.SfxVolume;
            _speech.volume = m * SettingsService.SpeechVolume;
        }

        // ------------------------------------------------------------- music

        private void OnPhase(GamePhase phase)
        {
            bool forest = phase == GamePhase.Afternoon;
            if (forest != _forest) PlayBed(forest);
        }

        /// <summary>
        /// Music under a cutscene, or null to go back to the day's own bed. The
        /// lullaby is the kettle-charm, the tune Grandmother hummed; the ending plays
        /// it again in a major key.
        /// </summary>
        public static void PlayStory(AlchemistsArsenal.Story.StoryMusic? music)
        {
            if (Instance == null || Instance._story == music) return;
            Instance._story = music;
            Instance.PlayBed(Instance._forest);
        }

        private void PlayBed(bool forest)
        {
            _forest = forest;
            AudioSource from = _activeMusic;
            AudioSource to = _activeMusic == _musicA ? _musicB : _musicA;
            to.clip = _story switch
            {
                AlchemistsArsenal.Story.StoryMusic.Lullaby => _lullaby,
                AlchemistsArsenal.Story.StoryMusic.Reveal => _reveal,
                AlchemistsArsenal.Story.StoryMusic.Resolution => _resolution,
                _ => forest ? _forestBed : _shopBed,
            };
            to.time = 0f;
            to.Play();
            _activeMusic = to;
            StopAllCoroutines();
            StartCoroutine(Crossfade(from, to));
        }

        private IEnumerator Crossfade(AudioSource from, AudioSource to)
        {
            float dur = SettingsService.ReduceMotion ? 0.2f : 1.4f;
            float target = SettingsService.MasterVolume * SettingsService.MusicVolume;
            for (float t = 0; t < dur; t += Time.unscaledDeltaTime)
            {
                float k = t / dur;
                if (from != null) from.volume = target * (1f - k);
                to.volume = target * k;
                yield return null;
            }
            if (from != null) { from.Stop(); from.volume = 0f; }
            to.volume = target;
        }

        // ------------------------------------------------------------- sfx

        public static void Play(Sfx cue)
        {
            if (Instance == null) return;
            Instance._sfx.PlayOneShot(Instance.Clip(cue));
        }

        private void OnDetonated(DetonationInfo d) => _sfx.PlayOneShot(Clip(Sfx.Detonate));

        private AudioClip Clip(Sfx cue)
        {
            if (_sfxCache.TryGetValue(cue, out var c) && c != null) return c;
            c = cue switch
            {
                Sfx.Confirm => Blip(660, 0.10f, 0.5f, rising: true),
                Sfx.Deny => Blip(180, 0.14f, 0.5f, rising: false),
                Sfx.Tab => Blip(520, 0.05f, 0.35f, rising: true),
                Sfx.Seal => Thud(120, 0.18f),
                Sfx.Detonate => Noise(0.22f, 0.6f),
                Sfx.Coin => Blip(1040, 0.07f, 0.4f, rising: true),
                Sfx.Chime => Chord(new[] { 523f, 659f, 784f }, 0.5f),
                Sfx.Hurt => Blip(220, 0.10f, 0.5f, rising: false),
                _ => Blip(440, 0.1f, 0.4f, true),
            };
            _sfxCache[cue] = c;
            return c;
        }

        // ------------------------------------------------------------- speech

        private Coroutine _speaking;

        /// <summary>Animalese: one short pitched vowel per non-space glyph. Only one line at a time.</summary>
        public static void Speak(string text, float basePitch = 1f)
        {
            if (Instance == null || string.IsNullOrEmpty(text)) return;
            if (Instance._speaking != null) Instance.StopCoroutine(Instance._speaking);
            Instance._speaking = Instance.StartCoroutine(Instance.SpeakRoutine(text, basePitch));
        }

        private IEnumerator SpeakRoutine(string text, float basePitch)
        {
            var wait = new WaitForSecondsRealtime(0.045f);
            foreach (char ch in text)
            {
                if (char.IsWhiteSpace(ch)) { yield return wait; continue; }
                float pitch = basePitch * (0.85f + ((ch * 37) % 40) / 100f);
                _speech.pitch = pitch;
                _speech.PlayOneShot(VowelClip());
                yield return wait;
            }
            _speech.pitch = 1f;
            _speaking = null;
        }

        private AudioClip _vowel;
        private AudioClip VowelClip()
        {
            if (_vowel != null) return _vowel;
            _vowel = Blip(300, 0.06f, 0.5f, rising: false);
            _vowel.name = "animalese_vowel";
            return _vowel;
        }

        // ------------------------------------------------------------- synths

        private static AudioClip Blip(float freq, float seconds, float amp, bool rising)
        {
            int n = Mathf.CeilToInt(seconds * SampleRate);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float env = Mathf.Exp(-6f * (i / (float)n));
                float f = rising ? freq * (1f + t * 3f) : freq * (1f - t * 1.5f);
                data[i] = Mathf.Sin(2f * Mathf.PI * f * t) * env * amp;
            }
            return FromData("blip", data);
        }

        private static AudioClip Thud(float freq, float seconds)
        {
            int n = Mathf.CeilToInt(seconds * SampleRate);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float env = Mathf.Exp(-14f * (i / (float)n));
                data[i] = Mathf.Sin(2f * Mathf.PI * freq * (i / (float)SampleRate)) * env * 0.7f;
            }
            return FromData("thud", data);
        }

        private static AudioClip Noise(float seconds, float amp)
        {
            int n = Mathf.CeilToInt(seconds * SampleRate);
            var data = new float[n];
            var rng = new System.Random(1);
            for (int i = 0; i < n; i++)
            {
                float env = Mathf.Exp(-9f * (i / (float)n));
                data[i] = (float)(rng.NextDouble() * 2 - 1) * env * amp;
            }
            return FromData("noise", data);
        }

        private static AudioClip Chord(float[] freqs, float seconds)
        {
            int n = Mathf.CeilToInt(seconds * SampleRate);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float env = Mathf.Exp(-4f * (i / (float)n));
                float s = 0f;
                foreach (float f in freqs) s += Mathf.Sin(2f * Mathf.PI * f * t);
                data[i] = s / freqs.Length * env * 0.5f;
            }
            return FromData("chord", data);
        }

        /// <summary>A short looping arpeggio bed — major/relaxed or minor/tense.</summary>
        private static AudioClip BuildBed(bool cozy)
        {
            float[] scale = cozy
                ? new[] { 261.6f, 329.6f, 392f, 523.3f, 392f, 329.6f }   // C major triad wander
                : new[] { 220f, 261.6f, 311.1f, 349.2f, 311.1f, 246.9f }; // A minor-ish, unsettled
            float noteLen = cozy ? 0.5f : 0.32f;
            int notes = scale.Length * 4;
            int nPerNote = Mathf.CeilToInt(noteLen * SampleRate);
            var data = new float[notes * nPerNote];
            for (int k = 0; k < notes; k++)
            {
                float f = scale[k % scale.Length] * (k / scale.Length % 2 == 1 ? 0.5f : 1f);
                for (int i = 0; i < nPerNote; i++)
                {
                    float t = i / (float)SampleRate;
                    float env = Mathf.Sin(Mathf.PI * (i / (float)nPerNote));
                    float pad = 0.15f * Mathf.Sin(2f * Mathf.PI * f * 0.5f * t);
                    data[k * nPerNote + i] = (Mathf.Sin(2f * Mathf.PI * f * t) * 0.22f + pad) * env * (cozy ? 0.8f : 0.7f);
                }
            }
            var clip = FromData(cozy ? "shop_bed" : "forest_bed", data);
            return clip;
        }

        /// <summary>
        /// The kettle-charm lullaby on a music box: a plucked note that rings and dies,
        /// with its octave, over a soft root. Minor while Tam sleeps; major once they wake.
        /// </summary>
        private static AudioClip BuildLullaby(bool major)
        {
            float C = major ? 554.4f : 523.3f, F = major ? 740f : 698.5f, G = major ? 830.6f : 784f;
            float[] melody = { 440f, C, 659.3f, 587.3f, C, 493.9f, 440f, 329.6f, 440f, C, 659.3f, G, F, 659.3f, 587.3f, 659.3f };
            float[] roots = { 110f, 110f, 146.8f, 164.8f };
            const float noteLen = 0.55f;
            int nPer = Mathf.CeilToInt(noteLen * SampleRate);
            var data = new float[melody.Length * nPer];
            for (int k = 0; k < melody.Length; k++)
            {
                float f = melody[k], root = roots[(k / 4) % roots.Length];
                for (int i = 0; i < nPer; i++)
                {
                    float t = i / (float)SampleRate;
                    float pluck = Mathf.Exp(-3.2f * t) * (Mathf.Sin(2f * Mathf.PI * f * t) + 0.3f * Mathf.Exp(-4f * t) * Mathf.Sin(4f * Mathf.PI * f * t));
                    float pad = 0.1f * Mathf.Sin(2f * Mathf.PI * root * (k * noteLen + t));
                    data[k * nPer + i] = (pluck * 0.22f + pad) * 0.8f;
                }
            }
            return FromData(major ? "story_resolution" : "story_lullaby", data);
        }

        /// <summary>The reveal: a low fifth with a tritone rubbing against it, breathing slowly, and a far bell.</summary>
        private static AudioClip BuildDrone()
        {
            const float seconds = 8f;
            int n = Mathf.CeilToInt(seconds * SampleRate);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float breathe = 0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * t / seconds);
                float s = Mathf.Sin(2f * Mathf.PI * 55f * t) + 0.7f * Mathf.Sin(2f * Mathf.PI * 82.4f * t) + 0.35f * Mathf.Sin(2f * Mathf.PI * 77.8f * t);
                float bellT = t % 4f;
                float bell = Mathf.Exp(-2.5f * bellT) * Mathf.Sin(2f * Mathf.PI * 1244.5f * bellT) * 0.18f;
                data[i] = s * 0.09f * breathe + bell;
            }
            return FromData("story_reveal", data);
        }

        private static AudioClip FromData(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
