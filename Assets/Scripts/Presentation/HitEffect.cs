using UnityEngine;

namespace My4KGame
{
    /// <summary>
    /// 一次打击特效：一圈向外扩散的圆环 + 一团柔光。
    /// 由 <see cref="HitEffectPool"/> 统一管理，播完自动回收。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HitEffect : MonoBehaviour
    {
        public SpriteRenderer ring;
        public SpriteRenderer glow;

        private float _time;
        private float _duration = 0.30f;
        private float _ringStart = 1.0f;
        private float _ringEnd = 2.4f;
        private float _glowStart = 1.0f;
        private float _glowEnd = 2.8f;
        private Color _color = Color.white;
        private System.Action<HitEffect> _onFinished;

        /// <summary>开始播放。位置是打击点（世界坐标）。</summary>
        public void Play(Vector3 position, Color color, float duration, System.Action<HitEffect> onFinished)
        {
            transform.position = position;
            _color = color;
            _duration = Mathf.Max(0.05f, duration);
            _time = 0f;
            _onFinished = onFinished;

            if (ring != null)
            {
                ring.sprite = SpriteFactory.Ring;
                ring.sortingOrder = 20;
                ring.transform.localScale = Vector3.one * _ringStart;
            }
            if (glow != null)
            {
                glow.sprite = SpriteFactory.Glow;
                glow.sortingOrder = 19;
                glow.transform.localScale = Vector3.one * _glowStart;
            }

            Apply(0f);
        }

        private void Update()
        {
            _time += Time.deltaTime;
            float k = Mathf.Clamp01(_time / _duration);
            Apply(k);

            if (k >= 1f)
            {
                _onFinished?.Invoke(this);
                _onFinished = null;
            }
        }

        private void Apply(float k)
        {
            // 用 1-(1-k)^3 做缓出：一开始扩得快，收尾慢，看起来有"冲劲"
            float ease = 1f - Mathf.Pow(1f - k, 3f);
            float fade = 1f - k;

            if (ring != null)
            {
                ring.transform.localScale = Vector3.one * Mathf.Lerp(_ringStart, _ringEnd, ease);
                Color c = _color;
                c.a = fade * 0.95f;
                ring.color = c;
            }

            if (glow != null)
            {
                glow.transform.localScale = Vector3.one * Mathf.Lerp(_glowStart, _glowEnd, ease);
                Color c = _color;
                c.a = fade * 0.55f;
                glow.color = c;
            }
        }
    }
}
