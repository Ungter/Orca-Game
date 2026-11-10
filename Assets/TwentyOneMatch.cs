using System;

// Shared counting game: taking the last tile loses, even when a roll would go past it.
// Each match has its own last tile, from 20 to 30.
public sealed class TwentyOneMatch
{
    public enum Side { Player, Opponent }
    public const int MinLimit = 20;
    public const int MaxLimit = 30;
    public int Limit { get; }
    public int Total { get; private set; }
    public int TurnCount { get; private set; }
    public int LastRoll { get; private set; }
    public Side Turn { get; private set; }
    public Side LastMover { get; private set; }
    public Side? Loser { get; private set; }
    public Side? Winner => Loser.HasValue ? Other(Loser.Value) : (Side?)null;
    public bool Started { get; private set; }
    public bool Finished => Loser.HasValue;

    public TwentyOneMatch(int limit)
    {
        if (limit < MinLimit || limit > MaxLimit)
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "The last tile must be from 20 to 30.");
        Limit = limit;
    }

    public void Start(bool opponentGoesSecond)
    {
        Total = TurnCount = LastRoll = 0;
        Loser = null;
        Turn = opponentGoesSecond ? Side.Player : Side.Opponent;
        Started = true;
    }

    public bool Take(Side side, int roll)
    {
        if (!Started || Finished || side != Turn || roll < 1 || roll > 3) return false;
        LastMover = side;
        LastRoll = roll;
        Total = Math.Min(Limit, Total + roll);
        TurnCount++;
        if (Total == Limit) Loser = side;
        else Turn = Other(side);
        return true;
    }

    // Whenever possible, leave the total at Limit - 1 minus a multiple of four: each later roll
    // can then be answered so the pair makes four, until the player has to take the last tile.
    // Without such a roll there is no forced win, so vary the roll rather than secretly
    // changing the player's roll.
    public int OpponentRoll()
    {
        if (!Started || Finished || Turn != Side.Opponent)
            throw new InvalidOperationException("It is not the opponent's turn.");
        int response = (Limit - 1 - Total) % 4;
        return response == 0 ? 1 + (TurnCount / 2) % 3 : response;
    }

    private static Side Other(Side side) => side == Side.Player ? Side.Opponent : Side.Player;
}
