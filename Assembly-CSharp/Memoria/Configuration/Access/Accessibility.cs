using System;

namespace Memoria
{
    public sealed partial class Configuration
    {
        public static class Accessibility
        {
            public static Boolean AnnounceIcons
            {
                get => Instance._accessibility.AnnounceIcons;
                set => Instance._accessibility.AnnounceIcons.Value = value;
            }

            public static Int32 NavigationUpdateInterval
            {
                get => Instance._accessibility.NavigationUpdateInterval;
                set => Instance._accessibility.NavigationUpdateInterval.Value = value;
            }

            public static Boolean CageMinigameAudioFeedback
            {
                get => Instance._accessibility.CageMinigameAudioFeedback;
                set => Instance._accessibility.CageMinigameAudioFeedback.Value = value;
            }

            public static Boolean CageMinigameSkipEnabled
            {
                get => Instance._accessibility.CageMinigameSkipEnabled;
                set => Instance._accessibility.CageMinigameSkipEnabled.Value = value;
            }

            public static Int32 CageMinigameFeedbackInterval
            {
                get => Instance._accessibility.CageMinigameFeedbackInterval;
                set => Instance._accessibility.CageMinigameFeedbackInterval.Value = value;
            }

            public static Boolean CageMinigameUseTones
            {
                get => Instance._accessibility.CageMinigameUseTones;
                set => Instance._accessibility.CageMinigameUseTones.Value = value;
            }

            public static void SaveValues()
            {
                SaveValue(Instance._accessibility.Name, Instance._accessibility.AnnounceIcons);
                SaveValue(Instance._accessibility.Name, Instance._accessibility.NavigationUpdateInterval);
                SaveValue(Instance._accessibility.Name, Instance._accessibility.CageMinigameAudioFeedback);
                SaveValue(Instance._accessibility.Name, Instance._accessibility.CageMinigameSkipEnabled);
                SaveValue(Instance._accessibility.Name, Instance._accessibility.CageMinigameFeedbackInterval);
                SaveValue(Instance._accessibility.Name, Instance._accessibility.CageMinigameUseTones);
            }
        }
    }
}
