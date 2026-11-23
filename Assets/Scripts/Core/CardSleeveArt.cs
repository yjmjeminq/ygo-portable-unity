using UnityEngine;

namespace YGOPortable.Core
{
    internal static class CardSleeveArt
    {
        internal static Sprite Create()
        {
            const int width = 256, height = 371;
            var pixels = new Color32[width * height];
            var dark = new Color32(11, 18, 30, 255);
            var blue = new Color32(31, 58, 77, 255);
            var jade = new Color32(71, 160, 158, 255);
            var silver = new Color32(161, 225, 213, 255);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float u = (x + .5f - width * .5f) / width;
                float v = (y + .5f - height * .5f) / width;
                float px = Mathf.Abs(u);
                float py = Mathf.Abs(v);
                float edge = Mathf.Min(.5f - px, height * .5f / width - py);
                float cut = .5f + height * .5f / width - .065f - px - py;
                float rim = Mathf.Min(edge, cut);
                float light = Mathf.Clamp01(1f - (u * u + v * v) * 2.4f);
                Color color = Color.Lerp(dark, blue, light * light);
                if (rim is > .018f and < .024f) color = jade;
                if (rim is > .032f and < .036f) color = Color.Lerp(blue, jade, .55f);

                float diamond = px + py * .69f;
                float lattice = Mathf.Abs(Mathf.Repeat((u + v * .69f) * 22f, 1f) - .5f);
                if (rim > .07f && lattice < .035f) color = Color.Lerp(color, jade, .22f);
                if (diamond is > .225f and < .234f) color = jade;
                if (diamond is > .192f and < .197f) color = blue;
                if (diamond < .14f)
                {
                    bool core = diamond < .082f;
                    Color facet = u * v >= 0f ? silver : jade;
                    color = core ? Color.Lerp(jade, facet, .72f) : Color.Lerp(blue, facet, .39f);
                }
                pixels[y * width + x] = rim > 0f ? color : dark;
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "Faceted jade sleeve",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, width, height), Vector2.one * .5f, 100f);
        }
    }
}
