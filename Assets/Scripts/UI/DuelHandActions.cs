using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    public partial class DuelFieldUI
    {
        private const float HandActionWidth = 92f;
        private const float HandActionHeight = 62f;
        private const float HandActionGap = 5f;
        private const float HandActionPointerGrace = .22f;

        private RectTransform _handActionsBar;
        private HandCardView _pointerHandActionCard;
        private float _pointerHandActionLeftAt;
        private HandCardView _handActionCard;
        private DuelPrompt _handActionsPrompt;
        private uint _handActionsCode;
        private uint _handActionsSequence;
        private bool _navigatingHandActions;
        private readonly Vector3[] _handActionCorners = new Vector3[4];

        private void BuildHandActions(Transform root)
        {
            _handActionsBar = LotdUiKit.CreateRect("HandActions", root, new Vector2(.5f, .5f),
                new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);
            _handActionsBar.pivot = new Vector2(.5f, 0f);
            _handActionsBar.gameObject.SetActive(false);
        }

        private bool CanShowHandActions =>
            CanAcceptPlayerAction && !_modals.IsPromptOpen && !IsBrowsingZone && !_modals.ResultRoot.activeSelf &&
            !_actionPopup.activeSelf && !_phasePicker.activeSelf && !_cardZoomModal.activeSelf &&
            CardDragManager.Instance?.IsDragging != true &&
            _session.ActivePrompt.messageType is YgoConstants.MSG_SELECT_IDLECMD or YgoConstants.MSG_SELECT_BATTLECMD;

        private void PointAtHandActions(HandCardView view)
        {
            _pointerHandActionCard = view;
            _pointerHandActionLeftAt = float.PositiveInfinity;
            RefreshHandActions();
        }

        private void LeaveHandActions(HandCardView view)
        {
            if (_pointerHandActionCard == view)
                _pointerHandActionLeftAt = Time.unscaledTime;
        }

        private bool IsPointerOverHandActions()
        {
            if (!_handActionsBar.gameObject.activeSelf) return false;
            var canvas = _handActionsBar.GetComponentInParent<Canvas>();
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            return RectTransformUtility.RectangleContainsScreenPoint(_handActionsBar, Input.mousePosition, camera);
        }

        private void RefreshHandActions()
        {
            if (!CanShowHandActions)
            {
                HideHandActions();
                return;
            }

            HandCardView view;
            if (_usingDuelPad)
            {
                view = _myHandViews.Find(card => card.Rect == _duelPadBoardFocus);
            }
            else
            {
                if (Time.unscaledTime > _pointerHandActionLeftAt + HandActionPointerGrace && !IsPointerOverHandActions())
                    _pointerHandActionCard = null;
                view = _pointerHandActionCard ?? (IsPointerOverHandActions() ? _handActionCard : null);
            }

            var hand = _session.Players[_session.HumanPlayerIndex].hand;
            int index = view == null ? -1 : _myHandViews.IndexOf(view);
            if (index < 0 || index >= hand.Count || hand[index] == null || !view.Rect.gameObject.activeInHierarchy)
            {
                HideHandActions();
                return;
            }

            var card = hand[index];
            var prompt = _session.ActivePrompt;
            if (view != _handActionCard || !ReferenceEquals(prompt, _handActionsPrompt) ||
                _handActionsCode != card.code || _handActionsSequence != card.sequence)
            {
                var actions = CollectActions((byte)_session.HumanPlayerIndex, YgoConstants.LOCATION_HAND, card.sequence);
                if (actions.Count == 0)
                {
                    HideHandActions();
                    return;
                }

                _handActionCard = view;
                _handActionsPrompt = prompt;
                _handActionsCode = card.code;
                _handActionsSequence = card.sequence;
                RebuildHandActions(view, card, prompt, actions);
            }

            PositionHandActions(view);
            _handActionsBar.gameObject.SetActive(true);
        }

        private void RebuildHandActions(HandCardView view, LiveCardState card, DuelPrompt prompt,
            List<(string label, int responseVal, bool gold)> actions)
        {
            for (int i = _handActionsBar.childCount - 1; i >= 0; i--)
            {
                var old = _handActionsBar.GetChild(i);
                old.gameObject.SetActive(false);
                old.SetParent(null, false);
                if (Application.isPlaying) Object.Destroy(old.gameObject);
                else Object.DestroyImmediate(old.gameObject);
            }

            var groups = actions.GroupBy(action => action.responseVal & 0xffff).ToArray();
            _handActionsBar.sizeDelta = new Vector2(groups.Length * (HandActionWidth + HandActionGap) - HandActionGap,
                HandActionHeight);
            var session = _session;
            byte controller = (byte)session.HumanPlayerIndex;
            uint sequence = card.sequence;
            uint code = card.code;
            for (int i = 0; i < groups.Length; i++)
            {
                var choices = groups[i].ToList();
                var first = choices[0];
                string iconName = CommandIcon(first.responseVal);
                                string label = iconName switch
                {
                    "ACTION" => "효과 발동",
                    "SETMONST" or "SET" => "세트",
                    _ => first.label,
                };
                float left = i * (HandActionWidth + HandActionGap);
                var slot = LotdUiKit.CreateRect(iconName, _handActionsBar, Vector2.zero, Vector2.zero,
                    new Vector2(left, 0f), new Vector2(left + HandActionWidth, HandActionHeight));
                var button = DuelStyle.Button(slot, "Button", label, () =>
                {
                    var hand = session.Players[controller].hand;
                    int index = _myHandViews.IndexOf(view);
                    if (!CanShowHandActions || !ReferenceEquals(_session, session) ||
                        !ReferenceEquals(session.ActivePrompt, prompt) || view != _handActionCard ||
                        index < 0 || index >= hand.Count || hand[index]?.code != code || hand[index].sequence != sequence)
                        return;

                    InspectCard(code);
                    if (choices.Count == 1)
                    {
                        _navigatingHandActions = false;
                        ExecuteActionInt(first.responseVal);
                    }
                    else
                    {
                        ShowCardActionsPopup(controller, YgoConstants.LOCATION_HAND, sequence, code, null, actions: choices);
                        HideHandActions();
                    }
                }, 13, gold: iconName == "ACTION");
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
        }

        private void PositionHandActions(HandCardView view)
        {
            var root = (RectTransform)Root.transform;
            var bounds = view.Rect.rect;
            view.Rect.GetWorldCorners(_handActionCorners);
            float top = float.NegativeInfinity;
            foreach (var corner in _handActionCorners)
                top = Mathf.Max(top, root.InverseTransformPoint(corner).y);
            float x = root.InverseTransformPoint(view.Rect.TransformPoint(bounds.center)).x;
            float inset = _handActionsBar.sizeDelta.x * .5f + 8f;
            _handActionsBar.anchoredPosition = new Vector2(Mathf.Clamp(x, root.rect.xMin + inset, root.rect.xMax - inset),
                top + 20f);
        }

        private void HideHandActions()
        {
            _handActionsBar.gameObject.SetActive(false);
            _handActionCard = null;
            _handActionsPrompt = null;
            _navigatingHandActions = false;
        }

        private void EnterHandActions()
        {
            RefreshHandActions();
            if (!_handActionsBar.gameObject.activeSelf) return;
            _navigatingHandActions = true;
        }
    }
}
