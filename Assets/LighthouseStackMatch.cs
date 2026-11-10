using System;
using UnityEngine;

// Endless, alternating-axis stacking. Only the overlap survives a placement.
public sealed class LighthouseStackMatch
{
    public const float StartingWidth = 8f;
    public int Score { get; private set; }
    public int WinThreshold { get; }
    public bool HasWon => Score >= WinThreshold;
    public bool Failed { get; private set; }
    public bool Perfect { get; private set; }
    public int Axis => Score % 2;
    public Vector2 Centre { get; private set; }
    public Vector2 Size { get; private set; } = Vector2.one * StartingWidth;
    public Vector2 MovingCentre
    {
        get
        {
            Vector2 position = Centre;
            position[Axis] += Offset;
            return position;
        }
    }
    public float Speed => 3f + Mathf.Min(Score * 0.18f, 4f);
    public float Travel => StartingWidth + 2f;
    public float Offset => (float)(distance <= Travel * 2f ? -Travel + distance : Travel * 3f - distance);
    public Vector2 CutCentre { get; private set; }
    public Vector2 CutSize { get; private set; }
    private double distance;

    public LighthouseStackMatch(int winThreshold = 10)
    {
        if (winThreshold < 1) throw new ArgumentOutOfRangeException(nameof(winThreshold));
        WinThreshold = winThreshold;
    }

    public void Tick(float deltaTime)
    {
        if (Failed || deltaTime <= 0f) return;
        distance = (distance + Speed * (double)deltaTime) % (Travel * 4.0);
    }

    public bool Drop()
    {
        if (Failed) return false;
        float offset = Offset;
        float width = Size[Axis];
        Perfect = Mathf.Abs(offset) <= Mathf.Min(0.08f, width * 0.1f);
        CutSize = Vector2.zero;
        if (!Perfect && Mathf.Abs(offset) >= width)
        {
            Failed = true;
            return false;
        }
        if (!Perfect)
        {
            CutSize = Size;
            Vector2 cut = CutSize;
            cut[Axis] = Mathf.Abs(offset);
            CutSize = cut;
            Vector2 centre = Centre;
            centre[Axis] += offset * 0.5f;
            Vector2 size = Size;
            size[Axis] -= Mathf.Abs(offset);
            Centre = centre;
            Size = size;
            cut = centre;
            cut[Axis] += Mathf.Sign(offset) * (width * 0.5f);
            CutCentre = cut;
        }
        Score++;
        distance = 0.0;
        return true;
    }
}
