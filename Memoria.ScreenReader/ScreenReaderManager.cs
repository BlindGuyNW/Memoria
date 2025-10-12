using System;
using Tolk;

namespace Memoria.ScreenReader
{
    /// <summary>
    /// Manages screen reader output for accessibility
    /// </summary>
    public sealed class ScreenReaderManager
    {
        private static ScreenReaderManager _instance;
        private static readonly object _lock = new object();
        private Tolk.Tolk _tolk;
        private bool _isInitialized;
        private bool _isEnabled;

        /// <summary>
        /// Gets the singleton instance
        /// </summary>
        public static ScreenReaderManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new ScreenReaderManager();
                        }
                    }
                }
                return _instance;
            }
        }

        private ScreenReaderManager()
        {
            _tolk = new Tolk.Tolk();
            _isInitialized = false;
            _isEnabled = true;
        }

        /// <summary>
        /// Initializes the screen reader library
        /// </summary>
        public void Initialize()
        {
            if (_isInitialized)
                return;

            try
            {
                _tolk.TrySAPI(true); // Try SAPI as fallback if no screen reader detected
                _tolk.Load();
                _isInitialized = true;

                // Announce that screen reader support is active
                if (_tolk.IsLoaded())
                {
                    string screenReaderName = _tolk.DetectScreenReader();
                    if (!string.IsNullOrEmpty(screenReaderName))
                    {
                        Speak("Screen reader support enabled for Final Fantasy IX: " + screenReaderName, true);
                    }
                    else
                    {
                        Speak("Screen reader support enabled for Final Fantasy IX", true);
                    }
                }
            }
            catch (Exception)
            {
                // Silently fail - screen reader not available
                _isInitialized = false;
                _isEnabled = false;
            }
        }

        /// <summary>
        /// Shuts down the screen reader library
        /// </summary>
        public void Shutdown()
        {
            if (!_isInitialized)
                return;

            try
            {
                _tolk.Unload();
                _isInitialized = false;
            }
            catch
            {
                // Silently fail
            }
        }

        /// <summary>
        /// Speaks text through the screen reader
        /// </summary>
        /// <param name="text">Text to speak</param>
        /// <param name="interrupt">Whether to interrupt currently speaking text</param>
        public void Speak(string text, bool interrupt = false)
        {
            if (!_isEnabled || !_isInitialized || string.IsNullOrEmpty(text))
                return;

            try
            {
                _tolk.Output(text, interrupt);
            }
            catch
            {
                // Silently fail
            }
        }

        /// <summary>
        /// Speaks text through the screen reader (always interrupts)
        /// </summary>
        /// <param name="text">Text to speak</param>
        public void SpeakInterrupt(string text)
        {
            Speak(text, true);
        }

        /// <summary>
        /// Silences the screen reader
        /// </summary>
        public void Silence()
        {
            if (!_isEnabled || !_isInitialized)
                return;

            try
            {
                _tolk.Silence();
            }
            catch
            {
                // Silently fail
            }
        }

        /// <summary>
        /// Checks if the screen reader is currently speaking
        /// </summary>
        public bool IsSpeaking()
        {
            if (!_isInitialized)
                return false;

            try
            {
                return _tolk.IsSpeaking();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if a screen reader is detected
        /// </summary>
        public bool IsScreenReaderActive()
        {
            if (!_isInitialized)
                return false;

            try
            {
                return _tolk.IsLoaded() && _tolk.HasSpeech();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gets the name of the active screen reader
        /// </summary>
        public string GetScreenReaderName()
        {
            if (!_isInitialized)
                return null;

            try
            {
                return _tolk.DetectScreenReader();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gets or sets whether screen reader output is enabled
        /// </summary>
        public bool Enabled
        {
            get { return _isEnabled; }
            set { _isEnabled = value; }
        }

        /// <summary>
        /// Gets whether the screen reader manager is initialized
        /// </summary>
        public bool IsInitialized
        {
            get { return _isInitialized; }
        }
    }
}
