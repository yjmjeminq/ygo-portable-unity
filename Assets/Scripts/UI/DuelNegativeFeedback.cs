using UnityEngine;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    public partial class DuelFieldUI
    {
        private static readonly Color DisabledInk = new Color(1f, .39f, .50f);
        private static readonly Color BindingInk = new Color(.72f, .38f, 1f);
        private RectTransform _activationNotice;
        private CanvasGroup _activationNoticeGroup;
        private Text _activationNoticeText;
        private float _activationNoticeUntil;
        private DuelPrompt _activationNoticePrompt;
        private GameObject _inspectorDisabledBadge;
        private GameObject _zoomDisabledBadge;
        private Vector2 _boardDamageOffset;

        private void ShakeBoardForDamage(float progress)
        {
            _boardRect.anchoredPosition -= _boardDamageOffset;
            float envelope = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress / .42f));
            float amplitude = Mathf.Clamp(_boardRect.rect.width * .010f, 3f, 11f) * envelope;
            _boardDamageOffset = new Vector2(Mathf.Sin(progress * 72f), Mathf.Sin(progress * 47f) * .45f) * amplitude;
            _boardRect.anchoredPosition += _boardDamageOffset;
        }

        private void BuildActivationNotice(Transform parent)
        {
            _activationNotice = LotdUiKit.CreateRect("ActivationNotice", parent, new Vector2(.24f, .86f),
                new Vector2(.76f, .943f), Vector2.zero, Vector2.zero);
            DuelReadoutPlate.Add(_activationNotice, new Color(.041f, .063f, .088f, .95f)).raycastTarget = false;
            var line = LotdUiKit.CreateRect("Accent", _activationNotice, new Vector2(.017f, .20f), new Vector2(.023f, .80f),
                Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(line.gameObject, LotdUiKit.WhiteSprite, DisabledInk, false, false);
            _activationNoticeText = LotdUiKit.AddFittedText(_activationNotice, "Reason", "", 22, 12,
                TextAnchor.MiddleCenter, LotdUiKit.TextPrimary, FontStyle.Bold);
            _activationNoticeGroup = _activationNotice.gameObject.AddComponent<CanvasGroup>();
            _activationNoticeGroup.blocksRaycasts = false;
            _activationNoticeGroup.interactable = false;
            _activationNotice.gameObject.SetActive(false);
        }

        private void HideActivationNotice()
        {
            _activationNoticePrompt = null;
            _activationNotice?.gameObject.SetActive(false);
        }

        private void UpdateNegativeFeedback()
        {
            if (_activationNotice.gameObject.activeSelf)
            {
                float remaining = _activationNoticeUntil - Time.unscaledTime;
                if (remaining <= 0f || !ReferenceEquals(_activationNoticePrompt, _session.ActivePrompt))
                    HideActivationNotice();
                else _activationNoticeGroup.alpha = Mathf.Clamp01(remaining / .27f);
            }

            float pulse = Mathf.Repeat(Time.unscaledTime / 2.9f, 1f);
            foreach (var slot in _fieldSlotsByRect.Values)
            {
                if (slot.disabledChains != null && slot.disabledChains.isActiveAndEnabled)
                    slot.disabledChains.Seek(pulse);
                if (slot.linkBeams != null && slot.linkBeams.isActiveAndEnabled)
                    slot.linkBeams.Seek(pulse);
            }
        }

        private bool ShowUnavailableEffect(LiveCardState card)
        {
            var prompt = CanAcceptPlayerAction && !IsStagingPlacement && !IsReadingCardDetails && !_phasePicker.activeSelf
                ? _session.ActivePrompt : null;
            if (!CanShowUnavailableEffect(prompt, card)) return false;
            string reason = card.effectsDisabled ? "이 카드의 효과가 무효화되어 있습니다." :
                _session.HasUsedSharedTurnEffect(card.controller, card.code)
                    ? "이 턴에는 더 이상 이 카드의 효과를 발동할 수 없습니다." :
                CannotMatchChainSpeed(prompt, card)
                    ? "체인할 수 있는 스펠 스피드가 아닙니다." : "발동 조건을 만족하지 않았습니다.";
            _activationNoticeText.text = reason;
            _activationNoticePrompt = prompt;
            _activationNoticeUntil = Time.unscaledTime + 2.25f;
            _activationNoticeGroup.alpha = 1f;
            _activationNotice.SetAsLastSibling();
            _activationNotice.gameObject.SetActive(true);
            return true;
        }

        private bool CanShowUnavailableEffect(DuelPrompt prompt, LiveCardState card)
        {
            if (prompt == null || card == null || card.controller != _session.HumanPlayerIndex ||
                prompt.messageType is not (YgoConstants.MSG_SELECT_IDLECMD or YgoConstants.MSG_SELECT_BATTLECMD or YgoConstants.MSG_SELECT_CHAIN) ||
                !HasPotentialEffect(card)) return false;
            return !_session.HasEffectActivationAt(prompt, ChainConfirmationMode.On, card.controller, card.location, card.sequence) &&
                CollectActions(card.controller, card.location, card.sequence).Count == 0;
        }

        private static bool HasPotentialEffect(LiveCardState card)
        {
            uint type = card.type != 0 ? card.type : CardDatabase.Get(card.code)?.type ?? 0;
            if (card.location == YgoConstants.LOCATION_HAND)
                return (type & (YgoConstants.TYPE_EFFECT | YgoConstants.TYPE_SPELL | YgoConstants.TYPE_TRAP |
                    YgoConstants.TYPE_PENDULUM)) != 0;
            return card.IsFaceUp && (type & YgoConstants.TYPE_EFFECT) != 0 ||
                card.location == YgoConstants.LOCATION_SZONE && (type & (YgoConstants.TYPE_SPELL | YgoConstants.TYPE_TRAP)) != 0;
        }

        private bool CannotMatchChainSpeed(DuelPrompt prompt, LiveCardState card)
        {
            if (prompt.messageType != YgoConstants.MSG_SELECT_CHAIN || _session.CurrentChain.Count == 0) return false;
            uint type = card.type != 0 ? card.type : CardDatabase.Get(card.code)?.type ?? 0;
            // A Counter Trap needs another Counter Trap; a normal-speed hand Spell cannot respond to a link.
            uint chainType = CardDatabase.Get(_session.CurrentChain[^1].code)?.type ?? 0;
            if ((chainType & (YgoConstants.TYPE_TRAP | YgoConstants.TYPE_COUNTER)) ==
                (YgoConstants.TYPE_TRAP | YgoConstants.TYPE_COUNTER))
                return (type & (YgoConstants.TYPE_TRAP | YgoConstants.TYPE_COUNTER)) !=
                    (YgoConstants.TYPE_TRAP | YgoConstants.TYPE_COUNTER);
            return card.location == YgoConstants.LOCATION_HAND && (type & YgoConstants.TYPE_SPELL) != 0 &&
                (type & YgoConstants.TYPE_QUICKPLAY) == 0;
        }

        private static void SetAvailabilityDim(Transform art, ref Image shade, bool visible)
        {
            if (visible && shade == null)
            {
                var rect = LotdUiKit.FillRect("UnavailableShade", art);
                shade = LotdUiKit.AddImage(rect.gameObject, LotdUiKit.WhiteSprite,
                    new Color(.14f, .16f, .19f, .32f), false, false);
            }
            if (shade != null) shade.gameObject.SetActive(visible);
        }

        private static void SetDisabledChains(FieldSlotView slot, bool visible)
        {
            if (visible && slot.disabledChains == null)
            {
                var rect = LotdUiKit.FillRect("DisabledChains", slot.focusArt);
                slot.disabledChains = rect.gameObject.AddComponent<DuelEffectGraphic>();
                slot.disabledChains.texture = Texture2D.whiteTexture;
                slot.disabledChains.Kind = DuelEffectKind.DisabledChains;
                slot.disabledChains.Tint = BindingInk;
                slot.disabledChains.raycastTarget = false;
            }
            if (slot.disabledChains != null)
            {
                slot.disabledChains.gameObject.SetActive(visible);
                if (visible) slot.disabledChains.transform.SetAsLastSibling();
            }
        }

        private static GameObject BuildDisabledBadge(Transform cardView, int fontSize)
        {
            var area = LotdUiKit.CreateRect("EffectDisabled", cardView.Find("AspectBox"), new Vector2(.054f, .055f),
                new Vector2(.946f, .225f), Vector2.zero, Vector2.zero);
            DuelReadoutPlate.Add(area, new Color(.13f, .029f, .067f, .96f)).raycastTarget = false;
            LotdUiKit.AddFittedText(area, "Label", "효과 무효화됨", fontSize, 9,
                TextAnchor.MiddleCenter, DisabledInk, FontStyle.Bold);
            area.gameObject.SetActive(false);
            return area.gameObject;
        }
    }
}
