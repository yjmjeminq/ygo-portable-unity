using UnityEngine;

namespace YGOPortable.UI
{
    internal static class RpgDialoguePortrait
    {
        private const float UpperBodyAspect = 1.38f;
        private const int ExpressionWidth = 448, ExpressionHeight = 576;

        internal static Sprite Crop(Texture2D texture, Rect source, string standing = null)
        {
            // Keep the authored padding. It fixes the head position across expression changes.
            if (source.height / source.width < UpperBodyAspect) return Create(texture, source);

            if (texture.isReadable && texture.width == 1024 && texture.height == 1536 &&
                source.width == texture.width && source.height == texture.height &&
                ExpressionFrame(standing) is { width: > 0 } frame)
                return FrameStanding(texture, frame, standing == "full_1" ? 0 : 19);

            var bust = new Rect(source.x, source.y + Mathf.Floor(source.height * .56f), source.width, 0f);
            bust.yMax = source.yMax;
            if (texture.isReadable)
            {
                var pixels = texture.GetPixels32();
                int left = (int)source.xMin, right = (int)source.xMax;
                int bottom = (int)source.yMin, top = (int)source.yMax;
                int firstRow = top, lastRow = bottom - 1;
                for (int y = bottom; y < top; y++)
                for (int x = left; x < right; x++)
                {
                    if (pixels[y * texture.width + x].a < 40) continue;
                    firstRow = Mathf.Min(firstRow, y);
                    lastRow = y;
                    break;
                }

                if (lastRow >= firstRow)
                {
                    int waist = firstRow + Mathf.FloorToInt((lastRow - firstRow) * .54f);
                    int firstColumn = right, lastColumn = left - 1;
                    for (int y = waist; y <= lastRow; y++)
                    for (int x = left; x < right; x++)
                    {
                        if (pixels[y * texture.width + x].a < 40) continue;
                        firstColumn = Mathf.Min(firstColumn, x);
                        lastColumn = Mathf.Max(lastColumn, x);
                    }
                    int padding = Mathf.CeilToInt((lastRow - waist) * .035f);
                    bust = Rect.MinMaxRect(Mathf.Max(left, firstColumn - padding), waist,
                        Mathf.Min(right, lastColumn + padding + 1), Mathf.Min(top, lastRow + padding + 1));
                }
            }
            return Create(texture, bust);
        }

        // Source x, top and width align the full standings with their authored 448 x 576 expression frames.
        // A negative top retains the same transparent headroom in both sprites.
        private static Rect ExpressionFrame(string standing)
        {
            (int x, int top, int width) = standing switch
            {
                "full_1" => (228, -133, 704),
                "full_10" => (212, -79, 628),
                "full_16" => (175, -115, 627),
                "full_19" => (221, -121, 638),
                "full_21" => (228, -63, 664),
                "full_24" => (213, -65, 658),
                "full_32" => (212, -77, 608),
                "full_35" => (148, -45, 649),
                "full_220" => (195, -165, 665),
                "full_230" => (172, -97, 660),
                "full_3200" => (244, -97, 609),
                "full_3204" => (216, -63, 649),
                "full_protagonist" => (218, -16, 557),
                _ => default,
            };
            return new Rect(x, top, width, width * (ExpressionHeight / (float)ExpressionWidth));
        }

        private static Sprite FrameStanding(Texture2D source, Rect frame, int bottomPadding)
        {
            var colors = new Color32[ExpressionWidth * ExpressionHeight];
            for (int y = bottomPadding; y < ExpressionHeight; y++)
            {
                float sourceY = 1f - (frame.y + (ExpressionHeight - y - .5f) * frame.height / ExpressionHeight) / source.height;
                if (sourceY < 0f || sourceY > 1f) continue;
                for (int x = 0; x < ExpressionWidth; x++)
                {
                    float sourceX = (frame.x + (x + .5f) * frame.width / ExpressionWidth) / source.width;
                    if (sourceX >= 0f && sourceX <= 1f)
                        colors[y * ExpressionWidth + x] = source.GetPixelBilinear(sourceX, sourceY);
                }
            }
            var texture = new Texture2D(ExpressionWidth, ExpressionHeight, TextureFormat.RGBA32, false);
            texture.SetPixels32(colors);
            texture.Apply(false, true);
            return Create(texture, new Rect(0, 0, ExpressionWidth, ExpressionHeight));
        }

        private static Sprite Create(Texture2D texture, Rect rect) =>
            Sprite.Create(texture, rect, Vector2.one * .5f, 100f, 0, SpriteMeshType.FullRect);
    }
}
