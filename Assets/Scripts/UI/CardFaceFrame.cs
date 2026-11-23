using System.Collections.Generic;
using UnityEngine;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    internal static class CardFaceFrame
    {
        internal static readonly Rect Artwork = Rect.MinMaxRect(.073f, .244f, .927f, .832f);
        internal static readonly Rect PendulumArtwork = Rect.MinMaxRect(.048f, .231f, .952f, .832f);
        internal static readonly Rect Title = Rect.MinMaxRect(.064f, .891f, .936f, .964f);
        internal static readonly Rect ReadingPanel = Rect.MinMaxRect(.064f, .045f, .936f, .228f);
        private static readonly Dictionary<(string Name, bool Pendulum), Sprite> Frames = new();

        internal static Sprite Get(uint type)
        {
            string name = YgoConstants.GetCardFrameName(type);
            bool pendulum = (type & YgoConstants.TYPE_PENDULUM) != 0;
            var frameKey = (name, pendulum);
            if (Frames.TryGetValue(frameKey, out var frame)) return frame;
            Color key = name switch
            {
                "card_nomal" or "card_pendulum_n" => new Color32(211, 177, 97, 255),
                "card_mahou" => new Color32(49, 163, 130, 255),
                "card_wana" => new Color32(191, 80, 132, 255),
                "card_gisiki" => new Color32(90, 115, 203, 255),
                "card_yugo" or "card_fusion_pendulum" => new Color32(153, 91, 195, 255),
                "card_sync" or "card_sync_pendulum" => new Color32(218, 231, 232, 255),
                "card_xyz" or "card_xyz_pendulum" => new Color32(63, 68, 92, 255),
                "card_link" => new Color32(45, 133, 184, 255),
                "card_token" => new Color32(154, 161, 155, 255),
                _ => new Color32(203, 121, 73, 255),
            };
            frame = Draw(name, key, pendulum);
            Frames.Add(frameKey, frame);
            return frame;
        }

        private static Sprite Draw(string name, Color theme, bool pendulum)
        {
            const int width = 320, height = 464;
            var pixels = new Color32[width * height];
            Rect art = pendulum ? PendulumArtwork : Artwork;
            Rect artRim = Rect.MinMaxRect(art.xMin - .012f, art.yMin - .008f, art.xMax + .012f, art.yMax + .008f);
            var dark = new Color(.031f, .043f, .066f);
            var title = Color.Lerp(dark, theme, .15f);
            var paper = Color.Lerp(new Color(.95f, .959f, .964f), theme, .084f);
            var pendulumColor = new Color(.22f, .65f, .53f);

            for (int y = 0; y < height; y++)
            {
                float v = (y + .5f) / height;
                Color face = pendulum ? Color.Lerp(pendulumColor, theme, Mathf.SmoothStep(.12f, .84f, v)) : theme;
                Color muted = Color.Lerp(face, dark, .62f);
                Color bright = Color.Lerp(face, Color.white, .40f);
                for (int x = 0; x < width; x++)
                {
                    float u = (x + .5f) / width;
                    int index = y * width + x;
                    var point = new Vector2(u, v);
                    if (art.Contains(point)) continue;

                    float dx = Mathf.Min(u, 1f - u);
                    float dy = Mathf.Min(v, 1f - v);
                    float edge = Mathf.Min(Mathf.Min(dx, dy * 1.45f), (dx + dy * 1.45f - .020f) * .68f);
                    if (edge < 0f) continue;

                    float slant = (u * .55f + v) * 3.4f;
                    float reflection = Mathf.SmoothStep(0f, 1f, 1f - Mathf.Abs(Mathf.Repeat(slant, 1f) - .5f) * 2f);
                    Color ink = Color.Lerp(face, muted, .23f + reflection * .27f);
                    if (edge < .014f) ink = dark;
                    else if (edge < .024f) ink = Color.Lerp(muted, bright, .38f + reflection * .61f);
                    else if (Title.Contains(point)) ink = v < Title.yMin + .005f ? bright : title;
                    else if (ReadingPanel.Contains(point)) ink = v < ReadingPanel.yMin + .005f ? muted : paper;
                    else if (artRim.Contains(point)) ink = v >= art.yMax ? bright : dark;
                    else if (v > artRim.yMax && v < Title.yMin && u > Title.xMin && u < Title.xMax)
                        ink = Color.Lerp(muted, dark, .40f);
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
