using System;
using System.Collections.Generic;
using System.Text;
using Memoria.Prime;

namespace Memoria.Accessibility
{
    /// <summary>
    /// Helper class for monitoring and logging event variables to identify minigame state variables.
    /// Used for discovering which variables control cage minigame mechanics.
    /// </summary>
    public class EventVariableMonitor
    {
        private Dictionary<int, int> _previousValues = new Dictionary<int, int>();
        private Dictionary<int, int> _changeCount = new Dictionary<int, int>();
        private bool _isTracking = false;

        /// <summary>
        /// Start tracking variable changes from this point forward.
        /// </summary>
        public void StartTracking()
        {
            _isTracking = true;
            _previousValues.Clear();
            _changeCount.Clear();
            Log.Message("[EventVariableMonitor] Started tracking variable changes");
        }

        /// <summary>
        /// Stop tracking and report which variables changed most frequently.
        /// </summary>
        public void StopTrackingAndReport()
        {
            if (!_isTracking)
                return;

            _isTracking = false;

            Log.Message("[EventVariableMonitor] ===== Variable Change Report =====");
            Log.Message("[EventVariableMonitor] Variables that changed during tracking:");

            // Sort by change count descending
            List<KeyValuePair<int, int>> sortedChanges = new List<KeyValuePair<int, int>>(_changeCount);
            sortedChanges.Sort((a, b) => b.Value.CompareTo(a.Value));

            foreach (var entry in sortedChanges)
            {
                int varOp = entry.Key;
                int changes = entry.Value;
                int currentValue = GetVariableValue(varOp);

                Log.Message("[EventVariableMonitor]   VarOp 0x{0:X8} - Changed {1} times, Final value: {2}",
                    varOp, changes, currentValue);
            }

            Log.Message("[EventVariableMonitor] ===== End Report =====");
        }

        /// <summary>
        /// Update tracking - call this each frame during the minigame.
        /// </summary>
        public void Update()
        {
            if (!_isTracking)
                return;

            // Track common map variables (Byte 0-99, Int16 0-99)
            for (int i = 0; i < 100; i++)
            {
                TrackVariable(EBin.getVarOperation(EBin.VariableSource.Map, EBin.VariableType.Byte, i));
                TrackVariable(EBin.getVarOperation(EBin.VariableSource.Map, EBin.VariableType.Int16, i));
            }
        }

        /// <summary>
        /// Track a specific variable and log if it changes.
        /// </summary>
        private void TrackVariable(int varOperation)
        {
            if (PersistenSingleton<EventEngine>.Instance == null)
                return;

            int currentValue = GetVariableValue(varOperation);

            // Check if this is the first time we're seeing this variable
            if (!_previousValues.ContainsKey(varOperation))
            {
                _previousValues[varOperation] = currentValue;
                return;
            }

            // Check if value changed
            int previousValue = _previousValues[varOperation];
            if (currentValue != previousValue)
            {
                // Increment change count
                if (!_changeCount.ContainsKey(varOperation))
                    _changeCount[varOperation] = 0;
                _changeCount[varOperation]++;

                Log.Message("[EventVariableMonitor] VarOp 0x{0:X8} changed: {1} -> {2}",
                    varOperation, previousValue, currentValue);

                _previousValues[varOperation] = currentValue;
            }
        }

        /// <summary>
        /// Read a map-scoped byte variable.
        /// </summary>
        public static int GetMapByte(int index)
        {
            return GetVariableValue(EBin.getVarOperation(EBin.VariableSource.Map, EBin.VariableType.Byte, index));
        }

        /// <summary>
        /// Read a map-scoped Int16 variable.
        /// </summary>
        public static int GetMapInt16(int index)
        {
            return GetVariableValue(EBin.getVarOperation(EBin.VariableSource.Map, EBin.VariableType.Int16, index));
        }

        /// <summary>
        /// Read a global-scoped byte variable.
        /// </summary>
        public static int GetGlobalByte(int index)
        {
            return GetVariableValue(EBin.getVarOperation(EBin.VariableSource.Global, EBin.VariableType.Byte, index));
        }

        /// <summary>
        /// Read a global-scoped Int16 variable.
        /// </summary>
        public static int GetGlobalInt16(int index)
        {
            return GetVariableValue(EBin.getVarOperation(EBin.VariableSource.Global, EBin.VariableType.Int16, index));
        }

        /// <summary>
        /// Read a global-scoped Int24 variable.
        /// </summary>
        public static int GetGlobalInt24(int index)
        {
            int varOp = EBin.getVarOperation(EBin.VariableSource.Global, EBin.VariableType.Int24, index);
            return GetVariableValue(varOp);
        }

        /// <summary>
        /// Read a map-scoped Int24 variable.
        /// </summary>
        public static int GetMapInt24(int index)
        {
            return GetVariableValue(EBin.getVarOperation(EBin.VariableSource.Map, EBin.VariableType.Int24, index));
        }

        /// <summary>
        /// Read any variable by its operation code.
        /// </summary>
        public static int GetVariableValue(int varOperation)
        {
            if (PersistenSingleton<EventEngine>.Instance == null)
                return 0;

            try
            {
                return PersistenSingleton<EventEngine>.Instance.eBin.getVarManually(varOperation);
            }
            catch (Exception ex)
            {
                Log.Warning("[EventVariableMonitor] Failed to read variable 0x{0:X8}: {1}", varOperation, ex.Message);
                return 0;
            }
        }

        /// <summary>
        /// Set a map-scoped byte variable (for skip functionality).
        /// </summary>
        public static void SetMapByte(int index, int value)
        {
            SetVariableValue(EBin.getVarOperation(EBin.VariableSource.Map, EBin.VariableType.Byte, index), value);
        }

        /// <summary>
        /// Set a map-scoped Int16 variable (for skip functionality).
        /// </summary>
        public static void SetMapInt16(int index, int value)
        {
            SetVariableValue(EBin.getVarOperation(EBin.VariableSource.Map, EBin.VariableType.Int16, index), value);
        }

        /// <summary>
        /// Set a map-scoped Int24 variable (for skip functionality).
        /// </summary>
        public static void SetMapInt24(int index, int value)
        {
            SetVariableValue(EBin.getVarOperation(EBin.VariableSource.Map, EBin.VariableType.Int24, index), value);
        }

        /// <summary>
        /// Set a global-scoped Int24 variable (for skip functionality).
        /// </summary>
        public static void SetGlobalInt24(int index, int value)
        {
            SetVariableValue(EBin.getVarOperation(EBin.VariableSource.Global, EBin.VariableType.Int24, index), value);
        }

        /// <summary>
        /// Set any variable by its operation code.
        ///
        /// IMPORTANT: We use SetVariableValueInternal directly instead of setVarManually
        /// because setVarManually corrupts the CalcStack (_s7) if called while an event
        /// script is paused mid-execution. SetVariableValueInternal writes directly to
        /// the gEventGlobal buffer without touching the stack, making it safe to call
        /// from outside the event execution context.
        /// </summary>
        public static void SetVariableValue(int varOperation, int value)
        {
            if (PersistenSingleton<EventEngine>.Instance == null ||
                PersistenSingleton<EventEngine>.Instance.eBin == null)
            {
                Log.Error("[EventVariableMonitor] Cannot set variable - EventEngine not initialized");
                return;
            }

            try
            {
                // Extract variable information from the operation code
                EBin.VariableSource varSource = (EBin.VariableSource)((varOperation >> 0) & 0x03);
                EBin.VariableType varType = (EBin.VariableType)((varOperation >> 2) & 0x07);
                int index = (varOperation >> 8) & 0xFFFF;

                // Write directly to the appropriate buffer
                switch (varSource)
                {
                    case EBin.VariableSource.Global:
                        PersistenSingleton<EventEngine>.Instance.eBin.SetVariableValueInternal(
                            FF9StateSystem.EventState.gEventGlobal,
                            index,
                            varType,
                            value,
                            0);
                        break;

                    case EBin.VariableSource.Map:
                        PersistenSingleton<EventEngine>.Instance.eBin.SetVariableValueInternal(
                            PersistenSingleton<EventEngine>.Instance.GetMapVar(),
                            index,
                            varType,
                            value,
                            0);
                        break;

                    default:
                        Log.Error("[EventVariableMonitor] Unsupported variable source: {0}", varSource);
                        return;
                }
            }
            catch (Exception ex)
            {
                Log.Error("[EventVariableMonitor] Failed to set variable 0x{0:X8}: {1}", varOperation, ex.Message);
            }
        }

        /// <summary>
        /// Dump all map variables for the current map (useful for initial investigation).
        /// </summary>
        public static void DumpAllMapVariables()
        {
            Log.Message("[EventVariableMonitor] ===== Dumping All Map Variables =====");

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Map Byte Variables (0-99):");
            for (int i = 0; i < 100; i++)
            {
                int value = GetMapByte(i);
                if (value != 0)
                    sb.AppendFormat("  Byte[{0}] = {1}\n", i, value);
            }

            sb.AppendLine("\nMap Int16 Variables (0-99):");
            for (int i = 0; i < 100; i++)
            {
                int value = GetMapInt16(i);
                if (value != 0)
                    sb.AppendFormat("  Int16[{0}] = {1}\n", i, value);
            }

            Log.Message(sb.ToString());
            Log.Message("[EventVariableMonitor] ===== End Dump =====");
        }
    }
}
