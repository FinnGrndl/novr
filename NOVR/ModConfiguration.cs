using System.ComponentModel;
using BepInEx.Configuration;
using UnityEngine;

namespace NOVR;

public class ModConfiguration
{
    public static ModConfiguration Instance;
    

    public readonly ConfigFile Config;
    public readonly ConfigEntry<bool> DisableUnityXrCameraAutoTracking;

    public ModConfiguration(ConfigFile config)
    {
        Instance = this;

        Config = config;
        
        
        DisableUnityXrCameraAutoTracking = config.Bind(
            "Camera",
            "Disable Unity XR Camera Auto Tracking",
            true,
            "Disables Unity's automatic XR camera tracking so NOVR can drive camera rotation manually. Turn off to test whether Unity's native camera tracking fixes Single Pass Instanced rendering.");
        
    }
}
