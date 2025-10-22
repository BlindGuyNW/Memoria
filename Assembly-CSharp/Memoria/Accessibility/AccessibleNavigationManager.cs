using System;
using System.Collections.Generic;
using UnityEngine;
using Memoria.ScreenReader;
using Memoria.Prime;
using Memoria.Data;
using Memoria.Assets;
using Assets.Sources.Scripts.UI.Common;

namespace Memoria.Accessibility
{
    public class AccessibleNavigationManager : PersistenSingleton<AccessibleNavigationManager>
    {
        private List<InteractiveObject> _nearbyObjects = new List<InteractiveObject>();
        private int _currentSelection = -1;
        private FieldMapActorController _playerController;
        private bool _isEnabled = false;
        private bool _hasInitialized = false;
        private int _initAttempts = 0;
        private InteractiveObject _trackedTarget = null;
        private float _lastDistanceUpdate = 0f;
        private List<Vector3> _pathWaypoints = new List<Vector3>();
        private int _currentWaypointIndex = 0;
        private int _currentMapId = -1;
        private bool _useDirectNavigation = false; // True when waypoint path failed, use compass guidance instead
        private int _lastInputFrame = -1; // Track last frame we processed input to prevent duplicate key presses

        public class InteractiveObject
        {
            public Obj obj;
            public string name;
            public string type;
            public Vector3 position;
            public float distance;
            public int clockDirection; // 1-12 for clock positions
            public bool showsIcon; // Would this show an interaction icon?
            public bool isInInteractionRange; // Within ~100 units, can interact now

            public InteractiveObject(Obj obj, string name, string type, Vector3 position, float distance, int clockDirection, bool showsIcon, bool isInInteractionRange)
            {
                this.obj = obj;
                this.name = name;
                this.type = type;
                this.position = position;
                this.distance = distance;
                this.clockDirection = clockDirection;
                this.showsIcon = showsIcon;
                this.isInInteractionRange = isInInteractionRange;
            }
        }

        public void Initialize()
        {
            Log.Message("[AccessibleNavigation] Initialize called");
            _isEnabled = true;
            _hasInitialized = false;
            _initAttempts = 0;
            _nearbyObjects.Clear();
            _currentSelection = -1;
            _playerController = null;
            _trackedTarget = null;
            _pathWaypoints.Clear();
            _currentWaypointIndex = 0;
            _useDirectNavigation = false;
            _lastInputFrame = -1;
        }

        private void TryFindPlayer()
        {
            if (_hasInitialized)
                return;

            _initAttempts++;

            // Use the game's own method to get the player character
            EventEngine eventEngine = PersistenSingleton<EventEngine>.Instance;
            if (eventEngine == null)
            {
                if (_initAttempts <= 3)
                    Log.Message("[AccessibleNavigation] Attempt {0}: EventEngine not ready yet", _initAttempts);
                return;
            }

            PosObj controlChar = eventEngine.GetControlChar();
            if (controlChar != null && controlChar.go != null)
            {
                _playerController = controlChar.go.GetComponent<FieldMapActorController>();
                if (_playerController != null)
                {
                    _hasInitialized = true;
                    Log.Message("[AccessibleNavigation] Player controller found on attempt {0}!", _initAttempts);
                    ScreenReaderManager.Instance.Speak("Navigation ready", false);
                    return;
                }
            }

            if (_initAttempts <= 3)
            {
                Log.Message("[AccessibleNavigation] Attempt {0}: Control char is {1}", _initAttempts, controlChar != null ? "not null" : "null");
            }

            // Stop trying after 300 attempts (about 10 seconds at 30fps)
            if (_initAttempts >= 300)
            {
                Log.Warning("[AccessibleNavigation] Could not find player controller after {0} attempts. Giving up.", _initAttempts);
                _hasInitialized = true; // Stop trying
            }
        }

        public void Shutdown()
        {
            Log.Message("[AccessibleNavigation] Shutdown called");
            _isEnabled = false;
            _hasInitialized = false;
            _initAttempts = 0;
            _nearbyObjects.Clear();
            _currentSelection = -1;
            _playerController = null;
            _currentMapId = -1;
            _trackedTarget = null;
            _pathWaypoints.Clear();
            _currentWaypointIndex = 0;
            _useDirectNavigation = false;
        }

        private void CheckForMapChange()
        {
            if (!_hasInitialized)
                return;

            int currentMap = FF9StateSystem.Common.FF9.fldMapNo;

            if (_currentMapId == -1)
            {
                // First time - just record the map
                _currentMapId = currentMap;
                return;
            }

            if (currentMap != _currentMapId)
            {
                Log.Message("[AccessibleNavigation] Map changed from {0} to {1}", _currentMapId, currentMap);
                _currentMapId = currentMap;

                // Clear navigation state on map change
                if (_trackedTarget != null)
                {
                    Log.Message("[AccessibleNavigation] Clearing navigation due to map change");
                    _trackedTarget = null;
                    _pathWaypoints.Clear();
                }

                // Clear object list - old objects are invalid
                _nearbyObjects.Clear();
                _currentSelection = -1;

                // Announce the area name - use LocationName lookup to get the NEW area name
                // mapNameStr can still contain the old map name at this point
                string areaName = FF9TextTool.LocationName(currentMap);
                Log.Message("[AccessibleNavigation] Area change - fldMapNo={0}, LocationName lookup='{1}'", currentMap, areaName ?? "null");

                if (!String.IsNullOrEmpty(areaName) && areaName != currentMap.ToString())
                    ScreenReaderManager.Instance.Speak($"Entered {areaName}", false);
                else
                    ScreenReaderManager.Instance.Speak($"Entered area {currentMap}", false);
            }
        }

        public void Update()
        {
            if (!_isEnabled)
                return;

            // Don't run on world map - WorldMapAccessibilityManager handles that
            if (ff9.w_moveActorPtr != null)
                return;

            // Keep trying to find the player until we succeed
            TryFindPlayer();

            // Check for map changes
            CheckForMapChange();

            // Update tracked target guidance
            UpdateTrackedTarget();

            // Prevent processing input multiple times in the same frame
            // This fixes the issue where a single key press gets detected multiple times
            int currentFrame = Time.frameCount;
            if (currentFrame == _lastInputFrame)
                return;

            // Try multiple key options since the game may intercept some
            // Option 1: [ and ] for cycling, \ for walk-to, ' for rescan
            // Option 2: PageUp/PageDown for cycling, Home for walk-to, Quote for rescan
            // Option 3: Insert/Delete for cycling, End for walk-to

            if (Input.GetKeyDown(KeyCode.LeftBracket) || Input.GetKeyDown(KeyCode.PageUp) || Input.GetKeyDown(KeyCode.Insert))
            {
                _lastInputFrame = currentFrame;
                Log.Message("[AccessibleNavigation] Previous key pressed");
                CyclePrevious();
            }
            else if (Input.GetKeyDown(KeyCode.RightBracket) || Input.GetKeyDown(KeyCode.PageDown) || Input.GetKeyDown(KeyCode.Delete))
            {
                _lastInputFrame = currentFrame;
                Log.Message("[AccessibleNavigation] Next key pressed");
                CycleNext();
            }
            else if (Input.GetKeyDown(KeyCode.Backslash) || Input.GetKeyDown(KeyCode.Home) || Input.GetKeyDown(KeyCode.End))
            {
                _lastInputFrame = currentFrame;
                Log.Message("[AccessibleNavigation] Walk-to toggle key pressed");
                ToggleNavigationGuidance();
            }
            else if (Input.GetKeyDown(KeyCode.Quote))
            {
                _lastInputFrame = currentFrame;
                Log.Message("[AccessibleNavigation] Rescan key pressed");
                ForceRescan();
            }
            else if (Input.GetKeyDown(KeyCode.F9))
            {
                _lastInputFrame = currentFrame;
                Log.Message("[AccessibleNavigation] Debug dump key pressed");
                DumpAllObjects();
            }
            else if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.Return))
            {
                _lastInputFrame = currentFrame;
                Log.Message("[AccessibleNavigation] Ctrl+Enter pressed - teleporting to target");
                TeleportToSelectedObject();
            }
            else if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
            {
                _lastInputFrame = currentFrame;
                Log.Message("[AccessibleNavigation] Minus key pressed - decreasing update interval");
                DecreaseUpdateInterval();
            }
            else if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus))
            {
                _lastInputFrame = currentFrame;
                Log.Message("[AccessibleNavigation] Equals key pressed - increasing update interval");
                IncreaseUpdateInterval();
            }
        }

        private void ScanNearbyObjects()
        {
            Log.Message("[AccessibleNavigation] ScanNearbyObjects called - clearing {0} existing objects", _nearbyObjects.Count);
            Log.Message("[AccessibleNavigation]   Stack trace: {0}", System.Environment.StackTrace);

            _nearbyObjects.Clear();
            _currentSelection = -1;

            if (_playerController == null)
            {
                Log.Warning("[AccessibleNavigation] Player controller is null, cannot scan");
                ScreenReaderManager.Instance.Speak("Cannot scan - player not found", false);
                return;
            }

            EventEngine eventEngine = PersistenSingleton<EventEngine>.Instance;
            if (eventEngine == null)
            {
                Log.Warning("[AccessibleNavigation] EventEngine is null, cannot scan");
                ScreenReaderManager.Instance.Speak("Cannot scan - event engine not found", false);
                return;
            }

            Vector3 playerPos = _playerController.curPos;

            // Get screen-space forward (accounting for camera twist)
            // In world space, character might face one way, but camera twist changes screen direction
            float twist = FF9StateSystem.Field.twist.y;
            Vector3 worldForward = _playerController.actor.transform.forward;
            Quaternion twistRotation = Quaternion.Euler(0f, twist, 0f);
            Vector3 playerForward = twistRotation * worldForward;

            Log.Message("[AccessibleNavigation] Player position: {0}, twist: {1}", playerPos, twist);

            // DEBUG: Count all objects by type
            int totalObjects = 0;
            int invisibleObjects = 0;
            int actorCount = 0;
            int quadCount = 0;
            int otherCount = 0;

            // Iterate through all active objects in the scene
            for (ObjList objList = eventEngine.GetActiveObjList(); objList != null; objList = objList.next)
            {
                totalObjects++;
                Obj obj = objList.obj;
                if (obj == null)
                    continue;

                // Count invisible objects
                if ((obj.flags & EventEngine.flagShow) == 0)
                {
                    invisibleObjects++;
                    continue;
                }

                // Count by type (including classObj which we might have been missing!)
                if (obj.cid == EventEngine.classActor)
                    actorCount++;
                else if (obj.cid == EventEngine.classQuad)
                    quadCount++;
                else
                    otherCount++; // Count ALL other types including classObj!
            }

            Log.Message("[AccessibleNavigation] Scene stats: Total={0}, Invisible={1}, Actors={2}, Quads={3}, Other={4}",
                totalObjects, invisibleObjects, actorCount, quadCount, otherCount);

            // Now do the actual scan
            for (ObjList objList = eventEngine.GetActiveObjList(); objList != null; objList = objList.next)
            {
                Obj obj = objList.obj;
                if (obj == null)
                    continue;

                // Skip all invisible objects
                if ((obj.flags & EventEngine.flagShow) == 0)
                    continue;

                // Handle Actors (cid == 4)
                if (obj.cid == EventEngine.classActor)
                {
                    Actor actor = obj as Actor;
                    if (actor == null)
                    {
                        Log.Message("[AccessibleNavigation] Found Actor but cast failed, sid={0}", obj.sid);
                        continue;
                    }

                    // Skip the player themselves
                    if (actor == _playerController.originalActor)
                        continue;

                    Vector3 objPos = new Vector3(actor.pos[0], actor.pos[1], actor.pos[2]);
                    Vector3 toObject = objPos - playerPos;

                    // Use horizontal (XZ) distance for consistency with pathfinding
                    // Vertical distance (Y) can be misleading in multi-floor environments
                    Vector3 toObject2D = new Vector3(toObject.x, 0, toObject.z);
                    float distance = toObject2D.magnitude;

                    // Check if object has any interactive events (talk or push)
                    bool hasTalk = eventEngine.GetIP((int)actor.sid, EventEngine.tagTalk, actor.ebData) != eventEngine.nil;
                    bool hasPush = eventEngine.GetIP((int)actor.sid, EventEngine.tagPush, actor.ebData) != eventEngine.nil;
                    bool hasDuel = eventEngine.GetIP((int)actor.sid, 8, actor.ebData) != eventEngine.nil; // Tag 8 = card game
                    bool isPartyMember = actor.sid >= eventEngine.sSourceObjN - 9;

                    // Check if this would actually show an icon (requires level > 1)
                    bool wouldShowIcon = (hasTalk || hasPush || hasDuel) && actor.level > 1;
                    bool isInteractive = hasTalk || hasPush;

                    // Log everything we find for debugging
                    string goName = actor.go != null ? actor.go.name : "null";
                    Log.Message("[AccessibleNavigation] Actor: sid={0} uid={1} model={2} go={3} dist={4:F0} level={5} talk={6} push={7} duel={8} party={9} showIcon={10}",
                        actor.sid, actor.uid, actor.model, goName, distance, actor.level, hasTalk, hasPush, hasDuel, isPartyMember, wouldShowIcon);

                    // Include all visible actors - no arbitrary model or distance filtering
                    // We sort by distance anyway, and FF9 fields aren't huge

                    // Test if we can reach this object
                    // If it shows an icon, the game already validated it's reachable - trust that
                    // Otherwise, test pathfinding
                    bool canReach = wouldShowIcon || TestPathfinding(objPos);
                    if (!canReach)
                    {
                        Log.Message("[AccessibleNavigation]   -> SKIPPED: No path found (distance={0:F0})", distance);
                        continue;
                    }

                    // Check if within interaction range (< 100 units, can interact now)
                    bool inInteractionRange = distance < 100f;

                    // Calculate clock direction
                    int clockDir = CalculateClockDirection(playerForward, toObject);

                    // Determine object type and name
                    string objType = GetObjectType(actor, eventEngine);
                    string objName = GetObjectName(actor, eventEngine);

                    InteractiveObject interactiveObj = new InteractiveObject(
                        obj,
                        objName,
                        objType,
                        objPos,
                        distance,
                        clockDir,
                        wouldShowIcon,
                        inInteractionRange
                    );

                    Log.Message("[AccessibleNavigation]   -> ADDED: {0} ({1}) [ShowsIcon={2}, InRange={3}, Dist={4:F0}]", objName, objType, wouldShowIcon, inInteractionRange, distance);
                    _nearbyObjects.Add(interactiveObj);
                }
                // Handle Quads (cid == 3) - exits/doors
                else if (obj.cid == EventEngine.classQuad)
                {
                    Quad quad = obj as Quad;
                    if (quad == null || quad.q == null || quad.q.Length == 0)
                    {
                        Log.Message("[AccessibleNavigation] Found Quad but invalid, sid={0}", obj.sid);
                        continue;
                    }

                    // Check if quad has interactive events (talk, push, default, or init)
                    bool hasTalk = eventEngine.GetIP((int)quad.sid, EventEngine.tagTalk, quad.ebData) != eventEngine.nil;
                    bool hasPush = eventEngine.GetIP((int)quad.sid, EventEngine.tagPush, quad.ebData) != eventEngine.nil;
                    bool hasDefault = eventEngine.GetIP((int)quad.sid, EventEngine.tagDefault, quad.ebData) != eventEngine.nil;
                    bool hasInit = eventEngine.GetIP((int)quad.sid, EventEngine.tagInit, quad.ebData) != eventEngine.nil;
                    bool isInteractive = hasTalk || hasPush || hasDefault || hasInit;

                    // Check if this would actually show an icon (requires level > 1)
                    bool wouldShowIcon = (hasTalk || hasPush) && quad.level > 1;

                    // Try to get a meaningful name from GameObject
                    string goName = quad.go != null ? quad.go.name : null;

                    // Calculate the center of the quad for better targeting
                    Vector3 objPos = Vector3.zero;
                    for (int i = 0; i < quad.n && i < quad.q.Length; i++)
                    {
                        objPos += quad.q[i].Vector3Val;
                    }
                    objPos /= quad.n; // Average position = center

                    Vector3 toObject = objPos - playerPos;

                    // Use horizontal (XZ) distance for consistency with pathfinding
                    // Vertical distance (Y) can be misleading in multi-floor environments
                    Vector3 toObject2D = new Vector3(toObject.x, 0, toObject.z);
                    float distance = toObject2D.magnitude;

                    Log.Message("[AccessibleNavigation] Quad: sid={0} uid={1} pos={2} dist={3:F0} level={4} init={5} default={6} push={7} talk={8} showIcon={9} go={10} flags={11}",
                        quad.sid, quad.uid, objPos, distance, quad.level, hasInit, hasDefault, hasPush, hasTalk, wouldShowIcon, goName ?? "null", quad.flags);

                    // Skip quads that have no interactive events
                    if (!isInteractive)
                    {
                        Log.Message("[AccessibleNavigation]   -> SKIPPED: No interactive events (init/default/push/talk)");
                        continue;
                    }

                    // Include all interactive quads - no distance filtering

                    // Test if we can reach this zone
                    // If it shows an icon, the game already validated it's reachable - trust that
                    // Otherwise, test pathfinding
                    bool canReach = wouldShowIcon || TestPathfinding(objPos);
                    if (!canReach)
                    {
                        Log.Message("[AccessibleNavigation]   -> SKIPPED: No path found (distance={0:F0})", distance);
                        continue;
                    }

                    // Check if within interaction range (< 100 units, can interact now)
                    bool inInteractionRange = distance < 100f;

                    // Calculate clock direction
                    int clockDir = CalculateClockDirection(playerForward, toObject);

                    // Determine name and type
                    string quadName = null;
                    string quadType = null;

                    // Try to analyze the event script first to get meaningful info
                    string scriptInfo = null;
                    if (hasTalk)
                        scriptInfo = AnalyzeEventScript(quad, EventEngine.tagTalk, eventEngine);
                    else if (hasPush)
                        scriptInfo = AnalyzeEventScript(quad, EventEngine.tagPush, eventEngine);
                    else if (hasDefault)
                        scriptInfo = AnalyzeEventScript(quad, EventEngine.tagDefault, eventEngine);
                    else if (hasInit)
                        scriptInfo = AnalyzeEventScript(quad, EventEngine.tagInit, eventEngine);

                    if (!String.IsNullOrEmpty(scriptInfo))
                    {
                        // We found meaningful info! Use it
                        quadName = scriptInfo;

                        // Determine type from the script info
                        if (scriptInfo.StartsWith("Item:"))
                            quadType = "Item";
                        else if (scriptInfo.StartsWith("Gil:"))
                            quadType = "Gil";
                        else if (scriptInfo.StartsWith("Door") || scriptInfo.StartsWith("Exit"))
                            quadType = "Door";
                        else
                            quadType = "Trigger";
                    }
                    // Use GameObject name if meaningful
                    else if (!String.IsNullOrEmpty(goName) && !goName.StartsWith("obj"))
                    {
                        quadName = goName;
                        quadType = "Interactive Zone";
                    }
                    // Otherwise use generic names based on event types
                    else if (hasTalk && hasPush)
                    {
                        quadName = $"Interactive Zone {quad.uid}";
                        quadType = "Interactive Zone";
                    }
                    else if (hasTalk)
                    {
                        quadName = $"Talk Zone {quad.uid}";
                        quadType = "Talk Zone";
                    }
                    else if (hasPush)
                    {
                        quadName = $"Trigger {quad.uid}";
                        quadType = "Trigger";
                    }
                    else
                    {
                        quadName = $"Zone {quad.uid}";
                        quadType = "Zone";
                    }

                    InteractiveObject interactiveObj = new InteractiveObject(
                        obj,
                        quadName,
                        quadType,
                        objPos,
                        distance,
                        clockDir,
                        wouldShowIcon,
                        inInteractionRange
                    );

                    Log.Message("[AccessibleNavigation]   -> ADDED: {0} ({1}) [ShowsIcon={2}, InRange={3}, Dist={4:F0}]", quadName, quadType, wouldShowIcon, inInteractionRange, distance);
                    _nearbyObjects.Add(interactiveObj);
                }
                // Handle classObj (cid == 0) - might be floor items!
                else if (obj.cid == EventEngine.classObj)
                {
                    PosObj posObj = obj as PosObj;
                    if (posObj != null)
                    {
                        Vector3 objPos = new Vector3(posObj.pos[0], posObj.pos[1], posObj.pos[2]);
                        Vector3 toObject = objPos - playerPos;
                        float distance = new Vector3(toObject.x, 0, toObject.z).magnitude;

                        // Check for events
                        bool hasTalk = eventEngine.GetIP((int)obj.sid, EventEngine.tagTalk, obj.ebData) != eventEngine.nil;
                        bool hasPush = eventEngine.GetIP((int)obj.sid, EventEngine.tagPush, obj.ebData) != eventEngine.nil;
                        bool hasDefault = eventEngine.GetIP((int)obj.sid, EventEngine.tagDefault, obj.ebData) != eventEngine.nil;
                        bool hasInit = eventEngine.GetIP((int)obj.sid, EventEngine.tagInit, obj.ebData) != eventEngine.nil;

                        Log.Message("[AccessibleNavigation] classObj: sid={0} uid={1} pos={2} dist={3:F0} init={4} default={5} push={6} talk={7}",
                            obj.sid, obj.uid, objPos, distance, hasInit, hasDefault, hasPush, hasTalk);

                        if (hasTalk || hasPush || hasDefault || hasInit)
                        {
                            string scriptInfo = null;
                            if (hasTalk)
                                scriptInfo = AnalyzeEventScript(posObj, EventEngine.tagTalk, eventEngine);
                            else if (hasPush)
                                scriptInfo = AnalyzeEventScript(posObj, EventEngine.tagPush, eventEngine);
                            else if (hasDefault)
                                scriptInfo = AnalyzeEventScript(posObj, EventEngine.tagDefault, eventEngine);
                            else if (hasInit)
                                scriptInfo = AnalyzeEventScript(posObj, EventEngine.tagInit, eventEngine);

                            Log.Message("[AccessibleNavigation]   classObj HAS EVENTS! Script: {0}", scriptInfo ?? "unknown");
                        }
                    }
                    else
                    {
                        Log.Message("[AccessibleNavigation] classObj (not PosObj): sid={0} uid={1}", obj.sid, obj.uid);
                    }
                }
                else if (obj.cid != EventEngine.classSeq && obj.cid != EventEngine.classThread)
                {
                    // Log any other CID types we haven't accounted for
                    Log.Message("[AccessibleNavigation] Unknown object type: cid={0} sid={1} uid={2}", obj.cid, obj.sid, obj.uid);
                }
            }

            // Sort by: 1) In interaction range first, 2) Then icons, 3) Then distance
            _nearbyObjects.Sort((a, b) =>
            {
                // Prioritize objects within interaction range (can interact now)
                if (a.isInInteractionRange != b.isInInteractionRange)
                    return b.isInInteractionRange.CompareTo(a.isInInteractionRange); // true before false
                // Then prioritize objects that show icons
                if (a.showsIcon != b.showsIcon)
                    return b.showsIcon.CompareTo(a.showsIcon); // true before false
                // Then sort by distance
                return a.distance.CompareTo(b.distance);
            });

            Log.Message("[AccessibleNavigation] ===== SCAN COMPLETE: Found {0} interactive objects =====", _nearbyObjects.Count);

            // Announce the scan results
            AnnounceObjectList();
        }

        private int CalculateClockDirection(Vector3 forward, Vector3 toTarget)
        {
            // Flatten to XZ plane
            forward.y = 0;
            toTarget.y = 0;

            if (forward.magnitude < 0.01f || toTarget.magnitude < 0.01f)
                return 12; // Default to 12 o'clock

            forward.Normalize();
            toTarget.Normalize();

            // Calculate signed angle manually (Unity 4 doesn't have Vector3.SignedAngle)
            float angle = Mathf.Atan2(toTarget.z, toTarget.x) - Mathf.Atan2(forward.z, forward.x);
            angle *= Mathf.Rad2Deg;

            // Convert to 0-360
            if (angle < 0)
                angle += 360f;

            // Convert to clock position (12 = straight ahead, 3 = right, 6 = behind, 9 = left)
            int clockPos = Mathf.RoundToInt(angle / 30f) % 12;
            if (clockPos == 0)
                clockPos = 12;

            return clockPos;
        }

        private string GetObjectType(Actor actor, EventEngine eventEngine)
        {
            // Check if it's a party character
            if (actor.sid >= eventEngine.sSourceObjN - 9)
                return "Party Character";

            // Check special model IDs
            switch (actor.model)
            {
                case 22: // Ladder (Alexandria rooftops)
                    return "Ladder";
                case 121: // Puck (rat kid in Alexandria)
                    return "NPC";
                case 200: // Air Cab
                case 294: // Gargan Car
                case 306: // Gargant
                    return "Vehicle";
                case 212: // Stiltzkin
                    return "Moogle";
                case 395: // Blue Magic Light
                    return "Special Effect";
                case 423: // Golden Frog
                    return "Creature";
            }

            // Check for interactive events
            bool hasTalk = eventEngine.GetIP((int)actor.sid, EventEngine.tagTalk, actor.ebData) != eventEngine.nil;
            bool hasPush = eventEngine.GetIP((int)actor.sid, EventEngine.tagPush, actor.ebData) != eventEngine.nil;

            if (hasTalk)
                return "NPC";
            else if (hasPush)
                return "Interactive Object";

            return "Object";
        }

        private string GetObjectName(Actor actor, EventEngine eventEngine)
        {
            // Check if it's a party character by SID
            if (actor.sid >= eventEngine.sSourceObjN - 9)
            {
                // Convert SID to event ID (0-11)
                int eventId = actor.sid - (eventEngine.sSourceObjN - 9);

                // Convert event ID to CharacterId
                CharacterId charId = ff9play.CharacterOldIndexToID((CharacterOldIndex)eventId);

                if (charId != CharacterId.NONE)
                {
                    // Get the actual player character name
                    PLAYER player = FF9StateSystem.Common.FF9.GetPlayer(charId);
                    if (player != null && !String.IsNullOrEmpty(player.Name))
                        return player.Name;

                    // Fall back to default name
                    return FF9TextTool.CharacterDefaultName(charId);
                }
            }

            // Check special model IDs
            switch (actor.model)
            {
                case 22: return "Ladder";
                case 121: return "Puck";
                case 200: return "Air Cab";
                case 294: return "Gargan Car";
                case 306: return "Gargant";
                case 212: return "Stiltzkin";
                case 395: return "Blue Magic Light";
                case 423: return "Golden Frog";
                case 488: return "Object " + actor.sid;
            }

            // Check if GameObject has a meaningful name
            if (actor.go != null && !actor.go.name.StartsWith("obj"))
            {
                return actor.go.name;
            }

            // For other NPCs, give a descriptive name
            string baseName = GetObjectType(actor, eventEngine);
            return $"{baseName} {actor.sid}";
        }

        /// <summary>
        /// Analyzes an event script to determine what a trigger/object actually does
        /// </summary>
        private string AnalyzeEventScript(Obj obj, int tagID, EventEngine eventEngine)
        {
            int scriptOffset = eventEngine.GetIP((int)obj.sid, tagID, obj.ebData);
            if (scriptOffset == eventEngine.nil || obj.ebData == null || obj.ebData.Length == 0)
            {
                Log.Message("[AccessibleNavigation] AnalyzeEventScript: No script found (offset={0}, ebData={1})",
                    scriptOffset, obj.ebData?.Length ?? 0);
                return null;
            }

            Log.Message("[AccessibleNavigation] AnalyzeEventScript: sid={0} tag={1} offset={2} dataLen={3}",
                obj.sid, tagID, scriptOffset, obj.ebData.Length);

            try
            {
                // Scan through the event bytecode looking for meaningful opcodes
                int maxScan = Math.Min(scriptOffset + 500, obj.ebData.Length); // Scan first ~500 bytes (increased!)
                Log.Message("[AccessibleNavigation]   Scanning from {0} to {1}", scriptOffset, maxScan);

                // Track what we find to categorize unknown triggers
                bool hasItem = false;
                bool hasGil = false;
                bool hasDoor = false;
                bool hasDialog = false;
                bool hasBattle = false;
                string itemInfo = null;
                string gilInfo = null;
                string doorInfo = null;

                for (int i = scriptOffset; i < maxScan && i < obj.ebData.Length; i++)
                {
                    EBin.event_code_binary opcode = (EBin.event_code_binary)obj.ebData[i];

                    // Log ALL opcodes if we're looking for doors (increased from 20)
                    if (i < scriptOffset + 100)
                        Log.Message("[AccessibleNavigation]   offset {0}: opcode={1} (0x{2:X2})", i, opcode, (byte)opcode);

                    switch (opcode)
                    {
                        case EBin.event_code_binary.ITEMADD: // 0x48
                            // Format: [opcode] [arg flags] [item ID low] [item ID high] [count]
                            if (i + 4 < obj.ebData.Length)
                            {
                                int itemID = obj.ebData[i + 2] | (obj.ebData[i + 3] << 8);
                                int count = obj.ebData[i + 4];
                                Log.Message("[AccessibleNavigation]   FOUND ITEMADD: itemID={0} count={1}", itemID, count);
                                string itemName = ETb.GetItemName(itemID);
                                if (!String.IsNullOrEmpty(itemName))
                                {
                                    if (count > 1)
                                        itemInfo = $"Item: {itemName} x{count}";
                                    else
                                        itemInfo = $"Item: {itemName}";
                                    hasItem = true;
                                }
                            }
                            break;

                        case EBin.event_code_binary.GILADD: // 0xCE
                            // Format: [opcode] [arg flags] [gil low] [gil mid] [gil high]
                            if (i + 4 < obj.ebData.Length)
                            {
                                int gil = obj.ebData[i + 2] | (obj.ebData[i + 3] << 8) | (obj.ebData[i + 4] << 16);
                                Log.Message("[AccessibleNavigation]   FOUND GILADD: gil={0}", gil);
                                if (gil > 0)
                                {
                                    gilInfo = $"Gil: {gil}";
                                    hasGil = true;
                                }
                            }
                            break;

                        case EBin.event_code_binary.MAPJUMP: // 0x2B
                        case EBin.event_code_binary.WMAPJUMP: // World map jump
                            // Map jump format: [opcode] [arg flags] [field ID low] [field ID high]
                            Log.Message("[AccessibleNavigation]   FOUND MAPJUMP at offset {0}!", i);
                            if (i + 3 < obj.ebData.Length)
                            {
                                // Only use the FIRST MAPJUMP we find, not the last
                                // Later MAPJUMPs are likely in conditional branches (after REPLYSW, etc.)
                                // Without full script execution, we can't know which branch executes,
                                // so we assume the first MAPJUMP is the primary/default path
                                if (!hasDoor)
                                {
                                    // Skip argument flag byte at i+1, read field ID at i+2 and i+3
                                    int destMap = obj.ebData[i + 2] | (obj.ebData[i + 3] << 8);
                                    Log.Message("[AccessibleNavigation]   MAPJUMP destMap={0}", destMap);
                                    string destName = FF9TextTool.LocationName(destMap);
                                    Log.Message("[AccessibleNavigation]   Location name lookup: '{0}'", destName ?? "null");
                                    // Include map ID to distinguish exits with the same location name
                                    if (!String.IsNullOrEmpty(destName) && destName != destMap.ToString())
                                        doorInfo = $"Door to {destName} (Map {destMap})";
                                    else
                                        doorInfo = $"Exit to area {destMap}";
                                    hasDoor = true;
                                    Log.Message("[AccessibleNavigation]   Set doorInfo='{0}' (first MAPJUMP)", doorInfo);
                                }
                                else
                                {
                                    Log.Message("[AccessibleNavigation]   Ignoring additional MAPJUMP (already have door info)");
                                }
                            }
                            else
                            {
                                Log.Message("[AccessibleNavigation]   MAPJUMP found but not enough bytes remaining");
                            }
                            break;

                        case EBin.event_code_binary.MES: // 0x1F - Show message
                        case EBin.event_code_binary.MESN: // 0x20 - Show named message
                        case EBin.event_code_binary.MESA: // Ask question variant
                        case EBin.event_code_binary.MESAN: // Ask question variant 2
                            hasDialog = true;
                            break;

                        case EBin.event_code_binary.ENCOUNT: // Random encounter
                        case EBin.event_code_binary.ENCOUNT2: // Encounter variant
                        case EBin.event_code_binary.BTLSET: // Battle setup
                            hasBattle = true;
                            break;
                    }
                }

                // Return the most important info we found (priority order)
                if (itemInfo != null)
                    return itemInfo;
                if (gilInfo != null)
                    return gilInfo;
                if (doorInfo != null)
                    return doorInfo;

                // Categorize by what we found (less specific)
                if (hasBattle)
                {
                    Log.Message("[AccessibleNavigation]   Categorized as: Battle Trigger");
                    return "Battle Encounter";
                }
                if (hasDialog)
                {
                    Log.Message("[AccessibleNavigation]   Categorized as: Story Trigger");
                    return "Story Event";
                }

                Log.Message("[AccessibleNavigation]   No meaningful opcodes found in scan range");
            }
            catch (Exception ex)
            {
                Log.Error($"[AccessibleNavigation] Error analyzing event script: {ex.Message}");
            }

            return null; // No meaningful info found
        }

        private void AnnounceObjectList()
        {
            Log.Message("[AccessibleNavigation] AnnounceObjectList called with {0} objects", _nearbyObjects.Count);

            if (_nearbyObjects.Count == 0)
            {
                Log.Message("[AccessibleNavigation] No nearby objects found");
                ScreenReaderManager.Instance.Speak("No nearby objects", false);
                return;
            }

            string announcement = String.Format("{0} objects", _nearbyObjects.Count);
            Log.Message("[AccessibleNavigation] Announcing: {0}", announcement);
            ScreenReaderManager.Instance.Speak(announcement, false);

            // Auto-select first object and announce it
            if (_nearbyObjects.Count > 0)
            {
                _currentSelection = 0;
                AnnounceCurrentSelection();
            }
        }

        private void AnnounceCurrentSelection()
        {
            Log.Message("[AccessibleNavigation] AnnounceCurrentSelection: index={0}, count={1}", _currentSelection, _nearbyObjects.Count);

            if (_currentSelection < 0 || _currentSelection >= _nearbyObjects.Count)
            {
                Log.Warning("[AccessibleNavigation]   Invalid selection index! Aborting announcement.");
                return;
            }

            InteractiveObject obj = _nearbyObjects[_currentSelection];
            string distanceDesc = GetDistanceDescription(obj.distance);

            // Format: "1 of 5: Puck, NPC, 3 o'clock, very close"
            string announcement = $"{_currentSelection + 1} of {_nearbyObjects.Count}: {obj.name}, {obj.type}, {obj.clockDirection} o'clock, {distanceDesc}";
            Log.Message("[AccessibleNavigation]   Announcing: {0}", announcement);
            ScreenReaderManager.Instance.Speak(announcement, true);
        }

        private string GetDistanceDescription(float distance)
        {
            if (distance < 500f)
                return "very close";
            else if (distance < 1000f)
                return "close";
            else if (distance < 2000f)
                return "medium distance";
            else if (distance < 3500f)
                return "far";
            else
                return "very far";
        }

        private void ForceRescan()
        {
            Log.Message("[AccessibleNavigation] ForceRescan called");
            ScreenReaderManager.Instance.Speak("Rescanning", true);
            ScanNearbyObjects();
        }

        private void DumpAllObjects()
        {
            if (_playerController == null)
            {
                ScreenReaderManager.Instance.Speak("Cannot dump - player not found", false);
                return;
            }

            EventEngine eventEngine = PersistenSingleton<EventEngine>.Instance;
            if (eventEngine == null)
            {
                ScreenReaderManager.Instance.Speak("Cannot dump - event engine not found", false);
                return;
            }

            Vector3 playerPos = _playerController.curPos;
            int currentMapId = FF9StateSystem.Common.FF9.fldMapNo;
            string areaName = FF9StateSystem.Common.FF9.mapNameStr;

            Log.Message("===== F10 DEBUG DUMP - ALL OBJECTS INCLUDING INVISIBLE =====");
            Log.Message("Map ID: {0} ({1})", currentMapId, areaName ?? "unknown");
            Log.Message("Player position: {0}", playerPos);
            Log.Message("");

            int totalVisible = 0;
            int totalInvisible = 0;
            int actorCount = 0;
            int quadCount = 0;
            int invisQuadCount = 0;
            int otherCount = 0;

            // First pass - count and categorize (including invisible)
            for (ObjList objList = eventEngine.GetActiveObjList(); objList != null; objList = objList.next)
            {
                Obj obj = objList.obj;
                if (obj == null)
                    continue;

                bool isInvisible = (obj.flags & EventEngine.flagShow) == 0;
                if (isInvisible)
                {
                    totalInvisible++;
                    if (obj.cid == EventEngine.classQuad)
                        invisQuadCount++;
                }
                else
                {
                    totalVisible++;
                    if (obj.cid == EventEngine.classActor)
                        actorCount++;
                    else if (obj.cid == EventEngine.classQuad)
                        quadCount++;
                    else
                        otherCount++;
                }
            }

            Log.Message("SUMMARY: {0} visible ({1} actors, {2} quads, {3} other), {4} invisible ({5} invisible quads)",
                totalVisible, actorCount, quadCount, otherCount, totalInvisible, invisQuadCount);
            Log.Message("");

            // Second pass - detailed dump
            int index = 0;
            for (ObjList objList = eventEngine.GetActiveObjList(); objList != null; objList = objList.next)
            {
                Obj obj = objList.obj;
                if (obj == null || (obj.flags & EventEngine.flagShow) == 0)
                    continue;

                index++;

                if (obj.cid == EventEngine.classActor)
                {
                    Actor actor = obj as Actor;
                    if (actor == null) continue;

                    Vector3 objPos = new Vector3(actor.pos[0], actor.pos[1], actor.pos[2]);
                    float distance = (objPos - playerPos).magnitude;

                    bool hasTalk = eventEngine.GetIP((int)actor.sid, EventEngine.tagTalk, actor.ebData) != eventEngine.nil;
                    bool hasPush = eventEngine.GetIP((int)actor.sid, EventEngine.tagPush, actor.ebData) != eventEngine.nil;
                    bool hasDuel = eventEngine.GetIP((int)actor.sid, 8, actor.ebData) != eventEngine.nil;
                    bool wouldShowIcon = (hasTalk || hasPush || hasDuel) && actor.level > 1;
                    bool isPartyMember = actor.sid >= eventEngine.sSourceObjN - 9;

                    string goName = actor.go != null ? actor.go.name : "null";
                    string objType = GetObjectType(actor, eventEngine);
                    string objName = GetObjectName(actor, eventEngine);

                    // Test pathfinding (always test, even if icon shows)
                    bool canReach = TestPathfinding(objPos);
                    string reachStatus = canReach ? "REACHABLE" : "UNREACHABLE";

                    Log.Message("[{0}] ACTOR: {1} ({2})", index, objName, objType);
                    Log.Message("    SID={0} UID={1} Model={2} Level={3} Flags={4}",
                        actor.sid, actor.uid, actor.model, actor.level, actor.flags);
                    Log.Message("    Position={0} Distance={1:F0} {2}", objPos, distance, reachStatus);
                    Log.Message("    Events: Talk={0} Push={1} Duel={2} ShowIcon={3} Party={4}",
                        hasTalk, hasPush, hasDuel, wouldShowIcon, isPartyMember);
                    Log.Message("    GameObject={0}", goName);
                    Log.Message("");
                }
                else if (obj.cid == EventEngine.classQuad)
                {
                    Quad quad = obj as Quad;
                    if (quad == null || quad.q == null || quad.q.Length == 0) continue;

                    // Calculate center
                    Vector3 objPos = Vector3.zero;
                    for (int i = 0; i < quad.n && i < quad.q.Length; i++)
                    {
                        objPos += quad.q[i].Vector3Val;
                    }
                    objPos /= quad.n;
                    float distance = (objPos - playerPos).magnitude;

                    // Check ALL event tags, not just Talk/Push
                    bool hasInit = eventEngine.GetIP((int)quad.sid, EventEngine.tagInit, quad.ebData) != eventEngine.nil;
                    bool hasDefault = eventEngine.GetIP((int)quad.sid, EventEngine.tagDefault, quad.ebData) != eventEngine.nil;
                    bool hasPush = eventEngine.GetIP((int)quad.sid, EventEngine.tagPush, quad.ebData) != eventEngine.nil;
                    bool hasTalk = eventEngine.GetIP((int)quad.sid, EventEngine.tagTalk, quad.ebData) != eventEngine.nil;
                    bool hasRefresh = eventEngine.GetIP((int)quad.sid, EventEngine.tagRefresh, quad.ebData) != eventEngine.nil;
                    bool hasTurn = eventEngine.GetIP((int)quad.sid, EventEngine.tagTurn, quad.ebData) != eventEngine.nil;
                    bool hasCounter = eventEngine.GetIP((int)quad.sid, EventEngine.tagCounter, quad.ebData) != eventEngine.nil;
                    bool hasReaction = eventEngine.GetIP((int)quad.sid, EventEngine.tagReaction, quad.ebData) != eventEngine.nil;

                    bool wouldShowIcon = (hasTalk || hasPush) && quad.level > 1;
                    bool hasAnyEvent = hasInit || hasDefault || hasPush || hasTalk || hasRefresh || hasTurn || hasCounter || hasReaction;

                    string goName = quad.go != null ? quad.go.name : "null";

                    // Analyze script
                    string scriptInfo = null;
                    if (hasPush)
                        scriptInfo = AnalyzeEventScript(quad, EventEngine.tagPush, eventEngine);
                    else if (hasTalk)
                        scriptInfo = AnalyzeEventScript(quad, EventEngine.tagTalk, eventEngine);
                    else if (hasDefault)
                        scriptInfo = AnalyzeEventScript(quad, EventEngine.tagDefault, eventEngine);

                    string quadName = scriptInfo ?? goName ?? ("Quad " + quad.uid);

                    // Test pathfinding (always test, even if icon shows)
                    bool canReach = TestPathfinding(objPos);
                    string reachStatus = canReach ? "REACHABLE" : "UNREACHABLE";

                    Log.Message("[{0}] QUAD: {1}", index, quadName);
                    Log.Message("    SID={0} UID={1} Level={2} Flags={3}",
                        quad.sid, quad.uid, quad.level, quad.flags);
                    Log.Message("    Position={0} Distance={1:F0} {2}", objPos, distance, reachStatus);
                    Log.Message("    Events: Init={0} Default={1} Push={2} Talk={3} Refresh={4} Turn={5} Counter={6} Reaction={7}",
                        hasInit, hasDefault, hasPush, hasTalk, hasRefresh, hasTurn, hasCounter, hasReaction);
                    Log.Message("    ShowIcon={0} HasAnyEvent={1}", wouldShowIcon, hasAnyEvent);
                    Log.Message("    GameObject={0}", goName);
                    if (!String.IsNullOrEmpty(scriptInfo))
                        Log.Message("    Script: {0}", scriptInfo);
                    Log.Message("");
                }
                else
                {
                    string goName = obj.go != null ? obj.go.name : "null";
                    Log.Message("[{0}] OTHER: CID={1} SID={2} UID={3} Flags={4} GO={5}",
                        index, obj.cid, obj.sid, obj.uid, obj.flags, goName);
                    Log.Message("");
                }
            }

            // Third pass - dump INVISIBLE Quads (might be floor items!)
            if (invisQuadCount > 0)
            {
                Log.Message("===== INVISIBLE QUADS ({0}) =====", invisQuadCount);
                Log.Message("");

                for (ObjList objList = eventEngine.GetActiveObjList(); objList != null; objList = objList.next)
                {
                    Obj obj = objList.obj;
                    if (obj == null || (obj.flags & EventEngine.flagShow) != 0 || obj.cid != EventEngine.classQuad)
                        continue;

                    Quad quad = obj as Quad;
                    if (quad == null || quad.q == null || quad.q.Length == 0) continue;

                    // Calculate center
                    Vector3 objPos = Vector3.zero;
                    for (int i = 0; i < quad.n && i < quad.q.Length; i++)
                    {
                        objPos += quad.q[i].Vector3Val;
                    }
                    objPos /= quad.n;
                    float distance = (objPos - playerPos).magnitude;

                    // Check ALL event tags
                    bool hasInit = eventEngine.GetIP((int)quad.sid, EventEngine.tagInit, quad.ebData) != eventEngine.nil;
                    bool hasDefault = eventEngine.GetIP((int)quad.sid, EventEngine.tagDefault, quad.ebData) != eventEngine.nil;
                    bool hasPush = eventEngine.GetIP((int)quad.sid, EventEngine.tagPush, quad.ebData) != eventEngine.nil;
                    bool hasTalk = eventEngine.GetIP((int)quad.sid, EventEngine.tagTalk, quad.ebData) != eventEngine.nil;

                    // Analyze script
                    string scriptInfo = null;
                    if (hasTalk)
                        scriptInfo = AnalyzeEventScript(quad, EventEngine.tagTalk, eventEngine);
                    else if (hasPush)
                        scriptInfo = AnalyzeEventScript(quad, EventEngine.tagPush, eventEngine);
                    else if (hasDefault)
                        scriptInfo = AnalyzeEventScript(quad, EventEngine.tagDefault, eventEngine);
                    else if (hasInit)
                        scriptInfo = AnalyzeEventScript(quad, EventEngine.tagInit, eventEngine);

                    Log.Message("INVISIBLE QUAD: SID={0} UID={1} Level={2} Flags={3}",
                        quad.sid, quad.uid, quad.level, quad.flags);
                    Log.Message("    Position={0} Distance={1:F0}", objPos, distance);
                    Log.Message("    Events: Init={0} Default={1} Push={2} Talk={3}",
                        hasInit, hasDefault, hasPush, hasTalk);
                    if (!String.IsNullOrEmpty(scriptInfo))
                        Log.Message("    Script: {0}", scriptInfo);
                    Log.Message("");
                }
            }

            Log.Message("===== END DEBUG DUMP =====");
            Log.Message("");

            ScreenReaderManager.Instance.Speak(String.Format("Dumped {0} visible and {1} invisible quads to log", totalVisible, invisQuadCount), false);
        }

        private void CyclePrevious()
        {
            Log.Message("[AccessibleNavigation] CyclePrevious: _currentSelection={0}, count={1}", _currentSelection, _nearbyObjects.Count);

            // Scan if we haven't yet
            if (_nearbyObjects.Count == 0)
            {
                Log.Message("[AccessibleNavigation]   List is empty, triggering scan");
                ScanNearbyObjects();
                return;
            }

            int oldSelection = _currentSelection;
            _currentSelection--;
            if (_currentSelection < 0)
                _currentSelection = _nearbyObjects.Count - 1;

            Log.Message("[AccessibleNavigation]   Changed selection from {0} to {1}", oldSelection, _currentSelection);
            AnnounceCurrentSelection();
        }

        private void CycleNext()
        {
            Log.Message("[AccessibleNavigation] CycleNext: _currentSelection={0}, count={1}", _currentSelection, _nearbyObjects.Count);

            // Scan if we haven't yet
            if (_nearbyObjects.Count == 0)
            {
                Log.Message("[AccessibleNavigation]   List is empty, triggering scan");
                ScanNearbyObjects();
                return;
            }

            int oldSelection = _currentSelection;
            _currentSelection++;
            if (_currentSelection >= _nearbyObjects.Count)
                _currentSelection = 0;

            Log.Message("[AccessibleNavigation]   Changed selection from {0} to {1}", oldSelection, _currentSelection);
            AnnounceCurrentSelection();
        }

        private void StartNavigationGuidance()
        {
            if (_currentSelection < 0 || _currentSelection >= _nearbyObjects.Count)
            {
                ScreenReaderManager.Instance.Speak("No object selected", false);
                return;
            }

            if (_playerController == null)
            {
                ScreenReaderManager.Instance.Speak("Cannot navigate - player not found", false);
                return;
            }

            InteractiveObject selectedObj = _nearbyObjects[_currentSelection];

            // Calculate path using game's pathfinding
            // This will automatically avoid paths that go through door triggers when possible
            if (!CalculatePath(selectedObj.position))
            {
                ScreenReaderManager.Instance.Speak("Cannot find path to target", false);
                return;
            }

            _trackedTarget = selectedObj;
            _lastDistanceUpdate = Time.time;
            _currentWaypointIndex = 0;

            // Give appropriate guidance based on navigation mode
            string guidance;
            if (_useDirectNavigation)
            {
                // Direct navigation mode - give compass direction
                guidance = GetDirectNavigationGuidance();
                string announcement = String.Format("Navigating to {0}. {1}", selectedObj.name, guidance);
                Log.Message("[AccessibleNavigation] Using direct navigation: {0}", announcement);
                ScreenReaderManager.Instance.Speak(announcement, false);
            }
            else
            {
                // Waypoint navigation mode - give waypoint guidance
                guidance = GetWaypointGuidance();
                string announcement = String.Format("{0}. {1}", selectedObj.name, guidance);
                Log.Message("[AccessibleNavigation] Path has {0} waypoints. {1}", _pathWaypoints.Count, announcement);
                ScreenReaderManager.Instance.Speak(announcement, false);
            }
        }

        private void TeleportToSelectedObject()
        {
            if (_currentSelection < 0 || _currentSelection >= _nearbyObjects.Count)
            {
                ScreenReaderManager.Instance.Speak("No object selected", false);
                return;
            }

            if (_playerController == null)
            {
                ScreenReaderManager.Instance.Speak("Cannot navigate - player not found", false);
                return;
            }

            InteractiveObject selectedObj = _nearbyObjects[_currentSelection];

            // Find walkable triangle near the target
            WalkMesh walkMesh = _playerController.walkMesh;
            if (walkMesh == null)
            {
                Log.Warning("[AccessibleNavigation] No walkmesh available");
                ScreenReaderManager.Instance.Speak("Cannot teleport - no walkable area", false);
                return;
            }

            int targetTriIdx = FindNearbyWalkableTriangle(selectedObj.position);
            if (targetTriIdx == -1)
            {
                ScreenReaderManager.Instance.Speak("Cannot find walkable area near target", false);
                return;
            }

            WalkMeshTriangle targetTri = walkMesh.tris[targetTriIdx];

            // Calculate teleport position
            // For interaction, we need to be within ~100-150 units of the target
            // Let's teleport to a position 80 units away from the target toward the player's current direction
            Vector3 playerPos = _playerController.curPos;
            Vector3 targetPos = selectedObj.position;

            Vector3 directionToPlayer = (playerPos - targetPos);
            directionToPlayer.y = 0; // Only consider horizontal direction

            Vector3 teleportPos;
            if (directionToPlayer.magnitude > 0.1f)
            {
                // Teleport 80 units away from target in the direction of the player
                directionToPlayer.Normalize();
                teleportPos = new Vector3(
                    targetPos.x + directionToPlayer.x * 80f,
                    targetTri.originalCenter.y,
                    targetPos.z + directionToPlayer.z * 80f
                );
            }
            else
            {
                // Player is at target, just use target position
                teleportPos = new Vector3(targetPos.x, targetTri.originalCenter.y, targetPos.z);
            }

            Log.Message("[AccessibleNavigation] Teleporting to {0} at ({1:F0}, {2:F0}, {3:F0}), distance from target: {4:F0}",
                selectedObj.name, teleportPos.x, teleportPos.y, teleportPos.z,
                Vector3.Distance(new Vector3(targetPos.x, 0, targetPos.z), new Vector3(teleportPos.x, 0, teleportPos.z)));

            // Use SetPosition to teleport
            _playerController.SetPosition(teleportPos, true, true);
            _playerController.ClearMoveTarget();

            ScreenReaderManager.Instance.Speak(String.Format("Teleported to {0}", selectedObj.name), false);
        }

        private void ToggleNavigationGuidance()
        {
            // If navigation is active, stop it
            if (_trackedTarget != null)
            {
                Log.Message("[AccessibleNavigation] Stopping navigation to {0}", _trackedTarget.name);
                ScreenReaderManager.Instance.Speak("Navigation cancelled", true);
                _trackedTarget = null;
                _pathWaypoints.Clear();
                _currentWaypointIndex = 0;
                _useDirectNavigation = false;
            }
            // Otherwise, start navigation
            else
            {
                Log.Message("[AccessibleNavigation] Starting navigation");
                StartNavigationGuidance();
            }
        }

        private int FindNearbyWalkableTriangle(Vector3 targetPos)
        {
            // Try the exact position first
            int triIdx = _playerController.GetActiveTriIdxAtPos(targetPos);
            if (triIdx != -1)
                return triIdx;

            // Object's exact position isn't on walkmesh
            // Find the closest walkable triangle, preferring triangles on the same floor (similar Y)
            WalkMesh walkMesh = _playerController.walkMesh;
            if (walkMesh == null || walkMesh.tris == null)
                return -1;

            Vector3 playerPos = _playerController.curPos;
            float maxFloorDifference = 500f; // Triangles more than 500 units vertically away are probably different floors

            float closestDist = float.MaxValue;
            int closestTriIdx = -1;
            float closestFallbackDist = float.MaxValue;
            int closestFallbackTriIdx = -1;

            // Flatten target position to XZ plane for horizontal distance
            Vector3 target2D = new Vector3(targetPos.x, 0, targetPos.z);

            for (int i = 0; i < walkMesh.tris.Count; i++)
            {
                WalkMeshTriangle tri = walkMesh.tris[i];

                // Skip inactive triangles
                if ((tri.triFlags & 1) == 0)
                    continue;

                // Calculate horizontal (XZ) distance
                Vector3 triCenter2D = new Vector3(tri.originalCenter.x, 0, tri.originalCenter.z);
                float horizontalDist = Vector3.Distance(triCenter2D, target2D);

                // Calculate vertical distance from player (to prefer same floor)
                float verticalDist = Mathf.Abs(tri.originalCenter.y - playerPos.y);

                // Primary search: triangles on the same floor as the player
                if (verticalDist <= maxFloorDifference)
                {
                    if (horizontalDist < closestDist)
                    {
                        closestDist = horizontalDist;
                        closestTriIdx = i;
                    }
                }

                // Fallback: any triangle (for when target is genuinely on another floor)
                if (horizontalDist < closestFallbackDist)
                {
                    closestFallbackDist = horizontalDist;
                    closestFallbackTriIdx = i;
                }
            }

            // Prefer triangles on the same floor, otherwise use fallback
            return closestTriIdx != -1 ? closestTriIdx : closestFallbackTriIdx;
        }

        /// <summary>
        /// Validates a path from FindPathReversed. The game accepts any non-NULL path,
        /// but we reject 1-waypoint cross-floor paths which are door/teleport shortcuts.
        /// </summary>
        /// <param name="path">The path returned by FindPathReversed</param>
        /// <param name="currentTri">The player's current triangle</param>
        /// <param name="targetTri">The target triangle we're trying to reach</param>
        private bool ValidatePathWaypoints(WalkMeshTriangle path, WalkMeshTriangle currentTri, WalkMeshTriangle targetTri)
        {
            if (path == null)
                return false;

            // Count waypoints in the path
            int totalWaypoints = 0;
            for (WalkMeshTriangle t = path; t != null; t = t.next)
                totalWaypoints++;

            // CRITICAL: Cross-floor paths with only 1 waypoint are ALWAYS door shortcuts
            // The walkmesh uses these as map transitions - we need to walk TO doors, not THROUGH them
            if (totalWaypoints == 1 && currentTri.floorIdx != targetTri.floorIdx)
            {
                Log.Message("[AccessibleNavigation] Rejected 1-waypoint cross-floor path (door shortcut)");
                return false;
            }

            return true;
        }

        private bool TestPathfinding(Vector3 targetPos)
        {
            Log.Message("[AccessibleNavigation] ===== TestPathfinding START =====");
            Log.Message("[AccessibleNavigation] Target position: ({0:F1}, {1:F1}, {2:F1})",
                targetPos.x, targetPos.y, targetPos.z);

            WalkMesh walkMesh = _playerController.walkMesh;
            if (walkMesh == null)
            {
                Log.Warning("[AccessibleNavigation] TestPathfinding FAILED: Walkmesh is null");
                return false;
            }

            // Log player's current state
            Log.Message("[AccessibleNavigation] Player position: ({0:F1}, {1:F1}, {2:F1})",
                _playerController.curPos.x, _playerController.curPos.y, _playerController.curPos.z);
            Log.Message("[AccessibleNavigation] Player activeTri: {0}, activeFloor: {1}, radius: {2:F1}",
                _playerController.activeTri, _playerController.activeFloor, _playerController.radius);

            // Find a walkable triangle near the target (using horizontal distance)
            int targetTriIdx = FindNearbyWalkableTriangle(targetPos);
            if (targetTriIdx == -1)
            {
                Log.Warning("[AccessibleNavigation] TestPathfinding FAILED: No walkable triangle found near target");
                return false;
            }

            // Now test if we can path to that triangle
            WalkMeshTriangle targetTri = walkMesh.tris[targetTriIdx];
            WalkMeshTriangle currentTri = walkMesh.tris[_playerController.activeTri];

            Log.Message("[AccessibleNavigation] Current triangle: idx={0} floor={1} center=({2:F1}, {3:F1}, {4:F1})",
                _playerController.activeTri, currentTri.floorIdx,
                currentTri.originalCenter.x, currentTri.originalCenter.y, currentTri.originalCenter.z);
            Log.Message("[AccessibleNavigation] Target triangle: idx={0} floor={1} center=({2:F1}, {3:F1}, {4:F1})",
                targetTriIdx, targetTri.floorIdx,
                targetTri.originalCenter.x, targetTri.originalCenter.y, targetTri.originalCenter.z);

            // Log neighbors
            Log.Message("[AccessibleNavigation] Current tri neighbors: [{0}, {1}, {2}]",
                currentTri.neighborIdx[0], currentTri.neighborIdx[1], currentTri.neighborIdx[2]);
            Log.Message("[AccessibleNavigation] Target tri neighbors: [{0}, {1}, {2}]",
                targetTri.neighborIdx[0], targetTri.neighborIdx[1], targetTri.neighborIdx[2]);

            // Try BOTH directions like the game does - A* can behave differently based on direction
            Log.Message("[AccessibleNavigation] Trying FindPathReversed(target→current)...");
            WalkMeshTriangle path1 = walkMesh.FindPathReversed(targetTri, currentTri, _playerController.radius);

            Log.Message("[AccessibleNavigation] Trying FindPathReversed(current→target)...");
            WalkMeshTriangle path2 = walkMesh.FindPathReversed(currentTri, targetTri, _playerController.radius);

            Log.Message("[AccessibleNavigation] Path1 result: {0}", path1 != null ? "SUCCESS" : "NULL");
            Log.Message("[AccessibleNavigation] Path2 result: {0}", path2 != null ? "SUCCESS" : "NULL");

            // Validate BOTH paths (if they exist) and return true if ANY valid path exists
            bool anyValidPath = false;

            if (path1 != null)
            {
                Log.Message("[AccessibleNavigation] Path1 validating...");
                if (ValidatePathWaypoints(path1, currentTri, targetTri))
                {
                    Log.Message("[AccessibleNavigation] Path1 is VALID");
                    anyValidPath = true;
                }
                else
                {
                    Log.Message("[AccessibleNavigation] Path1 is INVALID");
                }
            }

            if (path2 != null)
            {
                Log.Message("[AccessibleNavigation] Path2 validating...");
                if (ValidatePathWaypoints(path2, currentTri, targetTri))
                {
                    Log.Message("[AccessibleNavigation] Path2 is VALID");
                    anyValidPath = true;
                }
                else
                {
                    Log.Message("[AccessibleNavigation] Path2 is INVALID");
                }
            }

            if (anyValidPath)
            {
                Log.Message("[AccessibleNavigation] TestPathfinding SUCCESS: At least one valid path found");
                return true;
            }

            Log.Warning("[AccessibleNavigation] TestPathfinding FAILED: No valid paths found");
            return false;
        }

        private bool CalculatePath(Vector3 targetPos)
        {
            _pathWaypoints.Clear();

            WalkMesh walkMesh = _playerController.walkMesh;
            if (walkMesh == null)
                return false;

            // Find a walkable triangle near the target (using horizontal distance)
            int targetTriIdx = FindNearbyWalkableTriangle(targetPos);
            if (targetTriIdx == -1)
            {
                _pathWaypoints.Clear();
                _useDirectNavigation = true; // Use compass-style guidance instead
                return true; // Still allow navigation, just use direct mode
            }

            WalkMeshTriangle targetTri = walkMesh.tris[targetTriIdx];
            WalkMeshTriangle currentTri = walkMesh.tris[_playerController.activeTri];

            // Try BOTH directions like the game does
            WalkMeshTriangle path1 = walkMesh.FindPathReversed(targetTri, currentTri, _playerController.radius);
            WalkMeshTriangle path2 = walkMesh.FindPathReversed(currentTri, targetTri, _playerController.radius);

            // Smooth BOTH paths and calculate their lengths (like the game does)
            List<Int32> pathIdxList1 = new List<Int32>();
            List<Vector3> pathPos1 = new List<Vector3>();
            float pathLength1 = 0f;
            bool path1Valid = false;

            if (path1 != null && ValidatePathWaypoints(path1, currentTri, targetTri))
            {
                // Build triangle index list: walk the .next chain
                WalkMeshTriangle t = path1;
                while (t != null && t.next != null)
                {
                    pathIdxList1.Add(t.triIdx);
                    t = t.next;
                }
                if (t != null)
                    pathIdxList1.Add(t.triIdx);

                // Smooth using the game's algorithm (Path1 is target→current)
                pathPos1 = _playerController.SmoothPathsByForce(pathIdxList1, _playerController.curPos, targetPos);

                if (pathPos1 != null && pathPos1.Count > 0)
                {
                    // Calculate total path length
                    for (int i = 1; i < pathPos1.Count; i++)
                        pathLength1 += Vector3.Distance(pathPos1[i - 1], pathPos1[i]);
                    path1Valid = true;
                }
            }

            List<Int32> pathIdxList2 = new List<Int32>();
            List<Vector3> pathPos2 = new List<Vector3>();
            float pathLength2 = 0f;
            bool path2Valid = false;

            if (path2 != null && ValidatePathWaypoints(path2, currentTri, targetTri))
            {
                // Build triangle index list: walk the .next chain
                WalkMeshTriangle t = path2;
                while (t != null && t.next != null)
                {
                    pathIdxList2.Add(t.triIdx);
                    t = t.next;
                }
                if (t != null)
                    pathIdxList2.Add(t.triIdx);

                // Smooth using the game's algorithm (Path2 is current→target)
                pathPos2 = _playerController.SmoothPathsByForce(pathIdxList2, targetPos, _playerController.curPos);

                if (pathPos2 != null && pathPos2.Count > 0)
                {
                    pathPos2.Reverse();  // CRITICAL: Reverse Path2 like the game does

                    // Calculate total path length
                    for (int i = 1; i < pathPos2.Count; i++)
                        pathLength2 += Vector3.Distance(pathPos2[i - 1], pathPos2[i]);
                    path2Valid = true;
                }
            }

            // Pick the shortest VALID smoothed path (like the game does)
            List<Vector3> chosenPath = null;
            if (path1Valid && path2Valid)
                chosenPath = (pathLength1 <= pathLength2) ? pathPos1 : pathPos2;
            else if (path1Valid)
                chosenPath = pathPos1;
            else if (path2Valid)
                chosenPath = pathPos2;
            else
            {
                // No valid paths - fall back to direct navigation
                _pathWaypoints.Clear();
                _useDirectNavigation = true;
                return true;
            }

            // Use the smoothed waypoints
            _pathWaypoints.Clear();
            _pathWaypoints.AddRange(chosenPath);
            _useDirectNavigation = false;
            return _pathWaypoints.Count > 0;
        }

        private string GetDirectionGuidance(InteractiveObject target)
        {
            Vector3 playerPos = _playerController.curPos;
            Vector3 toTarget = target.position - playerPos;
            float distance = toTarget.magnitude;

            // Get direction in world space
            Vector3 worldDirection = new Vector3(toTarget.x, 0, toTarget.z);
            if (worldDirection.magnitude < 0.01f)
                return "You are at the target";

            worldDirection.Normalize();

            // Apply inverse twist to convert from world direction to screen direction
            float twist = FF9StateSystem.Field.twist.y;
            Quaternion inverseRotation = Quaternion.Euler(0f, -twist, 0f);
            Vector3 screenDirection = inverseRotation * worldDirection;

            // Now screenDirection.x = left/right input, screenDirection.z = up/down input
            float screenX = screenDirection.x;
            float screenZ = screenDirection.z;

            // Convert to arrow key instructions
            string directionText = GetArrowKeyDirection(screenX, screenZ);
            string distanceText = GetDistanceDescription(distance);
            return String.Format("{0}, {1}", directionText, distanceText);
        }

        private string GetArrowKeyDirection(float x, float z)
        {
            // x: negative = left, positive = right
            // z: negative = down, positive = up

            float angle = Mathf.Atan2(x, z) * Mathf.Rad2Deg;
            if (angle < 0) angle += 360f;

            // 8-directional guidance (including diagonals)
            if (angle >= 337.5f || angle < 22.5f)
                return "Up";
            else if (angle >= 22.5f && angle < 67.5f)
                return "Up right";
            else if (angle >= 67.5f && angle < 112.5f)
                return "Right";
            else if (angle >= 112.5f && angle < 157.5f)
                return "Down right";
            else if (angle >= 157.5f && angle < 202.5f)
                return "Down";
            else if (angle >= 202.5f && angle < 247.5f)
                return "Down left";
            else if (angle >= 247.5f && angle < 292.5f)
                return "Left";
            else // 292.5f && angle < 337.5f
                return "Up left";
        }

        /// <summary>
        /// Provides direct compass-style navigation guidance (like world map).
        /// Returns format: "distance direction" (e.g., "500 up-right")
        /// </summary>
        private string GetDirectNavigationGuidance()
        {
            if (_trackedTarget == null || _playerController == null)
                return "Navigation error";

            Vector3 playerPos = _playerController.curPos;
            Vector3 targetPos = _trackedTarget.position;
            Vector3 toTarget = targetPos - playerPos;

            // Calculate horizontal (XZ) distance only
            Vector3 toTarget2D = new Vector3(toTarget.x, 0, toTarget.z);
            float distance = toTarget2D.magnitude;

            if (distance < 0.01f)
                return "At destination";

            Vector3 worldDirection = toTarget2D.normalized;

            // Apply inverse twist to convert from world direction to screen direction
            float twist = FF9StateSystem.Field.twist.y;
            Quaternion inverseRotation = Quaternion.Euler(0f, -twist, 0f);
            Vector3 screenDirection = inverseRotation * worldDirection;

            string directionText = GetArrowKeyDirection(screenDirection.x, screenDirection.z);

            // Return concise format: "500 up-right" (similar to world map "500 northeast")
            return String.Format("{0:F0} {1}", distance, directionText.ToLower().Replace(" ", "-"));
        }

        private string GetWaypointGuidance()
        {
            if (_currentWaypointIndex >= _pathWaypoints.Count)
                return "No more waypoints";

            Vector3 targetWaypoint = _pathWaypoints[_currentWaypointIndex];
            Vector3 playerPos = _playerController.curPos;
            Vector3 toWaypoint = targetWaypoint - playerPos;

            // Calculate horizontal (XZ) distance only
            Vector3 toWaypoint2D = new Vector3(toWaypoint.x, 0, toWaypoint.z);
            float horizontalDistance = toWaypoint2D.magnitude;

            if (horizontalDistance < 0.01f)
                return "At waypoint";

            Vector3 worldDirection = toWaypoint2D.normalized;

            // Apply inverse twist to convert from world direction to screen direction
            float twist = FF9StateSystem.Field.twist.y;
            Quaternion inverseRotation = Quaternion.Euler(0f, -twist, 0f);
            Vector3 screenDirection = inverseRotation * worldDirection;

            string directionText = GetArrowKeyDirection(screenDirection.x, screenDirection.z);
            string distanceText = GetDistanceDescription(horizontalDistance);

            return String.Format("{0}, {1}", directionText, distanceText);
        }

        private void UpdateTrackedTarget()
        {
            if (_trackedTarget == null || _playerController == null)
                return;

            // Get update interval once at the start
            float updateInterval = Configuration.Accessibility.NavigationUpdateInterval;

            // Direct navigation mode - use compass guidance
            if (_useDirectNavigation)
            {
                // Check if we're close enough
                Vector3 toTarget = _trackedTarget.position - _playerController.curPos;
                Vector3 toTarget2D = new Vector3(toTarget.x, 0, toTarget.z);
                float distance = toTarget2D.magnitude;

                if (distance < 100f)
                {
                    // Close enough - check facing
                    if (Time.time - _lastDistanceUpdate < updateInterval)
                        return;
                    _lastDistanceUpdate = Time.time;
                    CheckFacingAndAnnounce();
                    return;
                }

                // Periodic updates
                if (Time.time - _lastDistanceUpdate < updateInterval)
                    return;

                _lastDistanceUpdate = Time.time;
                string guidance = GetDirectNavigationGuidance();
                Log.Message("[AccessibleNavigation] Direct navigation update: {0}", guidance);
                ScreenReaderManager.Instance.Speak(guidance, true);
                return;
            }

            // Waypoint navigation mode - use waypoint guidance
            // Check if we're at final destination (no more waypoints)
            if (_currentWaypointIndex >= _pathWaypoints.Count)
            {
                // We're at destination, check facing periodically
                if (Time.time - _lastDistanceUpdate < updateInterval)
                    return;
                _lastDistanceUpdate = Time.time;
                CheckFacingAndAnnounce();
                return;
            }

            // Check waypoint progress
            Vector3 currentWaypoint = _pathWaypoints[_currentWaypointIndex];
            float distToWaypoint = (currentWaypoint - _playerController.curPos).magnitude;

            // Close enough to this waypoint? Move to next
            if (distToWaypoint < 200f)
            {
                _currentWaypointIndex++;
                Log.Message("[AccessibleNavigation] Reached waypoint {0}/{1}", _currentWaypointIndex, _pathWaypoints.Count);

                if (_currentWaypointIndex >= _pathWaypoints.Count)
                {
                    // Reached final waypoint - check facing immediately
                    CheckFacingAndAnnounce();
                    return;
                }
            }

            // Update periodically
            if (Time.time - _lastDistanceUpdate < updateInterval)
                return;

            _lastDistanceUpdate = Time.time;

            // Give updated guidance to current waypoint
            Log.Message("[AccessibleNavigation] UpdateTrackedTarget: Announcing waypoint {0}/{1}",
                _currentWaypointIndex + 1, _pathWaypoints.Count);
            string direction = GetWaypointGuidance();
            Log.Message("[AccessibleNavigation]   -> Speaking: '{0}'", direction);
            ScreenReaderManager.Instance.Speak(direction, true);
        }

        private void CheckFacingAndAnnounce()
        {
            if (_trackedTarget == null || _playerController == null)
                return;

            // Calculate angle to target (same method as EventCollision.CollisionAngle)
            Vector3 playerPos = new Vector3(_playerController.curPos.x, _playerController.curPos.y, _playerController.curPos.z);
            Vector3 targetPos = _trackedTarget.position;
            Vector3 toTarget = (targetPos - playerPos).normalized;

            if (toTarget == Vector3.zero)
            {
                ScreenReaderManager.Instance.Speak(_trackedTarget.name, false);
                _trackedTarget = null;
                _pathWaypoints.Clear();
                _useDirectNavigation = false;
                return;
            }

            // Get player's current facing direction
            Vector3 playerRotation = _playerController.originalActor.rotAngle;
            Quaternion playerFacing = Quaternion.Euler(0f, playerRotation.y, 0f);
            Vector3 playerForward = playerFacing * Vector3.forward;

            // Calculate angle between facing and target
            Vector3 lookRotation = Quaternion.LookRotation(toTarget).eulerAngles;
            float angleDiff = lookRotation.y - playerRotation.y;

            // Normalize to -180 to 180
            while (angleDiff > 180f) angleDiff -= 360f;
            while (angleDiff < -180f) angleDiff += 360f;

            // Convert to fixed point (multiply by 4096/360 ≈ 11.38)
            int fixedPointAngle = (int)(angleDiff * 11.377778f);

            Log.Message("[AccessibleNavigation] Facing check - angle: {0} degrees, fixed: {1}", angleDiff, fixedPointAngle);

            // Check if within interaction angle (-1024 to 1024, roughly ±90 degrees)
            if (fixedPointAngle > -1024 && fixedPointAngle < 1024)
            {
                ScreenReaderManager.Instance.Speak(_trackedTarget.name, false);
                _trackedTarget = null;
                _pathWaypoints.Clear();
                _useDirectNavigation = false;
            }
            else
            {
                // Need to turn to face
                string turnDirection;
                if (angleDiff > 0)
                    turnDirection = angleDiff > 45f ? "Turn right" : "Turn slightly right";
                else
                    turnDirection = angleDiff < -45f ? "Turn left" : "Turn slightly left";

                ScreenReaderManager.Instance.Speak(String.Format("{0}. {1}", _trackedTarget.name, turnDirection), false);
            }
        }

        private void DecreaseUpdateInterval()
        {
            int currentInterval = Configuration.Accessibility.NavigationUpdateInterval;
            if (currentInterval > 1)
            {
                Configuration.Accessibility.NavigationUpdateInterval = currentInterval - 1;
                Configuration.Accessibility.SaveValues();
                Log.Message("[AccessibleNavigation] Update interval decreased to {0} seconds", currentInterval - 1);
                ScreenReaderManager.Instance.Speak(String.Format("Navigation updates every {0} seconds", currentInterval - 1), false);
            }
            else
            {
                Log.Message("[AccessibleNavigation] Update interval already at minimum (1 second)");
                ScreenReaderManager.Instance.Speak("Minimum interval: 1 second", false);
            }
        }

        private void IncreaseUpdateInterval()
        {
            int currentInterval = Configuration.Accessibility.NavigationUpdateInterval;
            if (currentInterval < 10)
            {
                Configuration.Accessibility.NavigationUpdateInterval = currentInterval + 1;
                Configuration.Accessibility.SaveValues();
                Log.Message("[AccessibleNavigation] Update interval increased to {0} seconds", currentInterval + 1);
                ScreenReaderManager.Instance.Speak(String.Format("Navigation updates every {0} seconds", currentInterval + 1), false);
            }
            else
            {
                Log.Message("[AccessibleNavigation] Update interval already at maximum (10 seconds)");
                ScreenReaderManager.Instance.Speak("Maximum interval: 10 seconds", false);
            }
        }
    }
}
