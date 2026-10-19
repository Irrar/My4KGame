using System.Collections.Generic;
using UnityEngine;

namespace My4KGame
{
    /// <summary>工程里用到的几种图形。名字和 Resources/Art 下的文件名一一对应。</summary>
    public enum SpriteKind
    {
        White,
        Note,
        Glow,
        Ring,
        LanePanel,
        JudgeLine,
    }

    /// <summary>
    /// 贴图工厂：优先用 Resources/Art 下的图片资源；找不到就当场用代码画一张。
    ///
    /// 为什么要留这条"代码画图"的后路：
    ///   1. 工程可以做到零美术资源也能跑，不会因为丢了一张图而满屏报错；
    ///   2. 别人拿走工程、删掉 Art 目录，游戏照样能玩；
    ///   3. 想换正式美术，把同名 png 丢进 Assets/Resources/Art 就自动生效，不用改代码。
    ///
    /// 所有贴图的 PixelsPerUnit 都是 100，所以像素尺寸 = 世界尺寸 × 100：
    ///   音符 200×50px = 2.0×0.5 世界单位（和手册第 5 章的 Scale 一致）
    ///   轨道 240×1200px = 2.4×12 世界单位
    ///   判定线 1040×48px = 10.4×0.48 世界单位
    /// </summary>
    public static class SpriteFactory
    {
        public const float PixelsPerUnit = 100f;

        private static readonly Dictionary<SpriteKind, Sprite> Cache = new Dictionary<SpriteKind, Sprite>();

        public static Sprite White => Get(SpriteKind.White);
        public static Sprite Note => Get(SpriteKind.Note);
        public static Sprite Glow => Get(SpriteKind.Glow);
        public static Sprite Ring => Get(SpriteKind.Ring);
        public static Sprite LanePanel => Get(SpriteKind.LanePanel);
        public static Sprite JudgeLine => Get(SpriteKind.JudgeLine);

        public static string ResourcePath(SpriteKind kind) => "Art/" + kind;

        /// <summary>取图：先找图片资源，没有就现画。</summary>
        public static Sprite Get(SpriteKind kind)
        {
            if (Cache.TryGetValue(kind, out Sprite cached) && cached != null) return cached;

            Sprite sprite = Resources.Load<Sprite>(ResourcePath(kind));
            if (sprite == null) sprite = Build(kind);

            Cache[kind] = sprite;
            return sprite;
        }

        /// <summary>清缓存（编辑器里重新导入图片后调用）。</summary>
        public static void ClearCache() => Cache.Clear();

        /// <summary>把贴图包装成 Sprite。</summary>
        public static Sprite Build(SpriteKind kind)
        {
            Texture2D texture = Generate(kind);
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), PixelsPerUnit);
            sprite.name = "Generated_" + kind;
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }

        /// <summary>生成贴图本体（编辑器写 png 时也用它，保证两边像素完全一致）。</summary>
        public static Texture2D Generate(SpriteKind kind)
        {
            switch (kind)
            {
                case SpriteKind.Note: return GenerateNote(200, 50, 16);
                case SpriteKind.Glow: return GenerateGlow(256);
                case SpriteKind.Ring: return GenerateRing(256, 0.62f);
                case SpriteKind.LanePanel: return GenerateLanePanel(240, 1200);
                case SpriteKind.JudgeLine: return GenerateJudgeLine(1040, 48);
                default: return GenerateWhite(4, 4);
            }
        }

        // ================================================================ 各种图形
        private static Texture2D NewTexture(int width, int height)
        {
            return new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "My4KGame_Generated",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0,
            };
        }

        private static Texture2D GenerateWhite(int w, int h)
        {
            var tex = NewTexture(w, h);
            var px = new Color32[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        /// <summary>音符：圆角矩形 + 上下渐变 + 顶部高光。颜色留给 SpriteRenderer.color 上色。</summary>
        private static Texture2D GenerateNote(int w, int h, int radius)
        {
            var tex = NewTexture(w, h);
            var px = new Color32[w * h];

            for (int y = 0; y < h; y++)
            {
                float ty = y / (float)(h - 1);
                float value = Mathf.Lerp(0.74f, 1.0f, ty);         // 底部略暗，有体积感
                if (y >= h - 4) value = 1.0f;                      // 顶部一条亮边

                for (int x = 0; x < w; x++)
                {
                    float mask = RoundedMask(x, y, w, h, radius);
                    // 边缘再压一点，圆角看起来更干脆
                    float edge = Mathf.Clamp01(mask * 1.15f);
                    byte a = (byte)Mathf.RoundToInt(255f * edge);
                    byte c = (byte)Mathf.RoundToInt(255f * value);
                    px[y * w + x] = new Color32(c, c, c, a);
                }
            }

            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        /// <summary>柔光（打击特效的底光）。</summary>
        private static Texture2D GenerateGlow(int size)
        {
            var tex = NewTexture(size, size);
            var px = new Color32[size * size];
            float half = (size - 1) * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - half) / half;
                    float dy = (y - half) / half;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - d);
                    a *= a;
                    px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * a));
                }
            }

            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        /// <summary>圆环（命中时向外扩散的那一圈）。</summary>
        private static Texture2D GenerateRing(int size, float inner)
        {
            var tex = NewTexture(size, size);
            var px = new Color32[size * size];
            float half = (size - 1) * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - half) / half;
                    float dy = (y - half) / half;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = 0f;
                    if (d <= 1f && d >= inner)
                    {
                        float t = (d - inner) / (1f - inner);
                        a = Mathf.Sin(t * Mathf.PI);
                    }
                    px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * a));
                }
            }

            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        /// <summary>轨道面板：左右柔边 + 上下淡出，中间留实色给 SpriteRenderer 上色。</summary>
        private static Texture2D GenerateLanePanel(int w, int h)
        {
            var tex = NewTexture(w, h);
            var px = new Color32[w * h];

            for (int y = 0; y < h; y++)
            {
                float ty = y / (float)(h - 1);
                // 顶部 18% 淡入，底部 10% 淡出
                float vertical = Mathf.Clamp01(ty / 0.18f) * Mathf.Clamp01((1f - ty) / 0.10f);

                for (int x = 0; x < w; x++)
                {
                    float tx = x / (float)(w - 1);
                    float horizontal = Mathf.Clamp01(tx / 0.08f) * Mathf.Clamp01((1f - tx) / 0.08f);
                    float a = vertical * horizontal * 0.85f;
                    px[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * a));
                }
            }

            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        /// <summary>判定线：中间一条最亮的实线，上下带柔光，左右两端淡出。</summary>
        private static Texture2D GenerateJudgeLine(int w, int h)
        {
            var tex = NewTexture(w, h);
            var px = new Color32[w * h];
            float halfH = (h - 1) * 0.5f;

            for (int y = 0; y < h; y++)
            {
                float dy = Mathf.Abs(y - halfH) / halfH;        // 0 中间，1 上下边缘
                float core = Mathf.Clamp01(1f - dy * 2.6f);      // 中间那 38% 是不透明的线
                float glow = Mathf.Clamp01(1f - dy) * 0.55f;     // 外围柔光
                float alpha = Mathf.Clamp01(core + glow);

                for (int x = 0; x < w; x++)
                {
                    float tx = x / (float)(w - 1);
                    float endFade = Mathf.Clamp01(tx / 0.06f) * Mathf.Clamp01((1f - tx) / 0.06f);
                    float a = alpha * endFade;
                    px[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * a));
                }
            }

            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        /// <summary>圆角矩形的覆盖率（边缘带一点抗锯齿）。</summary>
        private static float RoundedMask(int x, int y, int w, int h, int r)
        {
            float cx = Mathf.Min(Mathf.Max(x, r), w - 1 - r);
            float cy = Mathf.Min(Mathf.Max(y, r), h - 1 - r);
            float dx = x - cx;
            float dy = y - cy;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            return Mathf.Clamp01(r - d + 0.5f);
        }
    }
}
