using System;
using UnityEngine;
using UnityEngine.UI;

namespace My4KGame
{
    /// <summary>
    /// 用代码搭 uGUI 的小工具集。
    ///
    /// 为什么不用预制体界面：这个工程的界面元素很少，而且需要按屏幕自适应。
    /// 代码搭的好处是——没有"引用丢了变白块"的问题，且所有布局参数都在一处，
    /// 改字号、改间距只需要动这里，不用在 Hierarchy 里一个一个点。
    /// 代价是编辑器里看不见（要看效果就按播放，或者用菜单 My4KGame → 渲染预览图）。
    /// </summary>
    public static class UiFactory
    {
        private static Font _font;

        /// <summary>
        /// 界面字体。用系统里的中文字体，保证"分数""连击"这些字能正常显示；
        /// 全都不在就退回 Unity 内置字体（只能显示英文，但不至于报错）。
        /// </summary>
        public static Font UiFont
        {
            get
            {
                if (_font != null) return _font;

                string[] candidates =
                {
                    "Microsoft YaHei UI", "Microsoft YaHei", "微软雅黑",
                    "SimHei", "黑体", "PingFang SC", "Noto Sans CJK SC",
                    "Source Han Sans CN", "Segoe UI",
                };

                try
                {
                    _font = Font.CreateDynamicFontFromOSFont(candidates, 32);
                }
                catch (Exception)
                {
                    _font = null;
                }

                if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                return _font;
            }
        }

        // ================================================================ 基本容器
        public static RectTransform CreateRect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            rect.anchoredPosition3D = Vector3.zero;
            return rect;
        }

        /// <summary>拉伸填满父节点。</summary>
        public static RectTransform Stretch(RectTransform rect, float left = 0f, float bottom = 0f,
            float right = 0f, float top = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
            return rect;
        }

        /// <summary>按锚点放置，size 为宽高（像素，基于 1920×1080 参考分辨率）。</summary>
        public static RectTransform Place(RectTransform rect, Vector2 anchor, Vector2 position,
            Vector2 size, Vector2? pivot = null)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot ?? new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        // ================================================================ 基础控件
        public static Image CreateImage(Transform parent, string name, Color color, Sprite sprite = null)
        {
            RectTransform rect = CreateRect(parent, name);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite != null ? sprite : SpriteFactory.White;
            image.color = color;
            image.raycastTarget = false;
            image.type = Image.Type.Simple;
            return image;
        }

        public static Text CreateText(Transform parent, string name, string content, int fontSize,
            Color color, TextAnchor anchor = TextAnchor.MiddleCenter,
            FontStyle style = FontStyle.Normal)
        {
            RectTransform rect = CreateRect(parent, name);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = UiFont;
            text.text = content;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = anchor;
            text.fontStyle = style;
            text.raycastTarget = false;
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        /// <summary>带描边的文字（大字号标题用，背景再暗也看得清）。</summary>
        public static Text CreateOutlinedText(Transform parent, string name, string content, int fontSize,
            Color color, TextAnchor anchor = TextAnchor.MiddleCenter, FontStyle style = FontStyle.Bold)
        {
            Text text = CreateText(parent, name, content, fontSize, color, anchor, style);
            var outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.55f);
            outline.effectDistance = new Vector2(2f, -2f);
            return text;
        }

        public static Button CreateButton(Transform parent, string label, Vector2 size, int fontSize,
            Color background, Color textColor, Action onClick)
        {
            RectTransform rect = CreateRect(parent, "Button_" + label);
            rect.sizeDelta = size;

            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = SpriteFactory.White;
            image.color = background;
            image.raycastTarget = true;

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.targetGraphic.raycastTarget = true;

            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.22f, 1.22f, 1.22f, 1f);
            colors.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            if (onClick != null) button.onClick.AddListener(() => onClick());

            Text text = CreateText(rect, "Label", label, fontSize, textColor, TextAnchor.MiddleCenter, FontStyle.Bold);
            Stretch(text.rectTransform);
            return button;
        }

        /// <summary>水平滑条。</summary>
        public static Slider CreateSlider(Transform parent, string name, float min, float max, float value,
            Vector2 size, Action<float> onChanged)
        {
            RectTransform root = CreateRect(parent, name);
            root.sizeDelta = size;

            var slider = root.gameObject.AddComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = false;
            slider.direction = Slider.Direction.LeftToRight;

            // 底槽
            Image background = CreateImage(root, "Background", new Color(0.10f, 0.13f, 0.18f));
            Place(background.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size.x, 14f));

            // 填充区（Slider 会自己改 Fill 的锚点，所以 Fill 的父节点要铺满）
            // 上下各缩 10px，让蓝条看起来比底槽细一圈
            RectTransform fillArea = CreateRect(root, "Fill Area");
            Stretch(fillArea, 8f, 10f, 8f, 10f);
            Image fill = CreateImage(fillArea, "Fill", Theme.Accent);
            Stretch(fill.rectTransform);
            slider.fillRect = fill.rectTransform;

            // 手柄
            RectTransform handleArea = CreateRect(root, "Handle Slide Area");
            Stretch(handleArea, 12f, 0f, 12f, 0f);
            Image handle = CreateImage(handleArea, "Handle", Color.white);
            Place(handle.rectTransform, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(18f, 26f));
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            handle.raycastTarget = true;

            slider.value = Mathf.Clamp(value, min, max);
            if (onChanged != null) slider.onValueChanged.AddListener(v => onChanged(v));
            return slider;
        }

        // ================================================================ 加减档组合
        /// <summary>一行「[−] 数值 [+]」的三个部件。</summary>
        public struct StepperRow
        {
            public RectTransform root;
            public TapRepeatButton minus;
            public TapRepeatButton plus;
            public Text value;
        }

        /// <summary>
        /// 造一行「[−] 数值 [+]」。用加减按钮而不是滑条，是因为：
        ///   · 滑条只有那根细手柄能拖，轨道本身不接收拖拽，很容易"拖了没反应"；
        ///   · 音游的调速/校准都是"一格一格"的离散量，按钮更符合心智，也更好精确控制。
        /// </summary>
        public static StepperRow CreateStepperRow(Transform parent, string name, Vector2 position, Vector2 size,
            int fontSize, string initialText, Action onMinus, Action onPlus)
        {
            var row = new StepperRow();

            RectTransform root = CreateRect(parent, name);
            Place(root, new Vector2(1f, 0.5f), position, size, new Vector2(1f, 0.5f));
            row.root = root;

            const float buttonSize = 52f;
            Color buttonColor = new Color(0.118f, 0.145f, 0.196f);

            row.minus = CreateTapButton(root, "−", new Vector2(0f, 0.5f), Vector2.zero,
                new Vector2(buttonSize, buttonSize), buttonColor, Theme.TextPrimary, 36, onMinus);
            row.plus = CreateTapButton(root, "+", new Vector2(1f, 0.5f), Vector2.zero,
                new Vector2(buttonSize, buttonSize), buttonColor, Theme.TextPrimary, 36, onPlus);

            row.value = CreateText(root, name + "Value", initialText, fontSize, Theme.TextPrimary,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            Place(row.value.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(size.x - buttonSize * 2f - 24f, size.y), new Vector2(0.5f, 0.5f));

            return row;
        }

        /// <summary>
        /// 方按钮（带按住连发）。主界面的左右箭头、游戏内的悬浮窗箭头都用它。
        /// </summary>
        public static TapRepeatButton CreateTapButton(Transform parent, string label, Vector2 anchor, Vector2 position,
            Vector2 size, Color background, Color textColor, int fontSize, Action onClick)
        {
            RectTransform rect = CreateRect(parent, "Tap" + label);
            Place(rect, anchor, position, size, new Vector2(anchor.x, 0.5f));

            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = SpriteFactory.White;
            image.color = background;
            image.raycastTarget = true;

            var tap = rect.gameObject.AddComponent<TapRepeatButton>();
            tap.background = image;
            tap.Bind(onClick);

            Text text = CreateText(rect, "Label", label, fontSize, textColor, TextAnchor.MiddleCenter, FontStyle.Bold);
            Stretch(text.rectTransform);
            return tap;
        }

        // ================================================================ 世界坐标 → 界面坐标
        /// <summary>
        /// 把界面元素摆到某个世界坐标对应的屏幕位置上。
        /// 判定文字、连击数、键位提示都靠它"贴"在判定线上方，
        /// 这样不管窗口多大、屏幕比例多少，它们都不会跑位。
        /// </summary>
        public static void AnchorToWorld(RectTransform target, Canvas canvas, Camera camera, Vector3 world)
        {
            if (target == null || canvas == null || camera == null) return;

            Vector3 screen = camera.WorldToScreenPoint(world);
            if (screen.z < 0f) return;

            // 覆盖层画布（游戏里的常态）回调传 null；如果画布被改成"摄像机空间"（编辑器预览图会这么做），
            // 必须把摄像机传进去，否则换算出来的坐标是错的。
            Camera eventCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : camera;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    (RectTransform)canvas.transform, screen, eventCamera, out Vector2 local))
            {
                target.anchoredPosition = local;
            }
        }
    }
}
