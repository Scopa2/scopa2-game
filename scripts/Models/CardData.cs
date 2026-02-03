using Godot;

namespace Scopa2Game.Scripts.Models;

public partial class CardData : RefCounted
{
    public int Rank { get; set; }
    public string Suit { get; set; }

    public CardData() 
    {
        // Default constructor
    }

    public CardData(string serverCode)
    {
        // Handle special "BACK" code for face-down cards gracefully.
        if (string.IsNullOrEmpty(serverCode) || serverCode == "X")
        {
            Rank = -1;
            Suit = "X"; // Invalid suit
            return;
        }

        Suit = serverCode.Substring(serverCode.Length - 1);
        string rankStr = serverCode.Substring(0, serverCode.Length - 1);

        if (int.TryParse(rankStr, out int r))
        {
            Rank = r;
        }
        else
        {
            GD.PrintErr($"CardData: Invalid rank string '{rankStr}' from server_code '{serverCode}'");
            Rank = -1;
            Suit = "X";
        }
    }

    public override string ToString()
    {
        if (Suit == "X") return "X";
        return $"{Rank}{Suit}";
    }
}
