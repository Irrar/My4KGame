using UnityEngine;
using UnityEngine.UI;

namespace My4KGame
{
    /// <summary>
    /// 字体绑定器：把界面上所有 Text 的字体统一换成系统里的中文字体。
    ///
    /// 为什么需要它：Unity 的 Text 组件如果字体是"运行时从系统字体创建"的，
    /// 这个 Font 对象不是工程里的资源，保存场景时会被丢掉（变成 None），
    /// 结果就是"编辑器里看着好好的，重新打开工程后文字全没了"。
    /// 所以这里加一个 [ExecuteAlways] 的组件，每次启用时重新绑一遍 —
    /// 编辑器和播放模式都能正常显示中文。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class UiFontBinder : MonoBehaviour
    {
        private void OnEnable()
        {
            Apply();
        }

        /// <summary>给所有子物体上的 Text 换字体。可以手动调用。</summary>
        public void Apply()
        {
            Font font = UiFactory.UiFont;
            if (font == null) return;

            foreach (Text text in GetComponentsInChildren<Text>(true))
            {
                if (text == null) continue;
                if (text.font == null || !text.font.dynamic) text.font = font;
            }
        }
    }
}
