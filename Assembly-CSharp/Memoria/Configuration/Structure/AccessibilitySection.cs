using Memoria.Prime.Ini;
using System;

namespace Memoria
{
    public sealed partial class Configuration
    {
        private sealed class AccessibilitySection : IniSection
        {
            public readonly IniValue<Boolean> AnnounceIcons;
            public readonly IniValue<Int32> NavigationUpdateInterval;
            public readonly IniValue<Boolean> CageMinigameAudioFeedback;
            public readonly IniValue<Boolean> CageMinigameSkipEnabled;
            public readonly IniValue<Int32> CageMinigameFeedbackInterval;
            public readonly IniValue<Boolean> CageMinigameUseTones;

            public AccessibilitySection() : base(nameof(AccessibilitySection), true)
            {
                AnnounceIcons = BindBoolean(nameof(AnnounceIcons), true);
                NavigationUpdateInterval = BindInt32(nameof(NavigationUpdateInterval), 3);
                CageMinigameAudioFeedback = BindBoolean(nameof(CageMinigameAudioFeedback), true);
                CageMinigameSkipEnabled = BindBoolean(nameof(CageMinigameSkipEnabled), true);
                CageMinigameFeedbackInterval = BindInt32(nameof(CageMinigameFeedbackInterval), 500);
                CageMinigameUseTones = BindBoolean(nameof(CageMinigameUseTones), true);
            }
        }
    }
}
