using System.Numerics;
namespace Brutal.Authoring.Editor;

/// <summary>One model-space geometry part and its optional source material binding.</summary>
public sealed record MeshPart(MeshData Geometry, MeshPartMaterial? Material);
/// <summary>Model-local material ordinal, opaque linear factor and independently admitted texture asset GUID.</summary>
public sealed record MeshPartMaterial(int Ordinal, Vector4 BaseColor, Guid? Texture);
