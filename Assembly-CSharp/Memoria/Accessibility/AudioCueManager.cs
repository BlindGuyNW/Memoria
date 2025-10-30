using System;
using UnityEngine;
using Memoria.Prime;

namespace Memoria.Accessibility
{
    /// <summary>
    /// Manages non-speech audio cues for minigame accessibility.
    /// Generates procedural tones to provide real-time feedback about game state.
    /// </summary>
    public class AudioCueManager : MonoBehaviour
    {
        private AudioSource _audioSource;
        private float _targetFrequency = 440f;
        private float _currentFrequency = 440f;
        private float _volume = 0.3f;
        private bool _isPlaying = false;
        private float _phase = 0f;
        private const float FREQUENCY_SMOOTH_SPEED = 5f; // How quickly frequency changes

        // Frequency ranges for cage position feedback
        private const float MIN_FREQUENCY = 220f; // A3 - cage far left
        private const float CENTER_FREQUENCY = 440f; // A4 - cage centered
        private const float MAX_FREQUENCY = 880f; // A5 - cage far right

        private void Awake()
        {
            // Create and configure AudioSource for procedural audio
            _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _audioSource.loop = true;
            _audioSource.volume = _volume;
            _audioSource.spatialBlend = 0f; // 2D sound

            Log.Message("[AudioCueManager] Initialized");
        }

        /// <summary>
        /// Start playing continuous audio cues.
        /// </summary>
        public void StartContinuousTone()
        {
            if (!_isPlaying && Configuration.Accessibility.CageMinigameUseTones)
            {
                _isPlaying = true;
                _audioSource.Play();
                Log.Message("[AudioCueManager] Started continuous tone");
            }
        }

        /// <summary>
        /// Stop playing continuous audio cues.
        /// </summary>
        public void StopContinuousTone()
        {
            if (_isPlaying)
            {
                _isPlaying = false;
                _audioSource.Stop();
                _phase = 0f;
                Log.Message("[AudioCueManager] Stopped continuous tone");
            }
        }

        /// <summary>
        /// Update the tone frequency based on cage position.
        /// </summary>
        /// <param name="normalizedPosition">Position from -1 (far left) to +1 (far right), 0 = center</param>
        public void UpdatePositionTone(float normalizedPosition)
        {
            if (!Configuration.Accessibility.CageMinigameUseTones)
                return;

            // Clamp to valid range
            normalizedPosition = Mathf.Clamp(normalizedPosition, -1f, 1f);

            // Map position to frequency
            // -1 = MIN_FREQUENCY, 0 = CENTER_FREQUENCY, +1 = MAX_FREQUENCY
            if (normalizedPosition < 0)
            {
                // Left side: interpolate between MIN and CENTER
                _targetFrequency = Mathf.Lerp(CENTER_FREQUENCY, MIN_FREQUENCY, -normalizedPosition);
            }
            else
            {
                // Right side: interpolate between CENTER and MAX
                _targetFrequency = Mathf.Lerp(CENTER_FREQUENCY, MAX_FREQUENCY, normalizedPosition);
            }

            // Smoothly transition frequency to avoid jarring jumps
            _currentFrequency = Mathf.Lerp(_currentFrequency, _targetFrequency, Time.deltaTime * FREQUENCY_SMOOTH_SPEED);
        }

        /// <summary>
        /// Play a success sound (rising tone).
        /// </summary>
        public void PlaySuccess()
        {
            StopContinuousTone();
            PlayChord(new float[] { 523.25f, 659.25f, 783.99f }, 0.5f); // C-E-G major chord
            Log.Message("[AudioCueManager] Played success sound");
        }

        /// <summary>
        /// Play a failure sound (descending tone).
        /// </summary>
        public void PlayFailure()
        {
            StopContinuousTone();
            PlayTone(220f, 0.3f); // Low A
            Log.Message("[AudioCueManager] Played failure sound");
        }

        /// <summary>
        /// Play a single tone at a specific frequency for a duration.
        /// </summary>
        private void PlayTone(float frequency, float duration)
        {
            // Create a temporary GameObject for the one-shot sound
            GameObject tempAudio = new GameObject("TempAudioCue");
            AudioSource source = tempAudio.AddComponent<AudioSource>();
            ToneGenerator generator = tempAudio.AddComponent<ToneGenerator>();

            generator.frequency = frequency;
            generator.duration = duration;
            generator.audioSource = source;

            source.volume = _volume;
            source.spatialBlend = 0f;
            source.Play();

            // Destroy after playing
            Destroy(tempAudio, duration + 0.1f);
        }

        /// <summary>
        /// Play multiple tones simultaneously (chord) for a duration.
        /// </summary>
        private void PlayChord(float[] frequencies, float duration)
        {
            foreach (float freq in frequencies)
            {
                PlayTone(freq, duration);
            }
        }

        /// <summary>
        /// Unity callback for generating procedural audio.
        /// </summary>
        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (!_isPlaying)
                return;

            float sampleRate = AudioSettings.outputSampleRate;
            float increment = _currentFrequency * 2f * Mathf.PI / sampleRate;

            for (int i = 0; i < data.Length; i += channels)
            {
                // Generate sine wave
                float sample = Mathf.Sin(_phase) * _volume;

                // Apply to all channels
                for (int channel = 0; channel < channels; channel++)
                {
                    data[i + channel] = sample;
                }

                // Advance phase
                _phase += increment;

                // Wrap phase to prevent overflow
                if (_phase > 2f * Mathf.PI)
                    _phase -= 2f * Mathf.PI;
            }
        }

        /// <summary>
        /// Set the volume for audio cues (0.0 to 1.0).
        /// </summary>
        public void SetVolume(float volume)
        {
            _volume = Mathf.Clamp01(volume);
            if (_audioSource != null)
                _audioSource.volume = _volume;
        }

        private void OnDestroy()
        {
            StopContinuousTone();
            Log.Message("[AudioCueManager] Destroyed");
        }
    }

    /// <summary>
    /// Helper component for generating single-tone audio cues.
    /// Used for one-shot sounds like success/failure indicators.
    /// </summary>
    internal class ToneGenerator : MonoBehaviour
    {
        public float frequency = 440f;
        public float duration = 0.5f;
        public AudioSource audioSource;
        private float _phase = 0f;
        private float _elapsedTime = 0f;

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (_elapsedTime >= duration)
                return;

            float sampleRate = AudioSettings.outputSampleRate;
            float increment = frequency * 2f * Mathf.PI / sampleRate;

            for (int i = 0; i < data.Length; i += channels)
            {
                // Calculate envelope (fade in/out to avoid clicks)
                float envelope = 1f;
                float fadeTime = 0.05f; // 50ms fade
                if (_elapsedTime < fadeTime)
                    envelope = _elapsedTime / fadeTime;
                else if (_elapsedTime > duration - fadeTime)
                    envelope = (duration - _elapsedTime) / fadeTime;

                // Generate sine wave with envelope
                float sample = Mathf.Sin(_phase) * envelope;

                // Apply to all channels
                for (int channel = 0; channel < channels; channel++)
                {
                    data[i + channel] = sample;
                }

                // Advance phase and time
                _phase += increment;
                if (_phase > 2f * Mathf.PI)
                    _phase -= 2f * Mathf.PI;

                _elapsedTime += 1f / sampleRate;
            }
        }
    }
}
