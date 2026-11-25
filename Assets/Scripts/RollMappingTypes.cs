using System.Collections.Generic;
using UnityEngine;

/// <summary>How left/right presses are turned into a roll. Legacy = the original maths, untouched.</summary>
public enum RollMappingMode { Legacy, ScreenProjected }

/// <summary>ScreenProjected: what Right means when both roll results sit above each other on screen.</summary>
public enum StackedRule { RightIsUp, Clockwise }

/// <summary>The camera as it was on the frame a press was made (no allocations; copied by value).</summary>
public struct PressView
{
    public bool valid;
    public Vector3 pos;
    public Quaternion rot;
    public float fov, ortho, aspect;
    public bool isOrtho;
    public Vector2 screen;
    public float time;     // Time.unscaledTime of the press
    public int dir;        // -1 / +1 for a tap, 0 for a flip with no side
    public bool rolling;   // a roll was still playing (or cooling down) when the press was made
}

/// <summary>The held state of the mapping: the roll that means RIGHT, when it was last used, and the camera it was worked out under.</summary>
public struct RollState
{
    public int sign;          // raw roll (+1 / -1) that RIGHT means; 0 = not derived yet (new leg)
    public float lastPress;   // press time of the last press mapped
    public Quaternion rot;    // camera rotation the sign was derived under
}

/// <summary>Why a press got the sign it did (debug).</summary>
public enum RollWhy { Derived, Burst, Rolling, Gap, Camera, Hysteresis }

public struct RollInfo
{
    public Vector2 dA, dB;      // normalised screen displacement of the +1 / -1 roll result
    public float dxDiff;
    public bool clear;
    public StackedRule rule;
    public RollWhy why;
    public int sign;
    public Vector3 pointA, pointB;   // world points (debug gizmos)
}

/// <summary>Pure geometry for ScreenProjected (no scene state), so it can be tested outside a run.</summary>
public static class RollProjection
{
    public static Vector2 ScreenOf(in PressView v, Vector3 w)
    {
        Vector3 l = Quaternion.Inverse(v.rot) * (w - v.pos);
        if (v.isOrtho) return new Vector2(l.x, l.y) / Mathf.Max(0.0001f, v.ortho);
        float z = Mathf.Max(0.01f, l.z);
        float t = Mathf.Tan(v.fov * 0.5f * Mathf.Deg2Rad);
        return new Vector2(l.x, l.y) / (z * Mathf.Max(0.0001f, t));
    }

    /// <summary>Screen size of one cube edge, in the same units as ScreenOf.</summary>
    public static float CubeUnit(in PressView v, Vector3 centre, float cubeSize)
    {
        if (v.isOrtho) return cubeSize / Mathf.Max(0.0001f, v.ortho);
        float z = Mathf.Max(0.01f, (Quaternion.Inverse(v.rot) * (centre - v.pos)).z);
        return cubeSize / (z * Mathf.Max(0.0001f, Mathf.Tan(v.fov * 0.5f * Mathf.Deg2Rad)));
    }

    /// <summary>The two roll results (+1 and -1 about the heading, as ChangeSide does them) of a runner on face normal n0, and which is RIGHT.</summary>
    public static bool RightIsPlusOne(in PressView v, Vector3 centre, float cubeSize, Vector3 fwd, Vector3 n0,
                                      float clearThreshold, StackedRule rule, out RollInfo info)
    {
        float half = cubeSize * 0.5f;
        Vector3 nA = Quaternion.AngleAxis(-90f, fwd) * n0;   // +1 roll
        Vector3 nB = Quaternion.AngleAxis(90f, fwd) * n0;    // -1 roll
        Vector3 pNow = centre + n0 * half, pA = centre + nA * half, pB = centre + nB * half;
        float unit = Mathf.Max(0.0001f, CubeUnit(v, centre, cubeSize));
        Vector2 sNow = ScreenOf(v, pNow);
        Vector2 dA = (ScreenOf(v, pA) - sNow) / unit;
        Vector2 dB = (ScreenOf(v, pB) - sNow) / unit;
        float dxDiff = dA.x - dB.x;
        bool clear = Mathf.Abs(dxDiff) >= clearThreshold;
        bool rightIsA;
        if (clear) rightIsA = dxDiff > 0f;
        else if (rule == StackedRule.Clockwise)
        {
            Vector2 p0 = (sNow - ScreenOf(v, centre)) / unit;
            float cross = p0.x * dA.y - p0.y * dA.x;           // < 0 = clockwise on screen (y up)
            rightIsA = Mathf.Abs(cross) > 0.0001f ? cross < 0f : dxDiff >= 0f;
        }
        else rightIsA = Mathf.Abs(dA.y - dB.y) > 0.0001f ? dA.y > dB.y : dxDiff >= 0f;   // RightIsUp (tie: the rightmost)
        info = new RollInfo { dA = dA, dB = dB, dxDiff = dxDiff, clear = clear, rule = rule, pointA = pA, pointB = pB };
        return rightIsA;
    }
}
