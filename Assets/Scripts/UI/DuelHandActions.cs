using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    public partial class DuelFieldUI
    {
        private const float CardActionWidth = 86f;
        private const float CardActionHeight = 58f;
        private const float CardActionGap = 5f;

        private readonly Dictionary<RectTransform, FieldSlotView> _fieldSlotsByRect = new();
        private readonly Vector3[] _cardActionCorners = new Vector3[4];
        private RectTransform _cardActionsBar;
        private RectTransform _pointerActionAnchor;
        private float _pointerActionLeftAt;
        private RectTransform _cardActionAnchor;
        private RectTransform _cardActionArt;
        private DuelPrompt _cardActionsPrompt;
        private (byte controller, uint location, uint sequence, uint code, int materials) _cardActionKey;
        private bool _navigatingCardActions;

        private void BuildCardActions(Transform root)
        {
            _cardActionsBar = LotdUiKit.CreateRect("CardActions", root, new Vector2(.5f, .5f),
                new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);
            _cardActionsBar.pivot = new Vector2(.5f, 0f);
            _cardActionsBar.gameObject.SetActive(false);
        }

        private bool CanShowCardActions =>
            CanAcceptPlayerAction && !_modals.IsPromptOpen && !IsBrowsingZone && !_modals.ResultRoot.activeSelf &&
            !_actionPopup.activeSelf && !_phasePicker.activeSelf && !_cardZoomModal.activeSelf &&
            CardDragManager.Instance?.IsDragging != true &&
            _session.ActivePrompt.messageType is YgoConstants.MSG_SELECT_IDLECMD or YgoConstants.MSG_SELECT_BATTLECMD;

        private void PointAtCardActions(RectTransform anchor)
        {
            _pointerActionAnchor = anchor;
            _pointerActionLeftAt = float.PositiveInfinity;
            RefreshCardActions();
        }

        private void LeaveCardActions(RectTransform anchor)
        {
            if (_pointerActionAnchor == anchor)
                _pointerActionLeftAt = Time.unscaledTime;
        }

        private bool IsPointerOverCardActions()
        {
            if (!_cardActionsBar.gameObject.activeSelf) return false;
            var canvas = _cardActionsBar.GetComponentInParent<Canvas>();
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            return RectTransformUtility.RectangleContainsScreenPoint(_cardActionsBar, Input.mousePosition, camera);
        }

        private (LiveCardState card, RectTransform art) FindCardActionSource(RectTransform anchor)
        {
            if (anchor == null || !anchor.gameObject.activeInHierarchy) return default;
            if (_fieldSlotsByRect.TryGetValue(anchor, out var slot))
                return slot.cardView.gameObject.activeInHierarchy ? (slot.liveCard, slot.focusArt) : default;

            var hand = _session.Players[_session.HumanPlayerIndex].hand;
            int index = _myHandViews.FindIndex(view => view.Rect == anchor);
            if (index < 0 || index >= hand.Count) return default;
            return (hand[index], (RectTransform)_myHandViews[index].Card.transform);
        }

        private static (byte, uint, uint, uint, int) CardActionKey(LiveCardState card) =>
            (card.controller, card.location, card.sequence, card.code, card.overlayCodes.Count);

        private void RefreshCardActions()
        {
            if (!CanShowCardActions)
            {
                HideCardActions();
                return;
            }

            RectTransform anchor = _duelPadBoardFocus;
            if (!_usingDuelPad)
            {
                bool overActions = IsPointerOverCardActions();
                if (Time.unscaledTime > _pointerActionLeftAt + .22f && !overActions)
                    _pointerActionAnchor = null;
                anchor = _pointerActionAnchor ?? (overActions ? _cardActionAnchor : null);
            }

            var (card, art) = FindCardActionSource(anchor);
            if (card == null)
            {
                HideCardActions();
                return;
            }

            var prompt = _session.ActivePrompt;
            var key = CardActionKey(card);
            if (anchor != _cardActionAnchor || !ReferenceEquals(prompt, _cardActionsPrompt) || _cardActionKey != key)
            {
                var actions = CollectActions(card.controller, card.location, card.sequence);
                if (actions.Count == 0 && card.overlayCodes.Count == 0)
                {
                    HideCardActions();
                    return;
                }

                _cardActionAnchor = anchor;
                _cardActionArt = art;
                _cardActionsPrompt = prompt;
                _cardActionKey = key;
                RebuildCardActions(anchor, card, prompt, actions);
            }

            PositionCardActions(anchor, card.location == YgoConstants.LOCATION_HAND);
            _cardActionsBar.gameObject.SetActive(true);
        }

        private void RebuildCardActions(RectTransform anchor, LiveCardState card, DuelPrompt prompt,
            List<(string label, int responseVal, bool gold)> actions)
        {
            for (int i = _cardActionsBar.childCount - 1; i >= 0; i--)
            {
                var old = _cardActionsBar.GetChild(i);
                old.gameObject.SetActive(false);
                old.SetParent(null, false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(old.gameObject);
                else UnityEngine.Object.DestroyImmediate(old.gameObject);
            }

            var groups = actions.GroupBy(action => action.responseVal & 0xffff).ToArray();
            bool hasMaterials = card.overlayCodes.Count > 0;
            int count = groups.Length + (hasMaterials ? 1 : 0);
            _cardActionsBar.sizeDelta = new Vector2(count * (CardActionWidth + CardActionGap) - CardActionGap,
                CardActionHeight);
            var session = _session;
            var key = CardActionKey(card);
            LiveCardState GetLiveSource()
            {
                if (!ReferenceEquals(_session, session) || !CanShowCardActions || !ReferenceEquals(session.ActivePrompt, prompt) ||
                    anchor != _cardActionAnchor) return null;
                var current = FindCardActionSource(anchor).card;
                return current != null && CardActionKey(current) == key ? current : null;
            }

            for (int i = 0; i < groups.Length; i++)
            {
                var choices = groups[i].ToList();
                var first = choices[0];
                string icon = CommandIcon(first.responseVal);
                string label = icon switch
                {
                    "ACTION" => "효과 발동",
                    "TURN_DEF" => "표시 변경",
                    "SETMONST" or "SET" => "세트",
                    _ => first.label,
                };
                AddCardAction(i, icon, label, () =>
                {
                    var source = GetLiveSource();
                    if (source == null) return;
                    bool canInspect = source.controller == session.HumanPlayerIndex || source.IsFaceUp || source.isPublic;
                    if (canInspect) InspectCard(source.code);
                    if (choices.Count == 1)
                    {
                        _navigatingCardActions = false;
                        ExecuteActionInt(first.responseVal);
                    }
                    else
                    {
                        ShowCardActionsPopup(source.controller, source.location, source.sequence,
                            canInspect ? source.code : 0, null, actions: choices);
                        HideCardActions();
                    }
                });
            }

            if (hasMaterials)
            {
                AddCardAction(groups.Length, "LOOK", $"소재 {card.overlayCodes.Count}", () =>
                {
                    var source = GetLiveSource();
                    if (source == null) return;
                    _modals.ShowCodeListViewer("엑시즈 소재", source.overlayCodes);
                    HideCardActions();
                });
            }
        }

        private void AddCardAction(int index, string iconName, string label, Action action)
        {
            float left = index * (CardActionWidth + CardActionGap);
            var slot = LotdUiKit.CreateRect(iconName, _cardActionsBar, Vector2.zero, Vector2.zero,
                new Vector2(left, 0f), new Vector2(left + CardActionWidth, CardActionHeight));
            bool highlighted = iconName is "ACTION" or "ATTACK";
            var button = DuelStyle.Button(slot, "Button", label, action, 13, gold: highlighted);
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var caption = button.GetComponentInChildren<Text>().rectTransform;
            caption.anchorMin = new Vector2(.04f, .03f);
            caption.anchorMax = new Vector2(.96f, .32f);
            caption.offsetMin = caption.offsetMax = Vector2.zero;
            var icon = LotdUiKit.CreateRect("Icon", button.transform, new Vector2(.28f, .34f),
                new Vector2(.72f, .94f), Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(icon.gameObject, LotdUiKit.LoadSprite($"LOTD/Icons/ICON_ID_CMDID_48_{iconName}"),
                Color.white, true, false);
        }

        private void PositionCardActions(RectTransform anchor, bool inHand)
        {
            var root = (RectTransform)Root.transform;
            anchor.GetWorldCorners(_cardActionCorners);
            float top = float.NegativeInfinity;
            foreach (var corner in _cardActionCorners)
                top = Mathf.Max(top, root.InverseTransformPoint(corner).y);
            float x = root.InverseTransformPoint(anchor.TransformPoint(anchor.rect.center)).x;
            float inset = _cardActionsBar.sizeDelta.x * .5f + 8f;
            float y = Mathf.Min(root.rect.yMax - CardActionHeight - 8f, top + (inHand ? 20f : 6f));
            _cardActionsBar.anchoredPosition = new Vector2(Mathf.Clamp(x, root.rect.xMin + inset, root.rect.xMax - inset), y);
        }

        private void HideCardActions()
        {
            _cardActionsBar.gameObject.SetActive(false);
            _cardActionAnchor = _cardActionArt = null;
            _cardActionsPrompt = null;
            _navigatingCardActions = false;
        }

        private bool EnterCardActions()
        {
            RefreshCardActions();
            if (!_cardActionsBar.gameObject.activeSelf) return false;
            _navigatingCardActions = true;
            return true;
        }
    }
}
