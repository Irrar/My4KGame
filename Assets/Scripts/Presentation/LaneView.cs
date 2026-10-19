using UnityEngine;

namespace My4KGame
{
    /// <summary>
    /// 一条轨道的视觉表现：底色面板 + 判定区的亮块。
    ///
    /// 关键是"按下要有即刻反馈"——
    /// 按键到画面亮起之间的延迟如果超过 50ms，玩家就会觉得游戏"不跟手"。
    /// 所以这里不做事后动画，按下的当帧直接给满亮度，再用几帧衰减回去。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LaneView : MonoBehaviour
    {
        [Header("引用（WorldBuilder 自动连）")]
        [Tooltip("轨道底色面板")]
        public SpriteRenderer panel;
        [Tooltip("判定区的亮块，按下去会闪")]
        public SpriteRenderer zone;

        [Header("参数")]
        public int lane;
        [Tooltip("闪光衰减速度（1/秒），越大闪得越快")]
        public float flashDecay = 7f;

        private float _flash;
        private Color _flashColor = Color.white;
        private Color _laneColor = Color.white;

        /// <summary>设置轨道编号、位置和颜色。WorldBuilder 建完物体后调用。</summary>
        public void Configure(int laneIndex)
        {
            lane = Mathf.Clamp(laneIndex, 0, PlayfieldLayout.LaneCount - 1);
            gameObject.name = "Lane" + lane;
            _laneColor = PlayfieldLayout.LaneColor(lane);

            float x = PlayfieldLayout.LaneToX(lane);
            transform.position = new Vector3(x, 0f, 0f);

            if (panel == null) panel = GetComponent<SpriteRenderer>();
            if (zone == null)
            {
                Transform child = transform.Find("Zone");
                if (child != null) zone = child.GetComponent<SpriteRenderer>();
            }

            if (panel != null)
            {
                panel.sprite = SpriteFactory.LanePanel;
                panel.drawMode = SpriteDrawMode.Simple;
                panel.sortingOrder = -10;
                panel.transform.localScale = Vector3.one;
            }

            if (zone != null)
            {
                zone.sprite = SpriteFactory.White;
                zone.drawMode = SpriteDrawMode.Simple;
                zone.sortingOrder = -9;
                zone.transform.localPosition = new Vector3(0f, PlayfieldLayout.JudgeLineY, 0f);
                // White 是 4×4px / PPU 100 = 0.04 世界单位，所以要放大到轨道尺寸
                zone.transform.localScale = new Vector3(
                    PlayfieldLayout.LaneWidth / 0.04f,
                    1.0f / 0.04f,
                    1f);
            }

            ApplyColors();
        }

        /// <summary>按下 / 判定时闪一下。</summary>
        public void Flash(Judgement judgement)
        {
            _flash = 1f;
            _flashColor = JudgementInfo.ToColor(judgement);
            ApplyColors();
        }

        /// <summary>按下但没打到音符（空打）：给一点点白闪，让玩家知道"我确实按到了"。</summary>
        public void FlashEmpty()
        {
            _flash = Mathf.Max(_flash, 0.55f);
            _flashColor = Color.white;
            ApplyColors();
        }

        private void Update()
        {
            if (_flash <= 0f) return;
            _flash = Mathf.Max(0f, _flash - Time.deltaTime * flashDecay);
            ApplyColors();
        }

        private void ApplyColors()
        {
            if (panel != null)
            {
                // 底色：很暗的轨道色，四条轨道略有区别即可，不要抢音符的视线
                Color baseColor = Color.Lerp(Theme.LaneBase, _laneColor, 0.10f);
                panel.color = Color.Lerp(baseColor, _laneColor, 0.22f * _flash);
            }

            if (zone != null)
            {
                Color c = Color.Lerp(_laneColor, _flashColor, 0.55f * _flash);
                c.a = 0.10f + 0.62f * _flash;
                zone.color = c;
            }
        }
    }
}
