using System;

// Shared counting game: taking the 21st tile loses, even when a roll would go past it.
public sealed class TwentyOneMatch
{
    public enum Side { Player, Opponent }
    public const int Limit = 21;
    public int Total { get; private set; }
    public int TurnCount { get; private set; }
    public int LastRoll { get; private set; }
    public Side Turn { get; private set; }
    public Side LastMover { get; private set; }
    public Side? Loser { get; private set; }
    public Side? Winner => Loser.HasValue ? Other(Loser.Value) : (Side?)null;
    public bool Started { get; private set; }
    public bool Finished => Loser.HasValue;

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

    // Leave a multiple of four whenever possible. At a multiple of four there is
    // no forced win, so vary the opening rather than secretly changing the player's roll.
    public int OpponentRoll()
    {
        if (!Started || Finished || Turn != Side.Opponent)
            throw new InvalidOperationException("It is not the opponent's turn.");
        int response = (4 - Total % 4) % 4;
        return response == 0 ? 1 + (TurnCount / 2) % 3 : response;
    }

    private static Side Other(Side side) => side == Side.Player ? Side.Opponent : Side.Player;
}
