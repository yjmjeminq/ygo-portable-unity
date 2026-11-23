using System;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    internal sealed class OfflineRankedUI
    {
        private static readonly Color Gold = new Color32(248, 194, 87, 255);
        private static readonly Color Mint = new Color32(135, 226, 202, 255);
        private static readonly Color Loss = new Color32(249, 149, 139, 255);
        private static readonly Color[] TierColors =
        {
            new Color32(211, 151, 110, 255), new Color32(203, 220, 230, 255), Gold,
            new Color32(113, 226, 198, 255), new Color32(141, 198, 255, 255), new Color32(240, 183, 255, 255),
        };

        private readonly GameAppController _app;
        private readonly System.Random _random = new();
        private readonly RectTransform _lobby, _versus, _result, _reset;
        private readonly Text _season, _rank, _emblem, _progress, _notice, _streak, _record, _status, _emptyHistory;
        private readonly Text _deckName, _deckAce, _vsMine, _vsOpponent, _vsDeck, _vsNotice, _coinText, _toss;
        private readonly Text _resetText, _resultHeading, _resultRank, _resultDetails, _resultBadge;
        private readonly Text[][] _history = new Text[10][];
        private readonly Image[] _wins = new Image[5];
        private readonly Image _opponentPortrait;
        private readonly Image _rankRing;
        private readonly Image _lobbyStakes, _versusStakes;
        private readonly RectTransform _coin;
        private readonly CompositeCardView _deckPreview;
        private readonly Button _queue, _previous, _next, _resetSeason, _first, _second, _retry, _return;
        private UserDeck[] _decks = Array.Empty<UserDeck>();
        private int _index;
        private RankedMatch _match, _lastMatch;
        private RankedOutcome _lastOutcome;
        private float _tossAge;
        private bool _tossing;

        internal GameObject Root { get; }
        internal GameObject ActiveRoot => (_result.gameObject.activeSelf ? _result :
            _reset.gameObject.activeSelf ? _reset : _versus.gameObject.activeSelf ? _versus : _lobby).gameObject;

        internal OfflineRankedUI(GameAppController app, Transform parent, bool? mobileLayout = null)
        {
            _app = app;
            var root = LotdUiKit.FillRect("Screen_OfflineRanked", parent);
            Root = root.gameObject;
            DeckEditorStyle.Backdrop(root);
            var area = (mobileLayout ?? Application.isMobilePlatform) ? MobileSafeArea.Create(root) : root;

            _lobby = LotdUiKit.FillRect("Lobby", area);
            Title(_lobby, "랭크 매치", () => _app.ShowScreen(AppScreen.MainMenu));
            _season = Label(_lobby, "Season", .415f, .914f, .80f, .975f, 18, center: false, tone: Mint);
            var rankPanel = Panel(_lobby, "Rank", .029f, .379f, .468f, .881f);
            var insignia = Square(Slot(rankPanel, "Insignia", .29f, .425f, .71f, .94f));
            _rankRing = DuelStyle.AddHexPlate(insignia, new Color(.33f, .57f, .62f), new Color(.085f, .163f, .204f));
            _emblem = Label(insignia, "Emblem", .09f, .26f, .91f, .73f, 64, tone: Gold);
            _rank = Label(rankPanel, "RankLabel", .05f, .275f, .95f, .407f, 37);
            _streak = Label(rankPanel, "Streak", .66f, .848f, .96f, .959f, 21, tone: Gold);
            _progress = Label(rankPanel, "Progress", .06f, .195f, .94f, .277f, 21, tone: LotdUiKit.TextMuted);
            for (int i = 0; i < _wins.Length; i++)
            {
                float x = .15f + i * .143f;
                _wins[i] = DeckEditorStyle.Fill(Slot(rankPanel, "Win_" + i, x, .154f, x + .121f, .174f), Mint, false);
            }
            _notice = Label(rankPanel, "Promotion", .07f, .021f, .93f, .138f, 19, tone: Gold);
            _lobbyStakes = DeckEditorStyle.Fill((RectTransform)_notice.rectTransform.parent, Color.clear, false);

            var deckPanel = Panel(_lobby, "Deck", .029f, .118f, .468f, .362f);
            var art = Slot(deckPanel, "AceCard", .043f, .074f, .253f, .925f);
            _deckPreview = CompositeCardView.Create(art, "Card");
            Label(deckPanel, "Heading", .28f, .751f, .86f, .944f, 21, "출전 덱", false);
            _deckName = Label(deckPanel, "DeckName", .28f, .478f, .94f, .744f, 27, center: false);
            _deckAce = Label(deckPanel, "AceName", .28f, .364f, .94f, .481f, 15, center: false, tone: LotdUiKit.TextMuted);
            _previous = Button(deckPanel, "Previous", "◀", .28f, .10f, .435f, .302f, () => ChangeDeck(-1));
            _next = Button(deckPanel, "Next", "▶", .45f, .10f, .605f, .302f, () => ChangeDeck(1));
            Button(deckPanel, "Edit", "덱 편집", .621f, .10f, .95f, .302f, () => _app.OpenRankedDeckEditor());

            var history = Panel(_lobby, "History", .482f, .118f, .971f, .881f);
            Label(history, "Title", .043f, .888f, .54f, .966f, 26, "랭크 전적", false);
            _record = Label(history, "Record", .047f, .765f, .953f, .885f, 19, center: false, tone: LotdUiKit.TextMuted);
            Label(history, "HistoryTitle", .047f, .688f, .95f, .756f, 17, "최근 10경기", false, Mint);
            var headers = Slot(history, "Columns", .045f, .645f, .955f, .684f);
            Label(headers, "Result", .013f, 0f, .108f, 1f, 13, "결과", tone: LotdUiKit.TextMuted);
            Label(headers, "Order", .113f, 0f, .203f, 1f, 13, "순서", tone: LotdUiKit.TextMuted);
            Label(headers, "Player", .219f, 0f, .486f, 1f, 13, "출전 덱", false, LotdUiKit.TextMuted);
            Label(headers, "Opponent", .50f, 0f, .985f, 1f, 13, "상대 캐릭터 · 덱", false, LotdUiKit.TextMuted);
            _emptyHistory = Label(history, "Empty", .09f, .365f, .91f, .625f, 17, "기록된 랭크 경기가 없습니다.", tone: LotdUiKit.TextMuted);
            for (int i = 0; i < 10; i++)
            {
                float top = .644f - i * .061f;
                var row = Slot(history, "Match_" + i, .045f, top - .058f, .955f, top);
                DeckEditorStyle.Fill(row, i % 2 == 0 ? DeckEditorStyle.Inset : DeckEditorStyle.Surface, false);
                _history[i] = new[]
                {
                    Label(row, "Outcome", .013f, .05f, .108f, .95f, 16),
                    Label(row, "Turn", .113f, .05f, .203f, .95f, 14, tone: LotdUiKit.TextMuted),
                    Label(row, "MyDeck", .219f, .06f, .486f, .94f, 14, center: false),
                    Label(row, "OpponentDeck", .50f, .035f, .985f, .965f, 15, center: false),
                };
            }
            _queue = Button(_lobby, "Queue", "랭크 매칭 시작", .029f, .033f, .286f, .098f, Begin, true);
            _resetSeason = Button(_lobby, "ResetSeason", "새 시즌 시작", .302f, .033f, .467f, .098f, ShowReset);
            _status = Label(_lobby, "Status", .485f, .032f, .973f, .103f, 16, center: false, tone: LotdUiKit.TextMuted);

            _versus = LotdUiKit.FillRect("Versus", area);
            DeckEditorStyle.Backdrop(_versus);
            Title(_versus, "랭크 매칭", Back);
            var mine = Panel(_versus, "Player", .046f, .235f, .343f, .83f);
            var opponent = Panel(_versus, "Opponent", .657f, .235f, .954f, .83f);
            _vsMine = Label(mine, "Identity", .06f, .06f, .94f, .31f, 31);
            var playerMark = Slot(mine, "Profile", .22f, .38f, .78f, .84f);
            DuelStyle.AddHexPlate(playerMark, Mint, new Color(.077f, .148f, .185f));
            var profile = Slot(playerMark, "Portrait", .10f, .10f, .90f, .90f);
            LotdUiKit.AddImage(profile.gameObject, LotdUiKit.LoadSprite("LOTD/Busts/yamiyugi_neutral") ??
                LotdUiKit.LoadSprite("LOTD/Busts/atem_neutral"), Color.white, true, false);
            var portrait = Slot(opponent, "Portrait", .06f, .31f, .94f, .96f);
            _opponentPortrait = LotdUiKit.AddImage(portrait.gameObject, null, Color.white, true, false);
            _vsOpponent = Label(opponent, "Name", .045f, .175f, .955f, .315f, 31);
            _vsDeck = Label(opponent, "Deck", .05f, .045f, .95f, .17f, 17, tone: LotdUiKit.TextMuted);
            Label(_versus, "VS", .375f, .708f, .625f, .835f, 67, "VS", tone: Gold);
            _vsNotice = Label(_versus, "Stakes", .18f, .848f, .82f, .905f, 24, tone: Gold);
            _versusStakes = DeckEditorStyle.Fill((RectTransform)_vsNotice.rectTransform.parent, Color.clear, false);
            _coin = Square(Slot(_versus, "Coin", .429f, .376f, .571f, .636f));
            LotdUiKit.AddImage(_coin.gameObject, DuelStyle.Disc, Gold, true, false);
            var face = LotdUiKit.FillRect("MintedFace", _coin, 6f);
            LotdUiKit.AddImage(face.gameObject, DuelStyle.Disc, new Color(.25f, .16f, .082f), true, false);
            _coinText = Label(face, "Face", .08f, .13f, .92f, .86f, 64, "★", tone: Gold);
            _toss = Label(_versus, "TossResult", .28f, .236f, .72f, .351f, 27);
            _first = Button(_versus, "First", "선공", .303f, .095f, .491f, .187f, () => Start(true), true);
            _second = Button(_versus, "Second", "후공", .509f, .095f, .697f, .187f, () => Start(false));

            _result = Dialog(area, "RankedResult");
            var results = Panel(_result, "Window", .229f, .157f, .771f, .838f);
            _resultHeading = Label(results, "Heading", .06f, .804f, .94f, .961f, 46, tone: Gold);
            var resultEmblem = Square(Slot(results, "Emblem", .414f, .579f, .586f, .793f));
            DuelStyle.AddHexPlate(resultEmblem, Gold, new Color(.22f, .151f, .069f));
            _resultBadge = Label(resultEmblem, "Mark", .09f, .065f, .91f, .935f, 48, "★", tone: Gold);
            _resultRank = Label(results, "Rank", .05f, .452f, .95f, .562f, 31, tone: Mint);
            _resultDetails = Label(results, "Rewards", .082f, .219f, .918f, .449f, 22);
            _retry = Button(results, "Retry", "저장 재시도", .105f, .073f, .46f, .174f, CompleteResult);
            _return = Button(results, "Continue", "랭크 로비로", .535f, .073f, .893f, .174f, Back, true);

            _reset = Dialog(area, "ResetRankedSeason");
            var confirm = Panel(_reset, "Window", .255f, .236f, .745f, .765f);
            Label(confirm, "Title", .11f, .743f, .89f, .923f, 32, "새 시즌 시작");
            _resetText = Label(confirm, "Summary", .10f, .342f, .90f, .729f, 21);
            Button(confirm, "Cancel", "취소", .105f, .12f, .47f, .268f, Back);
            Button(confirm, "Apply", "시즌 시작", .526f, .12f, .891f, .268f, ResetSeason, true);
            Close();
        }

        internal void Open()
        {
            Root.SetActive(true);
            Refresh();
            Focus(_queue.interactable ? _queue : _next.interactable ? _next : _lobby.Find("Deck/Edit/Button").GetComponent<Button>());
        }

        internal void Close()
        {
            _tossing = false;
            _match = _lastMatch = null;
            _versus.gameObject.SetActive(false);
            _result.gameObject.SetActive(false);
            _reset.gameObject.SetActive(false);
            _lobby.gameObject.SetActive(true);
            Root.SetActive(false);
        }

        internal bool CanStart(RankedMatch match) => ReferenceEquals(_match, match) && _versus.gameObject.activeInHierarchy;
        internal void ShowStatus(string status) => _status.text = status ?? "";

        private void Refresh()
        {
            var state = CollectionProgress.Ranked;
            _decks = DeckRepository.UserDecks.Where(deck => GameAppController.CanStartWithDeck(deck, out _)).ToArray();
            string preferred = state.deckId ?? CollectionProgress.LastDuelDeckId;
            _index = Math.Max(0, Array.FindIndex(_decks, deck => deck.id == preferred));
            _rank.text = OfflineRanked.Label(state.rank);
            _rank.color = _emblem.color = TierColors[state.rank / 5];
            _rankRing.color = _rank.color;
            _emblem.text = state.rank == OfflineRanked.Champion ? "★" : "◆";
            _season.text = $"시즌 {state.season}   ·   오프라인" +
                (state.champions > 0 ? $"   ·   시즌 {state.championSeasons.Last()} 챔피언" : "");
            _streak.text = state.streak >= 2 ? $"{state.streak}연승 중" : "";
            bool champion = state.rank == OfflineRanked.Champion;
            int needed = OfflineRanked.WinsNeeded(state.rank);
            _progress.text = champion
                ? $"챔피언 레이팅 {state.rating:N0}   ·   최고 {state.bestRating:N0}"
                : $"승점 {state.points} / {needed}   ·   승급까지 {OfflineRanked.WinsUntilPromotion(state)}승";
            for (int i = 0; i < _wins.Length; i++)
            {
                _wins[i].gameObject.SetActive(!champion && i < needed);
                _wins[i].color = i < state.points ? _rank.color : DeckEditorStyle.Inset;
                var bar = _wins[i].rectTransform;
                float left = .5f - (needed * .143f - .022f) * .5f + i * .143f;
                bar.anchorMin = new Vector2(left, bar.anchorMin.y);
                bar.anchorMax = new Vector2(left + .121f, bar.anchorMax.y);
            }
            ShowStakes(_notice, _lobbyStakes, state);
            if (champion)
            {
                _notice.text = LocalRatingPosition(state.rating);
                _notice.color = Mint;
            }
            double decisions = (double)state.wins + state.losses;
            double rate = decisions == 0 ? 0d : state.wins * 100d / decisions;
            _record.text = $"{state.wins:N0}승  {state.losses:N0}패  {state.draws:N0}무  ·  승률 {rate:0.0}%\n" +
                $"최고 {OfflineRanked.Label(state.bestRank)}  ·  {state.bestStreak}연승  ·  시즌 챔피언 {state.champions}회";
            _emptyHistory.gameObject.SetActive(state.history.Count == 0);
            for (int i = 0; i < _history.Length; i++)
            {
                var row = _history[i];
                row[0].transform.parent.parent.gameObject.SetActive(i < state.history.Count);
                if (i >= state.history.Count) continue;
                var entry = state.history[i];
                row[0].text = Outcome(entry.outcome);
                row[0].color = entry.outcome == RankedOutcome.Win ? Mint : entry.outcome == RankedOutcome.Loss ? Loss : Color.white;
                row[1].text = entry.playerFirst ? "선공" : "후공";
                row[2].text = entry.playerDeck;
                row[3].text = $"{entry.opponent}\n{entry.opponentDeck}";
            }
            _resetSeason.gameObject.SetActive(OfflineRanked.CanReset(state));
            _status.text = _decks.Length == 0 ? DuelDeckAvailability.ExplainEmptyList(preferred) :
                $"보유 {CollectionProgress.Balance:N0} DP";
            ShowDeck();
        }

        private void ChangeDeck(int step)
        {
            if (_decks.Length < 2) return;
            int next = (_index + step + _decks.Length) % _decks.Length;
            if (!CollectionProgress.TrySelectRankedDeck(_decks[next], out string error)) { ShowStatus(error); return; }
            _index = next;
            ShowDeck();
        }

        private void ShowDeck()
        {
            var selected = _decks.ElementAtOrDefault(_index);
            uint ace = OfflineRanked.Ace(selected);
            _deckName.text = selected == null ? "출전 가능한 덱 없음" : $"{selected.name}  ·  {_index + 1}/{_decks.Length}";
            _deckAce.text = ace == 0 ? "" : $"에이스  {CardDatabase.GetName(ace)}";
            _deckPreview.gameObject.SetActive(ace != 0);
            if (ace != 0)
            {
                var copy = selected.Copies().First(entry => entry.Code == ace);
                _deckPreview.SetDeckCard(ace, copy.Appearance);
            }
            _previous.interactable = _next.interactable = _decks.Length > 1;
            _queue.interactable = selected != null;
        }

        private void Begin()
        {
            if (_versus.gameObject.activeSelf) return;
            var deck = _decks.ElementAtOrDefault(_index);
            if (!CollectionProgress.TrySelectRankedDeck(deck, out string error)) { ShowStatus(error); return; }
            var state = CollectionProgress.Ranked;
            if (OfflineRanked.Pick(state, _random) is not { } opponent)
            {
                ShowStatus("이 티어에 등록된 상대 덱을 불러올 수 없습니다.");
                return;
            }

            _match = new RankedMatch(state, opponent, deck, _random.Next(2) == 0);
            _vsMine.text = $"플레이어\n{OfflineRanked.Label(state.rank)}";
            _vsOpponent.text = opponent.Character.name;
            _vsDeck.text = $"{(state.rank == OfflineRanked.Champion ? $"AI 레이팅 {opponent.Rating}" : $"Lv.{opponent.Level}  ·  {OfflineRanked.Label(state.rank)}")}\n{opponent.Deck.title}";
            ShowStakes(_vsNotice, _versusStakes, state);
            _opponentPortrait.sprite = LotdUiKit.LoadOpponentPortrait(opponent.Character);
            _toss.text = "코인 토스";
            _coinText.text = "★";
            _coin.localScale = Vector3.one;
            _first.gameObject.SetActive(false);
            _second.gameObject.SetActive(false);
            _versus.gameObject.SetActive(true);
            _lobby.gameObject.SetActive(false);
            _tossAge = 0f;
            _tossing = true;
            EventSystem.current?.SetSelectedGameObject(null);
            LotdAudio.PlayChain();
            LotdAudio.PlayCoin();
            UiEntranceMotion.Play(_versus.gameObject, .72f, .20f);
        }

        internal void Update(float delta)
        {
            float glow = .82f + .16f * Mathf.Sin(Time.unscaledTime * 4.5f);
            Pulse(_lobbyStakes, glow);
            Pulse(_versusStakes, glow);
            if (!_tossing || _match == null) return;
            _tossAge += delta;
            float phase = Mathf.Clamp01(_tossAge / 1.1f);
            float spin = phase * Mathf.PI * 7f;
            _coin.localScale = new Vector3(Mathf.Max(.06f, Mathf.Abs(Mathf.Cos(spin))), 1f + .17f * Mathf.Sin(phase * Mathf.PI), 1f);
            _coinText.text = Mathf.Cos(spin) > 0f ? "★" : "Ⅱ";
            if (phase < 1f) return;
            _coin.localScale = Vector3.one;
            _coinText.text = _match.PlayerWonToss ? "★" : "Ⅱ";
            if (_match.PlayerWonToss)
            {
                _tossing = false;
                _toss.text = "토스 승리 · 선공 또는 후공을 선택하세요";
                _first.gameObject.SetActive(true);
                _second.gameObject.SetActive(true);
                Focus(_first);
                return;
            }
            bool opponentFirst = OfflineRanked.OpponentTakesFirst(_match.Opponent.Deck);
            _toss.text = opponentFirst ? "상대가 선공을 선택했습니다" : "상대가 후공을 선택했습니다";
            if (_tossAge >= 1.94f) Start(!opponentFirst);
        }

        private void Start(bool playerFirst)
        {
            _tossing = false;
            if (_match == null) return;
            _match.PlayerFirst = playerFirst;
            _app.StartRankedDuel(_match);
        }

        internal void ShowResult(RankedMatch match, RankedOutcome outcome)
        {
            _lastMatch = match;
            _lastOutcome = outcome;
            _result.gameObject.SetActive(true);
            CompleteResult();
        }

        private void CompleteResult()
        {
            if (_lastMatch == null) return;
            bool saved = CollectionProgress.TryFinishRankedMatch(_lastMatch, _lastOutcome, out var prize, out string error);
            _retry.gameObject.SetActive(!saved);
            _resultHeading.text = saved && prize.Promoted ? "TIER UP!" : Outcome(_lastOutcome);
            _resultRank.text = saved ? OfflineRanked.Label(prize.RankAfter) : "경기 기록 저장 실패";
            _resultBadge.color = saved ? TierColors[prize.RankAfter / 5] : LotdUiKit.TextMuted;
            _resultDetails.text = !saved ? error :
                $"{(prize.Demoted ? "디비전 하락\n" : "")}+{prize.Dp:N0} DP  ·  +{prize.Dust:N0} 가루" +
                (prize.Card == 0 ? "" : $"\n획득  {CardDatabase.GetName(prize.Card)}{(prize.Overframe ? " · 오버프레임" : "")}") +
                (prize.Promoted && prize.RankAfter == OfflineRanked.Champion ? "\n챔피언 칭호 · 레이팅 1,500 해금" :
                    prize.RankBefore == OfflineRanked.Champion ? $"\n레이팅 {prize.RatingChange:+0;-0;0}" : "");
            if (saved)
            {
                _lastMatch = null;
                Refresh();
                if (prize.Promoted) LotdAudio.PlayWin();
            }
            Focus(saved ? _return : _retry);
            UiEntranceMotion.Play(_result.gameObject, .60f, .22f);
        }

        private void ShowReset()
        {
            var state = CollectionProgress.Ranked;
            if (!OfflineRanked.CanReset(state)) return;
            _resetText.text = $"시즌 {state.season + 1}을 {OfflineRanked.Label((state.rank / 5 - 2) * 5)}에서 시작합니다.\n" +
                "현재 승점과 레이팅을 초기화하고 승급 보상을 다시 받을 수 있습니다.\n최고 기록과 챔피언 칭호는 보존됩니다.";
            _reset.gameObject.SetActive(true);
            Focus(_reset.Find("Window/Cancel/Button").GetComponent<Button>());
        }

        private void ResetSeason()
        {
            bool saved = CollectionProgress.TryResetRankedSeason(out string error);
            Back();
            if (!saved) ShowStatus(error);
            else ShowStatus($"시즌 {CollectionProgress.Ranked.season} 시작");
        }

        internal void Back()
        {
            if (_versus.gameObject.activeSelf || _result.gameObject.activeSelf || _reset.gameObject.activeSelf)
            {
                _tossing = false;
                _match = _lastMatch = null;
                _versus.gameObject.SetActive(false);
                _result.gameObject.SetActive(false);
                _reset.gameObject.SetActive(false);
                _lobby.gameObject.SetActive(true);
                Open();
                return;
            }
            _app.ShowScreen(AppScreen.MainMenu);
        }

        private static void ShowStakes(Text caption, Image banner, RankedSave state)
        {
            bool promotion = OfflineRanked.PromotionWin(state);
            bool demotion = OfflineRanked.DemotionLoss(state);
            banner.enabled = promotion || demotion;
            banner.color = promotion ? new Color(.30f, .187f, .041f, .91f) : new Color(.32f, .138f, .145f, .91f);
            caption.color = promotion ? Gold : Loss;
            caption.text = promotion ? $"승급전  ·  승리 시 {OfflineRanked.Label(state.rank + 1)}" :
                demotion ? "강등 위기  ·  패배 시 한 디비전 하락" : "";
        }

        private static void Pulse(Image banner, float alpha)
        {
            if (banner.isActiveAndEnabled) banner.canvasRenderer.SetAlpha(alpha);
        }

        private static string LocalRatingPosition(int rating)
        {
            var pool = OfflineRanked.Pool(OfflineRanked.Champion);
            return $"오프라인 AI 순위  {1 + pool.Count(opponent => opponent.Rating > rating)} / {pool.Count + 1}";
        }

        internal static GameObject AddChampionBadge(Transform parent, Vector2 min, Vector2 max)
        {
            var badge = DuelStyle.Panel(parent, "RankedChampion", min, max, fill: new Color(.19f, .12f, .039f), border: Gold);
            badge.GetComponent<Image>().raycastTarget = false;
            LotdUiKit.AddFittedText(badge, "Title", "★  MASTER I 챔피언", 20, 9,
                TextAnchor.MiddleCenter, Gold, FontStyle.Bold);
            return badge.gameObject;
        }

        private static string Outcome(RankedOutcome value) =>
            value == RankedOutcome.Win ? "승리" : value == RankedOutcome.Loss ? "패배" : "무승부";

        private static void Title(Transform parent, string text, Action back)
        {
            Label(parent, "Title", .029f, .897f, .41f, .976f, 38, text, false);
            Button(parent, "Back", "뒤로", .839f, .908f, .970f, .972f, back);
        }

        private static RectTransform Dialog(Transform parent, string name)
        {
            var overlay = LotdUiKit.FillRect(name, parent);
            DeckEditorStyle.Fill(overlay, DeckEditorStyle.Scrim);
            return overlay;
        }

        private static RectTransform Square(RectTransform holder)
        {
            var face = LotdUiKit.FillRect("Face", holder);
            var fit = face.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fit.aspectRatio = 1f;
            return face;
        }

        private static RectTransform Slot(Transform parent, string name, float x1, float y1, float x2, float y2) =>
            LotdUiKit.CreateRect(name, parent, new Vector2(x1, y1), new Vector2(x2, y2), Vector2.zero, Vector2.zero);
        private static RectTransform Panel(Transform parent, string name, float x1, float y1, float x2, float y2) =>
            DeckEditorStyle.Panel(parent, name, new Vector2(x1, y1), new Vector2(x2, y2));
        private static Text Label(Transform parent, string name, float x1, float y1, float x2, float y2, int size,
            string text = "", bool center = true, Color? tone = null) =>
            LotdUiKit.AddFittedText(Slot(parent, name, x1, y1, x2, y2), "Text", text, size, Math.Min(size, 10),
                center ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft, tone ?? LotdUiKit.TextPrimary, FontStyle.Bold);
        private static Button Button(Transform parent, string name, string text, float x1, float y1, float x2, float y2,
            Action action, bool primary = false) =>
            DeckEditorStyle.Button(Slot(parent, name, x1, y1, x2, y2), "Button", text, action, 22, primary);
        private static void Focus(Button button) => EventSystem.current?.SetSelectedGameObject(button.gameObject);
    }
}
