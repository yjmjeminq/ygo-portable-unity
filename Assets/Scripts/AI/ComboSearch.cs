using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using YGOPortable.Core;

namespace YGOPortable.AI
{
    internal sealed class ComboSearch
    {
        internal const float MaxPlanningScale = 1.65f;
        internal const int MasterPlanningMilliseconds = 1000;
        internal const int PlanningSliceMilliseconds = 8;
        // Leave time inside the decision budget to finish a native call and submit the selected response.
        internal const int MasterSearchDeadlineMilliseconds = MasterPlanningMilliseconds - 2 * PlanningSliceMilliseconds;
        internal const int KeepSetup = -1; // Planner decision, never sent to the duel engine.
        private const int MaxMainActions = 24;
        private const int AlternateOpeningLimit = 3;
        private const int MaxPlannedSelections = 2;
        private const int MaxProjectedPublicResponses = 2;
        internal const int DefaultResponseLimit = 144;
        internal const int RpgResponseLimit = 128;
        private const int MaxLateLinkSearches = 2;
        private const int BranchMilliseconds = 46;
        private const int OpeningDevelopmentBranchMilliseconds = 105;
        internal const int LinkOpeningPlanningMilliseconds = 360;
        private const int LinkOpeningBranchMilliseconds = 80;
        private const int LateLinkBranchMilliseconds = 48;
        private const int RecoveryBranchMilliseconds = 110;
        private const uint Machinex = 46593546, Siegfried = 44852429, Gilgamesh = 9024198;
        private const uint PhotonJumper = 97639441, MicroCoder = 2347477, JunkAnchor = 25148255, JunkSynchron = 63977008;
        private const uint RedDragonArchfiend = 70902743, Almiraj = 60303245;
        private const uint NegatesEffects = 1u << 8;
        private static readonly HashSet<uint> HandDisruption = new()
        {
            14558127, 23434538, 97268402, 10045474, 24224830, 73642296, 27204311, 94145021,
            42141493, 59438930, 38814750, 10443957, PhotonJumper, 52038441, 84192580, 87126721,
        };

        // High-impact boss / omni-negate / floodgate endboard monsters (best for Turn 1 / setup)
        private static readonly HashSet<uint> Tier1EndboardBosses = new()
        {
            84815190, // 플뢰르 드 바로네스
            37818794, // 초마도용기사-붉은 눈의 드라군
            21123811, // 코즈믹 블레이저 드래곤
            89474727, // 진섬광룡 스타더스트 크로니클
            9753964,  // 염마룡 레드 데몬 어비스
            99585850, // 스카레드 슈퍼노바 드래곤
            58481572, // 마스크드 히어로 다크 로우
            60461804, // 데스티니 히어로 디스트로이 피닉스 가이
            59822133, // 푸른 눈의 정령룡
            89604813, // 궁극의 푸른 눈의 영룡
            19652159, // 라이트 앤드 다크니스 드래곤로드
            47297616, // 라이트 앤드 다크니스 드래곤
            4280258,  // 소명의 신궁-아폴로우사
            29301450, // 에스:피 리틀나이트
            65741786, // 아이:피 마스카레나
            63436931, // 붉은 용
            26692769, // 팬텀 나이츠 러스티 바르디시
            61245672, // 디코드 토커 히트소울
            80453041, // 팬텀 오브 유벨
            47172959, // 유벨-다스 에비히 리베 베히터
            44146295, // 빙검룡 미라제이드
            38811586, // 진염룡 알비온
            24915933, // 혁언룡 그랑기뇰
            6855503,  // 혁작룡 마스카레이드
            48626373, // 크샤트리라 어라이즈하트
            73542331, // 크샤트리라 샹그릴라
            32909498, // 크샤트리라 펜리르
            82135803, // 데먼스미스 디에스이레
            79559912, // DDD 노도대왕 이그제큐티브 시져
            48546368, // 얼티미트 디클레어러
            44665365, // 퍼펙트 디클레어러
            10443957, // 사이버 드래곤 인피니티
            84330567, // 티아라멘츠 루루칼로스
            27548199, // 바렐로드 새비지 드래곤
            50954680, // 크리스탈윙 싱크로 드래곤
            56863746, // 드라이트론-메테오니스=DAD
            35952884, // 슈팅 퀘이사 드래곤
            40939228, // 슈팅 세이비어 스타 드래곤
            29981921, // 진 여섯 무사-Shi En
            33198837, // 나츄르 비스트
            69248256, // 상검대사-적소
            47710198, // 상검대사-칠성용연
            96633955, // 상검대공-승영
            27572350, // 비스테드 디스 파테르
            63767246, // No.38 희망괴룡 타이타닉 갤럭시
            8165596,  // No.90 갤럭시아이즈 포톤 로드
            34909328, // 라이제올 데드네이더
            93672138, // Evil★Twin's 트러블 써니
            75286621, // 소환수 메르카바
        };

        // Dedicated going-second board-breakers / OTK finishers
        private static readonly HashSet<uint> GoingSecondFinishers = new()
        {
            86066372, // 액세스코드 토커
            56832966, // SNo.39 유토피아 더 라이트닝
            64276752, // 아크 리벨리온 엑시즈 드래곤
            32828466, // Wake Up Your 엘리멘틀 히어로
            29095552, // 마스크드 히어로 애시드
            39765958, // 염마룡 레드 데몬
            90448279, // 네가로기어 아제우스
            93039339, // 로기어스텔라 티 폰
            21637210, // 파이어월 드래곤 싱귤래리티
            68934651, // 파이어월 드래곤 다크플루이드
        };

        // Graveyard floodgate / anti-GY bosses
        private static readonly HashSet<uint> GraveyardHateBosses = new()
        {
            58481572, // 마스크드 히어로 다크 로우
            59822133, // 푸른 눈의 정령룡
            96633955, // 상검대공-승영
            48626373, // 크샤트리라 어라이즈하트
        };

        private readonly float _planningScale;
        private readonly int _responseLimit;
        private readonly int _searchLimit;
        private Queue<(ulong state, int action)> _next = new();
        private List<SelectionOverride> _selectedTargets = new();
        private int _turn = -1;
        private int _searches;
        private int _lateLinkSearches;
        private int _interruptions;
        private DuelPrompt _prepared;
        internal bool ClearsBattleLethal { get; private set; }
        internal int SimulatedBranches { get; private set; }
        internal int ChangedLines { get; private set; }
        internal int ChosenAlternatives { get; private set; }

        internal ComboSearch(float planningScale = 1f, int responseLimit = DefaultResponseLimit)
        {
            _planningScale = Math.Clamp(planningScale, 1f, MaxPlanningScale);
            _responseLimit = responseLimit;
            _searchLimit = Scale(7);
        }

        private int Scale(int budget) => (int)Math.Round(budget * _planningScale);

        internal ComboSearch ForkForDetachedPlanning()
        {
            var copy = (ComboSearch)MemberwiseClone();
            copy._next = new Queue<(ulong, int)>(_next);
            copy._selectedTargets = new List<SelectionOverride>(_selectedTargets);
            return copy;
        }

        internal void RebindPreparedPrompt(DuelPrompt replica, DuelPrompt live)
        {
            if (ReferenceEquals(_prepared, replica)) _prepared = live;
        }

        private sealed class Line
        {
            internal double score = double.NegativeInfinity;
            internal bool complete;
            internal bool won;
            internal bool endPhaseDegradedBoard;
            internal bool needsHiddenInformation;
            internal bool verifiedDraw;
            internal bool exposedBeforeReveal;
            internal bool testedAttackPosition;
            internal bool measuredTurnEnd;
            internal bool unverifiedArchfiendEndPhase;
            internal Position ending;
            internal int unpaidAceTrade = int.MaxValue;
            internal Branch? defensiveSetFork;
            internal Branch? starterBeforeLink;
            internal int opposingCardsBeforeBattle = -1;
            internal HashSet<uint> battleAttackers;
            internal readonly List<(ulong state, int action)> moves = new();
            internal readonly List<(int[] prefix, int[] options)> forks = new();
            internal readonly List<(int[] Prefix, ulong State, int[] Options, SelectionOverride[] Prior)> targets = new();
            internal readonly List<SelectionOverride> selectedTargets = new();
        }

        private readonly struct SelectionOverride
        {
            internal readonly ulong State;
            internal readonly int Index;
            internal readonly int AfterMainActions;

            internal SelectionOverride(ulong state, int index, int afterMainActions)
            {
                State = state;
                Index = index;
                AfterMainActions = afterMainActions;
            }
        }

        private readonly struct Branch
        {
            internal readonly int[] MainActions;
            internal readonly SelectionOverride[] Targets;

            internal Branch(int[] mainActions, SelectionOverride[] targets = null)
            {
                MainActions = mainActions;
                Targets = targets ?? Array.Empty<SelectionOverride>();
            }
        }

        private readonly struct Position
        {
            internal readonly int rank, opposingCards, interaction, pressure, defense, defenders, monsters, hand;
            internal readonly int pendulumFollowUps;
            internal readonly bool lethal;
            internal readonly bool exposedToBattleLethal;
            internal readonly bool HasSetup;
            internal readonly bool lastDefenderAgainstLethal;
            internal readonly bool archfiendEndPhaseRisk;

            internal Position(DuelSession duel, byte player, bool canBattle)
            {
                var own = duel.Players[player];
                var rival = duel.Players[1 - player];
                int threat = 0, blockers = 0, rivalAttack = 0, rivalAttackTotal = 0;
                foreach (var monster in rival.mzone)
                {
                    if (monster == null) continue;
                    blockers++;
                    if (!monster.IsFaceUp) continue;
                    threat = Math.Max(threat, monster.IsDefense ? monster.defense : monster.attack);
                    if (monster.IsAttack)
                    {
                        rivalAttack = Math.Max(rivalAttack, monster.attack);
                        rivalAttackTotal += Math.Max(0, monster.attack);
                    }
                }
                int attackers = 0, establishedAttackers = 0, weakestAttack = int.MaxValue;
                bool activeArchfiend = false;
                HasSetup = false;
                exposedToBattleLethal = false;
                hand = own.hand.Count;
                rank = interaction = pressure = defense = defenders = monsters = 0;
                bool hasContract = own.szone.Any(card => card?.IsFaceUp == true && !card.effectsDisabled &&
                    DddTurnPlan.IsDarkContract(card.code));
                foreach (var monster in own.mzone)
                {
                    if (monster == null) continue;
                    monsters++;
                    if (monster.IsDefense) defenders++;
                    if (!monster.IsFaceUp) continue;
                    activeArchfiend |= monster.code == RedDragonArchfiend && !monster.effectsDisabled;
                    if (monster.IsAttack)
                    {
                        attackers += Math.Max(0, monster.attack);
                        weakestAttack = Math.Min(weakestAttack, Math.Max(0, monster.attack));
                        exposedToBattleLethal |= rivalAttack - Math.Max(0, monster.attack) >= own.lp;
                    }
                    if (monster.attack >= 1500 && monster.attack > threat && (!canBattle || monster.IsAttack))
                    {
                        pressure = Math.Max(pressure, monster.attack);
                        if (monster.attack >= 1800) establishedAttackers++;
                    }
                    if (monster.IsDefense && monster.defense >= Math.Max(1500, threat))
                        defense = Math.Max(defense, monster.defense);
                    rank = Math.Max(rank, LinkComboPolicy.Rank(monster.code));
                    if (!monster.effectsDisabled && CardDatabase.Get(monster.code) is CardInfo card &&
                    (HasFieldInteraction(card) || DuelTactics.CanNegateWithIllusionOfChaos(monster, own, rival)) &&
                    (card.code != Machinex || hasContract || monster.overlayCodes.Count >= 2))
                    {
                        interaction++;
                    HasSetup |= card.IsExtra || (card.type & YgoConstants.TYPE_RITUAL) != 0;
                    }
                }
                exposedToBattleLethal |= monsters == 0 && rivalAttackTotal >= own.lp ||
                    monsters == 1 && weakestAttack < rivalAttack && rivalAttackTotal - weakestAttack >= own.lp;
                lastDefenderAgainstLethal = monsters == 1 && defenders == 1 && rivalAttackTotal >= own.lp;
                opposingCards = blockers + rival.szone.Count(card => card != null);
                pendulumFollowUps = PendulumTurnPlan.ReadyFollowUps(own);
                lethal = canBattle && blockers == 0 && attackers >= rival.lp;
                archfiendEndPhaseRisk = activeArchfiend && monsters > 1;
                HasSetup |= rank >= 3 || pressure >= 2400 || defense >= 2400 || establishedAttackers >= 2 || pendulumFollowUps >= 2;
            }

            internal bool ImprovesOn(Position before) =>
                (lethal && !before.lethal) || opposingCards < before.opposingCards || interaction > before.interaction ||
                (interaction >= before.interaction && rank >= before.rank &&
                    (pressure > before.pressure || defense > before.defense ||
                        (rank >= 3 && rank > before.rank && pressure > 0) ||
                        (hand > before.hand && monsters >= before.monsters) || pendulumFollowUps > before.pendulumFollowUps));
        }

        // Material banishment and negating a monster revived on your own field do not interrupt the opponent.
        private static bool HasFieldInteraction(CardInfo card) => LinkComboPolicy.IsInteractive(card) &&
            CardDatabase.GetArchetypeCollectionKey(card) is not
                (JunkAnchor or JunkSynchron or 14558127 or 23434538 or 42141493 or 97268402 or 59438930 or 73642296 or 94145021);

        internal static bool CanDevelop(DuelPrompt prompt)
        {
            // A usable engine action is stronger evidence than the deck name or the contents of a filler Extra Deck.
            if (prompt.spSummonable.Any(card => card.controller == prompt.playerId)) return true;
            return prompt.activatable.Any(card => card.controller == prompt.playerId &&
                    (card.description == 1160 || CanDevelopFromCard(CardDatabase.Get(card.code)))) ||
                prompt.summonable.Any(card => card.controller == prompt.playerId && CanDevelopFromCard(CardDatabase.Get(card.code)));
        }

        internal static bool IsPublicFieldDraw(PlayerFieldSnapshot own, SelectableCardItem action) =>
            action.location == YgoConstants.LOCATION_SZONE && action.sequence < own.szone.Length &&
            own.szone[action.sequence] is { IsFaceUp: true, effectsDisabled: false } card && card.code == action.code &&
            ((CardDatabase.Get(action.code)?.DeckEffectCategories ?? 0) & (1u << 4)) != 0;

        internal static bool CanDevelopFromCard(CardInfo card) => card != null &&
            ((card.type & (YgoConstants.TYPE_TUNER | YgoConstants.TYPE_RITUAL)) != 0 ||
            (card.DeckEffectCategories & ((1u << 5) | (1u << 6) | (1u << 7) | (1u << 18))) != 0);

        internal static bool IsHandDisruption(uint code) => HandDisruption.Contains(code);
        internal static bool NeedsProvenNormalSummon(uint code) => IsHandDisruption(code) || code == MicroCoder;

        internal int? Choose(DuelSession live, DuelPrompt prompt, TacticalAI advisor)
        {
            if (!ReferenceEquals(_prepared, prompt))
            {
                using var prepare = Prepare(live, prompt, advisor).GetEnumerator();
                while (prepare.MoveNext()) { }
            }
            return TakePreparedAction(live, prompt);
        }

        internal int? TakePreparedAction(DuelSession live, DuelPrompt prompt) =>
            _next.Count > 0 && _next.Peek().state == Fingerprint(live, prompt) ? _next.Dequeue().action : null;

        internal int? TakePlannedSelection(DuelSession live, DuelPrompt prompt)
        {
            if (_turn != live.TurnNumber || _selectedTargets.Count == 0 ||
                prompt.messageType is not (YgoConstants.MSG_SELECT_CARD or YgoConstants.MSG_SELECT_UNSELECT_CARD or
                    YgoConstants.MSG_SELECT_OPTION or YgoConstants.MSG_SELECT_POSITION) ||
                !ReferenceEquals(live.ActivePrompt, prompt) || live.RetryCount != 0) return null;
            var target = _selectedTargets[0];
            if (Fingerprint(live, prompt) != target.State) return null;
            _selectedTargets.RemoveAt(0);
            ChosenAlternatives++;
            return target.Index;
        }

        internal IEnumerable<bool> Prepare(DuelSession live, DuelPrompt prompt, TacticalAI advisor,
            int masterBudget = MasterSearchDeadlineMilliseconds)
        {
            if (ReferenceEquals(_prepared, prompt)) yield break;
            _prepared = prompt;
            if (_turn != live.TurnNumber)
            {
                ClearsBattleLethal = false;
                _turn = live.TurnNumber;
                _searches = 0;
                _lateLinkSearches = 0;
                _interruptions = 0;
                _next.Clear();
                _selectedTargets.Clear();
            }

            bool recovering = live.TurnPlayer == prompt.playerId && live.TurnInterruptions > 0;
            if (recovering && _interruptions != live.TurnInterruptions)
            {
                ClearsBattleLethal = false;
                // Reserve a fresh search after the first disruption, even if the opening search ran out.
                if (_interruptions == 0) _searches = _lateLinkSearches = 0;
                _interruptions = live.TurnInterruptions;
                _next.Clear();
                _selectedTargets.Clear();
            }
            bool master = advisor.Difficulty == AIDifficulty.Master;
            var watch = new Stopwatch();
            if (master) watch.Start();
            ulong state = Fingerprint(live, prompt);
            var own = live.Players[prompt.playerId];
            bool keepResourceZoneOpen = DuelDeckStrategy.ShouldKeepMainMonsterZoneOpen(live, prompt.playerId);
            var held = recovering ? new Position(live, prompt.playerId, prompt.canBp) : default;
            double heldScore = recovering ? BoardValue(live, prompt.playerId, prompt.canBp, master) : 0;
            bool needsDefense = recovering && !held.HasSetup &&
                DuelCombat.VisibleAttackTotal(live.Players[1 - prompt.playerId]) >= own.lp;
            double AdditionalDefense(Position ending) =>
                needsDefense && !ending.exposedToBattleLethal && ending.rank >= held.rank && ending.interaction >= held.interaction
                    ? .38 * Math.Clamp(ending.defenders - held.defenders, 0, 2) : 0;
            bool CouldTrade(SelectableCardItem item) => LinkComboPolicy.NeedsMaterialProjection(own, item);
            bool cached = _next.Count > 0 && _next.Peek().state == state;
            if (cached && _next.Peek().action == KeepSetup) yield break;
            // An opening search may run out before the first strong Link monster reaches the board.
            bool lateLinkTrade = _searches >= _searchLimit && _lateLinkSearches < MaxLateLinkSearches &&
                prompt.spSummonable.Any(CouldTrade);
            if (cached && !lateLinkTrade) yield break;
            if (!cached)
            {
                ClearsBattleLethal = false;
                _next.Clear();
                _selectedTargets.Clear();
            }
            if (!lateLinkTrade && _searches >= _searchLimit) yield break;
            int alternativesPerFork = master ? 5 : 3;
            var options = advisor.ComboMainOptions(live, prompt);
            var lateOptions = Array.Empty<int>();
            if (lateLinkTrade)
            {
                var trades = options.Where(action => (action & 0xffff) == 1)
                    .Select(action => (action, summon: prompt.spSummonable[action >> 16]))
                    .Where(option => CouldTrade(option.summon));
                if (!advisor.UsesLinkPlannerPreference(live, prompt))
                    trades = trades.OrderByDescending(option => LinkComboPolicy.IsBridge(option.summon.code))
                        .ThenBy(option => LinkComboPolicy.Rank(option.summon.code));
                lateOptions = trades.Take(alternativesPerFork).Select(option => option.action).ToArray();
            }
            if (lateLinkTrade ? lateOptions.Length == 0 : options.Length == 0) yield break;
            if (lateLinkTrade) _lateLinkSearches++;
            else _searches++;

            int branchLimit = Scale(master ? 72 : 36);
            // Leave time to compare the action forks after the material selections in a Link opening.
            bool linkOpening = live.TurnNumber <= 2 && LinkComboPolicy.FitsDeck(own);
            int timeBudget = master ? masterBudget : Scale(linkOpening ? LinkOpeningPlanningMilliseconds : 195);
            bool Expired() => watch.ElapsedMilliseconds >= timeBudget;
            var queue = new Queue<Branch>();
            var alternateOpenings = new Queue<Branch>();
            int plannedSelectionLimit = master ? MaxPlannedSelections : 1;
            queue.Enqueue(new Branch(cached ? _next.TakeWhile(move => move.action != KeepSetup).Select(move => move.action).ToArray() : Array.Empty<int>(),
                _selectedTargets.ToArray()));
            var seen = new HashSet<string>();
            bool Remember(Branch branch)
            {
                string key = string.Join(",", branch.MainActions);
                foreach (var target in branch.Targets) key += $";{target.AfterMainActions}:{target.State}:{target.Index}";
                return seen.Add(key);
            }
            int branchBudget = lateLinkTrade ? LateLinkBranchMilliseconds : BranchMilliseconds;
            if (needsDefense) branchBudget = RecoveryBranchMilliseconds;
            else if (linkOpening && !lateLinkTrade) branchBudget = LinkOpeningBranchMilliseconds;
            int openingSuppression = 0;
            if (master && !cached && !lateLinkTrade)
                openingSuppression = options.FirstOrDefault(action => (action & 0xffff) == 5 &&
                    DuelTactics.CanProjectCostedBreakerLethal(live, prompt, prompt.activatable[action >> 16]));
            bool compareCostedBreakerLethal = openingSuppression != 0;
            if (!cached && !recovering && !lateLinkTrade && live.TurnNumber == 2 && prompt.canBp &&
                openingSuppression == 0 &&
                prompt.summonable.Any(item => !NeedsProvenNormalSummon(item.code) && CanDevelopFromCard(CardDatabase.Get(item.code))) &&
                live.Players[1 - prompt.playerId].mzone.Any(card => card is { IsFaceUp: true, effectsDisabled: false } &&
                    (card.type & YgoConstants.TYPE_EFFECT) != 0))
            {
                openingSuppression = options.FirstOrDefault(action => (action & 0xffff) == 5 &&
                    DuelTactics.IsFieldMonsterSuppression(prompt.activatable[action >> 16]));
            }
            if (openingSuppression != 0)
            {
                // Test the public blocker before Set siblings can consume the summon or the Quick-Play Spell.
                queue.Enqueue(new Branch(new[] { openingSuppression }));
                seen.Add(openingSuppression.ToString());
            }
            foreach (int option in lateOptions)
            {
                queue.Enqueue(new Branch(new[] { option }));
                seen.Add(option.ToString());
            }
            Line best = null;
            Line baseline = null;
            Branch? defensiveSet = null, starterBeforeLink = null;
            bool comparedDefensiveSet = false, comparedStarterBeforeLink = false;
            bool reservedOpeningBranch = false;
            long sliceStart = 0;
            bool SliceDue() => master && watch.ElapsedMilliseconds - sliceStart >= PlanningSliceMilliseconds;
            bool HasPendingBranches() => defensiveSet.HasValue || starterBeforeLink.HasValue ||
                alternateOpenings.Count != 0 || queue.Count != 0;
            for (int attempt = 0; HasPendingBranches() && attempt < branchLimit && !Expired(); attempt++)
            {
                bool comparingDefense = defensiveSet.HasValue;
                bool comparingStarter = !comparingDefense && starterBeforeLink.HasValue;
                bool comparingOpening = !comparingDefense && !comparingStarter && alternateOpenings.Count != 0;
                Branch choices = defensiveSet ?? starterBeforeLink ??
                    (comparingOpening ? alternateOpenings.Dequeue() : queue.Dequeue());
                if (comparingDefense) defensiveSet = null;
                if (comparingStarter) starterBeforeLink = null;
                var line = new Line();
                bool blocksResourceZone;
                bool stoppedAtDeadline = false;
                int currentBranchBudget = comparingStarter || comparingOpening ? OpeningDevelopmentBranchMilliseconds : branchBudget;
                if (openingSuppression != 0 && choices.MainActions.Length == 1 && choices.MainActions[0] == openingSuppression)
                {
                    currentBranchBudget = OpeningDevelopmentBranchMilliseconds;
                    reservedOpeningBranch = true;
                }
                else if (!reservedOpeningBranch && !recovering && !lateLinkTrade && live.TurnNumber <= 2 &&
                    best is { complete: true } && best.moves.Count == 1 && best.moves[0].action is 7 or KeepSetup &&
                    choices.MainActions.Length == 1 && (choices.MainActions[0] & 0xffff) == 1 &&
                    (uint)(choices.MainActions[0] >> 16) < (uint)prompt.spSummonable.Count &&
                    prompt.spSummonable[choices.MainActions[0] >> 16].location == YgoConstants.LOCATION_EXTRA)
                {
                    // An early End result leaves time to verify one legal development branch using the same total budget.
                    currentBranchBudget = OpeningDevelopmentBranchMilliseconds;
                    reservedOpeningBranch = true;
                }
                int scaledBranchBudget = Scale(currentBranchBudget);
                watch.Start();
                // Restoring the live position uses the total budget. Start the branch timer
                // once that position is ready so a slow replay cannot discard the whole search early.
                DuelSession restored = null;
                foreach (var copy in live.ReplayForPlanning(Expired, SliceDue))
                {
                    if (copy != null) { restored = copy; break; }
                    yield return true;
                    sliceStart = watch.ElapsedMilliseconds;
                    if (Expired()) break;
                }
                using (var trial = restored)
                {
                    if (trial == null || Fingerprint(trial, trial.ActivePrompt) != state) break;
                    long branchStart = watch.ElapsedMilliseconds;
                    bool BranchExpired() => Expired() || watch.ElapsedMilliseconds - branchStart >= scaledBranchBudget;
                    SimulatedBranches++;
                    trial.BeginSpeculation(prompt.playerId, stopAfterTurn: true);
                    foreach (bool pause in Explore(trial, advisor.ForkForComboSearch(), prompt.playerId, choices,
                        BranchExpired, SliceDue, recovering, plannedSelectionLimit, line))
                    {
                        yield return pause;
                        sliceStart = watch.ElapsedMilliseconds;
                        if (Expired())
                        {
                            stoppedAtDeadline = true;
                            break;
                        }
                    }
                    blocksResourceZone = !stoppedAtDeadline && keepResourceZoneOpen &&
                        DuelDeckStrategy.BlocksMainMonsterZoneSpells(trial, prompt.playerId);
                }
                if (stoppedAtDeadline) break;
                baseline ??= line;
                if (master && attempt == 0 && !cached && !lateLinkTrade && openingSuppression == 0 && !line.won &&
                    (!line.ending.HasSetup || recovering || line.unpaidAceTrade != int.MaxValue))
                {
                    int? original = line.moves.Count == 0 ? null : line.moves[0].action;
                    // Compare independent continuations before spending the budget on different targets of the same action.
                    foreach (int action in options.Where(action => action != original && (action & 0xffff) is 0 or 1 or 5)
                        .Take(AlternateOpeningLimit))
                    {
                        if (seen.Add(action.ToString())) alternateOpenings.Enqueue(new Branch(new[] { action }));
                    }
                }
                // The default route may be waiting for a draw or another reveal. Keep its confirmed
                // steps and let the next real prompt replan with the revealed cards.
                if (!recovering && attempt == 0 && line.needsHiddenInformation && !blocksResourceZone &&
                    !line.exposedBeforeReveal && !line.testedAttackPosition &&
                    line.unpaidAceTrade == int.MaxValue && line.moves.Count > 0 && !compareCostedBreakerLethal)
                {
                    best = line;
                    if (alternateOpenings.Count == 0) break;
                }
                bool valid = line.unpaidAceTrade == int.MaxValue && line.moves.Count > 0 &&
                    ((!line.testedAttackPosition && !blocksResourceZone) || line.won);
                if (recovering)
                {
                    double defenseValue = AdditionalDefense(line.ending);
                    valid &= (line.complete || line.verifiedDraw) && (line.won || (line.score + defenseValue > heldScore + .08 &&
                        (line.verifiedDraw || defenseValue > 0 || line.ending.ImprovesOn(held))));
                }
                double Value(Line candidate) => candidate.score + AdditionalDefense(candidate.ending) + (candidate.ending.HasSetup ? .48 : 0);
                if (comparingDefense)
                    valid &= line.won || (line.complete && line.measuredTurnEnd && !line.needsHiddenInformation);
                bool better = valid && (best == null || ((line.complete || line.verifiedDraw) &&
                    (!(best.complete || best.verifiedDraw) || Value(line) > Value(best) + .08)));
                if (better) best = line;
                if (master && best?.won == true) break;
                // Compare the legal Set while the original line is still the leading choice.
                if (better && !comparedDefensiveSet && line.defensiveSetFork is { } sibling &&
                    Remember(sibling))
                {
                    comparedDefensiveSet = true;
                    defensiveSet = sibling;
                }
                if (better || attempt == 0 || (line.complete && line.unpaidAceTrade != int.MaxValue))
                {
                    if (!comparedStarterBeforeLink && line.starterBeforeLink is { } starter &&
                        Remember(starter))
                    {
                        comparedStarterBeforeLink = true;
                        starterBeforeLink = starter;
                    }
                    // Preserve the native material and effect choices before queuing different main actions.
                    if (choices.Targets.Length < plannedSelectionLimit)
                    {
                        foreach (var target in line.targets.Take(3))
                            foreach (int option in target.Options)
                            {
                                var proposal = new Branch(target.Prefix,
                                    target.Prior.Append(new SelectionOverride(target.State, option, target.Prefix.Length)).ToArray());
                                if (Remember(proposal)) queue.Enqueue(proposal);
                            }
                    }
                    // Examine both the opening choice and the transition into a possible finish.
                    var forks = line.unpaidAceTrade == int.MaxValue
                        ? line.forks : line.forks.Where(fork => fork.prefix.Length > line.unpaidAceTrade).ToList();
                    for (int offset = 0; offset < Math.Min(master ? 13 : 9, forks.Count) && queue.Count < branchLimit; offset++)
                    {
                        int depth = offset % 2 == 0 ? offset / 2 : forks.Count - 1 - offset / 2;
                        var fork = forks[depth];
                        foreach (int option in fork.options.Take(alternativesPerFork))
                        {
                            var branch = new int[fork.prefix.Length + 1];
                            Array.Copy(fork.prefix, branch, fork.prefix.Length);
                            branch[^1] = option;
                            var child = new Branch(branch, master ? line.selectedTargets.Where(target =>
                                target.AfterMainActions <= fork.prefix.Length).ToArray() : null);
                            if (Remember(child)) queue.Enqueue(child);
                        }
                    }
                }
                if (!master) watch.Stop();
                // Group short replays so a faster engine can examine several branches in one frame.
                if (HasPendingBranches() && !Expired() && watch.ElapsedMilliseconds - sliceStart >= PlanningSliceMilliseconds)
                {
                    yield return true;
                    sliceStart = watch.ElapsedMilliseconds;
                }
            }

            if (best == null || best.moves.Count == 0) yield break;
            if (best != baseline) ChangedLines++;
            _next.Clear();
            ClearsBattleLethal = best.won || (best.measuredTurnEnd && !best.needsHiddenInformation &&
                !best.ending.exposedToBattleLethal);
            _selectedTargets.Clear();
            _selectedTargets.AddRange(best.selectedTargets.Select(target => new SelectionOverride(target.State, target.Index, 0)));
            foreach (var move in best.moves) _next.Enqueue(move);
        }

        private IEnumerable<bool> Explore(DuelSession trial, TacticalAI ai, byte player, Branch branch,
            Func<bool> expired, Func<bool> sliceDue, bool recovering, int plannedSelectionLimit, Line line)
        {
            int settledMoves = 0;
            (int Index, double Score)? ritualAceSpend = null;
            bool master = ai.Difficulty == AIDifficulty.Master;
            int startTurn = trial.TurnNumber;
            uint linkTarget = 0;
            int targetRank = 0;
            bool canBattle = false;
            bool checkEarlyStarter = startTurn <= 2 && trial.Players[player].extra.Any(card => LinkComboPolicy.Rank(card.code) >= 5);
            var beforeLink = default(Position);
            double beforeLinkScore = 0;
            var trades = new List<(int index, int rank, double score, Position before, bool ace)>();
            (int Index, double Score)? firstAceCommitment = null;
            bool canKeepSetup = false;
            HashSet<(uint Code, uint Sequence, ulong Description)> simulatedPublicResponses = null;
            bool checkDraw = false, drawBoardChanged = false, opponentDrew = false;
            int drawn = 0, handBeforeDraw = 0, lifeBeforeDraw = 0, rivalLifeBeforeDraw = 0, phaseBeforeDraw = 0;
            trial.OnCardsDrawn += (recipient, cards) =>
            {
                if (!checkDraw) return;
                if (recipient == player) drawn += cards.Count;
                else opponentDrew = true;
            };
            var initial = default(Position);
            (int step, ulong state, double score, Position before)? start = null;
            (int step, ulong state, double score, Position before)? checkpoint = null;
            (int step, ulong state, double score, Position before)? safeArchfiendCheckpoint = null;
            (int step, ulong state, double score, Position before)? spend = null;
            var safeSets = new List<(int Step, ulong State, double Score, Position Before, uint Code, int SetIndex)>();
            List<(int Step, ulong State, double Score, Position Before, uint Code, int CopiesOnField)> handSupportSummons = null;
            trial.OnCardMoved += move =>
            {
                if (checkDraw && (move.fromLoc is YgoConstants.LOCATION_MZONE or YgoConstants.LOCATION_SZONE ||
                    move.toLoc is YgoConstants.LOCATION_MZONE or YgoConstants.LOCATION_SZONE))
                    drawBoardChanged = true;
                if (!ritualAceSpend.HasValue && line.moves.Count > 0 && move.fromCon == player &&
                    DuelAiPolicy.SpentOwnRitualAce(move, trial.SelectionChainLink))
                    ritualAceSpend = (line.moves.Count - 1, line.score);
                if (master && !firstAceCommitment.HasValue && line.moves.Count > 0 &&
                    move.fromCon == player && DuelAiPolicy.SpentEstablishedFieldAce(move, trial.SelectionChainLink))
                    firstAceCommitment = (line.moves.Count - 1, line.score);
                if (canKeepSetup && !spend.HasValue && move.fromCon == player && move.fromLoc == YgoConstants.LOCATION_MZONE &&
                    (move.fromPos & YgoConstants.POS_FACEUP) != 0 &&
                    (move.toCon != player || move.toLoc != YgoConstants.LOCATION_MZONE))
                    spend = (line.moves.Count - 1, line.moves[^1].state, line.score, line.ending);
                if (targetRank == 0) return;
                if (move.code == linkTarget && move.toCon == player && move.toLoc == YgoConstants.LOCATION_MZONE &&
                    (move.reason & YgoConstants.REASON_SPSUMMON) != 0)
                    targetRank = 0;
                if (move.fromCon != player || move.fromLoc != YgoConstants.LOCATION_MZONE ||
                    (move.reason & YgoConstants.REASON_LINK) == 0) return;
                int rank = LinkComboPolicy.MaterialRiskRank(move.code, targetRank);
                if (LinkComboPolicy.IsDowngrade(rank, targetRank))
                    trades.Add((line.moves.Count - 1, rank, beforeLinkScore, beforeLink, LinkComboPolicy.ProtectAsLinkMaterial(move.code)));
            };
            for (int i = 0; i < _responseLimit && !expired() && trial.TurnNumber == startTurn &&
                trial.RetryCount == 0 && !trial.SpeculationUncertain; i++)
            {
                if (sliceDue()) yield return true;
                if (expired()) break;
                if (trial.IsFinished) break;
                var prompt = trial.ActivePrompt;
                if (prompt == null)
                {
                    foreach (bool paused in trial.StepUntilPromptInSlices(sliceDue)) yield return paused;
                    continue;
                }
                if (prompt.playerId != player)
                {
                    if (master && (simulatedPublicResponses?.Count ?? 0) < MaxProjectedPublicResponses &&
                        DuelTactics.FindPublicPlanningNegate(trial, prompt, player, simulatedPublicResponses) is int negate && negate >= 0)
                    {
                        var answer = prompt.candidates[negate];
                        (simulatedPublicResponses ??= new()).Add((answer.code, answer.sequence, answer.description));
                        foreach (bool paused in trial.SubmitVisiblePlanningReaction(negate, sliceDue)) yield return paused;
                        continue;
                    }
                    if (OptionalOpponentPass(prompt) is not int pass) break;
                    foreach (bool paused in trial.SubmitPlanningResponse(BitConverter.GetBytes(pass), sliceDue,
                        preserveDeckSelection: false)) yield return paused;
                    continue;
                }
                if (prompt.messageType != YgoConstants.MSG_SELECT_IDLECMD)
                {
                    if (HasPrivateOpponentChoices(prompt)) break;
                    byte[] choiceResponse = ai.ComputeResponse(trial, prompt);
                    if (TryTargetOptions(trial, prompt, choiceResponse, out ulong selectionState, out int[] targetAlternatives))
                    {
                        int nextSelection = line.selectedTargets.Count;
                        if (nextSelection < branch.Targets.Length && branch.Targets[nextSelection] is var target &&
                            target.State == selectionState && target.AfterMainActions <= line.moves.Count && targetAlternatives.Contains(target.Index))
                        {
                            choiceResponse = TacticalAI.EncodePlannedSelection(prompt, target.Index);
                            line.selectedTargets.Add(new SelectionOverride(selectionState, target.Index, line.moves.Count));
                            line.testedAttackPosition |= prompt.messageType == YgoConstants.MSG_SELECT_POSITION;
                        }
                        else if (branch.Targets.Length < plannedSelectionLimit && line.selectedTargets.Count == branch.Targets.Length &&
                            line.targets.Count < 3)
                            line.targets.Add((line.moves.Select(move => move.action).ToArray(), selectionState, targetAlternatives,
                                line.selectedTargets.ToArray()));
                    }
                    foreach (bool paused in trial.SubmitPlanningResponse(choiceResponse, sliceDue))
                        yield return paused;
                    continue;
                }

                canBattle = prompt.canBp;
                line.score = BoardValue(trial, player, canBattle, master) - line.moves.Count * .01;
                ulong state = Fingerprint(trial, prompt);
                line.ending = new Position(trial, player, canBattle);
                if (line.moves.Count == 0)
                {
                    initial = line.ending;
                    if (prompt.canEp || prompt.canBp) start = (0, state, line.score, initial);
                }
                if ((recovering || spend.HasValue || line.ending.HasSetup) && line.moves.Count > 0 && (prompt.canEp || prompt.canBp) &&
                    line.ending.ImprovesOn(spend?.before ?? initial))
                {
                    var candidate = (line.moves.Count, state, line.score, line.ending);
                    if (!checkpoint.HasValue || line.score > checkpoint.Value.score) checkpoint = candidate;
                    if (!line.ending.archfiendEndPhaseRisk &&
                        (!safeArchfiendCheckpoint.HasValue || line.score > safeArchfiendCheckpoint.Value.score))
                        safeArchfiendCheckpoint = candidate;
                }
                canKeepSetup = (line.ending.HasSetup || line.ending.lastDefenderAgainstLethal) && (prompt.canEp || prompt.canBp);
                settledMoves = line.moves.Count;
                if (settledMoves >= MaxMainActions) break;
                var options = ai.ComboMainOptions(trial, prompt);
                int? forced = settledMoves < branch.MainActions.Length ? branch.MainActions[settledMoves] : null;
                byte[] response = ai.ComputeComboResponse(trial, prompt, forced);
                int move = BitConverter.ToInt32(response, 0);
                bool defendedBeforePhaseChange = (forced is 6 or 7) && (move & 0xffff) == 2;
                if (forced.HasValue && forced != move && !defendedBeforePhaseChange) break;
                if ((move & 0xffff) == 0 && (prompt.canEp || prompt.canBp) &&
                    prompt.summonable[move >> 16] is { location: YgoConstants.LOCATION_HAND } normal)
                {
                    if (CardDatabase.Get(normal.code)?.atk < 1800 && DuelTactics.ChooseDefensiveSet(trial, prompt, normal,
                        followupUnproven: true, compareWithBattle: true) is int setIndex)
                        safeSets.Add((settledMoves, state, line.score, line.ending, normal.code, setIndex));
                    if (NeedsProvenNormalSummon(normal.code))
                    {
                        handSupportSummons ??= new();
                        int copies = trial.Players[player].mzone.Count(card => card?.code == normal.code);
                        handSupportSummons.Add((settledMoves, state, line.score, line.ending, normal.code, copies));
                    }
                }
                targetRank = 0;
                if ((move & 0xffff) == 1 && prompt.spSummonable[move >> 16] is { location: YgoConstants.LOCATION_EXTRA } summon)
                {
                    int next = LinkComboPolicy.Rank(summon.code);
                    var field = trial.Players[player].mzone;
                    if (checkEarlyStarter && next is >= 1 and <= 3 && line.starterBeforeLink == null &&
                        field.Count(card => card?.IsFaceUp == true) >= 2 &&
                        (startTurn == 1 || field.Any(card => card is { IsFaceUp: true } && LinkComboPolicy.Rank(card.code) > 0)))
                    {
                        // Compare developing the board before converting its bodies into a small Link.
                        int starter = Array.FindIndex(options, action => (action & 0xffff) == 0 &&
                            prompt.summonable[action >> 16] is { location: YgoConstants.LOCATION_HAND } normalStarter &&
                            !NeedsProvenNormalSummon(normalStarter.code) && CanDevelopFromCard(CardDatabase.Get(normalStarter.code)));
                        if (starter >= 0)
                            line.starterBeforeLink = new Branch(line.moves.Select(entry => entry.action).Append(options[starter]).ToArray());
                    }
                    if (LinkComboPolicy.HasStrongerLink(trial.Players[player], next) ||
                        LinkComboPolicy.HasAceAtRisk(trial.Players[player], next))
                    {
                        linkTarget = summon.code;
                        targetRank = next;
                        beforeLink = new Position(trial, player, canBattle);
                        beforeLinkScore = line.score;
                    }
                }
                int[] alternatives = options.Where(option => option != move).ToArray();
                if (alternatives.Length != 0)
                    line.forks.Add((line.moves.Select(entry => entry.action).ToArray(), alternatives));
                line.moves.Add((state, move));
                checkDraw = master && (move & 0xffff) == 5 &&
                    IsPublicFieldDraw(trial.Players[player], prompt.activatable[move >> 16]);
                drawn = 0;
                opponentDrew = drawBoardChanged = false;
                if (checkDraw)
                {
                    handBeforeDraw = trial.Players[player].hand.Count;
                    lifeBeforeDraw = trial.Players[player].lp;
                    rivalLifeBeforeDraw = trial.Players[1 - player].lp;
                    phaseBeforeDraw = trial.CurrentPhase;
                }
                if ((move & 0xffff) is 6 or 7)
                {
                    settledMoves = line.moves.Count;
                    line.complete = true;
                    canKeepSetup = false;
                    if (move == 6)
                    {
                        if (safeSets.Count != 0)
                        {
                            line.opposingCardsBeforeBattle = line.ending.opposingCards;
                            line.battleAttackers = new HashSet<uint>();
                        }
                        foreach (bool pause in MeasureBattle(trial, ai, player, response, expired, sliceDue, line))
                            yield return pause;
                    }
                    else
                        foreach (bool pause in MeasureTurnEnd(trial, ai, player, response, expired, sliceDue, line))
                            yield return pause;
                    break;
                }
                foreach (bool paused in trial.SubmitPlanningResponse(response, sliceDue, preserveDeckSelection: false))
                    yield return paused;
            }
            // The last permitted response may settle the duel before another iteration can inspect it.
            if (!trial.SpeculationUncertain && trial.RetryCount == 0 && trial.IsFinished)
            {
                line.won = trial.Winner == player;
                line.score = line.won ? 100 : trial.Winner == 2 ? 0 : -100;
                settledMoves = line.moves.Count;
                line.complete = true;
            }
            line.needsHiddenInformation = trial.SpeculationUncertain;
            var finalOwn = trial.Players[player];
            if (checkDraw && line.needsHiddenInformation && !drawBoardChanged && !opponentDrew &&
                trial.TurnNumber == startTurn && trial.CurrentPhase == phaseBeforeDraw && !trial.IsFinished && trial.RetryCount == 0 &&
                trial.Players[1 - player].lp <= rivalLifeBeforeDraw &&
                trial.ActivePrompt is { messageType: YgoConstants.MSG_SELECT_IDLECMD } drawBoundary &&
                drawBoundary.playerId == player && trial.CurrentChain.Count == 0 &&
                HasSafeDrawGain(drawn, finalOwn.hand.Count - handBeforeDraw, finalOwn.deckCount,
                    lifeBeforeDraw, finalOwn.lp))
            {
                // Score the confirmed change in quantity. Card identities belong to the next real decision.
                settledMoves = line.moves.Count;
                line.verifiedDraw = true;
                line.score += .42 * (finalOwn.hand.Count - handBeforeDraw);
            }
            line.exposedBeforeReveal = line.needsHiddenInformation && safeSets.Any(candidate => candidate.Step < settledMoves &&
                trial.Players[player].mzone.Any(card => card?.code == candidate.Code && card.IsAttack && card.attack < 1800));
            if (line.moves.Count > settledMoves) line.moves.RemoveRange(settledMoves, line.moves.Count - settledMoves);
            line.selectedTargets.RemoveAll(selected => selected.AfterMainActions > settledMoves);
            (int step, ulong state, double score, Position before)? stop;
            if (line.unverifiedArchfiendEndPhase)
            {
                // The End Phase can wipe the apparent gain. Keep only the prefix before that risk.
                line.complete = false;
                stop = safeArchfiendCheckpoint ?? start;
                if (!stop.HasValue)
                {
                    line.moves.Clear();
                    line.selectedTargets.Clear();
                    yield break;
                }
            }
            else
            {
                stop = (!line.complete && !line.verifiedDraw) ||
                    (!line.endPhaseDegradedBoard && checkpoint.HasValue && checkpoint.Value.score > line.score + .08)
                    ? checkpoint : null;
                if (spend is { } investment && !line.ending.ImprovesOn(investment.before) &&
                    !(master && PaidUpgrade(investment.step, investment.score)))
                    stop ??= investment;
                else if (spend.HasValue && !line.complete)
                    stop ??= spend;
                if (line.moves.Count > 1 && start.HasValue && !line.ending.ImprovesOn(initial) &&
                    line.score < start.Value.score - .08)
                    stop ??= start;
            }
            int stopAction = KeepSetup;
            foreach (var candidate in safeSets)
            {
                if (!line.complete || line.needsHiddenInformation || candidate.Step >= settledMoves ||
                    stop.HasValue && candidate.Step >= stop.Value.step) continue;
                var own = trial.Players[player];
                var attacker = own.mzone.FirstOrDefault(card => card?.code == candidate.Code && card.IsFaceUp &&
                    card.IsAttack && card.attack < 1800);
                if (attacker == null) continue;
                int threat = DuelTactics.StrongestAttacker(trial.Players[1 - player]);
                int incomingDamage = threat - Math.Max(0, attacker.attack);
                bool improvesField = line.ending.interaction > candidate.Before.interaction ||
                    line.ending.rank > candidate.Before.rank || line.ending.pressure > candidate.Before.pressure ||
                    line.ending.defense > candidate.Before.defense;
                bool opensLethal = line.ending.exposedToBattleLethal && (line.ending.monsters == 1 ||
                    !candidate.Before.exposedToBattleLethal && incomingDamage >= own.lp);
                // A weak monster without a follow-up gives the next turn's effects an attack target.
                bool clearedOpposingCard = line.ending.opposingCards < candidate.Before.opposingCards;
                bool risksSurvival = DuelCombat.IsWeakBattleLiability(attacker.attack, threat, own.lp) &&
                    !improvesField && (!clearedOpposingCard || 2L * incomingDamage >= own.lp);
                bool revealedLethal = !improvesField && DuelTactics.RisksRevealedBattleLethal(trial, player,
                    attacker.attack, attacker.baseAttack);
                bool needsDefense = (opensLethal || risksSurvival || revealedLethal) &&
                    !own.hand.Any(card => card.code == PhotonJumper);
                bool payoff = improvesField || clearedOpposingCard ||
                    (!attacker.effectsDisabled && CardDatabase.Get(attacker.code) is CardInfo info && HasFieldInteraction(info));
                if (!needsDefense && payoff)
                {
                    if (line.defensiveSetFork == null && !line.won && line.measuredTurnEnd && !improvesField &&
                        clearedOpposingCard && line.opposingCardsBeforeBattle == candidate.Before.opposingCards &&
                        line.battleAttackers is { Count: > 0 } && !line.battleAttackers.Contains(candidate.Code) &&
                        CardDatabase.Get(attacker.code) is CardInfo weak && !HasFieldInteraction(weak) &&
                        !CanDevelopFromCard(weak) && branch.Targets.Length == 0)
                    {
                        // Resimulate from the same prefix to check whether Set preserves the battle gain.
                        line.defensiveSetFork = new Branch(line.moves.Take(candidate.Step).Select(entry => entry.action)
                            .Append((candidate.SetIndex << 16) | 3).ToArray());
                    }
                    continue;
                }
                stop = (candidate.Step, candidate.State, candidate.Score + .15, candidate.Before);
                stopAction = (candidate.SetIndex << 16) | 3;
                break;
            }
            if (handSupportSummons != null && !line.won && !line.unverifiedArchfiendEndPhase)
            {
                // These cards have a role in the hand. Spend them when they lead to a follow-up.
                foreach (var candidate in handSupportSummons)
                {
                    if (candidate.Step >= settledMoves || stop.HasValue && candidate.Step > stop.Value.step) break;
                    if (trial.Players[player].mzone.Count(card => card?.code == candidate.Code) <= candidate.CopiesOnField) continue;
                    stop = (candidate.Step, candidate.State, candidate.Score, candidate.Before);
                    stopAction = KeepSetup;
                    break;
                }
            }
            if (stop is { } safe && !line.won)
            {
                // Return only the proven prefix. The live AI can attack or prepare defenses from this field.
                line.moves.RemoveRange(safe.step, line.moves.Count - safe.step);
                line.moves.Add((safe.state, stopAction));
                line.score = safe.score;
                line.ending = safe.before;
                line.complete = !line.unverifiedArchfiendEndPhase;
                line.measuredTurnEnd = false;
                line.needsHiddenInformation = false;
                line.verifiedDraw = false;
                line.selectedTargets.RemoveAll(picked => picked.AfterMainActions > safe.step);
                trades.RemoveAll(trade => trade.index >= safe.step);
                if (firstAceCommitment is { } commitment && commitment.Index >= safe.step) firstAceCommitment = null;
                if (ritualAceSpend is { } ritualCommitment && ritualCommitment.Index >= safe.step) ritualAceSpend = null;
            }
            if (trial.IsFinished && trial.Winner == player) yield break;
            if (ritualAceSpend is { } ritual && ritual.Index < line.moves.Count &&
                !(master && PaidUpgrade(ritual.Index, ritual.Score)))
                line.unpaidAceTrade = ritual.Index;
            if (firstAceCommitment is { } spendAce && spendAce.Index < settledMoves &&
                !PaidUpgrade(spendAce.Index, spendAce.Score))
                line.unpaidAceTrade = Math.Min(line.unpaidAceTrade, spendAce.Index);
            if (trades.Count == 0) yield break;

            // Leaving more small bodies is insufficient. The projected line must recover the lost Link rating
            // or gain an actionable advantage against the actual board before committing the material.
            var end = line.ending;
            var aceCounts = trades.Where(trade => trade.ace).GroupBy(trade => trade.index)
                .ToDictionary(group => group.Key, group => group.Count());
            foreach (var trade in trades)
            {
                if (trade.index >= settledMoves) continue;
                if (master)
                {
                    if (!PaidUpgrade(trade.index, trade.score))
                        line.unpaidAceTrade = Math.Min(line.unpaidAceTrade, trade.index);
                    continue;
                }
                bool payoff = end.rank >= trade.rank || end.opposingCards < trade.before.opposingCards ||
                    end.interaction > trade.before.interaction || (end.lethal && !trade.before.lethal);
                if (aceCounts.TryGetValue(trade.index, out int spent) && spent > 1 && !end.lethal &&
                    !trade.before.exposedToBattleLethal && trade.before.opposingCards - end.opposingCards < spent)
                    payoff = false;
                if (!line.complete || !payoff || line.score <= trade.score + .08)
                    line.unpaidAceTrade = Math.Min(line.unpaidAceTrade, trade.index);
            }

            bool PaidUpgrade(int index, double before)
            {
                double beforeRaw = before + index * .01;
                double afterRaw = line.score + Math.Max(0, line.moves.Count - 1) * .01;
                bool verified = !line.unverifiedArchfiendEndPhase &&
                    (line.won || ((line.measuredTurnEnd && !line.needsHiddenInformation) ||
                        (line.verifiedDraw && afterRaw > beforeRaw + .35)));
                return IsPaidAceUpgrade(beforeRaw, afterRaw, verified, line.won);
            }
        }

        internal static bool IsPaidAceUpgrade(double before, double after, bool finalPositionVerified, bool gainsLethal) =>
            finalPositionVerified && (gainsLethal || after > before + 1e-6);

        internal static bool HasSafeDrawGain(int drawn, int handGain, int deckRemaining, int previousLife, int life) =>
            drawn > 0 && handGain > 0 && handGain <= drawn && deckRemaining > 0 && life >= previousLife;

        private static int? OptionalOpponentPass(DuelPrompt prompt) => prompt.messageType switch
        {
            // Decline a trigger without evaluating the opponent's undisclosed cards.
            YgoConstants.MSG_SELECT_EFFECTYN => 0,
            YgoConstants.MSG_SELECT_CHAIN when !prompt.forced => -1,
            _ => null,
        };

        private static bool HasPrivateOpponentChoices(DuelPrompt prompt) => prompt.candidates.Any(card => card.controller != prompt.playerId &&
            card.location is YgoConstants.LOCATION_HAND or YgoConstants.LOCATION_DECK or YgoConstants.LOCATION_EXTRA);

        private static bool TryTargetOptions(DuelSession duel, DuelPrompt prompt, byte[] response, out ulong state, out int[] alternatives)
        {
            state = 0;
            alternatives = Array.Empty<int>();
            if (duel.SelectionChainLink is { } source && source.triggerPlayer == prompt.playerId &&
                (DuelTactics.IsGxFieldCost(source.code, prompt.SelectionHint) ||
                 prompt.SelectionHint is DuelTactics.Release or 509 && DuelAiPolicy.IsRitualSpell(source.code) &&
                    duel.Players[prompt.playerId].mzone.Any(DuelAiPolicy.IsEstablishedRitualAce))) return false;
            if (prompt.SelectionHint == DuelTactics.Banish && prompt.candidates.Any(card =>
                card.code == DuelTactics.Accesscode && card.controller == prompt.playerId &&
                card.location == YgoConstants.LOCATION_MZONE)) return false;
            if (prompt.messageType == YgoConstants.MSG_SELECT_POSITION && response.Length == 4 &&
                BitConverter.ToInt32(response, 0) == YgoConstants.POS_FACEUP_DEFENSE &&
                (prompt.allowedPositions & YgoConstants.POS_FACEUP_ATTACK) != 0 &&
                duel.CurrentPhase == YgoConstants.PHASE_MAIN1 && duel.TurnNumber > 1 &&
                duel.TurnPlayer == prompt.playerId && CardDatabase.Get(prompt.promptCardCode) is { atk: > 0 } monster &&
                DuelCombat.CanTestLethalPosition(duel, prompt.playerId, monster.atk))
            {
                // Test the actual Battle Phase before spending a potential defender as an attacker.
                alternatives = new[] { (int)YgoConstants.POS_FACEUP_ATTACK };
                state = Fingerprint(duel, prompt);
                return true;
            }
            if (prompt.messageType == YgoConstants.MSG_SELECT_OPTION && prompt.options.Count is > 1 and <= 6 && response.Length == 4)
            {
                int choice = BitConverter.ToInt32(response, 0);
                alternatives = Enumerable.Range(0, prompt.options.Count).Where(option => option != choice).Take(2).ToArray();
                state = Fingerprint(duel, prompt);
                return true;
            }
            if (prompt.candidates.Count < 2 || prompt.candidates.Count > 20) return false;
            int selected;
            if (prompt.messageType == YgoConstants.MSG_SELECT_CARD && prompt.min == 1 && prompt.max == 1 &&
                response.Length == 12 && BitConverter.ToInt32(response, 0) == 0)
                selected = BitConverter.ToInt32(response, 8);
            else if (prompt.messageType == YgoConstants.MSG_SELECT_UNSELECT_CARD && !prompt.finishable &&
                response.Length == 8 && BitConverter.ToInt32(response, 0) == 1)
                selected = BitConverter.ToInt32(response, 4);
            else return false;
            if (prompt.SelectionHint is not (501 or 502 or 503 or 504 or 506 or 509 or 511 or 512 or 513 or 533)) return false;
            alternatives = prompt.candidates.Select((card, index) => (card, index))
                .Where(entry => entry.index != selected && entry.card.code != 0 &&
                    (prompt.SelectionHint != DuelTactics.Destroy || entry.card.controller != prompt.playerId))
                .OrderByDescending(entry => DuelTactics.SelectionPriority(duel, prompt.playerId, entry.card, prompt.SelectionHint))
                .Take(2).Select(entry => entry.index).ToArray();
            if (alternatives.Length == 0) return false;
            state = Fingerprint(duel, prompt);
            return true;
        }

        private static IEnumerable<bool> MeasureBattle(DuelSession trial, TacticalAI ai, byte player, byte[] enter,
            Func<bool> expired, Func<bool> sliceDue, Line line)
        {
            int lifeBefore = trial.Players[1 - player].lp;
            int ownLifeBefore = trial.Players[player].lp;
            var before = line.ending;
            line.unverifiedArchfiendEndPhase = before.archfiendEndPhaseRisk;
            byte[] finishBattle = null;
            foreach (bool paused in trial.SubmitPlanningResponse(enter, sliceDue, preserveDeckSelection: false))
                yield return paused;
            for (int step = 0; step < 16 && !expired() && trial.RetryCount == 0 && !trial.SpeculationUncertain && !trial.IsFinished; step++)
            {
                if (sliceDue()) yield return true;
                if (expired()) break;
                var prompt = trial.ActivePrompt;
                if (prompt == null) break;
                if (prompt.playerId != player)
                {
                    if (OptionalOpponentPass(prompt) is not int pass) break;
                    foreach (bool paused in trial.SubmitPlanningResponse(BitConverter.GetBytes(pass), sliceDue,
                        preserveDeckSelection: false)) yield return paused;
                    continue;
                }
                if (prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD || HasPrivateOpponentChoices(prompt)) break;
                byte[] response = ai.ComputeResponse(trial, prompt);
                if (prompt.messageType == YgoConstants.MSG_SELECT_BATTLECMD && response.Length >= 4)
                {
                    int command = BitConverter.ToInt32(response, 0);
                    if ((command & 0xffff) is 2 or 3)
                    {
                        finishBattle = response;
                        break;
                    }
                    int index = command >> 16;
                    if ((command & 0xffff) == 1 && (uint)index < prompt.attackable.Count)
                        line.battleAttackers?.Add(prompt.attackable[index].code);
                }
                foreach (bool paused in trial.SubmitPlanningResponse(response, sliceDue))
                    yield return paused;
            }
            if (trial.SpeculationUncertain || trial.RetryCount != 0) yield break;
            if (trial.IsFinished)
            {
                line.unverifiedArchfiendEndPhase = false;
                line.won = trial.Winner == player;
                line.score = line.won ? 100 : trial.Winner == 2 ? 0 : -100;
            }
            else
            {
                var after = new Position(trial, player, canBattle: false);
                int damage = lifeBefore - trial.Players[1 - player].lp - (ownLifeBefore - trial.Players[player].lp);
                line.score += damage / 3300.0 + 1.15 * (before.opposingCards - after.opposingCards) -
                    1.1 * (before.monsters - after.monsters);
                line.ending = after;
                line.unverifiedArchfiendEndPhase = after.archfiendEndPhaseRisk;
                if (finishBattle != null && !expired())
                    foreach (bool pause in MeasureTurnEnd(trial, ai, player, finishBattle, expired, sliceDue, line))
                        yield return pause;
            }
        }

        private static IEnumerable<bool> MeasureTurnEnd(DuelSession trial, TacticalAI ai, byte player, byte[] end,
            Func<bool> expired, Func<bool> sliceDue, Line line)
        {
            int turn = trial.TurnNumber;
            line.unverifiedArchfiendEndPhase = line.ending.archfiendEndPhaseRisk;
            bool master = ai.Difficulty == AIDifficulty.Master;
            double before = BoardValue(trial, player, canBattle: false, master: master);
            foreach (bool paused in trial.SubmitPlanningResponse(end, sliceDue))
                yield return paused;
            for (int step = 0; step < 12 && trial.TurnNumber == turn && !expired() && !trial.IsFinished &&
                !trial.SpeculationUncertain && trial.RetryCount == 0; step++)
            {
                if (sliceDue()) yield return true;
                if (expired()) break;
                var prompt = trial.ActivePrompt;
                if (prompt == null || HasPrivateOpponentChoices(prompt)) break;
                bool currentPlayer = prompt.playerId == player;
                byte[] response = currentPlayer ? ai.ComputeResponse(trial, prompt) :
                    OptionalOpponentPass(prompt) is int pass ? BitConverter.GetBytes(pass) : null;
                if (response == null) break;
                foreach (bool paused in trial.SubmitPlanningResponse(response, sliceDue, preserveDeckSelection: currentPlayer))
                    yield return paused;
            }
            if (trial.SpeculationUncertain || trial.RetryCount != 0) yield break;
            if (trial.IsFinished)
            {
                line.unverifiedArchfiendEndPhase = false;
                line.won = trial.Winner == player;
                line.score = line.won ? 100 : trial.Winner == 2 ? 0 : -100;
            }
            else if (trial.TurnNumber > turn)
            {
                line.unverifiedArchfiendEndPhase = false;
                line.measuredTurnEnd = true;
                double change = BoardValue(trial, player, canBattle: false, master: master, turnEndVerified: master) - before;
                line.score += change;
                line.ending = new Position(trial, player, canBattle: false);
                line.endPhaseDegradedBoard = change < -.08;
            }
        }

        internal static double BoardValue(DuelSession duel, byte player, bool canBattle, bool master = false, bool turnEndVerified = false)
        {
            var own = duel.Players[player];
            var rival = duel.Players[1 - player];
            int opposingMonsters = rival.mzone.Count(card => card != null);
            int opposingSpells = rival.szone.Count(card => card != null);
            int rivalMaxAtk = DuelTactics.StrongestAttacker(rival);
            bool breakingBoard = canBattle && duel.TurnNumber > 1;
            var style = DuelDeckStrategy.For(duel, player);
            bool controlsResources = style != DuelDeckStyle.Development;
            bool rivalHeavyBoard = breakingBoard && (opposingMonsters >= 2 || rival.mzone.Any(m => m?.IsFaceUp == true && Math.Max(m.attack, m.defense) >= 2400));
            bool rivalUsesGrave = rival.grave.Count >= 4;
            bool contract = own.szone.Any(card => card?.IsFaceUp == true && !card.effectsDisabled && DddTurnPlan.IsDarkContract(card.code));
            int readyHand = master ? DuelAiPolicy.ReadyHandInterruptions(own) :
                own.hand.Count(card => HandDisruption.Contains(card.code));
            double score = .27 * own.hand.Count - .24 * rival.hand.Count + .06 * Math.Min(own.grave.Count, 12) +
                (breakingBoard ? .25 : .58) * readyHand -
                (breakingBoard ? 1.05 : .75) * opposingMonsters - (breakingBoard ? .64 : .43) * opposingSpells +
                (own.lp - rival.lp) / (breakingBoard ? 3300.0 : 5200.0);
            if (controlsResources)
                score += .24 * own.hand.Count + .16 * (own.szone.Count(card => card != null) - opposingSpells);
            if (style == DuelDeckStyle.Lock)
                score += .18 * own.hand.Count + .9 * own.hand.Count(card => DuelDeckStrategy.IsKeyPiece(card.code)) -
                    Math.Max(0, 3200 - own.lp) / 1400.0;
            if (style == DuelDeckStyle.Resource)
                score += DuelDeckStrategy.ResourcePosition(duel, player) +
                    .18 * (own.hand.Count - rival.hand.Count - opposingMonsters);
            score += .19 * Math.Min(3, PendulumTurnPlan.ReadyFollowUps(own));
            // Weight interaction that will actually be usable next turn, public opposing threats, and recovery.
            int readyBoard = DuelAiPolicy.ReadyInteractionCount(duel, player, inspectPrivateCards: true);
            score += (breakingBoard ? .55 : 1.05) * readyBoard;
            if (master && !breakingBoard)
                score += .5 * Math.Min(4, readyBoard) + .3 * Math.Min(4, readyBoard + readyHand);
            score -= (breakingBoard ? 1.15 : .65) * DuelAiPolicy.ReadyInteractionCount(duel, (byte)(1 - player), inspectPrivateCards: false);
            score += .24 * DuelAiPolicy.GraveyardFollowUps(own);
            foreach (var backrow in own.szone)
            {
                if (backrow == null) continue;
                score += .16;
                if (backrow.effectsDisabled || CardDatabase.Get(backrow.code) is not CardInfo info) continue;
                if ((!backrow.IsFaceUp && (info.IsTrap || (info.type & YgoConstants.TYPE_QUICKPLAY) != 0)) ||
                    (backrow.IsFaceUp && info.IsTrap && LinkComboPolicy.IsInteractive(info)))
                {
                    bool? fieldReady = DuelAiPolicy.BackrowFieldCondition(own, info);
                    if (fieldReady != false)
                        score += (controlsResources ? .88 : breakingBoard ? .19 : .55) + ((info.DeckEffectCategories & NegatesEffects) != 0 ? .26 : 0);
                    if (!backrow.IsFaceUp && !breakingBoard && fieldReady == true)
                        score += 1.5;
                }
                if (style == DuelDeckStyle.Lock && DuelDeckStrategy.IsKeyPiece(backrow.code)) score += 1.2;
            }
            int attack = 0;
            foreach (var monster in own.mzone)
            {
                if (monster == null) continue;
                if (!monster.IsFaceUp)
                {
                    score += .26;
                    continue;
                }
                if (CardDatabase.Get(monster.code) is not CardInfo card) continue;
                if (monster.IsAttack)
                {
                    attack += Math.Max(0, monster.attack);
                    if (!card.IsLink && rivalMaxAtk > monster.attack)
                    {
                        score -= monster.attack < 1800 && (monster.effectsDisabled || !LinkComboPolicy.IsInteractive(card))
                            ? .94 : breakingBoard ? .34 : .68;
                        if (rivalMaxAtk - Math.Max(0, monster.attack) >= own.lp)
                            score -= 2.2;
                    }
                }
                else if (!breakingBoard)
                    score += Math.Clamp(monster.defense, 0, 3300) / 8000.0;
                // Once the next turn actually starts, its live ATK already excludes effects that expired in the End Phase.
                score += .6 + Math.Clamp(DuelAiPolicy.StableAttack(monster, card, breakingBoard || turnEndVerified), 0, 4200) /
                    (breakingBoard ? 3000.0 : 4100.0);
                bool utilityLinkOne = card.IsLink && card.level <= 1 &&
                    monster.code is Almiraj or 41999284 or 34755994 or 73539069 or 14812471 or 2463794;
                bool developed = (card.IsExtra && !utilityLinkOne) || (card.type & YgoConstants.TYPE_RITUAL) != 0;
                if (developed)
                {
                    score += master ? .58 : .75;
                    if (!card.IsLink && card.level >= 4) score += master ? .22 : .35;
                    if (!card.IsLink && card.level >= 7) score += master ? .28 : .45;
                    if (!card.IsLink && card.level >= 9) score += master ? .25 : .45;
                }
                if (card.IsLink) score += (utilityLinkOne ? .12 : card.level * .28) + (card.level >= 5 ? 1.65 : card.level >= 4 ? .55 : 0);
                if (monster.effectsDisabled) continue;
                if (style == DuelDeckStyle.Lock && DuelDeckStrategy.IsKeyPiece(monster.code)) score += 1.2;
                if (!breakingBoard && monster.code == SignatureTurnPlan.ScarredDragonArchfiend) score += .7;
                if (monster.code == Machinex && (contract || monster.overlayCodes.Count >= 2))
                    score += 1.7 + .26 * Math.Min(3, opposingMonsters);
                else if (monster.code == Siegfried) score += 1.65 + .36 * Math.Min(3, opposingSpells);
                else if (Tier1EndboardBosses.Contains(monster.code))
                    score += breakingBoard ? 1.55 : 2.22;
                else if (DuelTactics.CanNegateWithIllusionOfChaos(monster, own, rival))
                    score += breakingBoard ? .85 : 1.62;
                else if (monster.code != Gilgamesh && !(monster.code == 2857636 && opposingSpells == 0) &&
                    developed && LinkComboPolicy.IsInteractive(card))
                    score += master
                        ? (breakingBoard ? .55 : (card.DeckEffectCategories & NegatesEffects) != 0 ? 1.15 : .85)
                        : (breakingBoard ? .85 : (card.DeckEffectCategories & NegatesEffects) != 0 ? 1.62 : 1.23);

                if (!breakingBoard && HasPassiveProtection(card)) score += .36;

                // Matchup / situation-adaptive bonus:
                if (GoingSecondFinishers.Contains(monster.code))
                    score += rivalHeavyBoard ? 1.35 : breakingBoard ? .45 : -.45;
                if (rivalUsesGrave && GraveyardHateBosses.Contains(monster.code))
                    score += 0.90;
            }
            foreach (var monster in rival.mzone)
                if (monster?.IsFaceUp == true) score -= Math.Max(0, monster.IsDefense ? monster.defense : monster.attack) / 4300.0;
            if (breakingBoard && opposingMonsters == 0 && attack > 0)
            {
                // The battle simulation rewards a resolved win (100). Still reward open-board lethal pressure
                // so pre-battle checkpoints and truncated branches prefer clearing the board for game.
                if (master) score += attack >= rival.lp ? 2.5 : Math.Min(1.0, (double)attack / Math.Max(8000, rival.lp));
                else score += attack >= rival.lp ? 8 : (double)attack / Math.Max(8000, rival.lp);
            }
            return score;
        }

        internal static bool HasPassiveProtection(CardInfo card) =>
            (card.desc?.Contains("효과의 대상이 되지", StringComparison.Ordinal) == true ||
             card.desc?.Contains("효과로는 파괴되지", StringComparison.Ordinal) == true ||
             card.desc?.Contains("효과를 받지 않는다", StringComparison.Ordinal) == true) &&
            // Almiraj must Tribute itself to protect a target.
            CardDatabase.GetArchetypeCollectionKey(card) != Almiraj;

        internal static ulong Fingerprint(DuelSession duel, DuelPrompt prompt)
        {
            ulong hash = 14695981039346656037UL;
            void Mix(ulong item) => hash = unchecked((hash ^ item) * 1099511628211UL);
            void Zone(IEnumerable<LiveCardState> cards, bool own)
            {
                foreach (var card in cards)
                {
                    Mix(card == null ? 0UL : 1UL);
                    if (card == null) continue;
                    Mix(card.position);
                    if (!own && !card.IsFaceUp) continue;
                    Mix(card.code);
                    Mix((uint)card.attack);
                    Mix((uint)card.defense);
                    Mix(card.effectsDisabled ? 1UL : 0);
                    if (card.code == 84815190) Mix(duel.HasSpentBaronneNegate(card) ? 1UL : 0);
                    foreach (uint material in card.overlayCodes) Mix(material);
                }
                Mix(0xffffffff);
            }
            void Options(List<SelectableCardItem> choices)
            {
                Mix((uint)choices.Count);
                foreach (var card in choices)
                {
                    Mix(card.code);
                    Mix(card.controller);
                    Mix(card.location);
                    Mix(card.sequence);
                    Mix(card.description);
                }
            }

            Mix((uint)duel.TurnNumber);
            Mix((uint)duel.TurnInterruptions);
            Mix((uint)duel.SummonedMonstersThisTurn(prompt.playerId));
            Mix((uint)DuelTactics.GetExtensionRisk(duel, prompt));
            Mix((uint)duel.CurrentPhase);
            Mix(duel.FieldDisabledMask);
            Mix(prompt.playerId);
            Mix(prompt.messageType);
            Mix((prompt.canBp ? 1UL : 0) | (prompt.canEp ? 2UL : 0));
            var own = duel.Players[prompt.playerId];
            var rival = duel.Players[1 - prompt.playerId];
            Mix((uint)own.lp);
            Mix((uint)rival.lp);
            Mix((uint)own.deckCount);
            Mix((uint)rival.deckCount);
            Mix((uint)rival.hand.Count);
            Zone(own.hand, true);
            Zone(own.mzone, true);
            Zone(own.szone, true);
            Zone(own.grave, true);
            Zone(own.extra, true);
            Zone(own.banished, true);
            Zone(rival.mzone, false);
            Zone(rival.szone, false);
            Zone(rival.grave, false);
            Zone(rival.banished, false);
            Options(prompt.summonable);
            Options(prompt.spSummonable);
            Options(prompt.repositionable);
            Options(prompt.mSetable);
            Options(prompt.sSetable);
            Options(prompt.activatable);
            if (prompt.messageType == YgoConstants.MSG_SELECT_BATTLECMD)
            {
                Mix(prompt.canM2 ? 1UL : 0UL);
                Options(prompt.attackable);
                foreach (var attack in prompt.attackable) Mix(attack.directAttackable ? 1UL : 0UL);
            }
            if (prompt.messageType != YgoConstants.MSG_SELECT_IDLECMD)
            {
                if (prompt.messageType == YgoConstants.MSG_SELECT_POSITION)
                {
                    Mix(prompt.promptCardCode);
                    Mix(prompt.allowedPositions);
                }
                Mix(prompt.SelectionHint);
                Mix(prompt.min);
                Mix(prompt.max);
                Mix(prompt.finishable ? 1UL : 0UL);
                Options(prompt.candidates);
                Options(prompt.unselectCandidates);
                Options(prompt.mustSelect);
                foreach (ulong option in prompt.options) Mix(option);
                foreach (var link in duel.CurrentChain)
                {
                    Mix(link.code);
                    Mix(link.triggerPlayer);
                    Mix(link.description);
                    Mix(link.negated ? 1UL : 0UL);
                }
            }
            return hash;
        }
    }
}
