using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    internal sealed partial class TagForceWorldUI
    {
        private const float RpgStageMoveTime = .65f;
        private readonly List<RpgStageActor> _rpgStageActors = new();
        private readonly Dictionary<string, RpgStageActor> _rpgStageSpeakers = new(StringComparer.Ordinal);
        private readonly Dictionary<int, RpgStageActor> _rpgStageIds = new();
        private RectTransform _rpgDialogueCast, _rpgDialogueOpponent;
        private RpgStageActor _rpgSpeakingActor, _rpgPreviousSpeaker;
        private StoryDialogueLine[] _rpgBeatLines;
        private RectTransform _rpgCameraFocus;
        private int[] _rpgBeats = Array.Empty<int>();
        private int _rpgNextBeat;
        private float _rpgBeatRemaining;
        private string _rpgHeroName;
        private int _rpgLastLine;
        private int _rpgGuestSlots;
        private int _rpgSceneEpisode, _rpgSceneHeroId;
        internal bool RpgStageRunning => _rpgBeatRemaining > 0f;

        private readonly struct RpgStagePose
        {
            internal readonly Vector2 Position;
            internal readonly bool Visible;
            internal readonly int? LookAt;

            internal RpgStagePose(Vector2 position, bool visible, int? lookAt = null)
            {
                Position = position;
                Visible = visible;
                LookAt = lookAt;
            }
        }

        private sealed class RpgStageActor
        {
            internal RectTransform Root, Art;
            internal CanvasGroup Visibility;
            internal Vector2 Entry, Mark, From;
            internal RpgStagePose Initial, Target;
            internal bool AutoJoin;
            internal float Progress, StartAlpha;
        }

        internal void BeginRpgDialogue(StoryEpisodeData episode, string hero, StoryDialogueLine[] lines)
        {
            Root.SetActive(true);
            if (_player == null || _rpgDialogueCast != null || _page != Page.Field) Show(Page.Field);

            _rpgDialogueCast = DuelUiKit.FillRect("DialogueCast", _rpgSceneSpace);
            _rpgSceneEpisode = RpgWardrobe.EpisodeNumber(episode);
            _rpgLastPlayedSceneId = episode.id;
            _rpgSceneHeroId = episode.playAsCharacterId;
            _rpgHeroName = hero;
            _rpgLastLine = -1;
            _moving = _manualWalk = _fieldMenu = false;
            _onArrival = null;
            _rpgApproachActor = null;
            _worldInput.interactable = _worldInput.blocksRaycasts = false;
            _header.gameObject.SetActive(false);
            _status.gameObject.SetActive(false);
            _back.gameObject.SetActive(false);
            _rpgFieldHud.gameObject.SetActive(false);
            _rpgNavigation.gameObject.SetActive(false);

            var entrances = new HashSet<int>(lines.SelectMany(line => line.stage ?? Array.Empty<StoryStageAction>())
                .Where(action => action?.action == "enter").Select(action => action.actor));
            int opponentId = StageOpponentId(episode);
            _rpgResidents.TryGetValue(opponentId, out _rpgDialogueOpponent);
            if (_rpgDialogueOpponent == null || !IsGxCampaign && FieldOutfit(opponentId) != RpgWardrobe.ForStory(opponentId, _rpgSceneEpisode))
            {
                var mark = _rpgDialogueOpponent != null ? _rpgDialogueOpponent.anchorMin : RpgNpcSlots[0];
                _rpgDialogueOpponent = Anchor(_rpgDialogueCast, "Opponent", mark, new Vector2(91f, 100f));
                CreateStageWalker(_rpgDialogueOpponent, opponentId, episode.opponentName);
            }
            RegisterRpgActor(_rpgSceneHeroId, _player, _playerSprite, _position, true, false);
            var opponentArt = (_rpgDialogueOpponent.Find("SD") ?? _rpgDialogueOpponent.Find("Sprite")) as RectTransform;
            var opponent = RegisterRpgActor(opponentId, _rpgDialogueOpponent, opponentArt,
                _rpgDialogueOpponent.anchorMin, !IsGxCampaign || !entrances.Contains(opponentId), false);
            _rpgStageSpeakers[episode.opponentName] = opponent;
            if (opponentId == 0) _rpgStageSpeakers["주인공"] = opponent;

            foreach (var resident in _rpgResidentMotion)
                resident.Marker.gameObject.SetActive(false);
            _rpgSceneSpace.Find("SideQuestItem")?.gameObject.SetActive(false);
            _rpgFieldObjects?.gameObject.SetActive(false);
            _rpgGreeting?.gameObject.SetActive(false);
            _rpgTravelBanner?.gameObject.SetActive(false);
            _rpgTravelFade?.gameObject.SetActive(false);
            _rpgRankBanner?.gameObject.SetActive(false);

            foreach (var line in lines)
            {
                string name = line.speaker;
                if (!string.IsNullOrWhiteSpace(name) && name is not ("설명" or "내레이션" or "나레이션") &&
                    !IsRpgHero(name) && !_rpgStageSpeakers.ContainsKey(name))
                {
                    int? id = _rpgSceneHeroId != 0 && IsOriginalRpgHero(name) ? 0 :
                        _rpgCampaign?.episodes.FirstOrDefault(item => item.opponentName == name)?.charId ??
                        DeckRepository.Characters.FirstOrDefault(character => character.name == name ||
                            character.aliases?.Contains(name) == true)?.id;
                    _rpgStageSpeakers[name] = GetRpgStageGuest(name, id, id.HasValue && entrances.Contains(id.Value));
                }
                foreach (var action in line.stage ?? Array.Empty<StoryStageAction>())
                {
                    if (action == null) continue;
                    EnsureRpgStageGuest(action.actor, entrances);
                    if (action.action == "face") EnsureRpgStageGuest(action.target, entrances);
                }
            }

            PoseRpgWalker(_rpgDialogueOpponent, opponentArt, _rpgDialogueOpponent.anchorMin, Vector2.zero, false,
                _position - _rpgDialogueOpponent.anchorMin);
            PoseRpgWalker(_player, _playerSprite, _position, Vector2.zero, false, RpgNpcSlots[0] - _position);
            _player.SetAsLastSibling();
            foreach (var resident in _rpgResidents.Values)
                if (!_rpgStageActors.Any(actor => actor.Root == resident)) resident.gameObject.SetActive(false);
            if (_rpgCompanion != null && !_rpgStageActors.Any(actor => actor.Root == _rpgCompanion))
                _rpgCompanion.gameObject.SetActive(false);
            FrameRpgCamera(_player.anchorMin);
        }

        private void EnsureRpgStageGuest(int id, HashSet<int> entrances)
        {
            if (_rpgStageIds.ContainsKey(id)) return;
            var character = DeckRepository.GetCharacter(id);
            if (character != null) GetRpgStageGuest(character.name, character.id, entrances.Contains(id));
        }

        private RpgStageActor GetRpgStageGuest(string name, int? id, bool enters)
        {
            if (id.HasValue && _rpgStageIds.TryGetValue(id.Value, out var existing)) return existing;
            int slot = _rpgGuestSlots++;
            bool left = slot % 2 == 0;
            float row = .49f + (slot / 2) * .12f;
            var mark = new Vector2(left ? .32f : .66f, row);
            var entry = new Vector2(left ? .052f : .948f, row);
            RectTransform actor, sprite;
            bool resident = false;
            if (!enters && id == RpgPartnerId && _rpgCompanion != null)
            {
                actor = _rpgCompanion;
                sprite = actor.Find("Sprite") as RectTransform;
                resident = true;
            }
            else if (!enters && id is int residentId && FieldOutfit(residentId) == RpgWardrobe.ForStory(residentId, _rpgSceneEpisode) &&
                _rpgResidents.TryGetValue(residentId, out actor))
            {
                sprite = actor.Find("SD") as RectTransform;
                resident = true;
            }
            else
            {
                string key = id.HasValue ? id.Value.ToString() : $"Unknown_{slot}";
                actor = Anchor(_rpgDialogueCast, $"Guest_{key}", entry, new Vector2(91f, 103f));
                sprite = CreateStageWalker(actor, id, name);
            }
            return RegisterRpgActor(id, actor, sprite, mark, resident, true);
        }

        private RectTransform CreateStageWalker(RectTransform actor, int? id, string name)
        {
            if (id is not int character) return WalkerSprite(actor, null, name);
            string outfit = IsGxCampaign ? null : RpgWardrobe.ForStory(character, _rpgSceneEpisode);
            return WalkerSprite(actor, CampaignSdPath(character), name,
                outfit == null ? null : RpgWardrobe.WalkAtlas(character, outfit));
        }

        private RpgStageActor RegisterRpgActor(int? id, RectTransform root, RectTransform art, Vector2 mark,
            bool visible, bool autoJoin)
        {
            var pose = new RpgStagePose(root.anchorMin, visible);
            var actor = new RpgStageActor
            {
                Root = root, Art = art, Visibility = root.gameObject.AddComponent<CanvasGroup>(),
                Entry = root.anchorMin, Mark = mark, Initial = pose, Target = pose, Progress = 1f, AutoJoin = autoJoin,
            };
            actor.Visibility.alpha = visible ? 1f : 0f;
            root.gameObject.SetActive(visible);
            _rpgStageActors.Add(actor);
            if (id.HasValue) _rpgStageIds.Add(id.Value, actor);
            return actor;
        }

        private RpgStageActor RpgSpeaker(string name)
        {
            if (IsRpgHero(name)) return _rpgStageIds.GetValueOrDefault(_rpgSceneHeroId);
            return name != null && _rpgStageSpeakers.TryGetValue(name, out var actor) ? actor : null;
        }

        internal void SetRpgDialogueLine(StoryDialogueLine[] lines, int index, bool playMotion)
        {
            bool playingNext = playMotion && index == _rpgLastLine + 1;
            _rpgSpeakingActor = RpgSpeaker(lines[index].speaker);
            _rpgPreviousSpeaker = null;
            for (int i = index - 1; i >= 0 && _rpgSpeakingActor != null; i--)
            {
                var previous = RpgSpeaker(lines[i].speaker);
                if (previous == null || previous == _rpgSpeakingActor) continue;
                _rpgPreviousSpeaker = previous;
                break;
            }
            _rpgLastLine = index;
            _rpgCameraFocus = null;
            _rpgBeatRemaining = 0f;
            if (playingNext)
            {
                _rpgBeatLines = lines;
                _rpgBeats = (lines[index].stage ?? Array.Empty<StoryStageAction>())
                    .Where(action => action != null).Select(action => action.step)
                    .Append(0).Distinct().OrderBy(step => step).ToArray();
                _rpgNextBeat = 0;
                PlayNextRpgBeat();
            }
            else
            {
                _rpgBeatLines = null;
                ApplyRpgBeat(lines, index, int.MaxValue, animate: false);
                var focus = lines[index].stage?.Where(action => action?.action == "focus")
                    .OrderBy(action => action.step).LastOrDefault();
                if (focus != null && _rpgStageIds.TryGetValue(focus.actor, out var subject)) SetRpgCameraFocus(subject);
                FrameRpgCamera(RpgDialogueCameraFocus());
            }
        }

        private void PlayNextRpgBeat()
        {
            while (_rpgNextBeat < _rpgBeats.Length)
            {
                int step = _rpgBeats[_rpgNextBeat++];
                ApplyRpgBeat(_rpgBeatLines, _rpgLastLine, step, animate: true);
                var focus = (_rpgBeatLines[_rpgLastLine].stage ?? Array.Empty<StoryStageAction>())
                    .LastOrDefault(action => action?.step == step && action.action == "focus");
                bool cameraMove = focus != null && _rpgStageIds.TryGetValue(focus.actor, out var subject) &&
                    SetRpgCameraFocus(subject);
                if (!cameraMove && !_rpgStageActors.Any(actor => actor.Progress < 1f)) continue;
                _rpgBeatRemaining = RpgStageMoveTime;
                return;
            }
            _rpgBeatLines = null;
        }

        private bool SetRpgCameraFocus(RpgStageActor actor)
        {
            if (!actor.Root.gameObject.activeSelf || _rpgCameraFocus == actor.Root) return false;
            _rpgCameraFocus = actor.Root;
            return true;
        }

        private void ApplyRpgBeat(StoryDialogueLine[] lines, int index, int step, bool animate)
        {
            foreach (var actor in _rpgStageActors)
            {
                var target = actor.Initial;
                bool staged = !actor.AutoJoin;
                int changedLine = -1;
                for (int i = 0; i <= index; i++)
                {
                    var line = lines[i];
                    if (!staged && RpgSpeaker(line.speaker) == actor)
                    {
                        target = new RpgStagePose(actor.Mark, true);
                        staged = true;
                        changedLine = i;
                    }
                    int latestStep = int.MinValue;
                    foreach (var action in line.stage ?? Array.Empty<StoryStageAction>())
                    {
                        if (action == null || action.step < latestStep || (i == index && action.step > step) ||
                            !_rpgStageIds.TryGetValue(action.actor, out var subject) || subject != actor)
                            continue;
                        if (action.action == "enter") target = new RpgStagePose(actor.Mark, true, target.LookAt);
                        else if (action.action == "move")
                            target = new RpgStagePose(new Vector2(Mathf.Clamp(action.x, .05f, .95f),
                                Mathf.Clamp(action.y, .12f, .64f)), true, target.LookAt);
                        else if (action.action == "exit")
                            target = new RpgStagePose(new Vector2(target.Position.x < .5f ? .04f : .96f, target.Position.y), false);
                        else if (action.action == "face" && _rpgStageIds.TryGetValue(action.target, out var facing) && facing != actor)
                            target = new RpgStagePose(target.Position, target.Visible, action.target);
                        else continue;
                        latestStep = action.step;
                        staged = true;
                        changedLine = i;
                    }
                }
                if (actor.Target.Visible == target.Visible && actor.Target.Position == target.Position &&
                    actor.Target.LookAt == target.LookAt &&
                    (animate || actor.Progress >= 1f)) continue;
                bool slide = animate && changedLine == index && (actor.Root.gameObject.activeSelf || target.Visible);
                BeginRpgStageMove(actor, target, slide);
            }
            RefreshRpgStageFacing();
        }

        internal void UpdateRpgDialogue(float delta)
        {
            UpdateRpgAtmosphere(delta);
            while (RpgStageRunning && delta > 0f)
            {
                float slice = Mathf.Min(delta, _rpgBeatRemaining);
                _rpgBeatRemaining -= slice;
                delta -= slice;
                foreach (var actor in _rpgStageActors)
                {
                    if (actor.Progress >= 1f) continue;
                    actor.Progress = RpgStageRunning ? Mathf.Min(1f, actor.Progress + slice / RpgStageMoveTime) : 1f;
                    PoseRpgStageActor(actor);
                }
                TrackRpgCamera(slice, RpgDialogueCameraFocus());
                if (!RpgStageRunning)
                {
                    RefreshRpgStageFacing();
                    PlayNextRpgBeat();
                }
            }
            TrackRpgCamera(delta, RpgDialogueCameraFocus());
        }

        private Vector2 RpgDialogueCameraFocus()
        {
            if (_rpgCameraFocus != null) return _rpgCameraFocus.anchorMin;
            if (!RpgStageRunning && _rpgSpeakingActor?.Target.Visible == true)
                return Vector2.Lerp(_player.anchorMin, _rpgSpeakingActor.Root.anchorMin, .38f);
            return _player.anchorMin;
        }

        private void BeginRpgStageMove(RpgStageActor actor, RpgStagePose target, bool animate)
        {
            bool visible = actor.Root.gameObject.activeSelf;
            actor.From = visible ? actor.Root.anchorMin : actor.Entry;
            actor.StartAlpha = visible ? actor.Visibility.alpha : 0f;
            actor.Target = target;
            actor.Progress = animate ? 0f : 1f;
            if (animate || target.Visible) actor.Root.gameObject.SetActive(true);
            PoseRpgStageActor(actor);
        }

        private void PoseRpgStageActor(RpgStageActor actor)
        {
            float progress = Mathf.SmoothStep(0f, 1f, actor.Progress);
            bool walking = actor.Progress < 1f && actor.From != actor.Target.Position;
            var position = Vector2.LerpUnclamped(actor.From, actor.Target.Position, progress);
            var heading = walking ? actor.Target.Position - actor.From : RpgStageHeading(actor, position);
            actor.Visibility.alpha = actor.Target.Visible
                ? Mathf.Lerp(actor.StartAlpha, 1f, Mathf.Clamp01(actor.Progress * 4f))
                : Mathf.Lerp(actor.StartAlpha, 0f, Mathf.Clamp01(actor.Progress * 2f - 1f));
            PoseRpgWalker(actor.Root, actor.Art, position, position - actor.Root.anchorMin, walking, heading);
            if (actor.Progress >= 1f && !actor.Target.Visible) actor.Root.gameObject.SetActive(false);
        }

        private Vector2 RpgStageHeading(RpgStageActor actor, Vector2 position)
        {
            if (actor.Target.LookAt is int id && _rpgStageIds[id].Root.gameObject.activeSelf)
                return _rpgStageIds[id].Root.anchorMin - position;
            var partner = actor == _rpgSpeakingActor ? _rpgPreviousSpeaker : _rpgSpeakingActor;
            if (partner?.Target.Visible == true && partner.Root.gameObject.activeSelf)
                return partner.Root.anchorMin - position;
            return (actor.Root == _player ? _rpgDialogueOpponent.anchorMin : _player.anchorMin) - position;
        }

        private void RefreshRpgStageFacing()
        {
            foreach (var actor in _rpgStageActors)
            {
                if (actor.Progress < 1f || !actor.Target.Visible ||
                    (_rpgSpeakingActor == null && !actor.Target.LookAt.HasValue)) continue;
                Vector2 position = actor.Root.anchorMin;
                PoseRpgWalker(actor.Root, actor.Art, position, Vector2.zero, false, RpgStageHeading(actor, position));
            }
        }

        private bool IsRpgHero(string name) => name == _rpgHeroName || (_rpgSceneHeroId > 0
            ? name is "아스카" or "텐죠인 아스카" : IsOriginalRpgHero(name));

        private static bool IsOriginalRpgHero(string name) => name?.StartsWith("주인공", StringComparison.Ordinal) == true ||
            name?.StartsWith("여주인공", StringComparison.Ordinal) == true;

        private void ClearRpgDialogue()
        {
            if (_rpgDialogueCast != null)
            {
                _worldInput.interactable = _worldInput.blocksRaycasts = true;
                _header.gameObject.SetActive(true);
                _status.gameObject.SetActive(true);
                _back.gameObject.SetActive(true);
            }
            _rpgFieldHud = null;
            _rpgDialogueCast = _rpgDialogueOpponent = null;
            _rpgSpeakingActor = _rpgPreviousSpeaker = null;
            _rpgBeatLines = null;
            _rpgCameraFocus = null;
            _rpgBeatRemaining = 0f;
            _rpgBeats = Array.Empty<int>();
            _rpgStageActors.Clear();
            _rpgStageSpeakers.Clear();
            _rpgStageIds.Clear();
            _rpgGuestSlots = 0;
        }
    }
}
