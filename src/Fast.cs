using UnityEngine;
using NV = System.Numerics.Vector3;
using NQ = System.Numerics.Quaternion;

namespace AL_HWatchers;

// In this game every Unity Vector3, Quaternion, Matrix4x4 and Mathf operation is a boxed call into the game's
// native code. That is fine now and then, but the masturbation poses and the hands touching her body do thousands
// of them each frame per girl, which slowed the game down with several girls at once. Hot code reads the
// game's values once and does its math with these managed copies instead.
internal static class Fast
{
    public static NV N(Vector3 v) => new(v.x, v.y, v.z);
    public static NQ N(Quaternion q) => new(q.x, q.y, q.z, q.w);
    public static Vector3 U(NV v) { Vector3 r = default; r.x = v.X; r.y = v.Y; r.z = v.Z; return r; }
    public static Quaternion U(NQ q) { Quaternion r = default; r.x = q.X; r.y = q.Y; r.z = q.Z; r.w = q.W; return r; }
    public static readonly Quaternion Identity = U(NQ.Identity);

    public static float Clamp01(float t) => t < 0f ? 0f : t > 1f ? 1f : t;
    public static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;

    // The same curve as Mathf.SmoothStep.
    public static float SmoothStep(float from, float to, float t)
    {
        t = Clamp01(t);
        t = -2f * t * t * t + 3f * t * t;
        return to * t + from * (1f - t);
    }

    public static NQ Slerp(NQ a, NQ b, float t) => NQ.Slerp(a, b, Clamp01(t));
    public static NV Lerp(NV a, NV b, float t) => NV.Lerp(a, b, Clamp01(t));

    // Like Vector3.normalized: zero for a vector too short to have a direction.
    public static NV Normalized(NV v)
    {
        var m = v.Length();
        return m > 1e-5f ? v / m : NV.Zero;
    }

    public static NV Project(NV v, NV onto)
    {
        var d = NV.Dot(onto, onto);
        return d < 1e-15f ? NV.Zero : onto * (NV.Dot(v, onto) / d);
    }

    // The rotation that turns one direction into the other (Quaternion.FromToRotation).
    public static NQ FromTo(NV from, NV to)
    {
        from = Normalized(from);
        to = Normalized(to);
        if (from == NV.Zero || to == NV.Zero) return NQ.Identity;
        var d = NV.Dot(from, to);
        if (d > 0.999999f) return NQ.Identity;
        if (d < -0.999999f)
        {
            var axis = NV.Cross(NV.UnitX, from);
            if (axis.LengthSquared() < 1e-6f) axis = NV.Cross(NV.UnitY, from);
            return NQ.CreateFromAxisAngle(NV.Normalize(axis), MathF.PI);
        }
        var c = NV.Cross(from, to);
        return NQ.Normalize(new NQ(c.X, c.Y, c.Z, 1f + d));
    }

    // Transform.InverseTransformDirection: her rotation undone, scale ignored.
    public static NQ InverseRotation(Transform t) => NQ.Inverse(N(t.rotation));
}

// A Unity transform matrix (localToWorldMatrix, worldToLocalMatrix), read once and applied in managed code.
internal readonly struct Affine
{
    private readonly float _m00, _m01, _m02, _m03, _m10, _m11, _m12, _m13, _m20, _m21, _m22, _m23;

    public Affine(Matrix4x4 m)
    {
        _m00 = m.m00; _m01 = m.m01; _m02 = m.m02; _m03 = m.m03;
        _m10 = m.m10; _m11 = m.m11; _m12 = m.m12; _m13 = m.m13;
        _m20 = m.m20; _m21 = m.m21; _m22 = m.m22; _m23 = m.m23;
    }

    public NV Point(NV v) => new(
        _m00 * v.X + _m01 * v.Y + _m02 * v.Z + _m03,
        _m10 * v.X + _m11 * v.Y + _m12 * v.Z + _m13,
        _m20 * v.X + _m21 * v.Y + _m22 * v.Z + _m23);

    public NV Vector(NV v) => new(
        _m00 * v.X + _m01 * v.Y + _m02 * v.Z,
        _m10 * v.X + _m11 * v.Y + _m12 * v.Z,
        _m20 * v.X + _m21 * v.Y + _m22 * v.Z);
}
