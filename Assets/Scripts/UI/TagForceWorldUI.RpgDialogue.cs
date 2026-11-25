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
        private const float RpgGuestEntranceTime = .60f;
        private readonly List<RpgDialogueGuest> _rpgDialogueGuests = new();
        private RectTransform _rpgDialogueCast, _rpgDialogueOpponent, _rpgSpeakerCue;
        private string _rpgHeroName;
        private string _rpgOpponentName;

        private sealed class RpgDialogueGuest
        {
            internal string Name;
            internal RectTransform Actor, Sprite;
            internal CanvasGroup Visibility;
            internal Vector2 Entry, Mark;
            internal float Entrance;
            internal bool Joined;
        }

        internal void BeginRpgDialogue(StoryEpisodeData episode, string hero, StoryDialogueLine[] lines)
        {
            Root.SetActive(true);
            if (_player == null || _rpgDialogueCast != null) Show(Page.Field);

            _rpgDialogueCast = DuelUiKit.FillRect("DialogueCast", _body);
            _rpgHeroName = hero;
            _rpgOpponentName = episode.opponentName;
            _moving = _manualWalk = _fieldMenu = false;
            _onArrival = null;
            _worldInput.interactable = _worldInput.blocksRaycasts = false;
            _header.gameObject.SetActive(false);
            _status.gameObject.SetActive(false);
            _back.gameObject.SetActive(false);
            _rpgFieldHud.gameObject.SetActive(false);
            _rpgNavigation.gameObject.SetActive(false);

            _rpgResidents.TryGetValue(episode.charId, out _rpgDialogueOpponent);
            if (_rpgDialogueOpponent == null)
            {
                _rpgDialogueOpponent = Anchor(_rpgDialogueCast, "Opponent", RpgNpcSlots[0], new Vector2(91f, 100f));
                WalkerSprite(_rpgDialogueOpponent, RpgArt + $"Characters_SD/sd_{episode.charId}.png", episode.opponentName);
            }

            foreach (var actor in _rpgResidents.Values)
            {
                actor.Find("Interact").gameObject.SetActive(false);
                var marker = actor.Find(actor == _rpgDialogueOpponent ? "MainQuestMarker" : "ResidentMarker");
                if (marker != null) marker.gameObject.SetActive(false);
            }

            foreach (string name in lines.Select(line => line.speaker).Distinct(StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(name) || name is "설명" or "내레이션" or "나레이션" ||
                    IsRpgHero(name) || name == _rpgOpponentName)
                    continue;
                int? id = _rpgCampaign?.episodes.FirstOrDefault(item => item.opponentName == name)?.charId ??
                    DeckRepository.Characters.FirstOrDefault(character => character.name == name ||
                        character.aliases?.Contains(name) == true)?.id;
                if (id == episode.charId) continue;
                AddRpgDialogueGuest(name, id);
            }

            FaceDirection((_rpgDialogueOpponent.Find("SD") ?? _rpgDialogueOpponent.Find("Sprite")) as RectTransform,
                _position.x - RpgNpcSlots[0].x);
            PoseWalker(_player, _playerSprite, _position, RpgNpcSlots[0] - _position, false, 0f);
            _player.SetAsLastSibling();
            _rpgSpeakerCue = Anchor(_rpgDialogueCast, "SpeakingCue", Vector2.zero, new Vector2(29f, 24f));
            Label(_rpgSpeakerCue, "▼", 17).color = DuelUiKit.GoldAccent;
            _rpgSpeakerCue.gameObject.SetActive(false);
        }

        private void AddRpgDialogueGuest(string name, int? id)
        {
            int slot = _rpgDialogueGuests.Count;
            bool left = slot % 2 == 0;
            float row = .49f + (slot / 2) * .12f;
            var mark = new Vector2(left ? .32f : .66f, row);
            var entry = new Vector2(left ? .052f : .948f, row);
            RectTransform actor, sprite;
            CanvasGroup visibility = null;
            if (id.HasValue && _rpgResidents.TryGetValue(id.Value, out actor))
            {
                entry = actor.anchorMin;
                sprite = actor.Find("SD") as RectTransform;
            }
            else
            {
                string key = id.HasValue ? id.Value.ToString() : $"Unknown_{slot}";
                actor = Anchor(_rpgDialogueCast, $"Guest_{key}", entry, new Vector2(91f, 103f));
                sprite = WalkerSprite(actor, id.HasValue ? RpgArt + $"Characters_SD/sd_{id}.png" : null, name);
                visibility = actor.gameObject.AddComponent<CanvasGroup>();
                actor.gameObject.SetActive(false);
            }
            _rpgDialogueGuests.Add(new RpgDialogueGuest
            {
                Name = name, Actor = actor, Sprite = sprite, Visibility = visibility, Entry = entry, Mark = mark,
            });
        }

        internal void SetRpgDialogueLine(StoryDialogueLine[] lines, int index)
        {
            string speaker = lines[index].speaker;
            RectTransform speaking = IsRpgHero(speaker) ? _player :
                speaker == _rpgOpponentName ? _rpgDialogueOpponent : null;
            foreach (var guest in _rpgDialogueGuests)
            {
                int first = Array.FindIndex(lines, 0, index + 1, line => line.speaker == guest.Name);
                if (first < 0)
                {
                    if (guest.Joined)
                    {
                        guest.Joined = false;
                        if (guest.Visibility != null) guest.Actor.gameObject.SetActive(false);
                        else PoseWalker(guest.Actor, guest.Sprite, guest.Entry, Vector2.zero, false, 0f);
                    }
                    continue;
                }
                if (!guest.Joined)
                {
                    guest.Joined = true;
                    guest.Entrance = first == index ? 0f : 1f;
                    PoseRpgGuest(guest);
                    guest.Actor.gameObject.SetActive(true);
                }
                if (speaker == guest.Name) speaking = guest.Actor;
            }
            _rpgSpeakerCue.gameObject.SetActive(speaking != null);
            if (speaking != null)
            {
                _rpgSpeakerCue.SetParent(speaking, false);
                _rpgSpeakerCue.anchorMin = _rpgSpeakerCue.anchorMax = new Vector2(.50f, 1.05f);
                _rpgSpeakerCue.anchoredPosition = Vector2.zero;
                _rpgSpeakerCue.SetAsLastSibling();
            }
        }

        internal void UpdateRpgDialogue(float delta)
        {
            foreach (var guest in _rpgDialogueGuests)
            {
                if (!guest.Joined || guest.Entrance >= 1f) continue;
                guest.Entrance = Mathf.Min(1f, guest.Entrance + delta / RpgGuestEntranceTime);
                PoseRpgGuest(guest);
            }
        }

        private static void PoseRpgGuest(RpgDialogueGuest guest)
        {
            float progress = Mathf.SmoothStep(0f, 1f, guest.Entrance);
            bool walking = guest.Entrance < 1f;
            var heading = walking ? guest.Mark - guest.Entry : RpgNpcSlots[0] - guest.Mark;
            if (guest.Visibility != null) guest.Visibility.alpha = Mathf.Clamp01(guest.Entrance * 5f);
            PoseWalker(guest.Actor, guest.Sprite, Vector2.LerpUnclamped(guest.Entry, guest.Mark, progress),
                heading, walking, guest.Entrance * 22f);
        }

        private bool IsRpgHero(string name) => name == _rpgHeroName || name?.StartsWith("주인공", StringComparison.Ordinal) == true ||
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
            _rpgDialogueCast = _rpgDialogueOpponent = _rpgSpeakerCue = null;
            _rpgDialogueGuests.Clear();
        }
    }
}
