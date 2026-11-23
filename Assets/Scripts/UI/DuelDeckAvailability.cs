using System.Linq;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    internal static class DuelDeckAvailability
    {
        internal static string ExplainEmptyList(string preferredId = null)
        {
            if (!string.IsNullOrEmpty(CollectionProgress.LoadError)) return CollectionProgress.LoadError;

            var decks = DeckRepository.UserDecks;
            var deck = decks.Find(candidate => candidate.id == preferredId) ??
                decks.Find(candidate => candidate.id == CollectionProgress.LastDuelDeckId) ??
                decks.FirstOrDefault();
            if (deck == null) return "덱 편집에서 덱을 저장하세요.";

            return GameAppController.CanStartWithDeck(deck, out string reason)
                ? "덱 편집에서 덱을 확인하세요." : $"{deck.name} · {reason}";
        }
    }
}
