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

            public static void SaveValues()
            {
                SaveValue(Instance._accessibility.Name, Instance._accessibility.AnnounceIcons);
                SaveValue(Instance._accessibility.Name, Instance._accessibility.NavigationUpdateInterval);
            }
        }
    }
}
