using System.Numerics;
namespace Bed;

// Shared scene2 transform-planner and body-validation policy; no engine dependency.
internal static class Scene2Numeric
{
    internal const float TransformTolerance=1e-4f;
    internal const double PlanarRotationTolerance=1e-6;
    internal static bool Finite(Vector3 value)=>float.IsFinite(value.X)&&float.IsFinite(value.Y)&&float.IsFinite(value.Z);
    internal static bool Unit(Quaternion value)=>float.IsFinite(value.X)&&float.IsFinite(value.Y)&&float.IsFinite(value.Z)&&float.IsFinite(value.W)&&MathF.Abs(value.LengthSquared()-1)<=TransformTolerance;
    internal static bool Close(Matrix4x4 a,Matrix4x4 b)
    {
        float[] left=[a.M11,a.M12,a.M13,a.M14,a.M21,a.M22,a.M23,a.M24,a.M31,a.M32,a.M33,a.M34,a.M41,a.M42,a.M43,a.M44];
        float[] right=[b.M11,b.M12,b.M13,b.M14,b.M21,b.M22,b.M23,b.M24,b.M31,b.M32,b.M33,b.M34,b.M41,b.M42,b.M43,b.M44];
        return left.Zip(right).All(p=>float.IsFinite(p.First)&&float.IsFinite(p.Second)&&MathF.Abs(p.First-p.Second)<=TransformTolerance*MathF.Max(1,MathF.Abs(p.First)));
    }
}
