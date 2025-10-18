using System;
using System.Collections.Generic;
using System.Text;
using Memoria.Data;
using Assets.Sources.Scripts.UI.Common;

namespace Memoria.Accessibility
{
    /// <summary>
    /// Helper class for formatting character information for screen reader announcements
    /// </summary>
    public static class CharacterAnnouncementHelper
    {
        /// <summary>
        /// Formats a character's full information for screen reader announcement
        /// </summary>
        /// <param name="player">The player character</param>
        /// <param name="includeStatus">Whether to include status effects</param>
        /// <returns>Formatted string with character info</returns>
        public static String FormatCharacterInfo(PLAYER player, Boolean includeStatus = true)
        {
            if (player == null)
                return "Empty slot";

            StringBuilder announcement = new StringBuilder();

            // Character name (strip formatting tags)
            String characterName = FF9TextTool.RemoveOpCode(player.Name);
            announcement.Append(characterName);

            // HP
            announcement.AppendFormat(", HP {0} of {1}", player.cur.hp, player.max.hp);

            // MP
            announcement.AppendFormat(", MP {0} of {1}", player.cur.mp, player.max.mp);

            // Status effects
            if (includeStatus)
            {
                String statusText = FormatStatusEffects(player);
                if (!String.IsNullOrEmpty(statusText))
                {
                    announcement.Append(", ");
                    announcement.Append(statusText);
                }
            }

            return announcement.ToString();
        }

        /// <summary>
        /// Formats status effects for a character
        /// </summary>
        /// <param name="player">The player character</param>
        /// <returns>Formatted status effects string</returns>
        public static String FormatStatusEffects(PLAYER player)
        {
            if (player == null)
                return String.Empty;

            List<String> statusNames = new List<String>();

            // Check for KO first (most important)
            if (player.cur.hp == 0)
            {
                statusNames.Add("KO");
            }
            else
            {
                // Get all active status effects
                foreach (BattleStatusId statusId in player.status.ToStatusList())
                {
                    String statusName = GetStatusName(statusId);
                    if (!String.IsNullOrEmpty(statusName))
                        statusNames.Add(statusName);
                }
            }

            if (statusNames.Count == 0)
                return String.Empty;

            // Join status names
            if (statusNames.Count == 1)
                return statusNames[0];
            else if (statusNames.Count == 2)
                return statusNames[0] + " and " + statusNames[1];
            else
            {
                StringBuilder result = new StringBuilder();
                for (int i = 0; i < statusNames.Count; i++)
                {
                    if (i == statusNames.Count - 1)
                        result.Append("and ");
                    result.Append(statusNames[i]);
                    if (i < statusNames.Count - 1)
                        result.Append(", ");
                }
                return result.ToString();
            }
        }

        /// <summary>
        /// Gets the readable name for a status effect
        /// </summary>
        /// <param name="statusId">The status ID</param>
        /// <returns>Human-readable status name</returns>
        private static String GetStatusName(BattleStatusId statusId)
        {
            switch (statusId)
            {
                case BattleStatusId.Death: return "KO";
                case BattleStatusId.Petrify: return "Petrify";
                case BattleStatusId.Venom: return "Venom";
                case BattleStatusId.Virus: return "Virus";
                case BattleStatusId.Silence: return "Silence";
                case BattleStatusId.Blind: return "Blind";
                case BattleStatusId.Trouble: return "Trouble";
                case BattleStatusId.Zombie: return "Zombie";
                case BattleStatusId.Confuse: return "Confuse";
                case BattleStatusId.Berserk: return "Berserk";
                case BattleStatusId.Stop: return "Stop";
                case BattleStatusId.Poison: return "Poison";
                case BattleStatusId.Sleep: return "Sleep";
                case BattleStatusId.Mini: return "Mini";
                case BattleStatusId.Heat: return "Heat";
                case BattleStatusId.Freeze: return "Freeze";
                case BattleStatusId.Slow: return "Slow";
                case BattleStatusId.Haste: return "Haste";
                case BattleStatusId.Regen: return "Regen";
                case BattleStatusId.Float: return "Float";
                case BattleStatusId.Shell: return "Shell";
                case BattleStatusId.Protect: return "Protect";
                case BattleStatusId.Vanish: return "Vanish";
                case BattleStatusId.Reflect: return "Reflect";
                case BattleStatusId.AutoLife: return "Auto-Life";
                default: return String.Empty;
            }
        }

        /// <summary>
        /// Formats item information for screen reader announcement
        /// </summary>
        /// <param name="itemId">The item ID</param>
        /// <param name="count">The item count</param>
        /// <param name="includeDescription">Whether to include the item description</param>
        /// <returns>Formatted string with item info</returns>
        public static String FormatItemInfo(RegularItem itemId, Int32 count, Boolean includeDescription = true)
        {
            StringBuilder announcement = new StringBuilder();

            // Item name (strip formatting tags)
            String itemName = FF9TextTool.RemoveOpCode(FF9TextTool.ItemName(itemId));
            announcement.Append(itemName);

            // Count
            announcement.AppendFormat(", {0} remaining", count);

            // Description (strip formatting tags)
            if (includeDescription)
            {
                String description = FF9TextTool.ItemHelpDescription(itemId);
                if (!String.IsNullOrEmpty(description))
                {
                    description = FF9TextTool.RemoveOpCode(description);
                    announcement.Append(", ");
                    announcement.Append(description);
                }
            }

            return announcement.ToString();
        }

        /// <summary>
        /// Formats key item information for screen reader announcement
        /// </summary>
        /// <param name="keyItemId">The key item ID</param>
        /// <param name="includeDescription">Whether to include the item description</param>
        /// <returns>Formatted string with key item info</returns>
        public static String FormatKeyItemInfo(Int32 keyItemId, Boolean includeDescription = true)
        {
            StringBuilder announcement = new StringBuilder();

            // Item name (strip formatting tags)
            String itemName = FF9TextTool.RemoveOpCode(FF9TextTool.ImportantItemName(keyItemId));
            announcement.Append(itemName);

            // Check if new/unread
            if (!ff9item.FF9Item_IsUsedImportant(keyItemId))
            {
                announcement.Append(", New");
            }

            // Description (strip formatting tags)
            if (includeDescription)
            {
                String description = FF9TextTool.ImportantItemHelpDescription(keyItemId);
                if (!String.IsNullOrEmpty(description))
                {
                    description = FF9TextTool.RemoveOpCode(description);
                    announcement.Append(", ");
                    announcement.Append(description);
                }
            }

            return announcement.ToString();
        }
    }
}
