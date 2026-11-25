using UnityEngine;

namespace YGOPortable.UI
{
    internal static class RpgDialoguePortrait
    {
        internal static Sprite Crop(Texture2D texture, Rect source)
        {
            float lowerBody = Mathf.Floor(source.height * .56f);
            var bust = new Rect(source.x, source.y + lowerBody, source.width, source.height - lowerBody);
            return Sprite.Create(texture, bust, Vector2.one * .5f, 100f, 0, SpriteMeshType.FullRect);
        }
    }
}
