using Memoria.ScreenReader;
using System;
using UnityEngine;

public class ResultText : MonoBehaviour
{
    public Int32 ID
    {
        set
        {
            this.content.ID = value;
            this.shadow.ID = value;
            this.content.transform.localPosition = this.map[value];
            this.shadow.transform.localPosition = this.map[value] + new Vector3(-0f, 0f, 0.1f);

            // Announce result for screen readers
            String resultText = GetResultText(value);
            String announcement = QuadMistAccessibility.GetResultAnnouncement(resultText);
            ScreenReaderManager.Instance.Speak(announcement, interrupt: false);
        }
    }

    private String GetResultText(Int32 id)
    {
        switch (id)
        {
            case 0: return "WIN";
            case 1: return "LOSE";
            case 2: return "DRAW";
            case 3: return "PERFECT";
            default: return "UNKNOWN";
        }
    }

    public Single Alpha
    {
        set
        {
            this.content.GetComponent<SpriteRenderer>().color = new Color(1f, 1f, 1f, value);
            this.shadow.GetComponent<SpriteRenderer>().color = new Color(1f, 1f, 1f, value);
        }
    }

    [SerializeField]
    private SpriteDisplay content;

    [SerializeField]
    private SpriteDisplay shadow;

    private Vector3[] map = new Vector3[]
    {
        new Vector3(1.08f, -0.88f, 0f),
        new Vector3(1.06f, -0.88f, 0f),
        new Vector3(0.92f, -0.88f, 0f),
        new Vector3(0.74f, -0.88f, 0f)
    };
}
