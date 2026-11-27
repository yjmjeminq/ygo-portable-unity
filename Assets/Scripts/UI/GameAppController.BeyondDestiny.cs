using System.Collections.Generic;
using UnityEngine;
using YGOPortable.AI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    public partial class GameAppController
    {
        private BeyondDestinyUI _beyondDestiny;
        private BeyondDestinyCampaign.Encounter _activeBeyondDestiny;
        private string _beyondDestinyDuelist, _beyondDestinyBattleMap;
        private string _beyondDestinyPlayerKey, _beyondDestinyOpponentKey;
        private BeyondDestinyAudio _beyondDestinyAudio;

        internal bool IsBeyondDestinyScreen => _screen == AppScreen.BeyondDestiny;

        internal void StartBeyondDestinyDuel(BeyondDestinyCampaign.Encounter encounter,
            BeyondDestinyCampaign.Deck player, BeyondDestinyCampaign.Deck opponent)
        {
            if (!IsBeyondDestinyScreen || encounter == null || encounter != _beyondDestiny.Pending ||
                player?.id != encounter.playerDeckId || opponent?.id != encounter.opponentDeckId) return;
            _activeBeyondDestiny = encounter;
            _beyondDestinyDuelist = player.charName;
            _beyondDestinyBattleMap = _beyondDestiny.BattleMap(encounter);
            _beyondDestinyPlayerKey = player.key;
            _beyondDestinyOpponentKey = opponent.key;
            var character = new OpponentCharacter { id = opponent.charId, name = opponent.charName, seriesIdx = 2 };
            var preset = new UserDeck
            {
                id = $"{BeyondDestinyCampaign.Id}/{player.id}", name = player.title,
                main = new List<uint>(player.main), extra = new List<uint>(player.extra), side = new List<uint>(player.side),
            };
            StartDuelMatch(character, opponent, preset, playerGoesFirst: true, isStoryDuel: false,
                planningScale: GxStoryPlanningScale(opponent.difficultyTier), opponentDifficulty: AIDifficulty.Master,
                isBeyondDestiny: true);
        }

        internal static (DuelOpening Player, DuelOpening Opponent) BeyondDestinyOpening(BeyondDestinyCampaign.Encounter encounter)
        {
            var (player, opponent) = encounter.episodeId switch
            {
                "beyond_destiny_ep_19" => (800, 2400),
                "beyond_destiny_ep_30" => (3200, 1100),
                "beyond_destiny_ep_31" => (5200, 4000),
                "beyond_destiny_ep_34" => (8000, 8000),
                "beyond_destiny_ep_35" => (1800, 6650),
                _ => (4000, 4000),
            };
            return (new DuelOpening(player, 5, 0, destinyUses: 0), new DuelOpening(opponent, 5, 0, destinyUses: 0));
        }

        private bool TryFinishBeyondDestinyDuel()
        {
            var encounter = _activeBeyondDestiny;
            if (encounter == null) return false;
            int goalTurn = BeyondDestinyPlayback.ObjectiveTurn(encounter.resolution);
            bool objective = goalTurn > 0 && _session.TurnNumber >= goalTurn;
            if (!objective && !_session.IsFinished) return false;
            bool validResult = _session.IsFinished && _session.Winner is >= 0 and <= 2;
            bool won = validResult && _session.Winner == _session.HumanPlayerIndex;
            ShowScreen(AppScreen.BeyondDestiny);
            _beyondDestiny.ShowOutcome(encounter, won, validResult, objective);
            SyncFrontMenuFocus();
            return true;
        }

        private BeyondDestinyAudio BeyondDestinyMixer => _beyondDestinyAudio != null ? _beyondDestinyAudio :
            _beyondDestinyAudio = gameObject.AddComponent<BeyondDestinyAudio>();

        internal void PlayBeyondDestinyCues(BeyondDestinyCampaign.Cue[] cues)
        {
            if (Application.isPlaying && cues.Length != 0) BeyondDestinyMixer.Apply(cues);
        }

        internal void RestoreBeyondDestinySoundscape(BeyondDestinySoundscape state)
        {
            if (Application.isPlaying) BeyondDestinyMixer.Restore(state);
        }

        private void StopBeyondDestinyAudio()
        {
            if (_beyondDestinyAudio != null) _beyondDestinyAudio.Stop();
        }
    }
}
