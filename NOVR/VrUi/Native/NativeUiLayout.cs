using UnityEngine;

namespace NOVR.VrUi.Native;

public static class NativeUiLayout
{
    public const float HeaderY = 505f;
    public const float FooterY = -520f;
    public const float FooterLeftX = -860f;
    public const float FooterCenterX = 0f;
    public const float FooterRightX = 860f;

    public static readonly Vector2 HeaderSize = new(1200f, 34f);
    public static readonly Vector2 FooterButtonSize = new(190f, 44f);
}
