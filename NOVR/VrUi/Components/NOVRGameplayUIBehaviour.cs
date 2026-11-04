using UnityEngine;

namespace NOVR.VrUi.SpecialBehavior;

public class NOVRGameplayUIBehaviour : UIRenderedCanvasBehavior
{

    private void Update()
    {
        transform.SetLocalScaleIfChanged(new Vector3(0.003f, 0.003f, 0.003f));
        transform.SetPositionIfChanged(new Vector3(0f, 0f, 3f));
    }
}