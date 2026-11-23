using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    internal sealed class SecretPackToast : IDisposable
    {
        private readonly RectTransform _root;
        private readonly CanvasGroup _canvas;
        private readonly Text _message;
        private readonly Queue<string> _pending = new();
        private float _age;

        internal SecretPackToast(Transform parent)
        {
            _root = DuelUiKit.CreateRect("SecretPackUnlocked", parent, new Vector2(.225f, .83f), new Vector2(.775f, .895f),
                Vector2.zero, Vector2.zero);
            DuelUiKit.AddImage(_root.gameObject, DuelUiKit.WhiteSprite, new Color(.044f, .152f, .18f, .97f), false, false);
            _canvas = _root.gameObject.AddComponent<CanvasGroup>();
            _canvas.interactable = _canvas.blocksRaycasts = false;
            _message = DuelUiKit.AddFittedText(DuelUiKit.FillRect("Message", _root, 10f), "Label", "", 24, 14,
                TextAnchor.MiddleCenter, DuelUiKit.GoldAccent, FontStyle.Bold);
            _root.gameObject.SetActive(false);
            CollectionProgress.SecretPacksUnlocked += Enqueue;
        }

        private void Enqueue(IReadOnlyList<CollectionPack> packs)
        {
            int displayCount = Math.Min(packs.Count, 3);
            for (int i = 0; i < displayCount; i++)
                _pending.Enqueue($"◆  {packs[i].Name} 해금!");
            if (displayCount < packs.Count)
                _pending.Enqueue($"시크릿 팩 {packs.Count - displayCount}개 추가 해금");
        }

        internal void Update(float delta)
        {
            if (_root.gameObject.activeSelf)
            {
                _age += delta;
                if (_age >= 2.9f) _root.gameObject.SetActive(false);
                else
                {
                    _root.SetAsLastSibling();
                    _canvas.alpha = Mathf.Min(Mathf.Clamp01(_age / .18f), Mathf.Clamp01((2.9f - _age) / .34f));
                }
            }
            if (_root.gameObject.activeSelf || _pending.Count == 0) return;
            _message.text = _pending.Dequeue();
            _age = _canvas.alpha = 0f;
            _root.SetAsLastSibling();
            _root.gameObject.SetActive(true);
        }

        public void Dispose() => CollectionProgress.SecretPacksUnlocked -= Enqueue;
    }
}
