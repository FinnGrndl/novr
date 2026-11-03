using UnityEngine;
using UnityEngine.Rendering;

namespace NOVR.Vegetation;

/// <summary>
/// The two materials the near trees draw with, built at runtime from shaders the
/// game already has in its build.
///
/// <para>The game's own <c>Shader Graphs/TreeShader_Instanced</c> cannot be
/// reused. It is a procedural-instancing shader: the vertex stage reads
/// <c>_PositionBuffer[SV_InstanceID]</c> and ignores the object-to-world matrix
/// entirely, which is exactly the property that makes the game's one indirect
/// draw possible and exactly the property that makes it useless to a renderer
/// whose whole point is a per-instance transform.</para>
///
/// <para>So: URP Lit, configured by hand, with the game's own albedo and normal
/// map pulled off its tree material. <c>MotionControllerVisual</c> already does
/// this dance elsewhere in the mod, fallback included — <c>Shader.Find</c> only
/// resolves shaders that were compiled into the build, and while URP Lit always
/// is, "always" is the kind of claim that ends up in this repo's journal.</para>
/// </summary>
internal sealed class NearTreeMaterials
{
    private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
    private static readonly int BumpMap = Shader.PropertyToID("_BumpMap");
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int Cutoff = Shader.PropertyToID("_Cutoff");
    private static readonly int AlphaClip = Shader.PropertyToID("_AlphaClip");
    private static readonly int Smoothness = Shader.PropertyToID("_Smoothness");
    private static readonly int CullProperty = Shader.PropertyToID("_Cull");

    internal Material Canopy = null!;
    internal Material Trunk = null!;

    /// <summary>
    /// Draw settings shared by every batch. The world bounds are the game's own
    /// trick from <c>TreeRenderer.CommandSetup</c> — a box far larger than the
    /// scene, so the renderer never culls the draw itself and the per-instance
    /// culling stays where it belongs. Light probes are off because the matrix
    /// overload of <c>RenderMeshInstanced</c> carries no probe data.
    /// </summary>
    internal RenderParams CanopyParams;

    internal RenderParams TrunkParams;

    /// <summary>
    /// Builds both materials from the game's tree material, or returns null and
    /// says why. A null return disables the feature rather than drawing magenta
    /// trees across the map.
    /// </summary>
    internal static NearTreeMaterials? TryBuild(Material gameTreeMaterial, float lush)
    {
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null)
        {
            Debug.LogError("[NOVR.NearTrees] Shader.Find(\"Universal Render Pipeline/Lit\") returned null; "
                           + "near trees are disabled. The game's render pipeline is not what this expects.");
            return null;
        }

        if (gameTreeMaterial == null)
        {
            Debug.LogError("[NOVR.NearTrees] TreeRenderer has no material; near trees are disabled.");
            return null;
        }

        var albedo = gameTreeMaterial.HasProperty(BaseMap) ? gameTreeMaterial.GetTexture(BaseMap) : null;
        var normal = gameTreeMaterial.HasProperty(BumpMap) ? gameTreeMaterial.GetTexture(BumpMap) : null;
        if (albedo == null)
        {
            Debug.LogError("[NOVR.NearTrees] The game's tree material has no _BaseMap; near trees are disabled. "
                           + "Without its texture the mod's trees would be a different species from the game's.");
            return null;
        }

        var canopy = new Material(lit) { name = "NOVR_NearTreeCanopy" };
        canopy.SetTexture(BaseMap, albedo);
        if (normal != null)
        {
            canopy.SetTexture(BumpMap, normal);
            canopy.EnableKeyword("_NORMALMAP");
        }

        // Alpha clip, not transparency: leaf cards overlap heavily and sorting
        // them per fragment would both cost more and look worse. 0.5 rather than
        // the game's 0.75 because the mod's cards are cut from the middle of the
        // canopy, where the painting is softer than at its silhouette.
        canopy.SetFloat(AlphaClip, 1f);
        canopy.SetFloat(Cutoff, 0.5f);
        canopy.EnableKeyword("_ALPHATEST_ON");
        canopy.renderQueue = (int)RenderQueue.AlphaTest;

        // Double-sided: a card seen from behind must still be a leaf.
        canopy.SetFloat(CullProperty, (float)CullMode.Off);
        canopy.SetFloat(Smoothness, 0.1f);
        canopy.SetColor(BaseColor, Color.Lerp(Color.white, NearTreeModel.Lush, Mathf.Clamp01(lush)));

        var trunk = new Material(lit) { name = "NOVR_NearTreeTrunk" };
        trunk.SetColor(BaseColor, NearTreeModel.Bark);
        trunk.SetFloat(Smoothness, 0.05f);

        // RenderMeshInstanced will not draw at all without this, and the failure
        // is silent.
        canopy.enableInstancing = true;
        trunk.enableInstancing = true;

        var bounds = new Bounds(Vector3.zero, Vector3.one * 16384f);
        return new NearTreeMaterials
        {
            Canopy = canopy,
            Trunk = trunk,
            CanopyParams = new RenderParams(canopy)
            {
                worldBounds = bounds,
                shadowCastingMode = ShadowCastingMode.On,
                receiveShadows = true,
                lightProbeUsage = LightProbeUsage.Off,
            },
            TrunkParams = new RenderParams(trunk)
            {
                worldBounds = bounds,
                shadowCastingMode = ShadowCastingMode.On,
                receiveShadows = true,
                lightProbeUsage = LightProbeUsage.Off,
            },
        };
    }

    internal void Destroy()
    {
        if (Canopy != null) Object.Destroy(Canopy);
        if (Trunk != null) Object.Destroy(Trunk);
    }
}
