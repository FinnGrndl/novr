using System.Collections.Generic;
using NuclearOption.SavedMission;
using UnityEngine;

namespace NOVR;

// Nuclear Option 0.34 removed MissionSaveLoad.QuickLoadMany; rebuild it on top of QuickLoadOne.
public static class MissionQuickLoadHelper
{
    public static List<(MissionKey key, MissionQuickLoad mission)> QuickLoadMany(IEnumerable<MissionKey> keys)
    {
        var result = new List<(MissionKey key, MissionQuickLoad mission)>();
        foreach (var key in keys)
        {
            if (MissionSaveLoad.QuickLoadOne(key, out var mission, out var error))
            {
                result.Add((key, mission));
            }
            else
            {
                Debug.LogWarning($"[NOVR] Failed to quick-load mission '{key}': {error}");
            }
        }

        return result;
    }
}
