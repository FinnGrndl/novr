using UnityEngine;

namespace NOVR.VrUi.SpecialBehavior;

public class PositionZeroBehavior : MonoBehaviour
{
    private void Update()
    {
        transform.SetPositionIfChanged(Vector3.zero);
    }
}