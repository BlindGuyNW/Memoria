using Assets.Sources.Scripts.UI.Common;
using System;
using System.Collections.Generic;
using System.Text;
using Memoria.Data;

/// <summary>
/// Helper class for screen reader accessibility in Tetra Master (Quad Mist).
/// Provides methods to convert game data into screen-reader-friendly text.
/// </summary>
public static class QuadMistAccessibility
{
    /// <summary>
    /// Converts board coordinates to chess-style notation.
    /// Normal Tetra Master: A1-D4 (4x4 board)
    /// Triple Triad mode: A1-C3 (3x3 board)
    /// </summary>
    /// <param name="x">Column index (0-3 for 4x4, 0-2 for 3x3)</param>
    /// <param name="y">Row index (0-3 for 4x4, 0-2 for 3x3)</param>
    /// <returns>Chess notation like "A1", "C3", "D4", etc.</returns>
    public static String ConvertToChessNotation(Int32 x, Int32 y)
    {
        if (x < 0 || x >= Board.SIZE_X || y < 0 || y >= Board.SIZE_Y)
            return "Invalid position";

        Char column = (Char)('A' + x);
        Int32 row = y + 1;
        return $"{column}{row}";
    }

    /// <summary>
    /// Converts arrow byte flags to a list of cardinal direction names.
    /// </summary>
    /// <param name="arrow">Arrow byte with bit flags for each direction</param>
    /// <returns>List of direction names like "North", "East", "Southwest", etc.</returns>
    public static List<String> GetCardinalDirections(Byte arrow)
    {
        List<String> directions = new List<String>();

        if (CardArrow.HasDirection(arrow, CardArrow.Type.UP))
            directions.Add("North");
        if (CardArrow.HasDirection(arrow, CardArrow.Type.RIGHT_UP))
            directions.Add("Northeast");
        if (CardArrow.HasDirection(arrow, CardArrow.Type.RIGHT))
            directions.Add("East");
        if (CardArrow.HasDirection(arrow, CardArrow.Type.RIGHT_DOWN))
            directions.Add("Southeast");
        if (CardArrow.HasDirection(arrow, CardArrow.Type.DOWN))
            directions.Add("South");
        if (CardArrow.HasDirection(arrow, CardArrow.Type.LEFT_DOWN))
            directions.Add("Southwest");
        if (CardArrow.HasDirection(arrow, CardArrow.Type.LEFT))
            directions.Add("West");
        if (CardArrow.HasDirection(arrow, CardArrow.Type.LEFT_UP))
            directions.Add("Northwest");

        return directions;
    }

    /// <summary>
    /// Gets a human-readable description of a card for screen readers.
    /// Format: "Card name, Attack X, Physical Defense Y, Magic Defense Z, Type, N arrows"
    /// </summary>
    /// <param name="card">The card to describe</param>
    /// <returns>Screen-reader-friendly card description</returns>
    public static String GetCardDescription(QuadMistCard card)
    {
        if (card == null)
            return "Empty";

        if (card.IsBlock)
            return "Block";

        StringBuilder sb = new StringBuilder();

        // Card name
        String cardName = GetCardName(card.id);
        sb.Append(cardName);

        // Attack
        sb.Append($", Attack {card.atk}");

        // Defenses
        sb.Append($", Physical Defense {card.pdef}");
        sb.Append($", Magic Defense {card.mdef}");

        // Type
        String typeName = GetCardTypeName(card.type);
        sb.Append($", {typeName} type");

        // Arrow directions
        Int32 arrowCount = card.ArrowNumber;
        if (arrowCount > 0)
        {
            List<String> directions = GetCardinalDirections(card.arrow);
            sb.Append($", arrows at ");
            for (Int32 i = 0; i < directions.Count; i++)
            {
                if (i > 0)
                {
                    if (i == directions.Count - 1)
                        sb.Append(" and ");
                    else
                        sb.Append(", ");
                }
                sb.Append(directions[i]);
            }
        }
        else
        {
            sb.Append(", no arrows");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Gets a detailed description including arrow directions.
    /// Used when the user specifically wants to know arrow positions.
    /// </summary>
    /// <param name="card">The card to describe</param>
    /// <returns>Detailed card description with arrow directions</returns>
    public static String GetDetailedCardDescription(QuadMistCard card)
    {
        if (card == null)
            return "Empty";

        if (card.IsBlock)
            return "Block";

        StringBuilder sb = new StringBuilder();
        sb.Append(GetCardDescription(card));

        // Add arrow directions if there are any
        if (card.ArrowNumber > 0)
        {
            List<String> directions = GetCardinalDirections(card.arrow);
            sb.Append(", arrows at ");
            for (Int32 i = 0; i < directions.Count; i++)
            {
                if (i > 0)
                {
                    if (i == directions.Count - 1)
                        sb.Append(" and ");
                    else
                        sb.Append(", ");
                }
                sb.Append(directions[i]);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Gets a description of a board position including occupancy status.
    /// </summary>
    /// <param name="board">The game board</param>
    /// <param name="x">Column index</param>
    /// <param name="y">Row index</param>
    /// <returns>Description like "B2, your Goblin, Attack 1, Physical type, arrows at North and East" or "C3, empty"</returns>
    public static String GetBoardPositionDescription(Board board, Int32 x, Int32 y)
    {
        String position = ConvertToChessNotation(x, y);
        QuadMistCard card = board[x, y];

        if (card == null)
            return $"{position}, empty";

        // Check if it's a block card (obstacle)
        if (card.IsBlock)
            return $"{position}, blocked";

        String cardName = GetCardName(card.id);

        // Fallback if card name is empty
        if (String.IsNullOrEmpty(cardName))
            cardName = $"Card {(Int32)card.id}";

        String owner = card.side == 0 ? "your" : "enemy";

        // Include key card stats for board positions
        String typeName = GetCardTypeName(card.type);
        Int32 arrowCount = card.ArrowNumber;

        // Build the announcement with arrow directions
        String announcement = $"{position}, {owner} {cardName}, Attack {card.atk}, {typeName} type";

        // Add arrow directions
        if (arrowCount > 0)
        {
            List<String> directions = GetCardinalDirections(card.arrow);
            announcement += $", arrows at ";
            for (Int32 i = 0; i < directions.Count; i++)
            {
                if (i > 0)
                {
                    if (i == directions.Count - 1)
                        announcement += " and ";
                    else
                        announcement += ", ";
                }
                announcement += directions[i];
            }
        }
        else
        {
            announcement += ", no arrows";
        }

        return announcement;
    }

    /// <summary>
    /// Gets the friendly name for a card type.
    /// </summary>
    /// <param name="type">Card type enum value</param>
    /// <returns>Type name like "Physical", "Magic", "Flexible", or "Assault"</returns>
    public static String GetCardTypeName(QuadMistCard.Type type)
    {
        switch (type)
        {
            case QuadMistCard.Type.PHYSICAL:
                return "Physical";
            case QuadMistCard.Type.MAGIC:
                return "Magic";
            case QuadMistCard.Type.FLEXIABLE:
                return "Flexible";
            case QuadMistCard.Type.ASSAULT:
                return "Assault";
            default:
                return "Unknown";
        }
    }

    /// <summary>
    /// Gets the display name of a card from its ID.
    /// </summary>
    /// <param name="cardId">Card ID enum value</param>
    /// <returns>Localized card name (e.g. "Goblin", "Sand Scorpion")</returns>
    public static String GetCardName(TetraMasterCardId cardId)
    {
        // Use FF9's localized card name system
        return FF9TextTool.CardName(cardId);
    }

    /// <summary>
    /// Helper method to add spaces before capital letters in camel case strings.
    /// E.g., "SandScorpion" becomes "Sand Scorpion"
    /// </summary>
    private static String AddSpacesToCamelCase(String text)
    {
        if (String.IsNullOrEmpty(text))
            return text;

        StringBuilder sb = new StringBuilder();
        sb.Append(text[0]);

        for (Int32 i = 1; i < text.Length; i++)
        {
            if (Char.IsUpper(text[i]) && !Char.IsUpper(text[i - 1]))
                sb.Append(' ');
            sb.Append(text[i]);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Announces a score update to the screen reader.
    /// </summary>
    /// <param name="playerScore">Player's current score</param>
    /// <param name="enemyScore">Enemy's current score</param>
    /// <returns>Score announcement string</returns>
    public static String GetScoreAnnouncement(Int32 playerScore, Int32 enemyScore)
    {
        return $"Player score: {playerScore}, Enemy score: {enemyScore}";
    }

    /// <summary>
    /// Announces a combo count to the screen reader.
    /// </summary>
    /// <param name="comboCount">Number of cards in the combo</param>
    /// <returns>Combo announcement string</returns>
    public static String GetComboAnnouncement(Int32 comboCount)
    {
        return $"Combo: {comboCount} {(comboCount == 1 ? "card" : "cards")} flipped";
    }

    /// <summary>
    /// Announces a game result to the screen reader.
    /// </summary>
    /// <param name="result">Result type (WIN, LOSE, DRAW, PERFECT)</param>
    /// <returns>Result announcement string</returns>
    public static String GetResultAnnouncement(String result)
    {
        return $"Game result: {result}";
    }
}
