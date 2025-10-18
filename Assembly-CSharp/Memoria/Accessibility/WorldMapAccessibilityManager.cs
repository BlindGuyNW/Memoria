using System;
using System.Collections.Generic;
using UnityEngine;
using Memoria.ScreenReader;
using Memoria.Prime;
using Assets.Sources.Scripts.UI.Common;

namespace Memoria.Accessibility
{
    /// <summary>
    /// Provides screen reader accessibility for the world map by announcing available destinations
    /// and providing either autopilot or manual turn-by-turn navigation guidance.
    /// </summary>
    public class WorldMapAccessibilityManager : PersistenSingleton<WorldMapAccessibilityManager>
    {
        private List<WorldDestination> _availableDestinations = new List<WorldDestination>();
        private int _currentSelection = -1;
        private bool _isEnabled = false;
        private NavigationMode _navigationMode = NavigationMode.None;
        private WorldDestination _selectedDestination = null;
        private float _lastGuidanceUpdate = 0f;
        private const float GUIDANCE_UPDATE_INTERVAL = 5f; // Announce direction every 5 seconds
        private const float CLOSE_RANGE_DISTANCE = 200f; // Announce more frequently when close
        private const float ARRIVAL_DISTANCE = 400f; // Consider "arrived" when within 400 units

        public enum NavigationMode
        {
            None,
            Autopilot,
            Manual
        }

        public class WorldDestination
        {
            public int locationId;
            public string name;
            public Vector3 position;
            public float distance;
            public int compassDirection; // 0-7 (N, NE, E, SE, S, SW, W, NW)

            public WorldDestination(int locationId, string name, Vector3 position, float distance, int compassDirection)
            {
                this.locationId = locationId;
                this.name = name;
                this.position = position;
                this.distance = distance;
                this.compassDirection = compassDirection;
            }

            public string CompassDirectionName
            {
                get
                {
                    switch (compassDirection)
                    {
                        case 0: return "North";
                        case 1: return "Northeast";
                        case 2: return "East";
                        case 3: return "Southeast";
                        case 4: return "South";
                        case 5: return "Southwest";
                        case 6: return "West";
                        case 7: return "Northwest";
                        default: return "Unknown";
                    }
                }
            }
        }

        public void Initialize()
        {
            Log.Message("[WorldMapAccessibility] Initialize called");
            _isEnabled = true;
            _availableDestinations.Clear();
            _currentSelection = -1;
            _navigationMode = NavigationMode.None;
            _selectedDestination = null;
            ScreenReaderManager.Instance.Initialize();
        }

        public void Shutdown()
        {
            Log.Message("[WorldMapAccessibility] Shutdown called");
            _isEnabled = false;
            _availableDestinations.Clear();
            _currentSelection = -1;
            _navigationMode = NavigationMode.None;
            _selectedDestination = null;
        }

        public void Update()
        {
            if (!_isEnabled)
                return;

            // Check if we're actually on the world map
            if (ff9.w_moveActorPtr == null)
                return;

            // Update manual navigation guidance
            if (_navigationMode == NavigationMode.Manual && _selectedDestination != null)
            {
                UpdateManualGuidance();
            }

            // Handle key inputs
            // [ - Previous destination
            if (Input.GetKeyDown(KeyCode.LeftBracket))
            {
                CyclePrevious();
            }
            // ] - Next destination
            else if (Input.GetKeyDown(KeyCode.RightBracket))
            {
                CycleNext();
            }
            // \ - Toggle manual navigation (like field maps)
            else if (Input.GetKeyDown(KeyCode.Backslash) && !Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift))
            {
                ToggleManualNavigation();
            }
            // Shift+\ (|) - Activate autopilot
            else if (Input.GetKeyDown(KeyCode.Backslash) && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
            {
                ActivateAutopilot();
            }
            // ' (Quote) - Where am I / Rescan
            else if (Input.GetKeyDown(KeyCode.Quote))
            {
                AnnounceCurrentLocation();
            }
            // Escape - Cancel navigation
            else if (Input.GetKeyDown(KeyCode.Escape) && _navigationMode != NavigationMode.None)
            {
                CancelNavigation();
            }
        }

        private void ScanDestinations()
        {
            Log.Message("[WorldMapAccessibility] ===== ScanDestinations called =====");
            _availableDestinations.Clear();
            _currentSelection = -1;

            if (ff9.w_moveActorPtr == null)
            {
                Log.Warning("[WorldMapAccessibility] Cannot scan - no active actor");
                ScreenReaderManager.Instance.Speak("Cannot scan destinations", false);
                return;
            }

            Vector3 playerPos = ff9.w_moveActorPtr.RealPosition;
            Log.Message("[WorldMapAccessibility] Player position: {0}", playerPos);

            // Scan terrain in a radius around player using the game's terrain detection
            // This is how entrances actually work - they're encoded in terrain mesh IDs
            ScanTerrainForEntrances(playerPos);

            // Sort by distance (closest first)
            _availableDestinations.Sort((a, b) => a.distance.CompareTo(b.distance));

            Log.Message("[WorldMapAccessibility] ===== Scan complete: Found {0} available destinations =====", _availableDestinations.Count);

            // Announce results
            if (_availableDestinations.Count == 0)
            {
                ScreenReaderManager.Instance.Speak("No destinations available", false);
            }
            else
            {
                string announcement = String.Format("{0} destination{1} available",
                    _availableDestinations.Count,
                    _availableDestinations.Count == 1 ? "" : "s");
                ScreenReaderManager.Instance.Speak(announcement, false);

                // Auto-select first (closest) destination
                _currentSelection = 0;
                AnnounceCurrentSelection();
            }
        }

        /// <summary>
        /// Scans available world map locations using the game's built-in location database
        /// </summary>
        private void ScanTerrainForEntrances(Vector3 playerPos)
        {
            Log.Message("[WorldMapAccessibility] Scanning world map locations from game database");

            // Get current map (0 = Mist Continent, 1 = Full World)
            int currentMapNo = WMUIData.ActiveMapNo;
            Log.Message("[WorldMapAccessibility] Current map: {0}", currentMapNo);

            // Get location text table (same as WorldHUD uses)
            string[] locationTexts = FF9TextTool.GetTableText(0u);
            Log.Message("[WorldMapAccessibility] Location text table has {0} entries", locationTexts?.Length ?? 0);

            int foundCount = 0;

            // Scan all 64 locations
            for (int locationId = 0; locationId < 64; locationId++)
            {
                // Check if location is available/discovered
                if (!ff9.w_naviLocationAvailable(locationId))
                    continue;

                // Get location position data
                ff9.navipos navPos = ff9.w_naviLocationPos[currentMapNo, locationId];

                // Skip locations with no coordinates
                if (navPos.vx == 0 && navPos.vy == 0)
                    continue;

                // Get location name using the CORRECT text table (same as WorldHUD)
                int textIndex = locationId;
                if (locationId == 63) // Chocobo's Paradise special case
                    textIndex = 49;

                string locationName = (locationTexts != null && textIndex + 1 < locationTexts.Length)
                    ? locationTexts[textIndex + 1]
                    : null;

                if (String.IsNullOrEmpty(locationName))
                {
                    Log.Message("[WorldMapAccessibility] Location {0} has no name, skipping", locationId);
                    continue;
                }

                // Calculate position and distance using world coordinates (tx, ty)
                // navPos coordinates are in fixed-point format - convert using ff9.S()
                float destX = ff9.S(navPos.tx);
                float destZ = ff9.S(navPos.ty);
                Vector3 destPos = new Vector3(destX, 0, destZ);

                Vector3 playerPos2D = new Vector3(playerPos.x, 0, playerPos.z);
                Vector3 toDestination = destPos - playerPos2D;
                float distance = toDestination.magnitude;

                // Calculate compass direction
                int compassDir = CalculateCompassDirection(toDestination);

                WorldDestination destination = new WorldDestination(
                    locationId,
                    locationName,
                    destPos,
                    distance,
                    compassDir
                );

                _availableDestinations.Add(destination);
                foundCount++;

                Log.Message("[WorldMapAccessibility] Found: {0} (ID {1}) at world ({2:F1}, {3:F1}), distance {4:F0}, direction {5}",
                    locationName, locationId, destX, destZ, distance, destination.CompassDirectionName);
            }

            Log.Message("[WorldMapAccessibility] Scan complete: Found {0} available locations", foundCount);
        }

        private int CalculateCompassDirection(Vector3 toTarget)
        {
            // Calculate angle from north (0 degrees = north, 90 = east, 180 = south, 270 = west)
            // FF9 world map has BOTH axes inverted (see autopilot code in ff9.cs:6194-6195)
            // The game negates both X and Z deltas, so we do the same
            float angle = Mathf.Atan2(-toTarget.x, -toTarget.z) * Mathf.Rad2Deg;

            // Normalize to 0-360
            if (angle < 0)
                angle += 360f;

            // Convert to 8-directional compass (0=N, 1=NE, 2=E, 3=SE, 4=S, 5=SW, 6=W, 7=NW)
            int direction = Mathf.RoundToInt(angle / 45f) % 8;
            return direction;
        }

        private string GetCompassDirectionName(int direction)
        {
            switch (direction)
            {
                case 0: return "north";
                case 1: return "northeast";
                case 2: return "east";
                case 3: return "southeast";
                case 4: return "south";
                case 5: return "southwest";
                case 6: return "west";
                case 7: return "northwest";
                default: return "unknown direction";
            }
        }

        private void CyclePrevious()
        {
            Log.Message("[WorldMapAccessibility] CyclePrevious called");

            // Scan if list is empty
            if (_availableDestinations.Count == 0)
            {
                ScanDestinations();
                return;
            }

            _currentSelection--;
            if (_currentSelection < 0)
                _currentSelection = _availableDestinations.Count - 1;

            AnnounceCurrentSelection();
        }

        private void CycleNext()
        {
            Log.Message("[WorldMapAccessibility] CycleNext called");

            // Scan if list is empty
            if (_availableDestinations.Count == 0)
            {
                ScanDestinations();
                return;
            }

            _currentSelection++;
            if (_currentSelection >= _availableDestinations.Count)
                _currentSelection = 0;

            AnnounceCurrentSelection();
        }

        private void AnnounceCurrentSelection()
        {
            if (_currentSelection < 0 || _currentSelection >= _availableDestinations.Count)
                return;

            WorldDestination dest = _availableDestinations[_currentSelection];
            string distanceDesc = GetDistanceDescription(dest.distance);

            // Format: "1 of 12: Alexandria, Southeast, close"
            string announcement = String.Format("{0} of {1}: {2}, {3}, {4}",
                _currentSelection + 1,
                _availableDestinations.Count,
                dest.name,
                dest.CompassDirectionName,
                distanceDesc);

            Log.Message("[WorldMapAccessibility] Announcing: {0}", announcement);
            ScreenReaderManager.Instance.Speak(announcement, true);
        }

        private void ActivateAutopilot()
        {
            if (_currentSelection < 0 || _currentSelection >= _availableDestinations.Count)
            {
                ScreenReaderManager.Instance.Speak("No destination selected", false);
                return;
            }

            // Check if we're on an airship (autopilot only works on airship)
            if (!CheckUsingAirShip())
            {
                ScreenReaderManager.Instance.Speak("Autopilot requires an airship", false);
                return;
            }

            WorldDestination dest = _availableDestinations[_currentSelection];

            // Activate the game's built-in autopilot
            ff9.w_frameAutoid = (byte)dest.locationId;
            ff9.w_frameSetParameter(34, 0);

            _navigationMode = NavigationMode.Autopilot;
            _selectedDestination = dest;

            string announcement = String.Format("Autopilot to {0}", dest.name);
            Log.Message("[WorldMapAccessibility] {0}", announcement);
            ScreenReaderManager.Instance.Speak(announcement, false);
        }

        private void ToggleManualNavigation()
        {
            // If already navigating, cancel it
            if (_navigationMode == NavigationMode.Manual)
            {
                CancelNavigation();
                return;
            }

            if (_currentSelection < 0 || _currentSelection >= _availableDestinations.Count)
            {
                ScreenReaderManager.Instance.Speak("No destination selected", false);
                return;
            }

            WorldDestination dest = _availableDestinations[_currentSelection];

            _navigationMode = NavigationMode.Manual;
            _selectedDestination = dest;
            _lastGuidanceUpdate = Time.time; // Reset timer for periodic updates

            // Give initial guidance
            string initialGuidance = GetManualGuidanceMessage();
            string announcement = String.Format("Navigating to {0}. {1}", dest.name, initialGuidance);

            Log.Message("[WorldMapAccessibility] Manual navigation to {0}", dest.name);
            ScreenReaderManager.Instance.Speak(announcement, false);
        }

        private void UpdateManualGuidance()
        {
            if (_selectedDestination == null || ff9.w_moveActorPtr == null)
                return;

            // Check if we're standing on entrance terrain (the proper way to detect arrival)
            int playerIndex = ff9.w_moveActorPtr.originalActor.index;
            ff9.s_moveCHRStatus chrStatus = ff9.w_moveCHRStatus[playerIndex];

            // Check if terrain has an entrance event
            int eventType = ff9.m_GetIDEvent(chrStatus.id);
            if (eventType != 0)
            {
                // We're on entrance terrain - check if it matches our destination
                int areaId = ff9.m_GetIDArea(chrStatus.id);
                if (areaId == _selectedDestination.locationId)
                {
                    string arrival = String.Format("You have arrived at {0}. Press confirm to enter.", _selectedDestination.name);
                    ScreenReaderManager.Instance.Speak(arrival, false);
                    _navigationMode = NavigationMode.None;
                    _selectedDestination = null;
                    return;
                }
            }

            // Calculate current distance for periodic updates
            Vector3 playerPos = ff9.w_moveActorPtr.RealPosition;
            Vector3 destPos = _selectedDestination.position;
            Vector3 toDestination = destPos - new Vector3(playerPos.x, 0, playerPos.z);
            float currentDistance = toDestination.magnitude;

            // Announce more frequently when close
            float updateInterval = currentDistance < CLOSE_RANGE_DISTANCE ? 3f : GUIDANCE_UPDATE_INTERVAL;

            // Periodic guidance updates (every 5 seconds, or 3 seconds when close)
            if (Time.time - _lastGuidanceUpdate >= updateInterval)
            {
                _lastGuidanceUpdate = Time.time;
                string guidance = GetManualGuidanceMessage();
                ScreenReaderManager.Instance.Speak(guidance, false);
            }
        }

        private string GetManualGuidanceMessage()
        {
            if (_selectedDestination == null || ff9.w_moveActorPtr == null)
                return "Navigation error";

            Vector3 playerPos = ff9.w_moveActorPtr.RealPosition;
            Vector3 destPos = _selectedDestination.position;
            Vector3 toDestination = destPos - playerPos;

            // Calculate horizontal distance only
            Vector3 toDestination2D = new Vector3(toDestination.x, 0, toDestination.z);
            float distance = toDestination2D.magnitude;

            if (distance < 0.01f)
                return "At destination";

            // Get compass direction (world-relative)
            int compassDir = CalculateCompassDirection(toDestination2D);
            string directionName = GetCompassDirectionName(compassDir);

            // Return concise message: "500 northeast"
            return String.Format("{0:F0} {1}", distance, directionName);
        }

        private string GetArrowKeyDirection(float x, float z)
        {
            // x: negative = left, positive = right
            // z: negative = down, positive = up

            float angle = Mathf.Atan2(x, z) * Mathf.Rad2Deg;
            if (angle < 0) angle += 360f;

            // 8-directional guidance
            if (angle >= 337.5f || angle < 22.5f)
                return "Straight ahead";
            else if (angle >= 22.5f && angle < 67.5f)
                return "Up and right";
            else if (angle >= 67.5f && angle < 112.5f)
                return "Turn right";
            else if (angle >= 112.5f && angle < 157.5f)
                return "Down and right";
            else if (angle >= 157.5f && angle < 202.5f)
                return "Turn around";
            else if (angle >= 202.5f && angle < 247.5f)
                return "Down and left";
            else if (angle >= 247.5f && angle < 292.5f)
                return "Turn left";
            else // 292.5f && angle < 337.5f
                return "Up and left";
        }

        private string GetDistanceDescription(float distance)
        {
            if (distance < 2000f)
                return "very close";
            else if (distance < 5000f)
                return "close";
            else if (distance < 10000f)
                return "medium distance";
            else if (distance < 20000f)
                return "far";
            else
                return "very far";
        }

        private void CancelNavigation()
        {
            string message = _navigationMode == NavigationMode.Autopilot ? "Autopilot cancelled" : "Navigation cancelled";
            ScreenReaderManager.Instance.Speak(message, false);

            _navigationMode = NavigationMode.None;
            _selectedDestination = null;
        }

        private void AnnounceCurrentLocation()
        {
            if (ff9.w_moveActorPtr == null)
            {
                ScreenReaderManager.Instance.Speak("Location unknown", false);
                return;
            }

            // Rescan destinations (this will announce how many are available)
            ScanDestinations();
        }

        private bool CheckUsingAirShip()
        {
            // Check if player is using an airship (required for autopilot)
            // Based on WorldHUD.CheckUsingAirShip() logic
            int controlNo = WMUIData.ControlNo;
            int statusNo = WMUIData.StatusNo;

            // Control 7, 8, 9 are the three airships (Blue Narciss, Hilda Garde, Invincible)
            bool isAirship = (controlNo == 7 || controlNo == 8 || controlNo == 9);

            return isAirship;
        }
    }
}
