using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    public enum DragSourceType
    {
        None,
        Trunk,
        DeckMain,
        DeckExtra,
        DeckSide,
        Hand,
        FieldMonster,
    }

    public class DragContext
    {
        public DragSourceType SourceType { get; set; }
        public uint CardCode { get; set; }
        public int SourceIndex { get; set; }
        public object CustomData { get; set; }
        public Vector2 DropPosition { get; set; }
    }

    public class CardDragManager : MonoBehaviour
    {
        public static CardDragManager Instance { get; private set; }

        public bool IsDragging { get; private set; }
        public DragSourceType CurrentSource { get; private set; }
        public uint CurrentCode { get; private set; }
        public int CurrentIndex { get; private set; }
        public object CurrentData { get; private set; }

        public event Action<DragContext> OnDragDropped;
        public event Action OnDragCancelled;

        private RectTransform _ghostRoot;
        private CanvasGroup _ghostCanvasGroup;
        private CompositeCardView _ghostCardView;
        private Outline _ghostOutline;

        public static CardDragManager Ensure(Canvas canvas)
        {
            if (Instance != null) return Instance;
            var go = new GameObject("CardDragManager", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            Instance = go.AddComponent<CardDragManager>();
            Instance.Initialize(rt);
            return Instance;
        }

        private void Initialize(RectTransform root)
        {
            var ghostGo = new GameObject("GhostCard", typeof(RectTransform));
            ghostGo.transform.SetParent(root, false);
            _ghostRoot = (RectTransform)ghostGo.transform;
            _ghostRoot.sizeDelta = new Vector2(100f, 145f);
            _ghostRoot.pivot = new Vector2(0.5f, 0.5f);

            _ghostCanvasGroup = ghostGo.AddComponent<CanvasGroup>();
            _ghostCanvasGroup.blocksRaycasts = false;
            _ghostCanvasGroup.interactable = false;
            _ghostCanvasGroup.alpha = 0.90f;

            _ghostOutline = ghostGo.AddComponent<Outline>();
            _ghostOutline.effectColor = LotdUiKit.CyanAccent;
            _ghostOutline.effectDistance = new Vector2(3f, -3f);

            _ghostCardView = CompositeCardView.Create(_ghostRoot, "CV", false);
            ghostGo.SetActive(false);
        }

        public void StartDrag(DragSourceType source, uint cardCode, int index = -1, object customData = null)
        {
            if (cardCode == 0) return;
            IsDragging = true;
            CurrentSource = source;
            CurrentCode = cardCode;
            CurrentIndex = index;
            CurrentData = customData;

            _ghostRoot.gameObject.SetActive(true);
            _ghostRoot.SetAsLastSibling();
            _ghostCardView.SetCard(cardCode);
            UpdateGhostPosition(Input.mousePosition);

            _ghostOutline.effectColor = source == DragSourceType.FieldMonster ? LotdUiKit.RedAccent : LotdUiKit.CyanAccent;
            LotdAudio.PlayClick();
        }

        public void UpdateGhostPosition(Vector2 screenPos)
        {
            if (_ghostRoot == null) return;
            _ghostRoot.position = screenPos;
        }

        public void EndDrag(Vector2 screenPos)
        {
            if (!IsDragging) return;
            var ctx = new DragContext
            {
                SourceType = CurrentSource,
                CardCode = CurrentCode,
                SourceIndex = CurrentIndex,
                CustomData = CurrentData,
                DropPosition = screenPos,
            };

            IsDragging = false;
            CurrentSource = DragSourceType.None;
            CurrentCode = 0;
            CurrentIndex = -1;
            CurrentData = null;
            if (_ghostRoot != null) _ghostRoot.gameObject.SetActive(false);

            OnDragDropped?.Invoke(ctx);
        }

        public void CancelDrag()
        {
            if (!IsDragging) return;
            IsDragging = false;
            CurrentSource = DragSourceType.None;
            CurrentCode = 0;
            CurrentIndex = -1;
            CurrentData = null;
            if (_ghostRoot != null) _ghostRoot.gameObject.SetActive(false);
            OnDragCancelled?.Invoke();
        }

        private void Update()
        {
            if (!IsDragging) return;

            UpdateGhostPosition(Input.mousePosition);

            if (Input.GetMouseButtonUp(0))
            {
                EndDrag(Input.mousePosition);
            }
            else if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape))
            {
                CancelDrag();
            }
        }

        public static bool ContainsScreenPoint(RectTransform rt, Vector2 screenPoint)
        {
            if (rt == null || !rt.gameObject.activeInHierarchy) return false;
            return RectTransformUtility.RectangleContainsScreenPoint(rt, screenPoint, null);
        }
    }

    public class CardDragTrigger : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        public DragSourceType SourceType = DragSourceType.None;
        public Func<uint> CardCodeGetter;
        public Func<int> IndexGetter;
        public Func<object> DataGetter;
        public Action OnLeftClick;
        public Action OnRightClick;

        private bool _isDraggingThis;

        public void OnPointerDown(PointerEventData eventData)
        {
            // Allow drag detection
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            uint code = CardCodeGetter != null ? CardCodeGetter() : 0;
            if (code == 0) return;

            int idx = IndexGetter != null ? IndexGetter() : -1;
            object data = DataGetter != null ? DataGetter() : null;

            _isDraggingThis = true;
            CardDragManager.Instance?.StartDrag(SourceType, code, idx, data);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_isDraggingThis && CardDragManager.Instance != null)
            {
                CardDragManager.Instance.UpdateGhostPosition(eventData.position);
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (_isDraggingThis)
            {
                _isDraggingThis = false;
                CardDragManager.Instance?.EndDrag(eventData.position);
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_isDraggingThis) return;
            if (eventData.button == PointerEventData.InputButton.Right)
            {
                OnRightClick?.Invoke();
            }
            else if (eventData.button == PointerEventData.InputButton.Left)
            {
                OnLeftClick?.Invoke();
            }
        }
    }
}
