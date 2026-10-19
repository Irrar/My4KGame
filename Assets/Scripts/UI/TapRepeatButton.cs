using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace My4KGame
{
    /// <summary>
    /// 支持"按住连发"的方按钮：点一下走一步，按住 0.4 秒后每 0.08 秒自动再走一步。
    ///
    /// 为什么不用 Unity 的 Button：Button 只在"抬起"时触发一次，按住不能连发；
    /// 而 0.25 一档的调速要从 1 档走到 10 档得按 36 次，不连发会点到手酸。
    /// 这里直接实现 PointerDown/Up，按下当帧就触发一次（反馈更快），并自己做按下变色。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TapRepeatButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [Tooltip("按下后多久开始连发（秒）")]
        public float initialDelay = 0.40f;

        [Tooltip("连发间隔（秒）")]
        public float repeatInterval = 0.08f;

        [Header("外观")]
        public Image background;
        public Color normalColor = new Color(0.118f, 0.145f, 0.196f);
        public Color pressedColor = new Color(0.30f, 0.78f, 0.96f);

        private Action _action;
        private bool _held;
        private float _nextRepeat;

        /// <summary>绑定点击动作（每次步进调用一次）。</summary>
        public void Bind(Action action)
        {
            _action = action;
            if (background != null)
            {
                normalColor = background.color;
                SetPressed(false);
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _held = true;
            _nextRepeat = Time.unscaledTime + initialDelay;
            SetPressed(true);
            Fire();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _held = false;
            SetPressed(false);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            // 手指/鼠标移出按钮范围就停，避免"按住不放一直跑"
            _held = false;
            SetPressed(false);
        }

        private void OnDisable()
        {
            _held = false;
            SetPressed(false);
        }

        private void Update()
        {
            if (!_held) return;
            if (Time.unscaledTime < _nextRepeat) return;

            _nextRepeat = Time.unscaledTime + repeatInterval;
            Fire();
        }

        private void Fire()
        {
            _action?.Invoke();
        }

        private void SetPressed(bool pressed)
        {
            if (background == null) return;
            background.color = pressed ? pressedColor : normalColor;
        }
    }
}
