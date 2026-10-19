using UnityEngine;
using UnityEngine.UI;

namespace My4KGame
{
    /// <summary>
    /// 流速实时预览：一小块"迷你轨道"，音符按当前流速循环下落。
    ///
    /// 存在的理由很实在：玩家在选曲界面调流速时，主舞台是空的（没在放歌），
    /// 调完看不到任何变化，就会以为"这个设置没用"。有了这块预览，按下 + 的瞬间
    /// 就能看到音符变快/变慢，反馈闭环才闭合。
    ///
    /// 它用的就是游戏里那一条映射：phase = t / TravelSeconds，travel 来自
    /// ScrollSpeedTrack，所以预览和实际手感是同一套公式，不会骗人。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpeedPreviewView : MonoBehaviour
    {
        [Header("引用（留空自动创建）")]
        public RectTransform root;
        public RectTransform judgeLine;
        public RectTransform[] noteRects = new RectTransform[PlayfieldLayout.LaneCount];
        public Image[] noteImages = new Image[PlayfieldLayout.LaneCount];
        public Text caption;

        [Header("外观")]
        public float noteWidth = 46f;
        public float noteHeight = 12f;

        /// <summary>迷你轨道之间的水平间距（必须大于轨道条宽度，否则会糊成一块）。</summary>
        public float laneSpacing = 62f;

        private readonly Color _frame = new Color(1f, 1f, 1f, 0.06f);

        public void EnsureBuilt()
        {
            if (root == null) root = (RectTransform)transform;
            if (noteRects == null || noteRects.Length < PlayfieldLayout.LaneCount)
            {
                noteRects = new RectTransform[PlayfieldLayout.LaneCount];
                noteImages = new Image[PlayfieldLayout.LaneCount];
            }

            // 背景
            if (transform.Find("PreviewBackdrop") == null)
            {
                Image backdrop = UiFactory.CreateImage(root, "PreviewBackdrop", new Color(0.02f, 0.03f, 0.05f, 0.85f));
                UiFactory.Stretch(backdrop.rectTransform);
            }

            // 四条迷你轨道
            for (int i = 0; i < PlayfieldLayout.LaneCount; i++)
            {
                string laneName = "PreviewLane" + i;
                if (transform.Find(laneName) == null)
                {
                    Image lane = UiFactory.CreateImage(root, laneName, _frame);
                    UiFactory.Place(lane.rectTransform, new Vector2(0.5f, 0.5f),
                        new Vector2(LanePreviewX(i), 2f),
                        new Vector2(56f, 116f));
                    Color c = PlayfieldLayout.LaneColor(i);
                    c.a = 0.10f;
                    lane.color = c;
                }
            }

            // 判定线
            if (judgeLine == null)
            {
                Transform existing = transform.Find("PreviewJudgeLine");
                if (existing != null) judgeLine = (RectTransform)existing;
                else
                {
                    Image line = UiFactory.CreateImage(root, "PreviewJudgeLine", new Color(1f, 0.86f, 0.42f, 0.85f));
                    judgeLine = line.rectTransform;
                    UiFactory.Place(judgeLine, new Vector2(0.5f, 0.5f), new Vector2(0f, -54f),
                        new Vector2(290f, 3f));
                }
            }

            // 四个音符
            for (int i = 0; i < PlayfieldLayout.LaneCount; i++)
            {
                if (noteImages[i] != null) continue;

                string noteName = "PreviewNote" + i;
                Transform existing = transform.Find(noteName);
                Image note = existing != null ? existing.GetComponent<Image>() : null;
                if (note == null)
                {
                    note = UiFactory.CreateImage(root, noteName,
                        Color.Lerp(PlayfieldLayout.LaneColor(i), Color.white, 0.25f));
                    UiFactory.Place(note.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero,
                        new Vector2(noteWidth, noteHeight));
                }

                noteImages[i] = note;
                noteRects[i] = note.rectTransform;
            }

            if (caption == null)
            {
                Transform existing = transform.Find("PreviewCaption");
                if (existing != null) caption = existing.GetComponent<Text>();
                else
                {
                    caption = UiFactory.CreateText(root, "PreviewCaption", "实时预览", 20,
                        Theme.TextMuted, TextAnchor.UpperLeft);
                    UiFactory.Place(caption.rectTransform, new Vector2(0f, 1f), new Vector2(12f, -10f),
                        new Vector2(320f, 26f), new Vector2(0f, 1f));
                }
            }

            // 编辑模式下 Update 不跑，先摆一次位置，让静态预览图里四个音符各就各位
            ApplyPositions(0.35f);
        }

        private float LanePreviewX(int lane)
        {
            // 面板内部的四条迷你轨道（比真实舞台压缩约 10 倍）
            return (lane - (PlayfieldLayout.LaneCount - 1) * 0.5f) * laneSpacing;
        }

        private void Update()
        {
            GameManager game = GameManager.Instance;
            if (game == null || game.Scroll == null) return;
            ApplyPositions(Time.unscaledTime);
        }

        /// <summary>按当前流速把四个音符摆到对应高度（visualTime 只用来驱动动画相位）。</summary>
        public void ApplyPositions(float visualTime)
        {
            GameManager game = GameManager.Instance;
            if (root == null || noteRects == null || noteImages == null) return;

            float travel = game != null && game.Scroll != null
                ? Mathf.Max(0.05f, game.Scroll.TravelSeconds)
                : PlayfieldLayout.TravelTime;

            float halfHeight = root.rect.height * 0.5f;
            if (halfHeight <= 1f) halfHeight = 75f;   // 布局还没算出来时用一个合理默认值

            // 顶部 = 出生点，底部 = 判定线；phase 0→1 正好是一屏
            float top = halfHeight - 16f;
            float bottom = -54f;

            for (int i = 0; i < noteRects.Length; i++)
            {
                RectTransform note = noteRects[i];
                if (note == null) continue;

                float phase = Mathf.Repeat(visualTime / travel + i * 0.23f, 1f);
                note.anchoredPosition = new Vector2(LanePreviewX(i), Mathf.Lerp(top, bottom, phase));
            }

            if (caption != null && game != null)
            {
                caption.text = "实时预览";
            }
        }
    }
}
