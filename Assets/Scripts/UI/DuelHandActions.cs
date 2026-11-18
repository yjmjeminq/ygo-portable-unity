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
        private const float CardActionGap = 5f;
        private static float CardActionWidth => Application.platform == RuntimePlatform.IPhonePlayer ? 110f : 77f;
        private static float CardActionHeight => Application.platform == RuntimePlatform.IPhonePlayer ? 100f : 73f;

        private readonly Dictionary<RectTransform, FieldSlotView> _fieldSlotsByRect = new();
        private readonly Dictionary<RectTransform, StackTileView> _stackTilesByRect = new();
        private readonly Vector3[] _cardActionCorners = new Vector3[4];
        private RectTransform _cardActionsBar;
        private RectTransform _cardActionsHoverArea;
        private RectTransform _pointerActionAnchor;
        private float _pointerActionLeftAt;
        private RectTransform _touchActionAnchor;
        private DuelPrompt _touchActionPrompt;
        private RectTransform _cardActionAnchor;
        private RectTransform _cardActionArt;
        private DuelPrompt _cardActionsPrompt;
        private (byte controller, uint location, uint sequence, uint code, int materials) _cardActionKey;
        private bool _infoActionShown;
        private bool _navigatingCardActions;

        private void BuildCardActions(Transform root)
        {
            _cardActionsBar = LotdUiKit.CreateRect("CardActions", root, new Vector2(.5f, .5f),
                new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);
            _cardActionsBar.pivot = new Vector2(.5f, 0f);
            _cardActionsHoverArea = LotdUiKit.FillRect("HoverArea", _cardActionsBar);
            LotdUiKit.AddImage(_cardActionsHoverArea.gameObject, LotdUiKit.WhiteSprite, Color.clear);
            _cardActionsBar.gameObject.SetActive(false);
        }

        private bool CanUseCardActions =>
            CanAcceptPlayerAction && !IsStagingPlacement && !_modals.IsPromptOpen && !_modals.ResultRoot.activeSelf &&
            !_actionPopup.activeSelf && !_phasePicker.activeSelf && !_cardZoomModal.activeSelf &&
            CardDragManager.Instance?.IsDragging != true &&
            _session.ActivePrompt.messageType is YgoConstants.MSG_SELECT_IDLECMD or YgoConstants.MSG_SELECT_BATTLECMD;

        private bool CanShowCardActions => !IsBrowsingZone && CanUseCardActions;

        private bool CanOpenTouchCardDetails =>
            _session != null && !_session.IsFinished && !IsPresenting && !IsPaused && !IsReadingLog &&
            !IsStagingPlacement && !_modals.IsPromptOpen && !IsBrowsingZone && !_modals.ResultRoot.activeSelf &&
            !_actionPopup.activeSelf && !_phasePicker.activeSelf && !_cardZoomModal.activeSelf &&
            CardDragManager.Instance?.IsDragging != true;

        private bool CanInspectCardActionSource(LiveCardState card) => card != null && card.code != 0 &&
            (card.controller == _session.HumanPlayerIndex || card.IsFaceUp || card.isPublic);

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

        private void SelectTouchCardActions(RectTransform anchor)
        {
            if (_session == null || anchor == null) return;
            var (card, _) = FindCardActionSource(anchor);
            if (CanOpenTouchCardDetails && CanInspectCardActionSource(card) &&
                (!CanShowCardActions || (card.overlayCodes.Count == 0 &&
                    CollectActions(card.controller, card.location, card.sequence).Count == 0)))
            {
                HideCardActions();
                ShowCardZoomModal(card);
                return;
            }

            _touchActionAnchor = anchor;
            _touchActionPrompt = _session.ActivePrompt;
            _pointerActionAnchor = null;
            RefreshCardActions();
        }

        private void UpdateTouchCardActions()
        {
            if (_touchActionAnchor == null) return;
            for (int i = 0; i < Input.touchCount; i++)
            {
                var touch = Input.GetTouch(i);
                if (touch.phase == TouchPhase.Began) DismissTouchCardActionsAt(touch.position);
            }
        }

        private void DismissTouchCardActionsAt(Vector2 point)
        {
            if (_touchActionAnchor == null) return;
            var camera = CardActionsCamera();
            if (RectTransformUtility.RectangleContainsScreenPoint(_touchActionAnchor, point, camera) ||
                RectTransformUtility.RectangleContainsScreenPoint(_cardActionsHoverArea, point, camera)) return;
            HideCardActions();
        }

        private Camera CardActionsCamera()
        {
            var canvas = _cardActionsBar.GetComponentInParent<Canvas>();
            return canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        }


        private bool IsPointerOverCardActions()
        {
            if (!_cardActionsBar.gameObject.activeSelf) return false;
            return RectTransformUtility.RectangleContainsScreenPoint(_cardActionsHoverArea, Input.mousePosition, CardActionsCamera());
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

            if (_usingDuelPad || !ReferenceEquals(_touchActionPrompt, _session.ActivePrompt))
                _touchActionAnchor = null;

            RectTransform anchor = _duelPadBoardFocus;
            if (!_usingDuelPad)
            {
                bool overActions = IsPointerOverCardActions();
                if (Time.unscaledTime > _pointerActionLeftAt + .22f && !overActions)
                    _pointerActionAnchor = null;
                anchor = _touchActionAnchor ?? _pointerActionAnchor ?? (overActions ? _cardActionAnchor : null);
            }

            if (anchor != null && _stackTilesByRect.TryGetValue(anchor, out var pile))
            {
                RefreshPileActions(pile);
                return;
            }

            var (card, art) = FindCardActionSource(anchor);
            if (card == null)
            {
                HideCardActions();
                return;
            }

            var prompt = _session.ActivePrompt;
            var key = CardActionKey(card);
            bool showInfo = anchor == _touchActionAnchor && CanInspectCardActionSource(card);
            if (anchor != _cardActionAnchor || !ReferenceEquals(prompt, _cardActionsPrompt) ||
                _cardActionKey != key || _infoActionShown != showInfo)
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
                _infoActionShown = showInfo;
                RebuildCardActions(anchor, card, prompt, actions, showInfo);
            }

            PositionCardActions(anchor, card.location == YgoConstants.LOCATION_HAND);
            _cardActionsBar.gameObject.SetActive(true);
        }

        private void RefreshPileActions(StackTileView pile)
        {
            var session = _session;
            if (pile.controller != session.HumanPlayerIndex ||
                pile.location is not (YgoConstants.LOCATION_GRAVE or YgoConstants.LOCATION_REMOVED) ||
                pile == _myGyTile && _graveEffectButton.gameObject.activeInHierarchy)
            {
                HideCardActions();
                return;
            }

            var prompt = session.ActivePrompt;
            if (_cardActionAnchor != pile.root || !ReferenceEquals(_cardActionsPrompt, prompt))
            {
                var actions = CollectZoneActions(pile.controller, pile.location);
                if (actions.Count == 0)
                {
                    HideCardActions();
                    return;
                }

                _cardActionAnchor = pile.root;
                _cardActionArt = null;
                _cardActionsPrompt = prompt;
                ClearCardActionButtons(1);
                bool activations = actions.Any(action => CommandIcon(action.responseVal) == "ACTION");
                AddCardAction(0, "ACTION", activations ? "효과 발동" : "행동 선택", () =>
                {
                    if (!CanShowCardActions || _session != session || !ReferenceEquals(session.ActivePrompt, prompt) ||
                        _cardActionAnchor != pile.root) return;
                    OpenStackZone(pile.controller, pile.location, $"내 {pile.label.text}", actionsOnly: true);
                    HideCardActions();
                });
            }

            PositionCardActions(pile.root, false);
            _cardActionsBar.gameObject.SetActive(true);
        }

        private void ClearCardActionButtons(int count)
        {
            for (int i = _cardActionsBar.childCount - 1; i > 0; i--)
            {
                var old = _cardActionsBar.GetChild(i);
                old.gameObject.SetActive(false);
                old.SetParent(null, false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(old.gameObject);
                else UnityEngine.Object.DestroyImmediate(old.gameObject);
            }

            _cardActionsBar.sizeDelta = new Vector2(count * (CardActionWidth + CardActionGap) - CardActionGap,
                CardActionHeight);
        }

        private void RebuildCardActions(RectTransform anchor, LiveCardState card, DuelPrompt prompt,
            List<(string label, int responseVal, bool gold)> actions, bool showInfo)
        {
            var groups = actions.GroupBy(action => action.responseVal & 0xffff).ToArray();
            bool hasMaterials = card.overlayCodes.Count > 0;
            int infoIndex = groups.Length + (hasMaterials ? 1 : 0);
            ClearCardActionButtons(infoIndex + (showInfo ? 1 : 0));
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
                    if (canInspect) InspectCard(source.code, GetSlot(source.controller, source.location, source.sequence));
                    if (choices.Count == 1)
                    {
                        _navigatingCardActions = false;
                        if (!TryStageCardPlacement(source, first.responseVal))
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
            if (showInfo)
            {
                AddCardAction(infoIndex, "LOOK", "상세 정보", () =>
                {
                    var source = GetLiveSource();
                    if (!CanInspectCardActionSource(source)) return;
                    HideCardActions();
                    ShowCardZoomModal(source);
                }, "Details");
            }
        }

        private void AddCardAction(int index, string iconName, string label, Action action, string name = null)
        {
            float left = index * (CardActionWidth + CardActionGap);
            var slot = LotdUiKit.CreateRect(name ?? iconName, _cardActionsBar, Vector2.zero, Vector2.zero,
                new Vector2(left, 0f), new Vector2(left + CardActionWidth, CardActionHeight));
            bool highlighted = iconName is "ACTION" or "ATTACK" or "SUMMON" or "SUMMONSP";
            var face = LotdUiKit.CreateRect("Circle", slot, new Vector2(.09f, .25f), new Vector2(.91f, 1f),
                Vector2.zero, Vector2.zero);
            var button = DuelStyle.RoundButton(face, "Button", highlighted ? DuelStyle.Gold : DuelStyle.Player, action);
            var caption = LotdUiKit.CreateRect("Caption", slot, Vector2.zero, new Vector2(1f, .27f),
                Vector2.zero, Vector2.zero);
            DuelStyle.Shadow(LotdUiKit.AddFittedText(caption, "Label", label, 14, 9, TextAnchor.MiddleCenter,
                Color.white, FontStyle.Bold), 1.5f);
            var icon = LotdUiKit.CreateRect("Icon", button.transform, new Vector2(.24f, .24f),
                new Vector2(.76f, .76f), Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(icon.gameObject, LotdUiKit.LoadSprite($"LOTD/Icons/ICON_ID_CMDID_48_{iconName}"),
                Color.white, true, false);
        }

        private void PositionCardActions(RectTransform anchor, bool inHand)
        {
            var root = (RectTransform)_cardActionsBar.parent;
            anchor.GetWorldCorners(_cardActionCorners);
            float top = float.NegativeInfinity;
            foreach (var corner in _cardActionCorners)
                top = Mathf.Max(top, root.InverseTransformPoint(corner).y);
            float x = root.InverseTransformPoint(anchor.TransformPoint(anchor.rect.center)).x;
            float inset = _cardActionsBar.sizeDelta.x * .5f + 8f;
            float y = Mathf.Min(root.rect.yMax - CardActionHeight - 8f, top + (inHand ? 20f : 6f));
            _cardActionsBar.anchoredPosition = new Vector2(Mathf.Clamp(x, root.rect.xMin + inset, root.rect.xMax - inset), y);

            // Keep pointer travel from the card to the controls above cards behind the toolbar.
            float topEdge = root.InverseTransformPoint(anchor.TransformPoint(new Vector2(anchor.rect.center.x, anchor.rect.yMax))).y;
            _cardActionsHoverArea.offsetMin = new Vector2(0f, Mathf.Min(0f, topEdge - y));
        }

        private void HideCardActions()
        {
            _cardActionsBar.gameObject.SetActive(false);
            _cardActionAnchor = _cardActionArt = _touchActionAnchor = null;
            _touchActionPrompt = null;
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
