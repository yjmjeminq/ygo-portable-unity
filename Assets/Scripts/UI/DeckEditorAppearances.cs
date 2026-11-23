using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    public partial class DeckEditorUI
    {
        private readonly Dictionary<uint, DeckCardAppearance> _trunkAppearances = new();
        private Button _inspectEdition;
        private Button _inspectArtwork;
        private Button _inspectDestiny;

        private static CompositeCardView CreatePrintedDeckCard(Transform parent, string name, bool detailedOverlay = false)
        {
            var view = CompositeCardView.Create(parent, name, detailedOverlay);
            view.UsePrintedFaceOnly();
            return view;
        }

        private Dictionary<(uint Code, bool Overframe), long> AvailableDeckCopies()
        {
            if (!CollectionProgress.RequiresOwnedCardsForDecks) return null;

            var available = new Dictionary<(uint Code, bool Overframe), long>();
            foreach (var copies in _workingDeck.Copies().GroupBy(copy => copy.Code))
            {
                uint code = copies.Key;
                int overframes = CollectionProgress.GetOwnedOverframeCount(code);
                int requestedOverframes = copies.Count(copy => copy.Appearance.overframe);
                // Duel preparation reserves explicitly selected Overframes before filling ordinary slots.
                int reservedOverframes = Mathf.Min(overframes, requestedOverframes);
                available[(code, true)] = reservedOverframes;
                available[(code, false)] = (long)CollectionProgress.GetOwnedCount(code) + overframes - reservedOverframes;
            }
            return available;
        }

        private void BuildAppearanceControls(Transform panel, float inset)
        {
            var left = DuelUiKit.CreateRect("Edition", panel, new Vector2(0f, .924f), new Vector2(.33f, .982f),
                new Vector2(inset, 0f), new Vector2(-4f, 0f));
            var right = DuelUiKit.CreateRect("Artwork", panel, new Vector2(.33f, .924f), new Vector2(.666f, .982f),
                new Vector2(3f, 0f), new Vector2(-3f, 0f));
            _inspectEdition = DeckEditorStyle.Button(left, "Button", "일반판", () =>
            {
                var (cards, index) = InspectedDeckSlot(_inspectedCode);
                ChangeEdition(_inspectedCode, cards, index);
            }, 14);
            _inspectArtwork = DeckEditorStyle.Button(right, "Button", "일러스트 변경", () =>
            {
                var (cards, index) = InspectedDeckSlot(_inspectedCode);
                ChangeArtwork(_inspectedCode, cards, index);
            }, 13);
            var destiny = DuelUiKit.CreateRect("DestinyDraw", panel, new Vector2(.666f, .924f), new Vector2(1f, .982f),
                new Vector2(4f, 0f), new Vector2(-inset, 0f));
            _inspectDestiny = DeckEditorStyle.Button(destiny, "Button", "데스티니 지정", () =>
            {
                var (cards, index) = InspectedDeckSlot(_inspectedCode);
                ToggleDestiny(_inspectedCode, cards, index);
            }, 13);
            foreach (var button in new[] { _inspectEdition, _inspectArtwork, _inspectDestiny })
            {
                var text = button.GetComponentInChildren<Text>();
                text.resizeTextForBestFit = true;
                text.resizeTextMinSize = 8;
                text.resizeTextMaxSize = 14;
            }
        }

        private (List<uint> Cards, int Index) InspectedDeckSlot(uint code)
        {
            var trigger = _inspectedSource?.GetComponent<CardDragTrigger>();
            if (trigger == null || trigger.CardCodeGetter?.Invoke() != code) return (null, -1);
            var cards = GetDeckList(trigger.SourceType);
            int index = trigger.IndexGetter?.Invoke() ?? -1;
            return cards != null && index >= 0 && index < cards.Count && cards[index] == code ? (cards, index) : (null, -1);
        }

        private DeckCardAppearance ReadAppearance(uint code, List<uint> cards, int index) =>
            cards != null ? _workingDeck.GetAppearance(cards, index) : _trunkAppearances.GetValueOrDefault(code);

        private bool HasUnusedEdition(uint code, bool overframe) => UnusedEditionCount(code, overframe) > 0;

        private int UnusedEditionCount(uint code, bool overframe, int releaseRegular = 0)
        {
            int owned = overframe ? CollectionProgress.GetOwnedOverframeCount(code) : CollectionProgress.GetOwnedCount(code);
            int used = CountEditionCopies(code, overframe);
            if (overframe)
                used += Mathf.Max(0, CountEditionCopies(code, false) - releaseRegular - CollectionProgress.GetOwnedCount(code));
            return Mathf.Max(0, owned - used);
        }

        private int CountEditionCopies(uint code, bool overframe) =>
            _workingDeck?.Copies().Count(copy => copy.Code == code && copy.Appearance.overframe == overframe) ?? 0;

        private DeckCardAppearance AppearanceForAddition(uint code)
        {
            var appearance = _trunkAppearances.GetValueOrDefault(code);
            if (!CollectionProgress.RequiresOwnedCardsForDecks || HasUnusedEdition(code, appearance.overframe)) return appearance;
            appearance.overframe = !appearance.overframe;
            return appearance.WithAvailableArtwork(code);
        }

        private bool CanChangeEdition(uint code, List<uint> cards, DeckCardAppearance appearance)
        {
            if (code == 0) return false;
            bool targetOverframe = !appearance.overframe;
            if (targetOverframe && CollectionProgress.GetOwnedOverframeCount(code) == 0)
                return false;
            return cards == null || !CollectionProgress.RequiresOwnedCardsForDecks ||
                UnusedEditionCount(code, targetOverframe, releaseRegular: targetOverframe ? 1 : 0) > 0;
        }

        private void ChangeEdition(uint code, List<uint> cards, int index)
        {
            var appearance = ReadAppearance(code, cards, index);
            if (!CanChangeEdition(code, cards, appearance)) return;
            appearance.overframe = !appearance.overframe;
            ApplyAppearance(code, code, cards, index, appearance.WithAvailableArtwork(code));
        }

        private void ChangeArtwork(uint code, List<uint> cards, int index)
        {
            var appearance = ReadAppearance(code, cards, index);
            var choices = CardArtLoader.GetSelectableArtwork(code, appearance.overframe);
            if (choices.Length < 2) return;
            int current = CardArtLoader.ArtworkChoiceIndex(code, choices, appearance.ArtworkVariant);
            if (!CardArtLoader.SetDuelArtworkVariant(code, choices[(current + 1) % choices.Length])) return;
            if (_workingDeck != null) RefreshDeckGrids();
            UpdateVirtualTrunkSlots(_lastFirstRow, force: true);
            InspectCard(code);
            if (_expandedDeckOverlay.gameObject.activeSelf && _expandedDeckCode == code) RefreshExpandedDeckArtwork();
        }

        private void ApplyAppearance(uint code, uint target, List<uint> cards, int index, DeckCardAppearance appearance)
        {
            var slot = _inspectedSource;
            if (cards == null)
            {
                _trunkAppearances[target] = appearance;
                uint key = DeckGroupKey(code);
                _trunkPrintChoices[key] = target;
                int row = _trunkResults.FindIndex(card => DeckGroupKey(card.code) == key);
                if (row >= 0 && _trunkCollectionFilters.Matches(target))
                    _trunkResults[row] = CardDatabase.Get(target);
                UpdateVirtualTrunkSlots(_lastFirstRow, force: true);
            }
            else
            {
                cards[index] = target;
                _workingDeck.SetAppearance(cards, index, appearance);
                RefreshDeckGrids();
            }
            _inspectedSource = slot;
            InspectCard(target);
        }

        private void RefreshInspectedAppearance(uint code)
        {
            var (cards, index) = InspectedDeckSlot(code);
            var appearance = ReadAppearance(code, cards, index);
            _previewCard.SetDeckCard(code, appearance);
            var label = _inspectEdition.GetComponentInChildren<Text>();
            label.text = appearance.overframe ? "오버프레임" : "일반판";
            label.color = appearance.overframe ? DuelUiKit.GoldAccent : DuelUiKit.TextPrimary;
            _inspectEdition.interactable = CanChangeEdition(code, cards, appearance);
            _inspectArtwork.gameObject.SetActive(CardArtLoader.GetSelectableArtwork(code, appearance.overframe).Length > 1);
            bool eligible = cards != null && ReferenceEquals(cards, _workingDeck.main);
            _inspectDestiny.interactable = eligible;
            var destiny = _inspectDestiny.GetComponentInChildren<Text>();
            destiny.text = eligible && appearance.destinyDraw ? "★ 데스티니" : "데스티니 지정";
            destiny.color = eligible && appearance.destinyDraw ? DuelUiKit.GoldAccent : DuelUiKit.TextPrimary;
        }

        private void ToggleDestiny(uint code, List<uint> cards, int index)
        {
            if (cards == null || !ReferenceEquals(cards, _workingDeck.main) || index < 0 ||
                index >= cards.Count || cards[index] != code) return;
            var appearance = _workingDeck.GetAppearance(cards, index);
            appearance.destinyDraw = !appearance.destinyDraw;
            ApplyAppearance(code, code, cards, index, appearance);
        }
    }
}
