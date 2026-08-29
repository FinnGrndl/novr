using System.Collections.Generic;
using UnityEngine;

namespace NOVR.Vegetation;

/// <summary>
/// Builds the mod's tree meshes in code, at load, with no asset to ship.
///
/// <para>Nothing in the game can be borrowed: all four vegetation meshes are
/// crossed quads (<c>TreeClump1</c>, <c>TreeRow1</c>, <c>tree1.138</c>,
/// <c>grassclump1_onesided</c>), so there is no solid tree anywhere in the
/// shipped data. Generating one avoids both a licence and an import path, and it
/// gives every level of detail for free — the reduced model is the same generator
/// with fewer sides and fewer cards, so the two cannot drift apart.</para>
///
/// <para>The canopy is a shell of leaf cards rather than one crossed pair, which
/// is the whole point: cards spread over an ellipsoid give the two eyes genuinely
/// different silhouettes, and that is what stereo needs in order to read a tree
/// as an object rather than a picture of one. Their normals point outward from
/// the crown centre rather than along the card, so the canopy shades like a
/// volume instead of like a set of flat plates.</para>
///
/// <para>The leaves are cut out of the game's own <c>Tree_deciduous</c> texture,
/// which is a 1024² painting of one whole tree — trunk at the bottom centre,
/// canopy filling the middle. <see cref="LeafCards"/> are four windows into the
/// leafy part, picked off the alpha channel at about 70 % coverage so each one
/// carries a ragged outer edge rather than a solid green rectangle. Using the
/// game's own art is what keeps the near trees the same species as the far ones.
/// The trunk gets no texture — the bark strip in that image is nine pixels wide
/// and would bleed into transparency at the first mip — so it is flat-shaded in
/// the bark colour sampled from the same image.</para>
///
/// <para>Everything is generated at unit height with its base at the origin, so
/// one mesh serves every tree and the per-instance matrix carries the real height,
/// the yaw and the lean.</para>
/// </summary>
internal static class NearTreeModel
{
    internal const int SubmeshCanopy = 0;
    internal const int SubmeshTrunk = 1;

    /// <summary>
    /// Bark, sampled from the opaque middle of the trunk in
    /// <c>Tree_deciduous</c> (sRGB 0.381, 0.388, 0.321) — a grey-olive, not the
    /// brown a tree is usually guessed to be.
    /// </summary>
    internal static readonly Color Bark = new Color(0.381f, 0.388f, 0.321f, 1f);

    /// <summary>
    /// The green the game tints its own trees towards (<c>_ColorLush</c> on
    /// <c>Tree_deciduous_Instanced</c>), used to approximate that tint flatly.
    /// </summary>
    internal static readonly Color Lush = new Color(0f, 0.92f, 0.06f, 1f);

    /// <summary>
    /// Windows into <c>Tree_deciduous</c> that are leaf mass with an edge:
    /// 160 px squares whose alpha coverage is about 0.7, chosen away from the
    /// central trunk column (x 500–590) so no card carries a stripe of bark.
    /// </summary>
    private static readonly Rect[] LeafCards =
    {
        Rect.MinMaxRect(0.2734f, 0.2871f, 0.4297f, 0.4434f),
        Rect.MinMaxRect(0.2539f, 0.5215f, 0.4102f, 0.6777f),
        Rect.MinMaxRect(0.6055f, 0.5801f, 0.7617f, 0.7363f),
        Rect.MinMaxRect(0.5664f, 0.2871f, 0.7227f, 0.4434f),
    };

    /// <summary>How many differently-shaped meshes exist at full detail.</summary>
    internal const int FullVariants = 2;

    /// <summary>
    /// One tree. <paramref name="full"/> picks the near model (trunk, branch
    /// stubs, a 14-card canopy — about 80 triangles) over the reduced one
    /// (short trunk, 5 cards — about 18).
    /// </summary>
    internal static Mesh Build(int variant, bool full)
    {
        var verts = new List<Vector3>(128);
        var norms = new List<Vector3>(128);
        var uvs = new List<Vector2>(128);
        var canopy = new List<int>(96);
        var trunk = new List<int>(96);

        var rng = new Lcg(unchecked((uint)(0x9E3779B9u * (uint)(variant + 1) + (full ? 17u : 71u))));

        // Proportions read off Tree_deciduous: the trunk is clear of foliage for
        // the bottom 30 % of the tree, and the crown is about 0.65 as wide as the
        // tree is tall.
        const float crownY = 0.63f;
        const float crownR = 0.33f;
        const float crownHalfHeight = 0.34f;

        var lean = new Vector3(rng.Range(-0.03f, 0.03f), 0f, rng.Range(-0.03f, 0.03f));

        if (full)
        {
            AddTube(verts, norms, uvs, trunk, sides: 8,
                new[] { Vector3.zero, new Vector3(lean.x * 0.4f, 0.22f, lean.z * 0.4f), lean + new Vector3(0f, 0.66f, 0f) },
                new[] { 0.036f, 0.026f, 0.013f });

            // Three stubs, so the silhouette has something between trunk and
            // canopy instead of a bare pole disappearing into a green ball.
            for (var i = 0; i < 3; i++)
            {
                var yaw = rng.Range(0f, 360f) + i * 120f;
                var dir = Quaternion.Euler(0f, yaw, 0f) * new Vector3(1f, rng.Range(0.5f, 1.1f), 0f).normalized;
                var root = new Vector3(lean.x * 0.6f, rng.Range(0.34f, 0.5f), lean.z * 0.6f);
                AddTube(verts, norms, uvs, trunk, sides: 4,
                    new[] { root, root + dir * rng.Range(0.18f, 0.28f) },
                    new[] { 0.018f, 0.006f });
            }

            AddCanopy(verts, norms, uvs, canopy, ref rng,
                centre: lean + new Vector3(0f, crownY, 0f),
                radius: new Vector3(crownR, crownHalfHeight, crownR),
                cards: 14, size: 0.36f);
        }
        else
        {
            AddTube(verts, norms, uvs, trunk, sides: 4,
                new[] { Vector3.zero, lean + new Vector3(0f, 0.62f, 0f) },
                new[] { 0.032f, 0.014f });

            // Fewer, larger cards: the reduced model only ever runs past the
            // detail radius, where a card is a handful of pixels across and its
            // job is to fill the silhouette, not to show structure.
            AddCanopy(verts, norms, uvs, canopy, ref rng,
                centre: lean + new Vector3(0f, crownY, 0f),
                radius: new Vector3(crownR, crownHalfHeight, crownR),
                cards: 5, size: 0.52f);
        }

        var mesh = new Mesh { name = full ? $"NOVR_NearTree_{variant}" : "NOVR_NearTree_Reduced" };
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(canopy, SubmeshCanopy);
        mesh.SetTriangles(trunk, SubmeshTrunk);
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// Leaf cards scattered over an ellipsoid. Each card faces outward from the
    /// crown centre and takes that direction as its normal, which is what makes a
    /// pile of flat quads shade like a ball.
    /// </summary>
    private static void AddCanopy(
        List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris,
        ref Lcg rng, Vector3 centre, Vector3 radius, int cards, float size)
    {
        // A golden-angle spiral rather than random directions: random points on a
        // sphere clump, and a clumped canopy has holes you can see the sky
        // through from inside the cockpit.
        const float golden = 2.399963f;
        for (var i = 0; i < cards; i++)
        {
            var t = (i + 0.5f) / cards;
            var y = 1f - 2f * t;
            var r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            var a = golden * i;
            var dir = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);

            // Pull the shell in a little, and vary how far, so the cards make a
            // thick layer rather than a hollow eggshell.
            var reach = rng.Range(0.62f, 1.0f);
            var pos = centre + new Vector3(dir.x * radius.x, dir.y * radius.y, dir.z * radius.z) * reach;

            // Bias the normal upward: leaves are lit from the sky, and a card on
            // the underside of the crown facing straight down reads as a hole.
            var normal = (dir + Vector3.up * 0.35f).normalized;

            AddCard(verts, norms, uvs, tris, pos, normal,
                Quaternion.AngleAxis(rng.Range(-25f, 25f), normal) * Vector3.up,
                size * rng.Range(0.82f, 1.18f),
                LeafCards[rng.Next() % LeafCards.Length]);
        }
    }

    private static void AddCard(
        List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris,
        Vector3 centre, Vector3 normal, Vector3 up, float size, Rect uv)
    {
        var right = Vector3.Cross(up, normal);
        if (right.sqrMagnitude < 1e-6f) right = Vector3.Cross(Vector3.forward, normal);
        right.Normalize();
        var top = Vector3.Cross(normal, right).normalized;

        var h = size * 0.5f;
        var b = verts.Count;
        verts.Add(centre - right * h - top * h);
        verts.Add(centre + right * h - top * h);
        verts.Add(centre + right * h + top * h);
        verts.Add(centre - right * h + top * h);
        for (var i = 0; i < 4; i++) norms.Add(normal);
        uvs.Add(new Vector2(uv.xMin, uv.yMin));
        uvs.Add(new Vector2(uv.xMax, uv.yMin));
        uvs.Add(new Vector2(uv.xMax, uv.yMax));
        uvs.Add(new Vector2(uv.xMin, uv.yMax));

        // The canopy material is double-sided — cull off — so the winding here
        // only has to be self-consistent.
        tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
        tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
    }

    /// <summary>A tapered tube through the given ring centres. No caps: the top
    /// is inside the canopy and the bottom is in the ground.</summary>
    private static void AddTube(
        List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris,
        int sides, Vector3[] centres, float[] radii)
    {
        var axis = (centres[centres.Length - 1] - centres[0]).normalized;
        var refUp = Mathf.Abs(axis.y) > 0.9f ? Vector3.forward : Vector3.up;
        var x = Vector3.Cross(refUp, axis).normalized;
        var z = Vector3.Cross(axis, x);

        var first = verts.Count;
        for (var ring = 0; ring < centres.Length; ring++)
        {
            for (var s = 0; s < sides; s++)
            {
                var a = s * Mathf.PI * 2f / sides;
                var radial = x * Mathf.Cos(a) + z * Mathf.Sin(a);
                verts.Add(centres[ring] + radial * radii[ring]);
                norms.Add(radial);
                uvs.Add(new Vector2(s / (float)sides, ring));
            }
        }

        for (var ring = 0; ring < centres.Length - 1; ring++)
        {
            var a0 = first + ring * sides;
            var b0 = a0 + sides;
            for (var s = 0; s < sides; s++)
            {
                var s1 = (s + 1) % sides;
                tris.Add(a0 + s); tris.Add(b0 + s); tris.Add(b0 + s1);
                tris.Add(a0 + s); tris.Add(b0 + s1); tris.Add(a0 + s1);
            }
        }
    }

    /// <summary>
    /// A small deterministic generator. Not <c>UnityEngine.Random</c>: that is
    /// global state the game also draws from, so using it here would make the
    /// mod's trees depend on how many bullets had been fired.
    /// </summary>
    internal struct Lcg
    {
        private uint _state;

        internal Lcg(uint seed) => _state = seed == 0u ? 0x6D2B79F5u : seed;

        internal uint Next()
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return _state;
        }

        internal float Value => (Next() & 0xFFFFFF) / (float)0x1000000;

        internal float Range(float min, float max) => min + (max - min) * Value;
    }
}
