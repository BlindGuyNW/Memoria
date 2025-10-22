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

            public AccessibilitySection() : base(nameof(AccessibilitySection), true)
            {
                AnnounceIcons = BindBoolean(nameof(AnnounceIcons), true);
                NavigationUpdateInterval = BindInt32(nameof(NavigationUpdateInterval), 3);
            }
        }
    }
}
