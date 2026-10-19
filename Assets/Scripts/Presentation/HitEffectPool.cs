using System.Collections.Generic;
using UnityEngine;

namespace My4KGame
{
    /// <summary>
    /// 打击特效的对象池。音游每秒可能触发十几次特效，
    /// 每次 Instantiate 都会产生垃圾回收压力，进而造成掉帧、进而造成判定漂移，
    /// 所以这类高频短命的东西一律池化。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HitEffectPool : MonoBehaviour
    {
        [Header("参数")]
        public int prewarm = 12;
        [Tooltip("单次特效时长（秒）")]
        public float lifeSeconds = 0.30f;

        private readonly List<HitEffect> _pool = new List<HitEffect>();
        private readonly List<HitEffect> _playing = new List<HitEffect>();
        private Transform _root;

        private void Awake()
        {
            _root = transform;
            for (int i = 0; i < prewarm; i++)
            {
                HitEffect effect = CreateEffect();
                if (effect == null) break;
                effect.gameObject.SetActive(false);
                _pool.Add(effect);
            }
        }

        /// <summary>在指定位置播一次特效。</summary>
        public void Play(Vector3 position, Color color)
        {
            HitEffect effect = Rent();
            if (effect == null) return;
            effect.gameObject.SetActive(true);
            effect.Play(position, color, lifeSeconds, OnFinished);
            _playing.Add(effect);
        }

        private void OnFinished(HitEffect effect)
        {
            if (effect == null) return;
            _playing.Remove(effect);
            effect.gameObject.SetActive(false);
            if (!_pool.Contains(effect)) _pool.Add(effect);
        }

        private HitEffect Rent()
        {
            while (_pool.Count > 0)
            {
                int last = _pool.Count - 1;
                HitEffect pooled = _pool[last];
                _pool.RemoveAt(last);
                if (pooled != null) return pooled;
            }
            return CreateEffect();
        }

        private HitEffect CreateEffect()
        {
            var go = new GameObject("HitEffect");
            go.transform.SetParent(_root != null ? _root : transform, false);

            var effect = go.AddComponent<HitEffect>();

            var ringGo = new GameObject("Ring");
            ringGo.transform.SetParent(go.transform, false);
            effect.ring = ringGo.AddComponent<SpriteRenderer>();
            effect.ring.sprite = SpriteFactory.Ring;
            effect.ring.sortingOrder = 20;

            var glowGo = new GameObject("Glow");
            glowGo.transform.SetParent(go.transform, false);
            effect.glow = glowGo.AddComponent<SpriteRenderer>();
            effect.glow.sprite = SpriteFactory.Glow;
            effect.glow.sortingOrder = 19;

            return effect;
        }
    }
}
