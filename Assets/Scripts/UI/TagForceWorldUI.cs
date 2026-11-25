using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    internal sealed class TagForceWorldUI
    {
        private enum Page { SeriesSelect, Map, Field, Room, Mail, Ledger, Talk, Topics, DialogueLog, Shop, Gift, Duel, Bond }

        private readonly GameAppController _app;
        private readonly TagForceWorldImages _images = new();
        private readonly RectTransform _screen, _body, _navigation;
        private readonly Text _header, _status;
        private readonly List<(Vector2 Point, Action Action)> _interactions = new();
        private readonly List<RectTransform> _visitors = new();
        private readonly List<string> _dialogueLog = new();
        private TagForceData _data;
        private TagForceManager _manager;
        private Page _page;
        private string _selectedId, _effect, _reply, _deckId;
        private TagForceBondEvent _bond;
        private bool _grandPrix;
        private int _deckLevel = 1;
        private Vector2 _position = new(.50f, .16f), _destination, _partnerPosition;
        private bool _moving, _manualWalk, _fieldMenu;
        private RectTransform _player, _partner, _playerSprite, _partnerSprite;
        private Text _fieldControls;
        private Action _onArrival;
        private Text _dialogue;
        private string _dialogueLine;
        private int[] _dialogueLetters;
        private float _dialogueProgress;
        private int _visibleDialogueLetters;

        private TagForceSave State => _manager.Progress;
        private TagForceSpot CurrentSpot => _manager.CurrentSpot;
        private TagForcePersona Selected => _data.Persona(_selectedId);
        internal GameObject Root => _screen.gameObject;
        internal bool CanWalk => _page == Page.Field && _player != null;

        internal TagForceWorldUI(GameAppController app, Transform parent)
        {
            _app = app;
            _screen = DuelUiKit.FillRect("Screen_TagForceWorld", parent);
            Tint(_screen, new Color(.027f, .064f, .102f));
            var layout = Application.isMobilePlatform ? MobileSafeArea.Create(_screen) : _screen;
            _header = Label(Area(layout, "Header", .05f, .899f, .78f, .975f), "", 24, TextAnchor.MiddleLeft);
            Btn(Area(layout, "Back", .82f, .908f, .95f, .967f), "뒤로", Back);
            _body = Area(layout, "World", .05f, .174f, .95f, .895f);
            _status = Label(Area(layout, "Status", .05f, .112f, .95f, .168f), "", 17, TextAnchor.MiddleLeft);

            _navigation = Area(layout, "WorldNavigation", .05f, .027f, .95f, .101f);
            string[] labels = { "월드맵", "내 방", "PDA 메일", "인연 명부", "매점", "덱 편집", "시리즈 선택" };
            Action[] actions = { () => Show(Page.Map), () => Show(Page.Room), () => Show(Page.Mail),
                () => Show(Page.Ledger), () => Show(Page.Shop), _app.OpenTagForceDeckEditor, ShowSeriesSelect };
            for (int i = 0; i < labels.Length; i++)
            {
                float x = (float)i / labels.Length;
                Btn(Area(_navigation, labels[i], x, 0, (float)(i + 1) / labels.Length - .005f, 1), labels[i], actions[i]);
            }
            Root.SetActive(false);
        }

        internal void Open(bool resume = false)
        {
            Root.SetActive(true);
            _status.text = "";
            if (resume && _data != null) Show(Page.Field);
            else ShowSeriesSelect();
        }

        internal void Close()
        {
            Root.SetActive(false);
            ClearBody();
            _images.Clear();
        }

        internal void Back()
        {
            if (_page == Page.SeriesSelect)
            {
                _app.ShowScreen(AppScreen.MainMenu);
                return;
            }
            if (_page == Page.Map) { ShowSeriesSelect(); return; }
            Show(_page is Page.Topics or Page.DialogueLog or Page.Gift or Page.Bond or Page.Duel ? Page.Talk :
                _page == Page.Talk ? Page.Field : Page.Map);
        }

        internal void ShowStatus(string message) => _status.text = message;

        private void ShowSeriesSelect()
        {
            _data = null;
            _manager = null;
            ShowStatus("");
            Show(Page.SeriesSelect);
        }

        private void OpenSeries(TagForceSeries series)
        {
            _data = TagForceData.Load(Application.streamingAssetsPath, series);
            if (_data == null)
            {
                ShowStatus("태그 포스 월드 데이터를 읽을 수 없습니다.");
                return;
            }
            _manager = new TagForceManager(_data);
            _selectedId = _effect = _reply = null;
            _bond = null;
            _grandPrix = false;
            _position = new(.50f, .16f);
            Show(Page.Field);
            ShowStatus("");
        }

        internal void ShowResult(TagForceDuel duel, bool won, string result, bool rewarded)
        {
            _selectedId = duel.Persona.personaId;
            _status.text = result;
            _reply = won ? duel.Form.loseLine : duel.Form.winLine;
            var ending = rewarded && duel.GrandPrix && won && State.grandPrixRound == 0 ? duel.Partner?.grandPrixEnding : null;
            if (ending != null) _reply = PrintDialogue(ending);
            Show(Page.Field);
            if (!string.IsNullOrWhiteSpace(_reply))
                Label(Area(_body, "ResultDialogue", .13f, .77f, .88f, .96f), _reply, 19);
            if (rewarded) DuelAudio.PlayTagForceReward();
        }

        private void Show(Page page)
        {
            _page = page;
            ClearBody();
            _images.BeginPage();
            _navigation.gameObject.SetActive(page != Page.SeriesSelect);
            if (page == Page.SeriesSelect)
            {
                _header.text = "태그 포스 월드";
                DuelAudio.PlayTagForceBgm(false);
                BuildSeriesSelect();
                _images.Trim();
                return;
            }
            var state = State;
            DuelAudio.PlayTagForceBgm(state.time == TagForceData.TimeKeys.Length - 1);
            string partner = _data.Persona(State.partnerId)?.displayName ?? "없음";
            string[] weekdays = { "월", "화", "수", "목", "금", "토", "일" };
            var series = TagForceData.Series.First(entry => entry.Id == _data.seriesId);
            _header.text = $"{series.Title}  ·  {state.day}일차 ({weekdays[(state.day - 1) % 7]})  ·  {TagForceData.TimeNames[state.time]}  ·  " +
                $"{CollectionProgress.Balance:N0} DP  ·  파트너 {partner}";
            switch (page)
            {
                case Page.Map: BuildMap(); break;
                case Page.Field: BuildField(); break;
                case Page.Room: BuildRoom(); break;
                case Page.Mail: BuildMail(); break;
                case Page.Ledger: BuildLedger(); break;
                case Page.Talk: BuildTalk(); break;
                case Page.Topics: BuildTopics(); break;
                case Page.DialogueLog: BuildDialogueLog(); break;
                case Page.Gift: BuildGifts(); break;
                case Page.Duel: BuildDuelChoice(); break;
                case Page.Bond: BuildBond(); break;
                case Page.Shop: BuildShop(); break;
            }
            _images.Trim();
        }

        private void ClearBody()
        {
            _interactions.Clear();
            _visitors.Clear();
            _player = _partner = _playerSprite = _partnerSprite = null;
            _fieldControls = null;
            _dialogue = null;
            _dialogueLine = null;
            _dialogueLetters = null;
            _moving = _manualWalk = _fieldMenu = false;
            _onArrival = null;
            for (int i = _body.childCount - 1; i >= 0; i--)
            {
                GameObject child = _body.GetChild(i).gameObject;
                child.SetActive(false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(child);
                else UnityEngine.Object.DestroyImmediate(child);
            }
        }

        private bool Commit(TagForceSave next, int dp = 0, uint reward = 0, bool foil = false, uint gift = 0)
        {
            if (_manager.TrySave(next, dp, out string error, reward, foil, gift))
            {
                if (dp > 0 || reward != 0) DuelAudio.PlayTagForceReward();
                return true;
            }
            _status.text = error;
            return false;
        }

        private void GoTo(string spot, string invite = null, string mailId = null)
        {
            var next = State.Copy();
            next.spotId = spot;
            if (invite != null)
            {
                next.inviteId = invite;
                next.inviteSpot = spot;
                if (!next.seenMail.Contains(mailId)) next.seenMail.Add(mailId);
            }
            if (!Commit(next)) return;
            _position = new(.50f, .16f);
            _reply = "";
            Show(Page.Field);
        }

        private void BuildSeriesSelect()
        {
            Tint(DuelUiKit.FillRect("SeriesBackground", _body), new Color(.06f, .11f, .16f));
            var series = TagForceData.Series;
            for (int i = 0; i < series.Length; i++)
            {
                var entry = series[i];
                float x = .038f + i % 3 * .317f;
                float y = i < 3 ? .535f : .09f;
                var tile = Area(_body, entry.AssetFolder, x, y, x + .29f, y + .365f);
                Btn(tile, $"{entry.Title}\n{entry.Region}", () => OpenSeries(entry), 26);
            }
        }

        private void BuildMap()
        {
            Backdrop(_data.assetConfig.worldMapPath, new Color(.14f, .31f, .37f));
            var spots = _data.spots;
            for (int i = 0; i < spots.Length; i++)
            {
                var spot = spots[i];
                float x = .035f + i % 3 * .319f;
                float y = i < 3 ? .535f : .065f;
                var tile = Area(_body, spot.spotId, x, y, x + .294f, y + .405f);
                Tint(tile, new Color(.031f, .124f, .169f, .97f));
                Label(Area(tile, "Spot", .04f, .78f, .96f, .97f), spot.name, 18, TextAnchor.MiddleLeft);
                var present = _data.Visitors(State, spot.spotId).Take(4).ToArray();
                float col = .042f;
                foreach (var (persona, form) in present)
                {
                    var chip = Area(tile, persona.personaId, col, .29f, col + .215f, .76f);
                    Portrait(chip, form.sdSpritePath, form.formName);
                    string notices = persona.NextBond(State.Relationship(persona.personaId)) != null ? " !" : "";
                    if (_data.Mail(State).Any(entry => entry.Persona == persona && entry.Mail.spotId == spot.spotId))
                        notices += " 메일";
                    Label(Area(tile, "Hearts", col, .215f, col + .215f, .34f),
                        $"{Hearts(persona)}{notices}", 12);
                    col += .229f;
                }
                Btn(Area(tile, "Visit", .04f, .039f, .96f, .205f), "이동", () => GoTo(spot.spotId));
            }
        }

        private void BuildField()
        {
            var spot = CurrentSpot;
            var floor = Backdrop(spot.mapPath, new Color(.14f, .24f, .25f));
            floor.raycastTarget = true;
            var tap = floor.gameObject.AddComponent<TagForceFloorTap>();
            tap.Click = point => MoveTo(point);
            Label(Area(_body, "SpotName", .02f, .88f, .60f, .99f), spot.name, 21, TextAnchor.MiddleLeft);
            Label(Area(_body, "SpotSubtitle", .02f, .83f, .68f, .885f), spot.subtitle, 15, TextAnchor.MiddleLeft);
            _fieldControls = Label(Area(_body, "FieldControls", .02f, .012f, .98f, .082f),
                "방향키 · 스틱 이동   선택 조사/대화   위쪽 버튼 메뉴", 15, TextAnchor.MiddleRight);
            if (_data.seriesId == TagForceData.GxId && spot.spotId == "spot_main_academy")
                Btn(Area(_body, "GrandPrix", .76f, .87f, .98f, .97f), "GX 그랑프리", BeginGrandPrix);

            var present = _data.Visitors(State, spot.spotId).ToArray();
            for (int i = 0; i < present.Length; i++)
            {
                var (persona, form) = present[i];
                var slot = new Vector2(.11f + i % 7 * .125f, .62f - i / 7 * .142f);
                var figure = Anchor(_body, persona.personaId, slot, new Vector2(110, 108));
                var face = Area(figure, "Face", .08f, .21f, .92f, 1);
                if (Portrait(face, form.sdSpritePath, form.formName)) _visitors.Add(face);
                Btn(Area(figure, "Talk", 0, 0, 1, .25f), form.formName, () => MoveTo(slot, () => Talk(persona)));
                _interactions.Add((slot, () => Talk(persona)));
            }
            for (int i = 0; i < (spot.defaultPickups?.Length ?? 0); i++)
            {
                int index = i;
                string key = spot.spotId + ":" + i;
                if (State.pickups.Contains(key)) continue;
                var position = new Vector2(.24f + i * .255f, .245f + (i % 2) * .09f);
                var button = Anchor(_body, "Pickup" + i, position, new Vector2(82, 30));
                Btn(button, "조사", () => MoveTo(position, () => Pickup(spot, index)));
                _interactions.Add((position, () => Pickup(spot, index)));
            }

            var partner = _data.Persona(State.partnerId);
            if (partner != null)
            {
                var form = partner.DisplayForm(State);
                _partnerPosition = _position - new Vector2(.06f, 0);
                _partner = Anchor(_body, "Partner", _partnerPosition, new Vector2(72, 94));
                _partnerSprite = WalkerSprite(_partner, form.sdSpritePath, form.formName);
            }
            _player = Anchor(_body, "Player", _position, new Vector2(83, 106));
            _playerSprite = WalkerSprite(_player, TagForceData.PlayerSd, TagForceData.ProtagonistName);
        }

        private RectTransform WalkerSprite(RectTransform actor, string path, string name)
        {
            var visual = DuelUiKit.FillRect("Sprite", actor);
            return Portrait(visual, path, name) ? visual : null;
        }

        private void Pickup(TagForceSpot spot, int index)
        {
            var next = State.Copy();
            string key = spot.spotId + ":" + index;
            if (next.pickups.Contains(key)) return;
            next.pickups.Add(key);
            string item = spot.defaultPickups[index];
            int dp = item switch { "dp_small" => 150, "dp_medium" => 350, "dp_large" => 600, _ => 0 };
            uint card = index == 2 && State.day % 3 == 0 ? CollectionProgress.TagForceBonusCard() : 0;
            if (dp == 0 && card == 0) next.ChangeItem(item, 1);
            if (Commit(next, dp, card))
            {
                if (dp == 0 && card == 0) DuelAudio.PlayTagForceReward();
                ShowStatus(card != 0 ? $"{CardDatabase.GetName(card)} 획득" :
                    dp > 0 ? $"+{dp} DP" : $"{_data.Item(item)?.name ?? "아이템"} 획득");
                Show(Page.Field);
            }
        }

        private void MoveTo(Vector2 point, Action arrive = null)
        {
            _manualWalk = false;
            _destination = new Vector2(Mathf.Clamp(point.x, .06f, .94f), Mathf.Clamp(point.y, .095f, .77f));
            _moving = true;
            _onArrival = arrive;
        }

        internal void Update(float delta, Vector2 padDirection = default)
        {
            UpdateDialogue(delta);
            if (!CanWalk) return;
            Vector2 movement = _fieldMenu ? Vector2.zero : padDirection;
            if (!_fieldMenu && movement == Vector2.zero)
            {
                movement = new Vector2(
                    (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1 : 0) -
                        (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1 : 0),
                    (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1 : 0) -
                        (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1 : 0));
            }
            if (movement != Vector2.zero)
            {
                MoveTo(_position + movement.normalized * .12f);
                _manualWalk = true;
            }
            else if (_manualWalk)
            {
                _manualWalk = _moving = false;
            }
            AdvanceWalk(delta, Time.time);
            for (int i = 0; i < _visitors.Count; i++)
                _visitors[i].anchoredPosition = Vector2.up * (1.6f * Mathf.Sin(Time.time * 3f + i));
        }

        private void AdvanceWalk(float delta, float time)
        {
            Vector2 previous = _position;
            bool arrived = false;
            if (_moving)
            {
                _position = Vector2.MoveTowards(_position, _destination, delta * .40f);
                arrived = _position == _destination;
                _moving = !arrived;
            }
            float phase = time * 16f;
            PoseWalker(_player, _playerSprite, _position, _position - previous, _moving, phase);
            if (_partner != null)
            {
                Vector2 before = _partnerPosition;
                Vector2 follow = _position - before;
                if (follow.magnitude > .064f)
                    _partnerPosition = Vector2.MoveTowards(before, _position - follow.normalized * .064f, delta * .46f);
                Vector2 step = _partnerPosition - before;
                bool walking = step.sqrMagnitude > 1e-8f &&
                    (_moving || (_position - _partnerPosition).sqrMagnitude > .065f * .065f);
                PoseWalker(_partner, _partnerSprite, _partnerPosition, step, walking, phase + .8f);
            }
            if (!arrived) return;
            var action = _onArrival;
            _onArrival = null;
            action?.Invoke();
        }

        private static void PoseWalker(RectTransform actor, RectTransform sprite, Vector2 position, Vector2 step,
            bool walking, float phase)
        {
            actor.anchorMin = actor.anchorMax = position;
            actor.anchoredPosition = Vector2.up * (walking ? Mathf.Sin(phase) * 6f : 0f);
            if (sprite != null && Mathf.Abs(step.x) > 1e-5f)
                sprite.localScale = new Vector3(step.x < 0f ? -1f : 1f, 1f, 1f);
        }

        internal bool HandleFieldInput(DeckPadFrame frame)
        {
            if (!CanWalk) return false;
            if (frame.Search)
            {
                _fieldMenu = !_fieldMenu;
                _manualWalk = _moving = false;
                _onArrival = null;
                _fieldControls.text = _fieldMenu ? "위쪽 버튼으로 보행 복귀" : "방향키 · 스틱 이동   선택 조사/대화   위쪽 버튼 메뉴";
                return true;
            }
            if (_fieldMenu) return false;
            if (frame.Submit && _interactions.Count > 0)
            {
                var closest = _interactions.OrderBy(entry => Vector2.SqrMagnitude(entry.Point - _position)).First();
                MoveTo(closest.Point, closest.Action);
            }
            return true;
        }

        private void BuildRoom()
        {
            Backdrop(_data.assetConfig.myRoomPath, new Color(.37f, .225f, .22f));
            Label(Area(_body, "Room", .07f, .67f, .94f, .91f), "내 기숙사 방", 38, TextAnchor.MiddleLeft);
            Btn(Area(_body, "Rest", .10f, .3f, .53f, .51f), "침대에서 휴식하기", () =>
            {
                var next = State.Copy();
                next.Advance();
                if (!Commit(next)) return;
                ShowStatus($"시간이 지났습니다. 도착한 메일 {_data.Mail(State).Count()}통");
                Show(Page.Room);
            });
            Btn(Area(_body, "CheckAssets", .61f, .30f, .94f, .51f), "추가 에셋 다시 읽기", () =>
            {
                ClearBody();
                _images.Clear();
                Show(Page.Room);
                ShowStatus("PNG를 다시 불러왔습니다.");
            });
        }

        private void BuildMail()
        {
            var list = List("Mail", .01f, .07f, .99f, .95f);
            foreach (var (persona, mail) in _data.Mail(State))
                Row(list, $"{(State.seenMail.Contains(mail.mailId) ? "" : "새 메일 · ")}{mail.subject}\n{mail.body}",
                    () => GoTo(mail.spotId, persona.personaId, mail.mailId), 76);
            if (list.childCount == 0) Row(list, "이 시간대에 도착한 메일이 없습니다.", null);
        }

        private void BuildLedger()
        {
            var list = List("Ledger", .01f, .07f, .99f, .95f);
            foreach (var persona in _data.personas)
            {
                string schedule = string.Join(" · ", persona.forms.Select(form => form.formName + " " +
                    string.Join("/", form.activeTimes.Select(time => TagForceData.TimeNames[Array.IndexOf(TagForceData.TimeKeys, time)])) +
                    " " + string.Join("/", form.spots.Select(id => _data.Spot(id)?.name))));
                Row(list, $"{persona.displayName}  {Hearts(persona)}\n{schedule}\n선호 {_data.conversationTopics.FirstOrDefault(t => t.topicId == persona.favoriteTopic)?.name} · " +
                    $"{_data.Item(persona.favoriteGiftId)?.name}",
                    () =>
                    {
                        var progress = State.Relationship(persona.personaId);
                        var events = persona.bondEvents.Where(bond => progress.clearedBonds.Contains(bond.heartLevel));
                        string memory = string.Join("\n", events.Select(PrintDialogue));
                        if (progress.wonGrandPrix || progress.Hearts == TagForceRelation.HeartCount) memory += "\n" + PrintDialogue(persona.grandPrixEnding);
                        ShowStatus(string.IsNullOrWhiteSpace(memory) ? "아직 회상할 인연 이벤트가 없습니다." : "");
                        if (string.IsNullOrWhiteSpace(memory)) return;
                        ClearBody();
                        Row(List("Memory", .05f, .06f, .95f, .97f), memory, null, 360);
                    }, 88);
            }
        }

        private void Talk(TagForcePersona persona)
        {
            _dialogueLog.Clear();
            _selectedId = persona.personaId;
            _grandPrix = false;
            _bond = null;
            _effect = null;
            _reply = "";
            Show(Page.Talk);
        }

        private void BuildTalk()
        {
            var persona = Selected;
            if (persona == null) { Show(Page.Field); return; }
            var form = persona.DisplayForm(State);
            var relationship = State.Relationship(persona.personaId);
            Backdrop(CurrentSpot.mapPath, new Color(.11f, .235f, .27f));
            Portrait(Area(_body, "Protagonist", .005f, .18f, .30f, .97f),
                TagForceData.PlayerStanding, TagForceData.ProtagonistName);
            Portrait(Area(_body, "Guest", .70f, .18f, .995f, .97f), form.standingSpritePath, form.formName);
            var panel = Area(_body, "Dialogue", .29f, .04f, .71f, .97f);
            Tint(panel, new Color(.027f, .089f, .128f, .96f));
            Label(Area(panel, "Name", .05f, .89f, .95f, .99f), form.formName, 23);
            Label(Area(panel, "Title", .05f, .825f, .95f, .89f), form.formTitle, 16);
            Label(Area(panel, "Hearts", .05f, .75f, .95f, .825f),
                $"{Hearts(persona)}   {relationship.affinity:N0} / {TagForceRelation.MaxAffinity:N0} pt", 19);
            string line = string.IsNullOrEmpty(_reply) ? form.Greet(relationship.Hearts) : _reply;
            if (!string.IsNullOrWhiteSpace(line) && (_dialogueLog.Count == 0 || _dialogueLog[^1] != line))
            {
                if (_dialogueLog.Count == 16) _dialogueLog.RemoveAt(0);
                _dialogueLog.Add(line);
            }
            TypeDialogue(Area(panel, "DialogueText", .06f, .54f, .94f, .74f), line);

            var actions = List("DialogueActions", .30f, .062f, .70f, .53f);
            Row(actions, "이야기하기", () => Show(Page.Topics));
            Row(actions, "샌드위치·선물 주기", () => Show(Page.Gift));
            Row(actions, "듀얼 신청", () => { _deckLevel = 1; Show(Page.Duel); });
            Row(actions, State.partnerId == persona.personaId ? "파트너 해제" : "파트너 신청", () => SetPartner(persona));
            Row(actions, "대화 기록", () => Show(Page.DialogueLog));
            if (persona.NextBond(relationship) is { } pending)
                Row(actions, "인연 승급전", () => { _bond = pending; Show(Page.Bond); });
            if (relationship.masterReward || relationship.wonGrandPrix || relationship.Hearts == TagForceRelation.HeartCount)
                Row(actions, "파트너 마스터 덱 레시피 받기", () => CopyMasterDeck(persona));
        }

        private void BuildTopics()
        {
            var persona = Selected;
            if (persona == null) { Show(Page.Field); return; }
            var list = List("Topics", .10f, .10f, .90f, .91f);
            Row(list, persona.displayName + "에게 이야기할 주제", null, 50);
            foreach (var topic in _data.conversationTopics)
                Row(list, topic.name, () => Discuss(persona, topic), 52);
        }

        private void BuildDialogueLog()
        {
            var list = List("DialogueLog", .10f, .10f, .90f, .91f);
            Row(list, Selected?.displayName + " · 대화 기록", null, 50);
            foreach (var line in _dialogueLog)
                Row(list, line, null, 64);
        }

        private void TypeDialogue(RectTransform area, string line)
        {
            _dialogue = Label(area, "", 18, TextAnchor.UpperLeft);
            _dialogue.supportRichText = true;
            _dialogueLine = line ?? "";
            _dialogueLetters = StringInfo.ParseCombiningCharacters(_dialogueLine);
            _visibleDialogueLetters = 0;
            _dialogueProgress = 0f;
            PaintDialogue();
        }

        private void UpdateDialogue(float delta)
        {
            if (_dialogue == null || _visibleDialogueLetters == _dialogueLetters.Length) return;
            _dialogueProgress += delta * 32f;
            int before = _visibleDialogueLetters;
            _visibleDialogueLetters = Math.Min((int)_dialogueProgress, _dialogueLetters.Length);
            if (_visibleDialogueLetters == before) return;
            if (before == 0 || before / 3 != _visibleDialogueLetters / 3) DuelAudio.PlayTick();
            PaintDialogue();
        }

        private void PaintDialogue()
        {
            int count = _visibleDialogueLetters;
            _dialogue.text = count == _dialogueLetters.Length ? _dialogueLine :
                $"{_dialogueLine[.._dialogueLetters[count]]}<color=#FFFFFF00>{_dialogueLine[_dialogueLetters[count]..]}</color>";
        }

        private void Discuss(TagForcePersona persona, TagForceTopic topic)
        {
            if (!_manager.TryTalk(persona, topic, out var line, out uint reward, out var error))
            {
                ShowStatus(error);
                return;
            }
            DuelAudio.PlayClick();
            ShowAffinityReward(reward);
            _reply = line;
            Show(Page.Talk);
        }

        private void BuildGifts()
        {
            var persona = Selected;
            if (persona == null) { Show(Page.Map); return; }
            var list = List("Gifts", .025f, .08f, .975f, .95f);
            foreach (var inventory in State.items.ToArray())
            {
                var item = _data.Item(inventory.id);
                if (item == null) continue;
                var row = Row(list, $"{item.name} · {inventory.count}개   {item.desc}", () =>
                    Gift(persona, item, 0), 56);
                Icon(row, item.iconPath);
            }
            foreach (var card in CardDatabase.AllCards.Where(card => CollectionProgress.GetOwnedCount(card.code) > 3)
                .OrderBy(card => card.name, StringComparer.CurrentCulture))
                Row(list, $"{card.name} · 여분 {CollectionProgress.GetOwnedCount(card.code) - 3}장",
                    () => Gift(persona, null, card.code));
            if (list.childCount == 0) Row(list, "선물할 빵, 물건 또는 여분 카드가 없습니다.", null);
        }

        private void Gift(TagForcePersona persona, TagForceItem item, uint card)
        {
            if (!_manager.TryGive(persona, item, card, out var line, out uint reward, out var error))
            {
                ShowStatus(error);
                return;
            }
            DuelAudio.PlayClick();
            ShowAffinityReward(reward);
            _reply = line;
            Show(Page.Talk);
        }

        private void ShowAffinityReward(uint reward)
        {
            if (reward != 0)
            {
                DuelAudio.PlayTagForceReward();
                ShowStatus($"마스터 달성 · 오버프레임 {CardDatabase.GetName(reward)}");
            }
        }

        private void SetPartner(TagForcePersona persona)
        {
            if (!_manager.TrySetPartner(persona, out var line, out var error))
            {
                ShowStatus(error);
                return;
            }
            _reply = line;
            Show(Page.Talk);
        }

        private void BuildBond()
        {
            var list = List("Bond", .04f, .05f, .96f, .97f);
            Row(list, PrintDialogue(_bond), null, 240);
            Row(list, _bond.choice.prompt, null, 52);
            foreach (var option in _bond.choice.options)
                Row(list, option.text, () =>
                {
                    _effect = option.effectType;
                    _reply = option.reply;
                    _deckLevel = 1;
                    DuelAudio.PlayClick();
                    Show(Page.Duel);
                }, 54);
        }

        private void BuildDuelChoice()
        {
            var persona = Selected;
            if (persona == null) { Show(Page.Field); return; }
            var form = persona.DisplayForm(State);
            var partner = _data.Persona(State.partnerId);
            int unlocked = _grandPrix ? form.Decks.Count : Math.Min(form.Decks.Count, State.Relationship(persona.personaId).unlockedLevel);
            _deckLevel = _grandPrix ? unlocked : Math.Clamp(_deckLevel, 1, Math.Max(1, unlocked));
            string headline = (_grandPrix ? $"그랑프리 {State.grandPrixRound + 1}/3 라운드 · " : "") + form.formName;
            string passive = partner == null ? "" :
                $"파트너 · {partner.partnerSkillName}\n시작 LP +{partner.OpeningLifeBonus:N0} · 시작 패 {partner.OpeningHand}장" +
                (partner.DestinyLimit > 2000 ? $" · 데스티니 드로우 {partner.DestinyLimit:N0} LP" : "");
            Label(Area(_body, "Match", .02f, .74f, .98f, .985f), $"{headline}\n{form.duelStartLine}\n{_reply}\n{passive}", 20);
            var yourDecks = DeckRepository.UserDecks.Where(deck => GameAppController.CanStartWithDeck(deck, out _)).ToArray();
            var playerDeck = yourDecks.FirstOrDefault(d => d.id == _deckId) ??
                yourDecks.FirstOrDefault(d => d.id == CollectionProgress.LastDuelDeckId) ?? yourDecks.FirstOrDefault();
            var player = List("YourDeck", .02f, .15f, .49f, .72f);
            foreach (var deck in yourDecks)
                Row(player, $"{(deck == playerDeck ? "✓ " : "")}{deck.name}", () => { _deckId = deck.id; Show(Page.Duel); });
            if (yourDecks.Length == 0) Row(player, "사용 가능한 내 덱이 없습니다.", null);
            var rival = List("RivalDeck", .51f, .15f, .98f, .72f);
            for (int i = _grandPrix ? Math.Max(0, unlocked - 1) : 0; i < unlocked; i++)
            {
                int level = i + 1;
                Row(rival, $"{(level == _deckLevel ? "✓ " : "")}Lv.{level} · {form.Decks[i].title}",
                    () => { _deckLevel = level; Show(Page.Duel); });
            }
            var start = Btn(Area(_body, "Start", .35f, .015f, .65f, .135f), "듀얼 시작", () =>
            {
                uint support = partner != null ? CollectionProgress.TagForceAce(partner.DisplayForm(State)) : 0;
                _app.StartTagForceDuel(new TagForceDuel(_data.seriesId, persona, form, partner, form.Decks[_deckLevel - 1], _deckLevel,
                    _bond, _effect, _grandPrix, support), playerDeck);
            });
            start.interactable = playerDeck != null && unlocked != 0;
        }

        private void BeginGrandPrix()
        {
            var partner = _data.Persona(State.partnerId);
            if (partner == null || State.Relationship(partner.personaId).Hearts < 4)
            {
                ShowStatus("하트 4개 이상인 파트너의 동행이 필요합니다.");
                return;
            }
            var roster = _data.personas.Where(persona => persona != partner)
                .OrderByDescending(persona => persona.DisplayForm(State).Decks.Count).ToArray();
            if (roster.Length <= State.grandPrixRound) return;
            _selectedId = roster[State.grandPrixRound].personaId;
            _grandPrix = true;
            _bond = null;
            _reply = "";
            _effect = null;
            Show(Page.Duel);
        }

        private void CopyMasterDeck(TagForcePersona persona)
        {
            var preset = persona.DisplayForm(State).Decks.LastOrDefault();
            if (preset == null) return;
            var deck = new UserDeck
            {
                id = Guid.NewGuid().ToString("N"),
                name = persona.displayName + " 마스터 레시피",
                main = preset.main.ToList(),
                extra = preset.extra.ToList(),
                side = preset.side.ToList(),
            };
            try
            {
                DeckRepository.SaveUserDeck(deck);
                DeckRepository.UserDecks.Add(deck);
                _app.OpenTagForceDeckEditor();
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                ShowStatus("덱 레시피를 저장할 수 없습니다.");
            }
        }

        private void BuildShop()
        {
            Backdrop(_data.assetConfig.breadShopPath, new Color(.41f, .25f, .19f));
            var shop = _data.drawBreadCatalog;
            if (shop == null)
            {
                Label(_body, "이 월드의 매점은 준비 중입니다.", 24);
                return;
            }
            Label(Area(_body, "ShopTitle", .05f, .74f, .95f, .97f),
                $"{shop.shopkeeperName}\n{shop.shopkeeperGreeting}\n남은 봉지 {12 - State.bags.Count}/12 · {shop.drawPriceDp} DP", 21);
            for (int i = 0; i < 12; i++)
            {
                int bag = i;
                float x = .06f + i % 6 * .149f;
                float y = i < 6 ? .473f : .252f;
                bool opened = State.bags.Contains(i);
                var button = Btn(Area(_body, "Bag" + i, x, y, x + .137f, y + .191f),
                    opened ? "개봉 완료" : $"빵 봉지 {i + 1}", () => DrawBread(bag));
                button.interactable = !opened && CollectionProgress.Balance >= shop.drawPriceDp;
            }
            var gifts = List("ShopGifts", .055f, .025f, .945f, .231f);
            foreach (var item in _data.gifts)
                Icon(Row(gifts, $"{item.name} · {item.priceDp} DP", () =>
                {
                    var next = State.Copy();
                    next.ChangeItem(item.Id, 1);
                    if (Commit(next, -item.priceDp))
                    {
                        DuelAudio.PlayTagForceReward();
                        ShowStatus($"{item.name} 구입");
                        Show(Page.Shop);
                    }
                }), item.iconPath);
        }

        private void DrawBread(int bag)
        {
            var next = State.Copy();
            if (next.bags.Contains(bag)) return;
            var shop = _data.drawBreadCatalog;
            int weight = shop.breads.Sum(item => Math.Max(0, item.weight));
            var bread = _data.DrawBread(UnityEngine.Random.Range(0, weight));
            next.bags.Add(bag);
            next.ChangeItem(bread.Id, 1);
            uint card = bread.bonusRandomCard ? CollectionProgress.TagForceBonusCard() : 0;
            if (!Commit(next, -shop.drawPriceDp, card)) return;
            if (card == 0) DuelAudio.PlayTagForceReward();
            ShowStatus(card == 0 ? $"{bread.name} 획득!" : $"{bread.name} · {CardDatabase.GetName(card)} 획득!");
            Show(Page.Shop);
        }

        private string Hearts(TagForcePersona persona)
        {
            int count = State.Relationship(persona.personaId).Hearts;
            return new string('♥', count) + new string('♡', TagForceRelation.HeartCount - count);
        }

        private static string PrintDialogue(TagForceBondEvent story) =>
            story.title + "\n" + string.Join("\n", story.dialogues.Select(line => $"{line.speaker}  {line.text}"));

        private Image Backdrop(string path, Color fallback)
        {
            var sprite = _images.Get(path);
            return DuelUiKit.AddImage(DuelUiKit.FillRect("Scenery", _body).gameObject, sprite,
                sprite == null ? fallback : Color.white, false, false);
        }

        private bool Portrait(RectTransform rect, string path, string personaName)
        {
            var sprite = _images.Get(path);
            if (sprite != null)
            {
                DuelUiKit.AddImage(rect.gameObject, sprite, Color.white, true, false);
                return true;
            }
            Tint(rect, new Color(.14f, .32f, .39f, .94f));
            Label(rect, personaName, 16);
            return false;
        }

        private void Icon(RectTransform root, string path)
        {
            var sprite = _images.Get(path);
            if (sprite == null) return;
            var icon = Area(root, "ItemArt", .016f, .07f, .107f, .93f);
            DuelUiKit.AddImage(icon.gameObject, sprite, Color.white, true, false);
            var label = root.GetComponentInChildren<Text>().rectTransform;
            label.anchorMin = new Vector2(.13f, label.anchorMin.y);
        }

        private RectTransform List(string name, float l, float b, float r, float t)
        {
            var viewport = Area(_body, name, l, b, r, t);
            viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            var items = DuelUiKit.CreateRect("Items", viewport, Vector2.up, Vector2.one, Vector2.zero, Vector2.zero);
            items.pivot = Vector2.up;
            items.gameObject.AddComponent<VerticalLayoutGroup>().spacing = 4;
            items.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = items;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28;
            return items;
        }

        private static RectTransform Row(Transform parent, string text, Action click, float height = 37)
        {
            var row = DuelUiKit.FillRect("Row", parent);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
            if (click == null) Label(row, text, 17, TextAnchor.MiddleLeft);
            else Btn(row, text, click);
            return row;
        }

        private static RectTransform Anchor(Transform parent, string name, Vector2 point, Vector2 size)
        {
            var rect = DuelUiKit.CreateRect(name, parent, point, point, Vector2.zero, Vector2.zero);
            rect.sizeDelta = size;
            return rect;
        }

        private static RectTransform Area(Transform parent, string name, float l, float b, float r, float t) =>
            DuelUiKit.CreateRect(name, parent, new(l, b), new(r, t), Vector2.zero, Vector2.zero);

        private static Button Btn(RectTransform rect, string text, Action action, int size = 17)
        {
            var background = DuelUiKit.AddImage(rect.gameObject, DuelUiKit.WhiteSprite, new Color(.12f, .31f, .39f));
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(() => action());
            Label(rect, text, size);
            return button;
        }

        private static Text Label(RectTransform rect, string text, int size, TextAnchor align = TextAnchor.MiddleCenter) =>
            DuelUiKit.AddFittedText(rect, "Label", text, size, Math.Max(10, size - 8), align,
                new Color(.95f, .97f, .93f), FontStyle.Bold);
        private static void Tint(RectTransform rect, Color color) =>
            DuelUiKit.AddImage(rect.gameObject, DuelUiKit.WhiteSprite, color, false, false);
    }

    internal sealed class TagForceFloorTap : MonoBehaviour, IPointerClickHandler
    {
        internal Action<Vector2> Click;
        public void OnPointerClick(PointerEventData eventData)
        {
            var field = (RectTransform)transform;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(field, eventData.position, eventData.pressEventCamera, out var point))
                Click?.Invoke(Rect.PointToNormalized(field.rect, point));
        }
    }

    internal sealed class TagForceWorldImages
    {
        private readonly Dictionary<string, Sprite> _loaded = new();
        private readonly HashSet<string> _visible = new();

        internal void BeginPage() => _visible.Clear();

        internal Sprite Get(string relative)
        {
            if (string.IsNullOrWhiteSpace(relative) || !relative.StartsWith("TagForce/", StringComparison.Ordinal) ||
                !relative.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                relative.IndexOfAny(new[] { '\\', ':' }) >= 0 || relative.Split('/').Contains("..")) return null;
            _visible.Add(relative);
            if (_loaded.TryGetValue(relative, out var result)) return result;
            result = Read(Path.Combine(Application.persistentDataPath, relative)) ??
                Read(Path.Combine(Application.streamingAssetsPath, relative));
            _loaded.Add(relative, result);
            return result;
        }

        private static Sprite Read(string file)
        {
            Texture2D texture = null;
            bool keep = false;
            try
            {
                if (!File.Exists(file) || new FileInfo(file).Length > 20 * 1024 * 1024) return null;
                byte[] bytes = File.ReadAllBytes(file);
                if (bytes.Length < 24 || bytes[0] != 137 || bytes[1] != 80 || bytes[2] != 78 || bytes[3] != 71)
                    return null;
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!texture.LoadImage(bytes, true)) return null;
                var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.50f, .50f));
                keep = sprite != null;
                return sprite;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException)
            {
                Debug.LogWarning($"[TagForceWorld] {Path.GetFileName(file)}: {error.Message}");
                return null;
            }
            finally
            {
                if (!keep && texture != null) DestroyAsset(texture);
            }
        }

        internal void Clear()
        {
            _visible.Clear();
            Trim();
        }

        internal void Trim()
        {
            foreach (string path in _loaded.Keys.Where(path => !_visible.Contains(path)).ToArray())
            {
                var sprite = _loaded[path];
                _loaded.Remove(path);
                if (sprite == null) continue;
                var texture = sprite.texture;
                DestroyAsset(sprite);
                DestroyAsset(texture);
            }
        }

        private static void DestroyAsset(UnityEngine.Object asset)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(asset);
            else UnityEngine.Object.DestroyImmediate(asset);
        }
    }
}
