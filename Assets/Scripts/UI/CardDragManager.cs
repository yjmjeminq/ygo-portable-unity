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
        ExtraDeck,
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
        private static CardDragManager _instance;
        public static CardDragManager Instance => _instance != null ? _instance : null;

        public bool IsDragging { get; private set; }
        internal Vector2 PointerPosition { get; private set; }
        public DragSourceType CurrentSource { get; private set; }
        public uint CurrentCode { get; private set; }
        public int CurrentIndex { get; private set; }
        public object CurrentData { get; set; }

        public event Action<DragSourceType, uint, int, object> OnDragStarted;
        public event Action<DragContext> OnDragDropped;
        public event Action OnDragCancelled;

        private RectTransform _ghostRoot;
        private CanvasGroup _ghostCanvasGroup;
        private CompositeCardView _ghostCardView;
        private Outline _ghostOutline;
        private int _dragPointerId;

        public static CardDragManager Ensure(Canvas canvas)
        {
            if (Instance != null)
            {
                Instance.transform.SetAsLastSibling();
                return Instance;
            }
            var go = new GameObject("CardDragManager", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            _instance = go.AddComponent<CardDragManager>();
            _instance.Initialize(rt);
            _instance.transform.SetAsLastSibling();
            return _instance;
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(_instance, this)) _instance = null;
        }

        private void Initialize(RectTransform root)
        {
            var ghostGo = new GameObject("GhostCard", typeof(RectTransform));
            ghostGo.transform.SetParent(root, false);
            _ghostRoot = (RectTransform)ghostGo.transform;
            const float cardWidth = 126f;
            _ghostRoot.sizeDelta = new Vector2(cardWidth, cardWidth / DuelUiKit.CardAspectRatio);
            _ghostRoot.pivot = new Vector2(0.5f, 0.5f);

            _ghostCanvasGroup = ghostGo.AddComponent<CanvasGroup>();
            _ghostCanvasGroup.blocksRaycasts = false;
            _ghostCanvasGroup.interactable = false;
            _ghostCanvasGroup.alpha = 0.94f;

            _ghostOutline = ghostGo.AddComponent<Outline>();
            _ghostOutline.effectColor = DuelUiKit.CyanAccent;
            _ghostOutline.effectDistance = new Vector2(3f, -3f);

            _ghostCardView = CompositeCardView.Create(_ghostRoot, "CV", false);
            ghostGo.SetActive(false);
        }

        public void StartDrag(DragSourceType source, uint cardCode, int index = -1, object customData = null,
            int pointerId = -1, Vector2? screenPosition = null, DeckCardAppearance? appearance = null)
        {
            if (cardCode == 0) return;
            _dragPointerId = pointerId;
            IsDragging = true;
            CurrentSource = source;
            CurrentCode = cardCode;
            CurrentIndex = index;
            CurrentData = customData;

            transform.SetAsLastSibling();
            _ghostRoot.gameObject.SetActive(true);
            _ghostRoot.SetAsLastSibling();
            _ghostCardView.UsePrintedFaceOnly(source == DragSourceType.ExtraDeck);
            _ghostCardView.SetDeckCard(cardCode, appearance);
            UpdateGhostPosition(screenPosition ?? (Vector2)Input.mousePosition);

            _ghostOutline.effectColor = source == DragSourceType.FieldMonster ? DuelUiKit.RedAccent
                : (source == DragSourceType.Hand || source == DragSourceType.ExtraDeck ? DuelUiKit.GoldAccent : DuelUiKit.CyanAccent);
            DuelAudio.PlayClick();
            OnDragStarted?.Invoke(source, cardCode, index, customData);
        }

        public void UpdateGhostPosition(Vector2 screenPos)
        {
            PointerPosition = screenPos;
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
            if (_dragPointerId >= 0)
            {
                for (int i = 0; i < Input.touchCount; i++)
                {
                    var touch = Input.GetTouch(i);
                    if (touch.fingerId != _dragPointerId) continue;
                    if (touch.phase == TouchPhase.Canceled) CancelDrag();
                    else if (touch.phase == TouchPhase.Ended) EndDrag(touch.position);
                    return;
                }
                CancelDrag();
                return;
            }

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
            if (rt.TryGetComponent<DuelZoneImage>(out var zone)) return zone.IsRaycastLocationValid(screenPoint, null);
            return RectTransformUtility.RectangleContainsScreenPoint(rt, screenPoint, null);
        }
    }

    public class CardDragTrigger : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler,
        IPointerUpHandler
    {
        public DragSourceType SourceType = DragSourceType.None;
        public Func<uint> CardCodeGetter;
        public Func<int> IndexGetter;
        public Func<object> DataGetter;
        public Action OnLeftClick;
        public Action OnTouchClick;
        public Action OnTouchHold;
        public Action OnRightClick;
        public Action OnPointerEnterAction;
        public Action OnPointerExitAction;
        public bool EnableHoverLift;
        internal bool PreferTouchScroll;

        private bool _isDraggingThis;
        private bool _hovered;
        private int _restingSibling;
        private CanvasGroup _cg;
        private ScrollRect _parentScroll;
        private const float TouchHoldSeconds = .46f;
        private int _touchPressId = -1;
        private int _consumedTouchId = -1;
        private uint _touchPressCode;
        private Vector2 _touchPressPoint;
        private float _touchPressAt;

        private bool HandlesTouchTap(PointerEventData data) => data.pointerId >= 0 && OnTouchClick != null;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!_isDraggingThis && !HandlesTouchTap(eventData))
            {
                if (EnableHoverLift)
                {
                    if (!_hovered) _restingSibling = transform.GetSiblingIndex();
                    _hovered = true;
                    transform.SetAsLastSibling();
                }
                OnPointerEnterAction?.Invoke();
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (eventData.pointerId == _touchPressId) _touchPressId = -1;
            if (!_isDraggingThis && !HandlesTouchTap(eventData))
            {
                EndHover();
                OnPointerExitAction?.Invoke();
            }
        }

        private void Update()
        {
            if (_touchPressId >= 0)
            {
                for (int i = 0; i < Input.touchCount; i++)
                {
                    var touch = Input.GetTouch(i);
                    if (touch.fingerId != _touchPressId) continue;
                    if (touch.phase == TouchPhase.Canceled) _touchPressId = -1;
                    else TryCompleteTouchHold(touch.fingerId, touch.position, Time.unscaledTime);
                    break;
                }
            }
            if (!EnableHoverLift || _isDraggingThis || !(transform is RectTransform rect)) return;
            float lift = _hovered ? 25f : 0f;
            float smoothing = 1f - Mathf.Exp(-24f * Time.unscaledDeltaTime);
            Vector2 offset = Vector2.Lerp(rect.offsetMin, Vector2.up * lift, smoothing);
            if (Mathf.Abs(lift - offset.y) < .04f) offset = Vector2.up * lift;
            if (rect.offsetMin != offset) rect.offsetMin = offset;
            if (rect.offsetMax != offset) rect.offsetMax = offset;
            Vector3 target = Vector3.one * (_hovered ? 1.105f : 1f);
            var scale = Vector3.Lerp(rect.localScale, target, smoothing);
            if ((target - scale).sqrMagnitude < .000001f) scale = target;
            if (rect.localScale != scale) rect.localScale = scale;
        }

        private void EndHover()
        {
            if (!_hovered) return;
            _hovered = false;
            transform.SetSiblingIndex(_restingSibling);
        }

        private void ResetVisualState()
        {
            if (EnableHoverLift)
            {
                EndHover();
                var rt = transform as RectTransform;
                if (rt != null)
                {
                    rt.offsetMin = Vector2.zero;
                    rt.offsetMax = Vector2.zero;
                    rt.localScale = Vector3.one;
                }
            }
            if (_cg != null) _cg.alpha = 1f;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.pointerId < 0 || eventData.button != PointerEventData.InputButton.Left) return;
            if (_consumedTouchId == eventData.pointerId) _consumedTouchId = -1;
            if (OnTouchHold == null || CardCodeGetter == null || _touchPressId >= 0) return;
            _touchPressCode = CardCodeGetter();
            if (_touchPressCode == 0) return;
            _touchPressId = eventData.pointerId;
            _touchPressPoint = eventData.position;
            _touchPressAt = Time.unscaledTime;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId == _touchPressId) _touchPressId = -1;
        }

        internal bool TryCompleteTouchHold(int finger, Vector2 position, float now)
        {
            if (_touchPressId < 0 || finger != _touchPressId) return false;
            float threshold = Mathf.Max(EventSystem.current?.pixelDragThreshold ?? 10, Screen.dpi * .035f);
            if ((position - _touchPressPoint).sqrMagnitude > threshold * threshold ||
                CardCodeGetter?.Invoke() != _touchPressCode || OnTouchHold == null ||
                CardDragManager.Instance?.IsDragging == true)
            {
                _touchPressId = -1;
                return false;
            }
            if (now - _touchPressAt < TouchHoldSeconds) return false;
            _touchPressId = -1;
            _consumedTouchId = finger;
            OnTouchHold.Invoke();
            return true;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.pointerId == _touchPressId) _touchPressId = -1;
            _parentScroll = null;
            if (eventData.pointerId >= 0 && eventData.pointerId == _consumedTouchId) return;
            bool canDragCard = SourceType != DragSourceType.None && CardCodeGetter != null;
            if (!canDragCard || eventData.pointerId >= 0)
            {
                var scroll = GetComponentInParent<ScrollRect>();
                if (!canDragCard || PreferTouchScroll || ShouldScrollOnTouch(scroll, eventData.position - eventData.pressPosition))
                {
                    _parentScroll = scroll;
                    _parentScroll?.OnBeginDrag(eventData);
                    return;
                }
            }
            if (eventData.button != PointerEventData.InputButton.Left || CardDragManager.Instance?.IsDragging == true) return;
            uint code = CardCodeGetter();
            if (code == 0) return;

            int idx = IndexGetter != null ? IndexGetter() : -1;
            object data = DataGetter != null ? DataGetter() : null;

            _isDraggingThis = true;
            _cg = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
            _cg.alpha = 0.35f;

            var card = GetComponentInChildren<CompositeCardView>();
            CardDragManager.Instance?.StartDrag(SourceType, code, idx, data, eventData.pointerId, eventData.position,
                card?.CurrentCode == code ? card.Appearance : null);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_parentScroll != null)
            {
                _parentScroll.OnDrag(eventData);
                return;
            }
            if (_isDraggingThis && CardDragManager.Instance != null)
            {
                CardDragManager.Instance.UpdateGhostPosition(eventData.position);
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (_parentScroll != null)
            {
                _parentScroll.OnEndDrag(eventData);
                _parentScroll = null;
                return;
            }
            if (_isDraggingThis)
            {
                _isDraggingThis = false;
                ResetVisualState();
                // The drag manager checks the owning touch for cancellation and release.
                if (eventData.pointerId < 0) CardDragManager.Instance?.EndDrag(eventData.position);
            }
        }

        private static bool ShouldScrollOnTouch(ScrollRect scroll, Vector2 movement)
        {
            if (scroll == null || !scroll.isActiveAndEnabled || scroll.content == null) return false;
            var viewport = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
            return Mathf.Abs(movement.y) >= Mathf.Abs(movement.x)
                ? scroll.vertical && scroll.content.rect.height > viewport.rect.height
                : scroll.horizontal && scroll.content.rect.width > viewport.rect.width;
        }

        private void OnDisable()
        {
            _touchPressId = -1;
            _consumedTouchId = -1;
            _parentScroll = null;
            if (_isDraggingThis || EnableHoverLift)
            {
                _isDraggingThis = false;
                ResetVisualState();
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_isDraggingThis || _parentScroll != null) return;
            if (eventData.pointerId >= 0 && eventData.pointerId == _consumedTouchId)
            {
                _consumedTouchId = -1;
                return;
            }
            if (eventData.button == PointerEventData.InputButton.Right)
            {
                OnRightClick?.Invoke();
            }
            else if (eventData.button == PointerEventData.InputButton.Left)
            {
                (HandlesTouchTap(eventData) ? OnTouchClick : OnLeftClick)?.Invoke();
            }
        }
    }
}
