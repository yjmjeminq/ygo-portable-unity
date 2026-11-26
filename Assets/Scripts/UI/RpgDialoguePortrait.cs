using UnityEngine;

namespace YGOPortable.UI
{
    internal static class RpgDialoguePortrait
    {
        internal static Sprite Crop(Texture2D texture, Rect source)
        {
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
            return Sprite.Create(texture, bust, Vector2.one * .5f, 100f, 0, SpriteMeshType.FullRect);
        }
    }
}
