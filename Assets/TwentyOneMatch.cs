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
    // After the first 40% of the board, Al stops choosing casually.
    public bool OpponentPlaysOptimally => Total * 5 >= Limit * 2;
    public bool HasWinningMove => !Finished && (Limit - 1 - Total) % 4 != 0;
    // In a losing position all safe rolls tie; take one tile rather than the last tile.
    public int BestRoll => Finished ? 0 : HasWinningMove ? (Limit - 1 - Total) % 4 : 1;

    // Fixed strategic stops leave the next player 1, 5, 9, ... tiles from the losing tile.
    // These depend on the board's limit, never the current move.
    public bool IsOptimalStoppingTile(int tile) => tile > 0 && tile < Limit && (Limit - 1 - tile) % 4 == 0;

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

    public void DebugComplete()
    {
        if (Winner == Side.Player) return;
        Started = true;
        Loser = null;
        Total = Limit - 1;
        Turn = Side.Opponent;
        Take(Side.Opponent, 1);
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

    // Casual rolls for the opening 40%, then leave Limit - 1 minus a multiple of four
    // whenever possible. Neither strategy changes the player's chosen die face.
    public int OpponentRoll()
    {
        if (!Started || Finished || Turn != Side.Opponent)
            throw new InvalidOperationException("It is not the opponent's turn.");
        return OpponentPlaysOptimally ? BestRoll : UnityEngine.Random.Range(1, 4);
    }

    private static Side Other(Side side) => side == Side.Player ? Side.Opponent : Side.Player;
}
