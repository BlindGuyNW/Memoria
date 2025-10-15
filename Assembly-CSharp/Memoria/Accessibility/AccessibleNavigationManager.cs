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
                    ScreenReaderManager.Instance.Speak("Navigation ready. Press page down to scan for objects.", false);
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

                // Announce the area name
                string areaName = FF9StateSystem.Common.FF9.mapNameStr;
                Log.Message("[AccessibleNavigation] Area change - fldMapNo={0}, mapNameStr='{1}'", currentMap, areaName ?? "null");

                string lookupResult = FF9TextTool.LocationName(currentMap);
                Log.Message("[AccessibleNavigation] FF9TextTool.LocationName({0}) returned '{1}'", currentMap, lookupResult ?? "null");

                if (!String.IsNullOrEmpty(areaName))
                    ScreenReaderManager.Instance.Speak($"Entered {areaName}", false);
                else
                    ScreenReaderManager.Instance.Speak($"Entered area {currentMap}", false);
            }
        }

        public void Update()
        {
            if (!_isEnabled)
                return;

            // Keep trying to find the player until we succeed
            TryFindPlayer();

            // Check for map changes
            CheckForMapChange();

            // Update tracked target guidance
            UpdateTrackedTarget();

            // Try multiple key options since the game may intercept some
            // Option 1: [ and ] for cycling, \ for walk-to
            // Option 2: PageUp/PageDown for cycling, Home for walk-to
            // Option 3: Insert/Delete for cycling, End for walk-to

            if (Input.GetKeyDown(KeyCode.LeftBracket) || Input.GetKeyDown(KeyCode.PageUp) || Input.GetKeyDown(KeyCode.Insert))
            {
                Log.Message("[AccessibleNavigation] Previous key pressed");
                CyclePrevious();
            }
            else if (Input.GetKeyDown(KeyCode.RightBracket) || Input.GetKeyDown(KeyCode.PageDown) || Input.GetKeyDown(KeyCode.Delete))
            {
                Log.Message("[AccessibleNavigation] Next key pressed");
                CycleNext();
            }
            else if (Input.GetKeyDown(KeyCode.Backslash) || Input.GetKeyDown(KeyCode.Home) || Input.GetKeyDown(KeyCode.End))
            {
                Log.Message("[AccessibleNavigation] Walk-to key pressed");
                StartNavigationGuidance();
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
            Vector3 playerForward = _playerController.actor.transform.forward;

            Log.Message("[AccessibleNavigation] Player position: {0}", playerPos);

            // Iterate through all active objects in the scene
            for (ObjList objList = eventEngine.GetActiveObjList(); objList != null; objList = objList.next)
            {
                Obj obj = objList.obj;
                if (obj == null)
                    continue;

                // Skip invisible objects
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
                    float distance = toObject.magnitude;

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

                    // Skip non-interactive objects (unless they're party members or would show icon)
                    if (!isInteractive && !isPartyMember && !wouldShowIcon)
                    {
                        Log.Message("[AccessibleNavigation]   -> SKIPPED: Not interactive and not party member and won't show icon");
                        continue;
                    }

                    // Prioritize objects that would show an icon
                    if (!wouldShowIcon && !isPartyMember)
                    {
                        Log.Message("[AccessibleNavigation]   -> SKIPPED: Won't show icon (level={0}, needs level>1)", actor.level);
                        continue;
                    }

                    // Test if we can reach this object
                    // If it shows an icon, the game already validated it's reachable - trust that
                    // Otherwise, test pathfinding for very distant objects
                    bool canReach = wouldShowIcon || distance < 3000f || TestPathfinding(objPos);
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

                    // Check if quad has talk or push events
                    bool hasTalk = eventEngine.GetIP((int)quad.sid, EventEngine.tagTalk, quad.ebData) != eventEngine.nil;
                    bool hasPush = eventEngine.GetIP((int)quad.sid, EventEngine.tagPush, quad.ebData) != eventEngine.nil;

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
                    float distance = toObject.magnitude;

                    Log.Message("[AccessibleNavigation] Quad: sid={0} uid={1} pos={2} dist={3:F0} level={4} talk={5} push={6} showIcon={7} go={8}",
                        quad.sid, quad.uid, objPos, distance, quad.level, hasTalk, hasPush, wouldShowIcon, goName ?? "null");

                    // Skip quads that won't show an icon
                    if (!wouldShowIcon)
                    {
                        Log.Message("[AccessibleNavigation]   -> SKIPPED: Won't show icon (level={0}, needs level>1)", quad.level);
                        continue;
                    }

                    // Test if we can reach this zone
                    // If it shows an icon, the game already validated it's reachable - trust that
                    // Otherwise, test pathfinding for very distant objects
                    bool canReach = wouldShowIcon || distance < 3000f || TestPathfinding(objPos);
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
                    if (hasPush)
                        scriptInfo = AnalyzeEventScript(quad, EventEngine.tagPush, eventEngine);
                    else if (hasTalk)
                        scriptInfo = AnalyzeEventScript(quad, EventEngine.tagTalk, eventEngine);

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
                else if (obj.cid != EventEngine.classObj && obj.cid != EventEngine.classSeq && obj.cid != EventEngine.classThread)
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

            // Check for other types based on flags
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
                                // Skip argument flag byte at i+1, read field ID at i+2 and i+3
                                int destMap = obj.ebData[i + 2] | (obj.ebData[i + 3] << 8);
                                Log.Message("[AccessibleNavigation]   MAPJUMP destMap={0}", destMap);
                                string destName = FF9TextTool.LocationName(destMap);
                                Log.Message("[AccessibleNavigation]   Location name lookup: '{0}'", destName ?? "null");
                                if (!String.IsNullOrEmpty(destName) && destName != destMap.ToString())
                                    doorInfo = $"Door to {destName}";
                                else
                                    doorInfo = $"Exit to area {destMap}";
                                hasDoor = true;
                                Log.Message("[AccessibleNavigation]   Set doorInfo='{0}'", doorInfo);
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

            string announcement = String.Format("{0} objects nearby. Use page up and page down to cycle, home to walk to selected.", _nearbyObjects.Count);
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

            // Build status string
            string status = "";
            if (obj.isInInteractionRange)
                status = "ready to interact, ";
            else if (obj.showsIcon)
                status = "interaction available, ";

            string announcement = $"{_currentSelection + 1} of {_nearbyObjects.Count}: {obj.name}, {status}{obj.type}, {obj.clockDirection} o'clock, {distanceDesc}";
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
            if (!CalculatePath(selectedObj.position))
            {
                ScreenReaderManager.Instance.Speak("Cannot find path to target", false);
                return;
            }

            _trackedTarget = selectedObj;
            _lastDistanceUpdate = Time.time;
            _currentWaypointIndex = 0;

            // Give directional guidance to first waypoint
            string direction = GetWaypointGuidance();
            string announcement = String.Format("Navigate to {0}. {1}", selectedObj.name, direction);
            Log.Message("[AccessibleNavigation] Path has {0} waypoints. {1}", _pathWaypoints.Count, announcement);
            ScreenReaderManager.Instance.Speak(announcement, false);
        }

        private bool TestPathfinding(Vector3 targetPos)
        {
            // Quick test if we can reach the target - don't store the path
            WalkMesh walkMesh = _playerController.walkMesh;
            if (walkMesh == null)
                return false;

            int targetTriIdx = _playerController.GetActiveTriIdxAtPos(targetPos);
            if (targetTriIdx == -1)
                return false; // Not on walkmesh

            WalkMeshTriangle targetTri = walkMesh.tris[targetTriIdx];
            WalkMeshTriangle currentTri = walkMesh.tris[_playerController.activeTri];

            // Test if pathfinding succeeds
            WalkMeshTriangle pathResult = walkMesh.FindPathReversed(targetTri, currentTri, _playerController.radius);
            return pathResult != null;
        }

        private bool CalculatePath(Vector3 targetPos)
        {
            _pathWaypoints.Clear();

            WalkMesh walkMesh = _playerController.walkMesh;
            if (walkMesh == null)
            {
                Log.Warning("[AccessibleNavigation] No walkmesh available");
                return false;
            }

            // Find triangle at target position
            int targetTriIdx = _playerController.GetActiveTriIdxAtPos(targetPos);
            if (targetTriIdx == -1)
            {
                Log.Warning("[AccessibleNavigation] Target position not on walkmesh");
                return false;
            }

            WalkMeshTriangle targetTri = walkMesh.tris[targetTriIdx];
            WalkMeshTriangle currentTri = walkMesh.tris[_playerController.activeTri];

            // Use game's pathfinding
            WalkMeshTriangle pathResult = walkMesh.FindPathReversed(targetTri, currentTri, _playerController.radius);

            if (pathResult == null)
            {
                Log.Warning("[AccessibleNavigation] No path found");
                return false;
            }

            // Build waypoint list from triangle centers
            WalkMeshTriangle tri = pathResult;
            while (tri != null)
            {
                _pathWaypoints.Add(tri.originalCenter);
                tri = tri.next;
            }

            // Add final target position as last waypoint
            _pathWaypoints.Add(targetPos);

            Log.Message("[AccessibleNavigation] Path calculated with {0} waypoints", _pathWaypoints.Count);
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

            Log.Message("[AccessibleNavigation] Guidance - screenX: {0}, screenZ: {1}, twist: {2}", screenX, screenZ, twist);
            return String.Format("{0}. Distance: {1}", directionText, distanceText);
        }

        private string GetArrowKeyDirection(float x, float z)
        {
            // x: negative = left, positive = right
            // z: negative = down, positive = up

            float angle = Mathf.Atan2(x, z) * Mathf.Rad2Deg;
            if (angle < 0) angle += 360f;

            // 8-directional guidance (including diagonals)
            if (angle >= 337.5f || angle < 22.5f)
                return "Press up arrow";
            else if (angle >= 22.5f && angle < 67.5f)
                return "Press up and right arrows";
            else if (angle >= 67.5f && angle < 112.5f)
                return "Press right arrow";
            else if (angle >= 112.5f && angle < 157.5f)
                return "Press down and right arrows";
            else if (angle >= 157.5f && angle < 202.5f)
                return "Press down arrow";
            else if (angle >= 202.5f && angle < 247.5f)
                return "Press down and left arrows";
            else if (angle >= 247.5f && angle < 292.5f)
                return "Press left arrow";
            else // 292.5f && angle < 337.5f
                return "Press up and left arrows";
        }

        private string GetWaypointGuidance()
        {
            if (_currentWaypointIndex >= _pathWaypoints.Count)
                return "No more waypoints";

            Vector3 targetWaypoint = _pathWaypoints[_currentWaypointIndex];
            Vector3 playerPos = _playerController.curPos;
            Vector3 toWaypoint = targetWaypoint - playerPos;
            float distance = toWaypoint.magnitude;

            // Get direction in world space
            Vector3 worldDirection = new Vector3(toWaypoint.x, 0, toWaypoint.z);
            if (worldDirection.magnitude < 0.01f)
                return "At waypoint";

            worldDirection.Normalize();

            // Apply inverse twist to convert from world direction to screen direction
            float twist = FF9StateSystem.Field.twist.y;
            Quaternion inverseRotation = Quaternion.Euler(0f, -twist, 0f);
            Vector3 screenDirection = inverseRotation * worldDirection;

            float screenX = screenDirection.x;
            float screenZ = screenDirection.z;

            string directionText = GetArrowKeyDirection(screenX, screenZ);
            string distanceText = GetDistanceDescription(distance);

            int waypointsRemaining = _pathWaypoints.Count - _currentWaypointIndex;
            return String.Format("{0}. Distance: {1}. {2} waypoints remaining", directionText, distanceText, waypointsRemaining);
        }

        private void UpdateTrackedTarget()
        {
            if (_trackedTarget == null || _playerController == null)
                return;

            // Check if we're at final destination (no more waypoints)
            if (_currentWaypointIndex >= _pathWaypoints.Count)
            {
                // We're at destination, check facing periodically
                if (Time.time - _lastDistanceUpdate < 2f)
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

            // Update every 2 seconds
            if (Time.time - _lastDistanceUpdate < 2f)
                return;

            _lastDistanceUpdate = Time.time;

            // Give updated guidance to current waypoint
            string direction = GetWaypointGuidance();
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
                ScreenReaderManager.Instance.Speak(String.Format("Arrived at {0}. Press confirm to interact.", _trackedTarget.name), false);
                _trackedTarget = null;
                _pathWaypoints.Clear();
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
                ScreenReaderManager.Instance.Speak(String.Format("Arrived at {0}. Press confirm to interact.", _trackedTarget.name), false);
                _trackedTarget = null;
                _pathWaypoints.Clear();
            }
            else
            {
                // Need to turn to face
                string turnDirection;
                if (angleDiff > 0)
                    turnDirection = angleDiff > 45f ? "Turn right" : "Turn slightly right";
                else
                    turnDirection = angleDiff < -45f ? "Turn left" : "Turn slightly left";

                ScreenReaderManager.Instance.Speak(String.Format("Near {0}. {1} to face them, then press confirm.", _trackedTarget.name, turnDirection), false);
            }
        }
    }
}
