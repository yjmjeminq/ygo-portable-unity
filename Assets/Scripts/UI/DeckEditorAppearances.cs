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

        private void BuildAppearanceControls(Transform panel, float inset)
        {
            var left = LotdUiKit.CreateRect("Edition", panel, new Vector2(0f, .924f), new Vector2(.33f, .982f),
                new Vector2(inset, 0f), new Vector2(-4f, 0f));
            var right = LotdUiKit.CreateRect("Artwork", panel, new Vector2(.33f, .924f), new Vector2(.666f, .982f),
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
            var destiny = LotdUiKit.CreateRect("DestinyDraw", panel, new Vector2(.666f, .924f), new Vector2(1f, .982f),
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

        private bool HasUnusedEdition(uint code, bool overframe)
        {
            int owned = overframe ? CollectionProgress.GetOwnedOverframeCount(code) : CollectionProgress.GetOwnedCount(code);
            return owned > CountEditionCopies(code, overframe);
        }

        private int CountEditionCopies(uint code, bool overframe) =>
            _workingDeck?.Copies().Count(copy => copy.Code == code && copy.Appearance.overframe == overframe) ?? 0;

        private DeckCardAppearance AppearanceForAddition(uint code)
        {
            var appearance = _trunkAppearances.GetValueOrDefault(code);
            if (!CollectionProgress.RequiresOwnedCardsForDecks || HasUnusedEdition(code, appearance.overframe)) return appearance;
            appearance.overframe = !appearance.overframe;
            return FitEditionArtwork(code, appearance);
        }

        private static DeckCardAppearance FitEditionArtwork(uint code, DeckCardAppearance appearance)
        {
            if (!appearance.overframe || CardArtLoader.SupportsOverframe(code, appearance.ArtworkVariant)) return appearance;
            appearance.artwork = CardArtLoader.FirstOverframeVariant(code) + 1;
            return appearance;
        }

        private bool CanChangeEdition(uint code, List<uint> cards, DeckCardAppearance appearance)
        {
            if (code == 0) return false;
            bool targetOverframe = !appearance.overframe;
            if (targetOverframe && (!CardArtLoader.SupportsOverframe(code) || CollectionProgress.GetOwnedOverframeCount(code) == 0))
                return false;
            return cards == null || !CollectionProgress.RequiresOwnedCardsForDecks || HasUnusedEdition(code, targetOverframe);
        }

        private void ChangeEdition(uint code, List<uint> cards, int index)
        {
            var appearance = ReadAppearance(code, cards, index);
            if (!CanChangeEdition(code, cards, appearance)) return;
            appearance.overframe = !appearance.overframe;
            ApplyAppearance(code, code, cards, index, FitEditionArtwork(code, appearance));
        }

        private List<(uint Code, int Artwork)> ArtworkChoices(uint code, DeckCardAppearance appearance, bool deckSlot)
        {
            var result = new List<(uint, int)>();
            if (code == 0) return result;
            var candidates = CardDatabase.TryGetDeckPrintings(code, out var printings)
                ? printings.Select(printing => printing.code) : new[] { code };
            foreach (uint printing in candidates)
            {
                if (printing != code && (CollectionProgress.RequiresOwnedCardsForDecks || appearance.overframe))
                {
                    int owned = appearance.overframe ? CollectionProgress.GetOwnedOverframeCount(printing) :
                        CollectionProgress.GetOwnedCount(printing);
                    if (owned == 0 || deckSlot && !HasUnusedEdition(printing, appearance.overframe)) continue;
                }
                bool CanUse(int variant) => !appearance.overframe || CardArtLoader.SupportsOverframe(printing, variant);
                if (CanUse(-1)) result.Add((printing, 0));
                foreach (int variant in CardArtLoader.GetDuelArtworkChoices(printing))
                {
                    if (!CardArtLoader.HasDuelArtwork(printing, variant) || !CanUse(variant)) continue;
                    result.Add((printing, variant + 1));
                }
            }
            return result.OrderBy(choice => choice.Item2 != 0).ToList();
        }

        private void ChangeArtwork(uint code, List<uint> cards, int index)
        {
            var appearance = ReadAppearance(code, cards, index);
            var choices = ArtworkChoices(code, appearance, cards != null);
            if (choices.Count < 2) return;
            int current = choices.IndexOf((code, appearance.artwork));
            var next = choices[(current + 1) % choices.Count];
            appearance.artwork = next.Artwork;
            ApplyAppearance(code, next.Code, cards, index, appearance);
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
            label.color = appearance.overframe ? LotdUiKit.GoldAccent : LotdUiKit.TextPrimary;
            _inspectEdition.interactable = CanChangeEdition(code, cards, appearance);
            _inspectArtwork.interactable = ArtworkChoices(code, appearance, cards != null).Count > 1;
            bool eligible = cards != null && ReferenceEquals(cards, _workingDeck.main);
            _inspectDestiny.interactable = eligible;
            var destiny = _inspectDestiny.GetComponentInChildren<Text>();
            destiny.text = eligible && appearance.destinyDraw ? "★ 데스티니" : "데스티니 지정";
            destiny.color = eligible && appearance.destinyDraw ? LotdUiKit.GoldAccent : LotdUiKit.TextPrimary;
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
