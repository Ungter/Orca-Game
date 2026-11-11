using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class MouseTrapBoardTests
{
    // Every hex within reach steps of the middle: a round island.
    private static List<Hex> Disk(int reach)
    {
        var cells = new List<Hex>();
        for (int q = -reach; q <= reach; q++)
            for (int r = -reach; r <= reach; r++)
            {
                var hex = new Hex(q, r);
                if (Hex.Distance(hex, new Hex(0, 0)) <= reach) cells.Add(hex);
            }
        return cells;
    }

    [Test]
    public void HexesSitWhereTheGridSaysAndEachEdgeFacesItsNeighbour()
    {
        var grid = new HexGrid(new Vector2(9f, 34f), 10f);
        foreach (Hex hex in Disk(3))
        {
            Vector2 c = grid.Centre(hex);
            Assert.AreEqual(hex, grid.At(c.x, c.y), "A hex's own middle is in it.");
            Assert.AreEqual(hex, grid.At(c.x + 6f, c.y - 4f), "So is a point well inside it.");
            for (int d = 0; d < 6; d++)
            {
                Vector2 next = grid.Centre(hex.Neighbour(d));
                Assert.AreEqual(grid.Width, Vector2.Distance(c, next), 1e-3f, "Neighbours are a hex width apart.");
                Assert.AreEqual(10f, Vector2.Distance(c, grid.Corner(hex, d)), 1e-3f, "Corners are a radius out.");
                Vector2 edgeMiddle = (grid.Corner(hex, d) + grid.Corner(hex, d + 1)) * 0.5f;
                Assert.Greater(Vector2.Dot((edgeMiddle - c).normalized, (next - c).normalized), 0.999f, "Edge d faces neighbour d.");
                Assert.AreEqual(1, Hex.Distance(hex, hex.Neighbour(d)));
            }
        }
    }

    [Test]
    public void TheVillainRunsTheShortestWayToTheShoreAndGetsAwayFromIt()
    {
        var board = new MouseTrapBoard(Disk(4), null, 3);
        board.PlaceMouse(new Hex(0, 0));
        Dictionary<Hex, int> toShore = board.ShoreDistances();
        Assert.AreEqual(4, toShore[board.Mouse]);
        for (int step = 3; step >= 0; step--)
        {
            MouseTrapBoard.Step move = board.MouseTurn();
            Assert.AreEqual(MouseTrapBoard.StepKind.Moved, move.Kind);
            Assert.AreEqual(1, Hex.Distance(move.From, move.To), "One hex at a time.");
            Assert.AreEqual(step, toShore[board.Mouse], "Each step is one nearer the shore.");
        }
        Assert.IsTrue(board.IsShore(board.Mouse));
        MouseTrapBoard.Step away = board.MouseTurn();
        Assert.AreEqual(MouseTrapBoard.StepKind.Escaped, away.Kind, "From the shore he gets away into the sea.");
        Assert.IsFalse(board.Contains(away.To), "Off the island.");
        Assert.AreEqual(MouseTrapBoard.Outcome.Escaped, board.State);
        Assert.IsFalse(board.CanStrike(new Hex(0, 0)), "Nothing more to strike once the game is over.");
    }

    [Test]
    public void ShuttingHimInCatchesHimEvenWithRoomLeftToRunAbout()
    {
        var board = new MouseTrapBoard(Disk(5), null, 4);
        board.PlaceMouse(new Hex(0, 0));
        var ring = new List<Hex>();
        foreach (Hex hex in Disk(2))
            if (Hex.Distance(hex, new Hex(0, 0)) == 2) ring.Add(hex);
        for (int i = 0; i < ring.Count - 1; i++)
        {
            Assert.IsTrue(board.Strike(ring[i]));
            Assert.AreEqual(MouseTrapBoard.StepKind.Moved, board.MouseTurn().Kind, "A gap is left, so he still runs.");
            board.PlaceMouse(new Hex(0, 0));
        }
        Assert.IsTrue(board.Strike(ring[ring.Count - 1]));
        Assert.IsFalse(board.CanReachShore());
        Assert.AreEqual(MouseTrapBoard.StepKind.Trapped, board.MouseTurn().Kind, "No way to the shore: caught.");
        Assert.AreEqual(MouseTrapBoard.Outcome.Trapped, board.State);
    }

    [Test]
    public void OnlyOpenHexesOtherThanHisCanBeStruck()
    {
        var wall = new Hex(1, 0);
        var board = new MouseTrapBoard(Disk(3), new[] { wall }, 5);
        board.PlaceMouse(new Hex(0, 0));
        Assert.IsFalse(board.CanStrike(wall), "The houses can't be struck.");
        Assert.IsFalse(board.CanStrike(new Hex(0, 0)), "Nor the hex he stands on.");
        Assert.IsFalse(board.CanStrike(new Hex(9, 9)), "Nor the sea.");
        Assert.IsTrue(board.Strike(new Hex(-1, 0)));
        Assert.IsFalse(board.Strike(new Hex(-1, 0)), "A sunk hex is gone.");
        Assert.AreEqual(1, board.Strikes);
        Assert.IsFalse(board.IsOpen(wall));
        Assert.IsFalse(board.IsOpen(new Hex(-1, 0)));
    }

    // The clue letter's map turns half round for the lighthouse's game: the same island, seen
    // from the other side.
    [Test]
    public void TheLettersMapTurnsHalfRoundForTheLighthouse()
    {
        var frame = new ClueMapArt.Frame(new Bounds(new Vector3(9f, 0f, 34f), new Vector3(220f, 40f, 390f)), ClueMapArt.MapArea);
        ClueMapArt.Frame turned = frame.Turn();
        Assert.IsFalse(frame.Turned);
        Assert.IsTrue(turned.Turned);
        foreach (Vector3 p in new[] { new Vector3(9f, 0f, 34f), new Vector3(100f, 0f, -120f), new Vector3(-60f, 0f, 180f) })
        {
            Vector2 a = frame.ToLetter(p), b = turned.ToLetter(p);
            Assert.AreEqual(frame.Centre.x * 2f - a.x, b.x, 1e-3f, "Mirrored through the map's middle.");
            Assert.AreEqual(frame.Centre.y * 2f - a.y, b.y, 1e-3f);
            Vector3 back = turned.ToWorld(b.x, b.y);
            Assert.AreEqual(p.x, back.x, 1e-3f, "And back again.");
            Assert.AreEqual(p.z, back.z, 1e-3f);
        }
        Assert.Less(turned.ToLetter(new Vector3(-100f, 0f, 34f)).y, turned.ToLetter(new Vector3(100f, 0f, 34f)).y, "Turned, west (-x) is up.");
    }

    [Test]
    public void HeStartsInTheHeartOfTheIslandTheSameWayForTheSameSeed()
    {
        var walls = new[] { new Hex(0, 0), new Hex(1, -1) };
        var board = new MouseTrapBoard(Disk(5), walls, 11);
        Dictionary<Hex, int> depth = board.Depths();
        int deepest = 0;
        foreach (var pair in depth)
            if (board.IsOpen(pair.Key)) deepest = Mathf.Max(deepest, pair.Value);
        List<Hex> heart = board.CentralCells();
        Assert.IsNotEmpty(heart);
        foreach (Hex hex in heart)
        {
            Assert.IsTrue(board.IsOpen(hex), "Never on a house.");
            Assert.GreaterOrEqual(depth[hex], deepest - 1, "Only towards the middle.");
        }
        CollectionAssert.Contains(heart, board.Mouse);
        Assert.AreEqual(board.Mouse, new MouseTrapBoard(Disk(5), walls, 11).Mouse, "Same seed, same start.");
    }
}
