using System;
using System.IO;
using UnityEngine;

namespace NOVR.VrUi.Native;

public static class NativeMainMenuLogo
{
    private const string LogoAssetRelativePath = "Assets/MainMenuLogo.png";
    private static Texture2D? _texture;
    private static bool _loadAttempted;

    public static Texture2D? GetTexture()
    {
        if (_texture != null) return _texture;
        if (_loadAttempted) return null;

        _loadAttempted = true;
        var modFolder = NOVRPlugin.ModFolderPath;
        if (string.IsNullOrWhiteSpace(modFolder))
        {
            return null;
        }

        var path = Path.Combine(modFolder, LogoAssetRelativePath);
        if (!File.Exists(path))
        {
            Debug.LogWarning($"[NOVR] Native main menu logo asset was not found at '{path}'.");
            return null;
        }

        try
        {
            var bytes = File.ReadAllBytes(path);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                name = "NOVR Native Main Menu Logo",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            if (ImageConversion.LoadImage(texture, bytes, markNonReadable: true))
            {
                _texture = texture;
                return _texture;
            }

            UnityEngine.Object.Destroy(texture);
            Debug.LogWarning($"[NOVR] Native main menu logo asset at '{path}' could not be decoded.");
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[NOVR] Native main menu logo asset failed to load from '{path}': {exception}");
        }

        return null;
    }
}
