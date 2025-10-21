using Memoria.ScreenReader;
using System;
using UnityEngine;

public class Combo : MonoBehaviour
{
    public Int32 Number
    {
        set
        {
            this.comboText.Text = value.ToString();
            this.shadowText.Text = value.ToString();

            // Announce combo for screen readers
            if (value > 0)
            {
                String comboAnnouncement = QuadMistAccessibility.GetComboAnnouncement(value);
                ScreenReaderManager.Instance.Speak(comboAnnouncement, interrupt: false);
            }
        }
    }

    public SpriteText comboText;

    public SpriteText shadowText;
}
