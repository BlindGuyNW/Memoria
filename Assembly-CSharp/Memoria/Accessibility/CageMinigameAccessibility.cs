using System;
using UnityEngine;
using Memoria.ScreenReader;
using Memoria.Prime;

namespace Memoria.Accessibility
{
    /// <summary>
    /// Provides accessibility support for the cage swinging minigame on map 1208.
    /// Offers both audio feedback and skip functionality for blind players.
    /// </summary>
    public class CageMinigameAccessibility : PersistenSingleton<CageMinigameAccessibility>
    {
        private bool _isActive = false;
        private EventVariableMonitor _variableMonitor;
        private AudioCueManager _audioCueManager;
        private float _lastAnnouncementTime = 0f;
        private float _lastUpdateTime = 0f;
        private float _lastLogTime = 0f;
        private bool _hasAnnouncedInitialization = false;
        private bool _skipRequested = false;

        // Track previous state to only announce on changes
        private bool _previousIsBalanced = false;
        private string _previousDirection = "";
        private int _previousLeanPercentage = 0;
        private string _previousSwingDirection = ""; // Track which way cage is moving

        // Cooldown to prevent rapid re-initialization after shutdown
        private static float _lastShutdownTime = 0f;
        private const float INITIALIZATION_COOLDOWN = 2.0f; // 2 seconds

        // Cage minigame variable indices (identified from event script analysis)
        // These are GLOBAL scope variables used by Code7_Loop in map 1208
        private const int VAR_CAGE_POSITION_INDEX = 34; // Global Int24 - horizontal swing position
        private const int VAR_CAGE_VELOCITY_INDEX = 40; // Global Int24 - swing velocity/momentum
        private const int VAR_INPUT_FORCE_INDEX = 43;   // Global Int16 - applied input force
        private const int VAR_INPUT_COOLDOWN_INDEX = 45; // Global Int16 - button cooldown timer

        // Variable operation code for globDialogProgression (controls HUD state)
        private const int VAR_OP_GLOB_DIALOG_PROGRESSION = 6357;

        // Game state interpretation (from script analysis)
        // Success zone: position between -500 and +500, velocity between -100 and +100, force == 0
        private const int POSITION_SUCCESS_MIN = -500;
        private const int POSITION_SUCCESS_MAX = 500;
        private const int POSITION_FAIL_THRESHOLD = 55000; // Loop exits when position exceeds this

        private const int VELOCITY_SUCCESS_MIN = -100;
        private const int VELOCITY_SUCCESS_MAX = 100;

        // For normalized display (treating values >32768 as negative in signed 16-bit)
        private const int POSITION_DISPLAY_RANGE = 10000; // Approximate maximum swing before failure

        /// <summary>
        /// Initialize the accessibility system for the cage minigame.
        /// </summary>
        public void Initialize()
        {
            if (_isActive)
                return;

            // Prevent rapid re-initialization after shutdown (cooldown period)
            if (Time.time - _lastShutdownTime < INITIALIZATION_COOLDOWN)
            {
                Log.Message("[CageMinigameAccessibility] Initialization blocked - cooldown active ({0:F1}s remaining)",
                    INITIALIZATION_COOLDOWN - (Time.time - _lastShutdownTime));
                return;
            }

            Log.Message("[CageMinigameAccessibility] Initializing");

            _isActive = true;
            _hasAnnouncedInitialization = false;
            _skipRequested = false;
            _lastAnnouncementTime = 0f;
            _lastUpdateTime = Time.time;

            // Reset state tracking
            _previousIsBalanced = false;
            _previousDirection = "";
            _previousLeanPercentage = 0;
            _previousSwingDirection = "";

            // Initialize screen reader
            ScreenReaderManager.Instance.Initialize();

            // Create variable monitor
            if (_variableMonitor == null)
                _variableMonitor = new EventVariableMonitor();

            // Create audio cue manager if configured
            if (Configuration.Accessibility.CageMinigameUseTones)
            {
                if (_audioCueManager == null)
                {
                    GameObject audioObject = new GameObject("CageMinigameAudio");
                    audioObject.transform.SetParent(transform);
                    _audioCueManager = audioObject.AddComponent<AudioCueManager>();
                }
            }

            // Start tracking variables for debugging (can be disabled after variables are identified)
            if (Application.isEditor)
            {
                _variableMonitor.StartTracking();
                EventVariableMonitor.DumpAllMapVariables();
            }

            // Log initial variable state
            Log.Message("[CageMinigame] ====== MINIGAME STARTED ======");
            Log.Message("[CageMinigame] Initial Position (VAR_34): {0}", EventVariableMonitor.GetGlobalInt24(34));
            Log.Message("[CageMinigame] Initial Velocity (VAR_40): {0}", EventVariableMonitor.GetGlobalInt24(40));
            Log.Message("[CageMinigame] Initial InputForce (VAR_43): {0}", EventVariableMonitor.GetGlobalInt16(43));
            Log.Message("[CageMinigame] Map: {0}, ScenarioCounter: {1}",
                FF9StateSystem.Common.FF9.fldMapNo,
                FF9StateSystem.EventState.ScenarioCounter);

            // Initial announcement with clear instructions
            string message = Configuration.Accessibility.CageMinigameSkipEnabled
                ? "Cage minigame started. Press Enter to skip. Goal: Build momentum to break the cage. Press left when swinging left, right when swinging right."
                : "Cage minigame started. Goal: Build momentum to break the cage. Press left when swinging left, right when swinging right.";

            ScreenReaderManager.Instance.Speak(message, false);
            _hasAnnouncedInitialization = true;

            // Start audio feedback if enabled
            if (Configuration.Accessibility.CageMinigameAudioFeedback && _audioCueManager != null)
            {
                _audioCueManager.StartContinuousTone();
            }

            Log.Message("[CageMinigameAccessibility] Initialized successfully");
        }

        /// <summary>
        /// Shutdown the accessibility system.
        /// </summary>
        public void Shutdown()
        {
            if (!_isActive)
                return;

            Log.Message("[CageMinigameAccessibility] Shutting down");

            _isActive = false;
            _lastShutdownTime = Time.time; // Record shutdown time for cooldown

            // Stop audio
            if (_audioCueManager != null)
            {
                _audioCueManager.StopContinuousTone();
            }

            // Stop variable tracking and report
            if (_variableMonitor != null && Application.isEditor)
            {
                _variableMonitor.StopTrackingAndReport();
            }

            Log.Message("[CageMinigameAccessibility] Shutdown complete");
        }

        /// <summary>
        /// Update loop - called each frame while the minigame is active.
        /// </summary>
        public void Update()
        {
            if (!_isActive)
                return;

            // Auto-shutdown if we've left map 1208 (field changed)
            if (FF9StateSystem.Common.FF9.fldMapNo != 1208)
            {
                Log.Message("[CageMinigameAccessibility] Detected field change from map 1208, shutting down");
                Shutdown();
                return;
            }

            // Update variable monitoring for debugging
            if (_variableMonitor != null && Application.isEditor)
            {
                _variableMonitor.Update();
            }

            // Check for skip key (Enter/Return to avoid conflict with AccessibleNavigation)
            if (Configuration.Accessibility.CageMinigameSkipEnabled && Input.GetKeyDown(KeyCode.Return))
            {
                SkipMinigame();
                return;
            }

            // Read current game state from event variables
            CageState state = ReadCageState();

            // Update audio feedback
            if (Configuration.Accessibility.CageMinigameAudioFeedback && _audioCueManager != null)
            {
                UpdateAudioFeedback(state);
            }

            // Provide periodic speech announcements
            if (Configuration.Accessibility.CageMinigameAudioFeedback)
            {
                ProvidePeriodicFeedback(state);
            }

            // Check for completion
            CheckForCompletion(state);

            _lastUpdateTime = Time.time;
        }

        /// <summary>
        /// Skip the minigame by forcing it to close.
        /// </summary>
        private void SkipMinigame()
        {
            Log.Message("[CageMinigameAccessibility] Skip requested");

            _skipRequested = true;

            ScreenReaderManager.Instance.Speak("Skipping cage minigame", false);

            try
            {
                // To skip, we ONLY set position >= 55000 to exit the event script's while loop.
                // We let the event script naturally handle everything else:
                // - Setting General_ScenarioCounter to 5030
                // - HP/MP restoration
                // - Field(1209) transition
                //
                // This is the minimal intervention approach - we just force the loop exit condition
                // and let the script run its normal cleanup and transition code.

                // Force the event script loop to exit by exceeding the position threshold
                EventVariableMonitor.SetMapInt24(VAR_CAGE_POSITION_INDEX, POSITION_FAIL_THRESHOLD + 1);
                Log.Message("[CageMinigameAccessibility] Set position to {0} to exit event loop", POSITION_FAIL_THRESHOLD + 1);
                Log.Message("[CageMinigameAccessibility] Letting event script handle natural cleanup and field transition");
            }
            catch (Exception ex)
            {
                Log.Error("[CageMinigameAccessibility] Failed to skip minigame: {0}", ex.Message);
            }

            // Note: We stay active and let the event script naturally transition to Field 1209.
            // CloseSpecialHUD will be called when the field changes, which will trigger our Shutdown().
        }

        /// <summary>
        /// Read the current cage state from event variables.
        /// </summary>
        private CageState ReadCageState()
        {
            CageState state = new CageState();

            // Read variables (Map scope - Hades "Global" variables are stored in C# Map buffer)
            state.position = EventVariableMonitor.GetMapInt24(VAR_CAGE_POSITION_INDEX);
            state.velocity = EventVariableMonitor.GetMapInt24(VAR_CAGE_VELOCITY_INDEX);
            state.inputForce = EventVariableMonitor.GetMapInt16(VAR_INPUT_FORCE_INDEX);
            state.inputCooldown = EventVariableMonitor.GetMapInt16(VAR_INPUT_COOLDOWN_INDEX);

            // Calculate normalized position (-1 to +1) for audio feedback
            // Clamp to display range to avoid extreme values
            int clampedPosition = Math.Max(-POSITION_DISPLAY_RANGE, Math.Min(POSITION_DISPLAY_RANGE, state.position));
            state.normalizedPosition = (float)clampedPosition / POSITION_DISPLAY_RANGE;

            // Determine if balanced (in success zone)
            state.isBalanced = state.position >= POSITION_SUCCESS_MIN &&
                              state.position <= POSITION_SUCCESS_MAX &&
                              state.velocity >= VELOCITY_SUCCESS_MIN &&
                              state.velocity <= VELOCITY_SUCCESS_MAX &&
                              state.inputForce == 0;

            // Determine direction based on position
            if (state.position < POSITION_SUCCESS_MIN)
                state.direction = "left";
            else if (state.position > POSITION_SUCCESS_MAX)
                state.direction = "right";
            else
                state.direction = "center";

            // Calculate lean percentage (0-100)
            int absPosition = Math.Abs(state.position);
            state.leanPercentage = Math.Min(100, (absPosition * 100) / POSITION_DISPLAY_RANGE);

            // Determine swing direction based on velocity
            // Velocity > 100 means moving right, < -100 means moving left
            if (state.velocity > 100)
                state.swingDirection = "swinging right";
            else if (state.velocity < -100)
                state.swingDirection = "swinging left";
            else
                state.swingDirection = "stopped";

            // Calculate progress toward goal (right side = 55000)
            // Progress is based on peak rightward position reached
            state.progressPercentage = Math.Min(100, Math.Max(0, (state.position * 100) / POSITION_FAIL_THRESHOLD));

            // Check for failure condition
            state.hasFailed = state.position >= POSITION_FAIL_THRESHOLD;

            return state;
        }

        /// <summary>
        /// Update audio tone based on cage position.
        /// </summary>
        private void UpdateAudioFeedback(CageState state)
        {
            if (_audioCueManager != null && Configuration.Accessibility.CageMinigameUseTones)
            {
                _audioCueManager.UpdatePositionTone(state.normalizedPosition);
            }
        }

        /// <summary>
        /// Provide speech announcements about cage state changes.
        /// Only announces when the state actually changes to avoid disorienting repetition.
        /// Focuses on swing direction to help players time their button presses.
        /// </summary>
        private void ProvidePeriodicFeedback(CageState state)
        {
            // Check if swing direction changed (most important - tells player when to press buttons)
            bool swingDirectionChanged = state.swingDirection != _previousSwingDirection;

            // Also check position changes for progress feedback
            bool directionChanged = state.direction != _previousDirection;
            int leanDifference = Math.Abs(state.leanPercentage - _previousLeanPercentage);
            bool leanChangedSignificantly = leanDifference >= 20; // 20% threshold for position changes

            // Only announce if something changed
            if (!swingDirectionChanged && !directionChanged && !leanChangedSignificantly)
                return;

            // Also respect minimum interval to avoid too-frequent announcements
            float currentTime = Time.time;
            float minInterval = 0.2f; // 200ms minimum between announcements
            if (currentTime - _lastAnnouncementTime < minInterval)
                return;

            _lastAnnouncementTime = currentTime;

            // Update previous state
            _previousSwingDirection = state.swingDirection;
            _previousDirection = state.direction;
            _previousLeanPercentage = state.leanPercentage;

            // Generate announcement focused on swing direction
            string announcement;

            if (state.swingDirection == "stopped")
            {
                announcement = "Stopped at center";
            }
            else
            {
                // Main announcement: which way the cage is swinging
                // This tells the player which button to press
                announcement = state.swingDirection;

                // Add progress indicator when they make significant rightward progress
                if (state.progressPercentage >= 80)
                    announcement += ", almost there";
                else if (state.progressPercentage >= 50)
                    announcement += String.Format(", {0} percent", state.progressPercentage);
            }

            ScreenReaderManager.Instance.Speak(announcement, true);
        }

        /// <summary>
        /// Check if the minigame has been completed (success or failure).
        /// </summary>
        private void CheckForCompletion(CageState state)
        {
            // The script will automatically exit and transition when position >= 55000
            // We detect completion by checking if the minigame is ending
            if (state.hasFailed)
            {
                // If we're skipping, don't shutdown - let the event script run its cleanup
                // and field transition naturally. Shutdown will be called when the map changes.
                if (_skipRequested)
                {
                    Log.Message("[CageMinigameAccessibility] Position threshold reached during skip - waiting for field transition");
                    return;
                }

                Log.Message("[CageMinigameAccessibility] Minigame ending - position threshold reached");
                ScreenReaderManager.Instance.Speak("Cage broken! Escaped successfully!", false);

                if (_audioCueManager != null)
                {
                    _audioCueManager.PlaySuccess();
                }

                Shutdown();
            }
            // Announce when very close to success
            else if (state.isBalanced)
            {
                // This will be announced via periodic feedback, but we could add a special sound here
                // to indicate they're in the success zone
            }
        }

        /// <summary>
        /// Struct to hold cage state information.
        /// </summary>
        private struct CageState
        {
            public int position;             // Raw position value (VAR_GlobInt24_34)
            public int velocity;             // Raw velocity value (VAR_GlobInt24_40)
            public int inputForce;           // Applied input force (VAR_GlobInt16_43)
            public int inputCooldown;        // Button cooldown timer (VAR_GlobInt16_45)
            public float normalizedPosition; // -1 (left) to +1 (right) for audio
            public bool isBalanced;          // True if in success zone
            public bool hasFailed;           // True if position >= 55000 (loop exit)
            public string direction;         // "left", "right", or "center" (position relative to center)
            public int leanPercentage;       // 0-100% how far from center
            public string swingDirection;    // "swinging left", "swinging right", or "stopped" (velocity direction)
            public int progressPercentage;   // 0-100% progress toward breaking free (right side)
        }

        /// <summary>
        /// Debug command to dump current variable state (call from console or debug menu).
        /// </summary>
        public void DebugDumpVariables()
        {
            Log.Message("[CageMinigameAccessibility] === Debug Variable Dump ===");
            Log.Message("  VAR_GlobInt24_34 (Position): {0}", EventVariableMonitor.GetGlobalInt24(VAR_CAGE_POSITION_INDEX));
            Log.Message("  VAR_GlobInt24_40 (Velocity): {0}", EventVariableMonitor.GetGlobalInt24(VAR_CAGE_VELOCITY_INDEX));
            Log.Message("  VAR_GlobInt16_43 (Input Force): {0}", EventVariableMonitor.GetGlobalInt16(VAR_INPUT_FORCE_INDEX));
            Log.Message("  VAR_GlobInt16_45 (Input Cooldown): {0}", EventVariableMonitor.GetGlobalInt16(VAR_INPUT_COOLDOWN_INDEX));

            CageState state = ReadCageState();
            Log.Message("  Normalized Position: {0:F2}", state.normalizedPosition);
            Log.Message("  Direction: {0}", state.direction);
            Log.Message("  Lean Percentage: {0}%", state.leanPercentage);
            Log.Message("  Is Balanced: {0}", state.isBalanced);
            Log.Message("  Has Failed: {0}", state.hasFailed);
            Log.Message("[CageMinigameAccessibility] === End Dump ===");
        }

        private void OnDestroy()
        {
            Shutdown();
        }
    }
}
