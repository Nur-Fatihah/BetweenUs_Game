using System;
using UnityEngine;
using UnityEngine.UI;

namespace Crony
{
    /// <summary>Warm pixel toggle with an animated sliding knob and normal uGUI button input.</summary>
    [DisallowMultipleComponent]
    public sealed class PixelToggleSwitch : MonoBehaviour
    {
        [SerializeField] Image track;
        [SerializeField] RectTransform knob;
        [SerializeField] float slideSpeed = 16f;
        Button button;
        Action<bool> changed;
        bool isOn;
        float targetX;
        bool initialized;

        public bool IsOn => isOn;

        public void Bind(Button toggleButton, Image trackImage, RectTransform knobRect, bool initial, Action<bool> onChanged)
        {
            button = toggleButton;
            track = trackImage;
            knob = knobRect;
            changed = onChanged;
            if (button != null)
            {
                button.onClick.RemoveListener(Flip);
                button.onClick.AddListener(Flip);
            }
            SetIsOn(initial, false);
        }

        public void SetIsOn(bool value, bool notify = true)
        {
            isOn = value;
            targetX = value ? 21f : -21f;
            if (track != null) track.color = value ? PixelUiTheme.Mint : PixelUiTheme.Coral;
            if (knob != null && !initialized) knob.anchoredPosition = new Vector2(targetX, 0f);
            initialized = true;
            if (notify) changed?.Invoke(isOn);
        }

        void Flip()
        {
            SetIsOn(!isOn);
        }

        void Update()
        {
            if (knob == null) return;
            float nextX = Mathf.Lerp(knob.anchoredPosition.x, targetX, 1f - Mathf.Exp(-slideSpeed * Time.unscaledDeltaTime));
            knob.anchoredPosition = new Vector2(nextX, knob.anchoredPosition.y);
        }
    }
}
