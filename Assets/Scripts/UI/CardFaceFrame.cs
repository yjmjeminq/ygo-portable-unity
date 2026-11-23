using System.Collections.Generic;
using UnityEngine;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    internal static class CardFaceFrame
    {
        internal static readonly Rect Artwork = Rect.MinMaxRect(.096f, .279f, .904f, .825f);
        internal static readonly Rect PendulumArtwork = Rect.MinMaxRect(.065f, .263f, .935f, .825f);
        private static readonly Dictionary<string, Sprite> Frames = new();

        internal static Sprite Get(uint type)
        {
            string name = YgoConstants.GetCardFrameName(type);
            if (Frames.TryGetValue(name, out var frame)) return frame;
            bool pendulum = (type & YgoConstants.TYPE_PENDULUM) != 0;
            Color key = name switch
            {
                "card_nomal" or "card_pendulum_n" => new Color32(199, 164, 84, 255),
                "card_mahou" => new Color32(62, 163, 140, 255),
                "card_wana" => new Color32(179, 78, 123, 255),
                "card_gisiki" => new Color32(80, 126, 189, 255),
                "card_yugo" or "card_fusion_pendulum" => new Color32(143, 96, 197, 255),
                "card_sync" or "card_sync_pendulum" => new Color32(207, 218, 224, 255),
                "card_xyz" or "card_xyz_pendulum" => new Color32(61, 68, 88, 255),
                "card_link" => new Color32(51, 117, 180, 255),
                "card_token" => new Color32(146, 155, 155, 255),
                _ => new Color32(185, 112, 67, 255),
            };
            frame = Draw(name, key, pendulum);
            Frames.Add(name, frame);
            return frame;
        }

        private static Sprite Draw(string name, Color theme, bool pendulum)
        {
            const int width = 224, height = 325;
            var pixels = new Color32[width * height];
            Rect art = pendulum ? PendulumArtwork : Artwork;
            var dark = new Color(.041f, .059f, .093f);
            var metal = Color.Lerp(theme, dark, .32f);
            var shine = Color.Lerp(theme, Color.white, .44f);
            var title = Color.Lerp(dark, theme, .22f);
            var paper = Color.Lerp(new Color(.91f, .92f, .905f), theme, .14f);
            var pendulumColor = new Color(.27f, .65f, .58f);

            for (int y = 0; y < height; y++)
            {
                float v = (y + .5f) / height;
                Color face = pendulum ? Color.Lerp(pendulumColor, theme, Mathf.SmoothStep(0f, 1f, v)) : theme;
                Color body = Color.Lerp(face, metal, .16f + .30f * Mathf.Abs(2f * v - 1f));
                for (int x = 0; x < width; x++)
                {
                    float u = (x + .5f) / width;
                    int index = y * width + x;
                    var point = new Vector2(u, v);
                    if (art.Contains(point)) continue;

                    float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
                    if (edge < .014f)
                    {
                        pixels[index] = dark;
                        continue;
                    }
                    if (edge < .029f)
                    {
                        pixels[index] = v + u < .98f ? metal : shine;
                        continue;
                    }

                    Color ink = body;
                    bool inPanels = u is > .071f and < .929f;
                    if (inPanels && v is > .871f and < .964f)
                        ink = v is > .957f or < .878f ? metal : title;
                    else if (inPanels && v is > .048f and < .255f)
                        ink = v is > .249f or < .054f ? metal : paper;
                    else if (u > art.xMin - .012f && u < art.xMax + .012f &&
                        v > art.yMin - .009f && v < art.yMax + .009f)
                        ink = v >= art.yMax ? shine : dark;
                    pixels[index] = ink;
                }
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, width, height), Vector2.one * .5f, 100f, 0, SpriteMeshType.FullRect);
        }
    }
}
