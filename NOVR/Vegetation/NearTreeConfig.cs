using BepInEx.Configuration;

namespace NOVR.Vegetation;

/// <summary>
/// Solid trees close to the aircraft, drawn by the mod alongside the game's own
/// GPU vegetation rather than instead of it.
///
/// <para>Why: every vegetation mesh in the game is a pair of crossed 2D planes —
/// <c>TreeClump1</c> is 36 trees of 4 triangles each, and all 144 of its
/// triangles are exactly vertical. A crossed quad has no volume, so the two eyes
/// get the same flat card from slightly different angles and it reads as
/// cardboard. A monitor hides that; a headset is what it was built to
/// expose.</para>
///
/// <para>Why only close: the shipped maps carry 549,864 clumps (≈19.8 M trees),
/// and the drawn far field at the game's 4 km cull radius is already about 1.7 M
/// triangles per eye. Multiplying that globally is not affordable. But everything
/// inside 300 m is roughly 15 k triangles — a rounding error against the field it
/// sits inside — so replacing only the near trees is nearly free, and near is
/// exactly where flatness is visible. See LOD-TREES-VEGETATION.md in the research
/// repo for the measurements.</para>
///
/// <para>How the two renderers avoid drawing the same tree twice: the game's cull
/// has an outer radius and no inner one, so the mod takes the near clumps out of
/// the game's own input instead. <c>TreeRenderer.CommandSetup</c> fills
/// <c>treePositionBuffer</c> once and nothing refills it, so writing a sentinel
/// position over a handful of entries — a few dozen bytes, not the 6.6 MB
/// array — hides those clumps from the game's compute cull, and restoring them
/// puts them back. See <see cref="NearTreeField"/>.</para>
///
/// <para>Off by default. It adds geometry and draw calls to a renderer that is
/// already the most expensive thing on the map, and unlike the rest of the mod it
/// changes how the game looks rather than where it is drawn.</para>
///
/// <para>The settings live here rather than in ModConfiguration because this mod
/// is developed as a stack of independent branches, and a setting declared in the
/// shared file puts every layer that adds one in conflict with every other. See
/// <see cref="ConfigSectionAttribute"/>.</para>
/// </summary>
[ConfigSection(Order = 92)]
public static class NearTreeConfig
{
    private const string Section = "Vegetation";

    public static ConfigEntry<bool> NearTrees;
    public static ConfigEntry<float> NearTreeRadius;
    public static ConfigEntry<float> NearTreeDetailRadius;
    public static ConfigEntry<int> NearTreeBudget;
    public static ConfigEntry<float> NearTreeLush;
    public static ConfigEntry<bool> NearTreeDebug;

    // Every read goes through these: ConfigSections catches a section that fails
    // to bind and carries on, so an entry can legitimately be null and the
    // renderer has to keep working — as "off" — when it is.
    public static bool Enabled => NearTrees != null && NearTrees.Value;
    public static float Radius => NearTreeRadius != null ? NearTreeRadius.Value : 300f;
    public static float DetailRadius => NearTreeDetailRadius != null ? NearTreeDetailRadius.Value : 90f;
    public static int Budget => NearTreeBudget != null ? NearTreeBudget.Value : 6000;
    public static float Lush => NearTreeLush != null ? NearTreeLush.Value : 0.35f;
    public static bool Debug => NearTreeDebug != null && NearTreeDebug.Value;

    public static void Bind(ConfigFile config)
    {
        NearTrees = config.Bind(
            Section,
            "Near Trees",
            false,
            "Draw solid, rotated, varied trees for the vegetation closest to you, instead of the game's "
            + "crossed flat planes. The game's own trees keep drawing everywhere beyond 'Near Tree Radius', "
            + "and the ones inside it are hidden from the game's culler so nothing is drawn twice. Off by "
            + "default: it adds geometry and draw calls, and it changes how the game looks rather than only "
            + "where it is drawn. Takes effect at the next mission load — the near set is built once when the "
            + "map's vegetation is set up.");

        NearTreeRadius = config.Bind(
            Section,
            "Near Tree Radius",
            300f,
            new ConfigDescription(
                "How far out, in metres, the mod's trees replace the game's. 300 m is about 105 clumps "
                + "(3,800 trees) on the shipped maps, which is the distance at which a flat card still reads "
                + "as flat. Raising it costs geometry quadratically — 600 m is roughly 3.4x the trees — and "
                + "buys progressively less, because past a few hundred metres a tree is a few pixels wide.",
                new AcceptableValueRange<float>(50f, 800f)));

        NearTreeDetailRadius = config.Bind(
            Section,
            "Near Tree Detail Radius",
            90f,
            new ConfigDescription(
                "Inside this radius, in metres, trees get the full model (trunk, branches and a shell of "
                + "leaf cards); between here and 'Near Tree Radius' they get a reduced one. Keep it well "
                + "below the radius: the full model is where nearly all the triangles are, and past about "
                + "100 m the branches are smaller than a pixel.",
                new AcceptableValueRange<float>(20f, 400f)));

        NearTreeBudget = config.Bind(
            Section,
            "Near Tree Budget",
            6000,
            new ConfigDescription(
                "Hard cap on how many trees the mod will draw at once. When a forest puts more than this "
                + "inside the radius, the closest ones win and the rest stay as the game's own flat trees — "
                + "so a dense wood degrades to stock rather than to a stall. Each tree is about 80 triangles "
                + "at full detail and 18 reduced.",
                new AcceptableValueRange<int>(500, 40000)));

        NearTreeLush = config.Bind(
            Section,
            "Near Tree Lush",
            0.35f,
            new ConfigDescription(
                "How far to tint the mod's trees towards the green the game tints its own with. The game "
                + "colours every tree on the GPU from a lushness map, altitude and blast damage, and the mod "
                + "cannot read that per tree — so this is one flat approximation of it, and its only job is "
                + "to stop the near trees reading as a different species from the far ones. 0 leaves the "
                + "game's own tree texture untouched.",
                new AcceptableValueRange<float>(0f, 1f)));

        NearTreeDebug = config.Bind(
            Section,
            "Near Tree Debug",
            false,
            "Log the near-tree set once a second: clumps and trees drawn, how many were hidden from the "
            + "game's culler, and how long the last rebuild took. For diagnosing 'the trees look wrong' "
            + "without a headset.");
    }
}
