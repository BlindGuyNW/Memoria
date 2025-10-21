using Memoria.Prime.Ini;
using System;

namespace Memoria
{
    public sealed partial class Configuration
    {
        private sealed class AccessibilitySection : IniSection
        {
            public readonly IniValue<Boolean> AnnounceIcons;

            public AccessibilitySection() : base(nameof(AccessibilitySection), true)
            {
                AnnounceIcons = BindBoolean(nameof(AnnounceIcons), true);
            }
        }
    }
}
