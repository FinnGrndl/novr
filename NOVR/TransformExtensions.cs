using UnityEngine;

namespace NOVR;

/// <summary>
/// Transform writes that do nothing when the value is already right. Assigning a transform marks
/// it changed even when the value is the same, and a changed rect makes its canvas rebuild, so a
/// behaviour that re-asserts a pose every frame keeps a whole canvas rebuilding for nothing. That
/// cost grows with what the canvas holds: the tactical map carries an icon per unit.
/// </summary>
public static class TransformExtensions
{
    public static void SetPositionIfChanged(this Transform transform, Vector3 position)
    {
        if (transform.position != position) transform.position = position;
    }

    public static void SetRotationIfChanged(this Transform transform, Quaternion rotation)
    {
        if (transform.rotation != rotation) transform.rotation = rotation;
    }

    public static void SetLocalPositionIfChanged(this Transform transform, Vector3 localPosition)
    {
        if (transform.localPosition != localPosition) transform.localPosition = localPosition;
    }

    public static void SetLocalRotationIfChanged(this Transform transform, Quaternion localRotation)
    {
        if (transform.localRotation != localRotation) transform.localRotation = localRotation;
    }

    public static void SetLocalScaleIfChanged(this Transform transform, Vector3 localScale)
    {
        if (transform.localScale != localScale) transform.localScale = localScale;
    }

    public static void SetSizeDeltaIfChanged(this RectTransform transform, Vector2 sizeDelta)
    {
        if (transform.sizeDelta != sizeDelta) transform.sizeDelta = sizeDelta;
    }
}
