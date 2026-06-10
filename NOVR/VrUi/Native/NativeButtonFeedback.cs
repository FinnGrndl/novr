using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NOVR.VrUi.Native;

public sealed class NativeButtonFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    private static readonly Color DefaultDisabledColor = new(0.16f, 0.18f, 0.19f, 0.55f);

    private Button? _button;
    private RectTransform? _rectTransform;
    private Outline? _outline;
    private Vector3 _baseScale = Vector3.one;
    private bool _hasBaseScale;
    private bool _hovered;
    private bool _pressed;

    public static void Configure(Button button, Color normalColor, Color? disabledColor = null)
    {
        var colors = button.colors;
        colors.normalColor = normalColor;
        colors.highlightedColor = Brighten(normalColor, 0.22f);
        colors.pressedColor = Darken(normalColor, 0.24f);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = disabledColor ?? DefaultDisabledColor;
        colors.colorMultiplier = 1.08f;
        colors.fadeDuration = 0.06f;
        button.colors = colors;

        if (button.targetGraphic != null)
        {
            button.targetGraphic.color = normalColor;
        }

        var feedback = button.GetComponent<NativeButtonFeedback>() ??
                       button.gameObject.AddComponent<NativeButtonFeedback>();
        feedback.Bind(button);
    }

    public static void SetNormalColor(Button button, Color normalColor, Color? disabledColor = null)
    {
        Configure(button, normalColor, disabledColor);
    }

    private static Color Brighten(Color color, float amount)
    {
        return Color.Lerp(color, Color.white, amount);
    }

    private static Color Darken(Color color, float amount)
    {
        return Color.Lerp(color, Color.black, amount);
    }

    private void Awake()
    {
        Bind(GetComponent<Button>());
    }

    private void OnEnable()
    {
        _hovered = false;
        _pressed = false;
        if (_rectTransform != null)
        {
            if (!_hasBaseScale)
            {
                CaptureBaseScale();
            }
            else
            {
                _rectTransform.localScale = _baseScale;
            }
        }
    }

    private void OnDisable()
    {
        _hovered = false;
        _pressed = false;
        if (_rectTransform != null && _hasBaseScale)
        {
            _rectTransform.localScale = _baseScale;
        }
    }

    private void Bind(Button? button)
    {
        var buttonChanged = _button != button;
        _button = button;
        _rectTransform ??= (RectTransform)transform;
        _outline ??= gameObject.GetComponent<Outline>() ?? gameObject.AddComponent<Outline>();
        _outline.effectColor = new Color(1f, 1f, 1f, 0.34f);
        _outline.effectDistance = new Vector2(2f, -2f);
        _outline.enabled = false;
        if (buttonChanged || !_hasBaseScale)
        {
            CaptureBaseScale();
        }
    }

    private void CaptureBaseScale()
    {
        if (_rectTransform == null) return;

        _baseScale = _rectTransform.localScale;
        _hasBaseScale = true;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        _hovered = true;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _hovered = false;
        _pressed = false;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        _pressed = true;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        _pressed = false;
    }

    private void Update()
    {
        if (_button == null || _rectTransform == null || _outline == null) return;

        var active = _button.interactable;
        var targetScale = _baseScale;
        if (active && _pressed)
        {
            targetScale = _baseScale * 0.965f;
        }
        else if (active && _hovered)
        {
            targetScale = _baseScale * 1.025f;
        }

        _rectTransform.localScale = Vector3.Lerp(_rectTransform.localScale, targetScale, Time.unscaledDeltaTime * 18f);
        _outline.enabled = active && (_hovered || _pressed);
    }
}
