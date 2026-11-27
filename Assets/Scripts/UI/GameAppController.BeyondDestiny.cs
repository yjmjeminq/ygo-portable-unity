using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
        private Coroutine _beyondDestinyAudioRequest;
        private AudioSource _beyondDestinyMusic, _beyondDestinyEffects;
        private readonly List<AudioClip> _beyondDestinyLoadedAudio = new();

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

        internal void PlayBeyondDestinyCues(BeyondDestinyCampaign.Cue[] cues)
        {
            if (!Application.isPlaying || cues.Length == 0) return;
            if (_beyondDestinyAudioRequest != null) StopCoroutine(_beyondDestinyAudioRequest);
            if (_beyondDestinyMusic == null)
            {
                _beyondDestinyMusic = gameObject.AddComponent<AudioSource>();
                _beyondDestinyMusic.playOnAwake = false;
                _beyondDestinyMusic.loop = true;
                _beyondDestinyEffects = gameObject.AddComponent<AudioSource>();
                _beyondDestinyEffects.playOnAwake = false;
            }
            _beyondDestinyAudioRequest = StartCoroutine(PlayBeyondDestinyAudio(cues));
        }

        private IEnumerator PlayBeyondDestinyAudio(BeyondDestinyCampaign.Cue[] cues)
        {
            foreach (var cue in cues)
            {
                if (cue.kind == "audio" || cue.action is "stop" or "fadeOut")
                {
                    if (cue.action == "fadeOut" || cue.text.Contains("페이드 아웃"))
                    {
                        for (float t = 0f; t < .35f; t += Time.unscaledDeltaTime)
                        {
                            _beyondDestinyMusic.volume = DuelAudio.BgmVolume * (1f - t / .35f);
                            yield return null;
                        }
                    }
                    _beyondDestinyMusic.Stop();
                    if (cue.kind == "audio") _beyondDestinyEffects.Stop();
                    continue;
                }
                bool music = cue.kind == "music";
                var ids = music ? cue.clipKeys ?? Array.Empty<string>() : new[] { cue.key };
                foreach (string id in ids)
                {
                    if (string.IsNullOrEmpty(id) || id != Path.GetFileName(id)) continue;
                    var path = Path.Combine(Application.streamingAssetsPath, "TagForce", "5DS", "Audio",
                        music ? "Music" : "Sfx", id + ".wav");
                    if (!File.Exists(path)) continue;
                    var clip = DuelVisualAssets.ReadSound(path, id);
                    if (clip == null) continue;
                    _beyondDestinyLoadedAudio.Add(clip);
                    if (music)
                    {
                        if (_beyondDestinyMusic.clip != null)
                        {
                            _beyondDestinyLoadedAudio.Remove(_beyondDestinyMusic.clip);
                            Destroy(_beyondDestinyMusic.clip);
                        }
                        DuelAudio.StopBgm();
                        _beyondDestinyMusic.clip = clip;
                        _beyondDestinyMusic.volume = DuelAudio.BgmVolume;
                        _beyondDestinyMusic.Play();
                    }
                    else
                    {
                        _beyondDestinyEffects.PlayOneShot(clip, DuelAudio.SfxVolume);
                        StartCoroutine(ReleaseBeyondDestinyEffect(clip));
                    }
                }
            }
            _beyondDestinyAudioRequest = null;
        }

        private IEnumerator ReleaseBeyondDestinyEffect(AudioClip clip)
        {
            yield return new WaitForSecondsRealtime(clip.length + .1f);
            if (_beyondDestinyLoadedAudio.Remove(clip)) Destroy(clip);
        }

        private void StopBeyondDestinyAudio()
        {
            if (_beyondDestinyAudioRequest != null) StopCoroutine(_beyondDestinyAudioRequest);
            _beyondDestinyAudioRequest = null;
            if (_beyondDestinyMusic != null)
            {
                _beyondDestinyMusic.Stop();
                _beyondDestinyMusic.clip = null;
                _beyondDestinyEffects.Stop();
            }
            foreach (var clip in _beyondDestinyLoadedAudio) Destroy(clip);
            _beyondDestinyLoadedAudio.Clear();
        }
    }
}
