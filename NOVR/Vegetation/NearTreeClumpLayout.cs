using UnityEngine;

namespace NOVR.Vegetation;

/// <summary>
/// Where the 36 trees stand inside one <c>TreeClump1</c>, and how tall each one
/// is: xyz is the base of the trunk in the clump's own space, w is the height in
/// metres.
///
/// <para>This is measured off the shipped mesh rather than invented — decoded
/// from <c>sharedassets1.assets.resS</c>, split into connected islands over the
/// index buffer, one centroid and one height each. It is baked in because the
/// mesh is <b>not CPU-readable at runtime</b>: <c>Mesh.isReadable</c> is false on
/// it, which is the normal import setting for geometry nothing was ever meant to
/// read back, and was measured to be false in a live run rather than assumed.
/// <see cref="NearTreeField"/> still prefers the mesh when it can read one, so a
/// future build that flips that flag needs no change here.</para>
///
/// <para>Two things in the table are worth knowing before changing it. The bases
/// are all <i>below</i> the clump origin, from −3.5 m to −8.2 m, because the
/// artist sank the trunks so they cut into sloping terrain instead of hovering
/// over it. And the heights are not one number: the clump holds six distinct
/// sizes between 15.0 m and 31.3 m, so the size variety a wood needs is already
/// in the game's data and does not have to be faked.</para>
/// </summary>
internal static class NearTreeClumpLayout
{
    /// <summary>The 36 trees of <c>TreeClump1</c>: (x, baseY, z, height).</summary>
    internal static readonly Vector4[] TreeClump1 =
    {
        new Vector4(-3.04f, -3.51f, -24.43f, 15.7f),
        new Vector4(-0.53f, -3.51f, -24.12f, 15.7f),
        new Vector4(-2.06f, -3.51f, -22.10f, 15.7f),
        new Vector4(6.78f, -6.54f, -18.70f, 26.2f),
        new Vector4(11.01f, -6.54f, -16.43f, 26.2f),
        new Vector4(-9.46f, -6.54f, -14.55f, 26.2f),
        new Vector4(6.94f, -6.54f, -13.87f, 26.2f),
        new Vector4(-5.59f, -6.54f, -11.71f, 26.2f),
        new Vector4(21.06f, -3.59f, -11.22f, 18.4f),
        new Vector4(17.87f, -3.59f, -10.41f, 18.4f),
        new Vector4(-9.96f, -6.54f, -9.74f, 26.2f),
        new Vector4(-20.27f, -3.49f, -8.27f, 15.0f),
        new Vector4(20.17f, -3.59f, -8.05f, 18.4f),
        new Vector4(-21.90f, -3.49f, -6.62f, 15.0f),
        new Vector4(-19.65f, -3.49f, -6.04f, 15.0f),
        new Vector4(13.31f, -8.24f, -3.61f, 31.3f),
        new Vector4(-2.91f, -5.24f, -0.52f, 31.3f),
        new Vector4(8.29f, -8.24f, -0.43f, 31.3f),
        new Vector4(13.55f, -8.24f, 2.34f, 31.3f),
        new Vector4(2.24f, -5.24f, 2.45f, 31.3f),
        new Vector4(-13.99f, -6.54f, 2.97f, 26.2f),
        new Vector4(-18.74f, -6.54f, 3.64f, 26.2f),
        new Vector4(-2.91f, -5.24f, 5.42f, 31.3f),
        new Vector4(-15.74f, -6.54f, 7.44f, 26.2f),
        new Vector4(11.07f, -6.54f, 11.65f, 26.2f),
        new Vector4(-7.09f, -6.54f, 12.48f, 26.2f),
        new Vector4(22.72f, -3.52f, 13.88f, 16.2f),
        new Vector4(20.06f, -3.52f, 13.97f, 16.2f),
        new Vector4(-2.57f, -6.54f, 14.07f, 26.2f),
        new Vector4(7.88f, -6.54f, 15.28f, 26.2f),
        new Vector4(12.58f, -6.54f, 16.20f, 26.2f),
        new Vector4(21.47f, -3.52f, 16.23f, 16.2f),
        new Vector4(-6.24f, -6.54f, 17.20f, 26.2f),
        new Vector4(-19.31f, -3.49f, 17.87f, 15.0f),
        new Vector4(-16.99f, -3.49f, 17.90f, 15.0f),
        new Vector4(-18.17f, -3.49f, 19.89f, 15.0f),
    };
}
