using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YGOPortable.Core;

namespace YGOPortable.AI
{
    public enum AIDifficulty
    {
        Easy = 0,
        Normal = 1,
        Smart = 2,
    }

    public class TacticalAI
    {
        public AIDifficulty Difficulty { get; set; } = AIDifficulty.Smart;
        internal bool UsesTrainedPolicy => Difficulty == AIDifficulty.Smart && _policy != null;
        internal bool HasLinkPolicy => Difficulty == AIDifficulty.Smart && _linkPolicy != null;
        internal int SimulatedComboBranches => _comboSearch?.SimulatedBranches ?? 0;
        internal int ChangedComboLines => _comboSearch?.ChangedLines ?? 0;
        internal int ChosenComboAlternatives => _comboSearch?.ChosenAlternatives ?? 0;
        private bool UsesTacticalRules => Difficulty == AIDifficulty.Smart && _tacticalRules;
        private bool UsesComboPlanning => _comboSearch != null || _comboProjection;

        private const int IdleRecoveryBudgetMilliseconds = 45;
        private const uint TrickstarLightStage = 35371948;
        private const uint PrimevalPlanetPerlereino = 77103950;
        private const uint TheHiddenCity = 5697558;
        private const uint UnionHangar = 66399653;
        private const uint AromaGarden = 5050644;
        private const uint PotOfGreed = 55144522;
        private const uint GracefulCharity = 79571449, AHeroLives = 8949584, Tuning = 96363153;
        private const uint Faris = 18094166, ShadowMist = 50720316, Malicious = 9411399, Scheiren = 572850;
        private const uint VisasStarfrost = 56099748;
        private const uint MelffyFootsteps = 25687946;
        private const ushort MelffySet = 0x147;
        private const ushort TrickstarSet = 0xfb;
        private const ushort TearlamentsSet = 0x182;
        private const ushort SubterrorSet = 0xed;
        private const ushort AromaSet = 0xc9;

        private int _lastTurn = -1;
        private int _actionsThisTurn;
        private int _battleActions;
        private int _unselectCount;
        private uint _attackerSequence = uint.MaxValue;
        private uint _plannedAttackTargetSequence = uint.MaxValue;
        private uint _linkSummon;
        private uint? _crossoutDeclaration;
        private bool? _linkDeck;
        private readonly Dictionary<string, int> _effectCounts = new Dictionary<string, int>();
        private readonly HashSet<uint> _repositionedSeqs = new HashSet<uint>();
        private readonly DuelAiPolicy _policy;
        private readonly LinkComboPolicy _linkPolicy;
        private readonly ComboSearch _comboSearch;
        private bool _comboProjection;
        private bool _checkedIdleRecovery;
        private DuelPrompt _preparingCombo;
        private IEnumerator<bool> _comboPreparation;
        private readonly bool _tacticalRules;
        private bool _checkedDirectAttackOrder;

        public TacticalAI() { }

        internal TacticalAI(DuelAiPolicy policy, LinkComboPolicy linkPolicy = null, bool searchCombos = false)
        {
            _policy = policy;
            _linkPolicy = linkPolicy;
            if (searchCombos) _comboSearch = new ComboSearch();
            _tacticalRules = true;
        }

        public void ResetTurnState(int turn)
        {
            if (_lastTurn != turn)
            {
                _lastTurn = turn;
                _actionsThisTurn = 0;
                _battleActions = 0;
                _checkedDirectAttackOrder = false;
                _checkedIdleRecovery = false;
                _unselectCount = 0;
                _attackerSequence = uint.MaxValue;
                _plannedAttackTargetSequence = uint.MaxValue;
                _linkSummon = 0;
                _effectCounts.Clear();
                _repositionedSeqs.Clear();
            }
        }

        public byte[] ComputeResponse(DuelSession session, DuelPrompt prompt) => ComputeComboResponse(session, prompt, null);

        internal byte[] ComputeComboResponse(DuelSession session, DuelPrompt prompt, int? plannedMainAction)
        {
            if (prompt == null) return Array.Empty<byte>();
            ResetTurnState(session.TurnNumber);
            if (prompt.messageType != YgoConstants.MSG_ANNOUNCE_CARD) _crossoutDeclaration = null;
            if (UsesTacticalRules && prompt.messageType == YgoConstants.MSG_SELECT_CARD && prompt.SelectionHint == 551 &&
                prompt.min == 1 && prompt.max == 1 && DuelTactics.ChooseWidowAnchorTarget(session, prompt) is int anchorTarget)
                return PackIndexList(new[] { anchorTarget });
            if (_comboSearch?.TakePlannedSelection(session, prompt) is int selected)
            {
                if (prompt.messageType == YgoConstants.MSG_SELECT_UNSELECT_CARD) _unselectCount++;
                return EncodePlannedSelection(prompt, selected);
            }

            // Prevent retry deadlock: fallback safely if core rejected our last answer
            bool emergencyFallback = session.RetryCount > 3;

            switch (prompt.messageType)
            {
                case YgoConstants.MSG_SELECT_IDLECMD:
                    return BuildIdleCmd(session, prompt, emergencyFallback, plannedMainAction);

                case YgoConstants.MSG_SELECT_BATTLECMD:
                    return BuildBattleCmd(session, prompt, emergencyFallback);

                case YgoConstants.MSG_SELECT_CHAIN:
                {
                    int choice = PickChain(session, prompt, emergencyFallback);
                    if (UsesTacticalRules && choice >= 0 && prompt.candidates[choice].code == DuelTactics.CrossoutDesignator)
                        _crossoutDeclaration = DuelTactics.FindCrossoutTarget(session, prompt.playerId);
                    return BitConverter.GetBytes(choice);
                }

                case YgoConstants.MSG_SELECT_EFFECTYN:
                    return BitConverter.GetBytes(DecideEffectYesNo(session, prompt));

                case YgoConstants.MSG_SELECT_YESNO:
                    if (UsesTacticalRules && session.SelectionChainLink is { } source && source.triggerPlayer == prompt.playerId &&
                        ((source.code is 98338152 or 98338153 && prompt.promptDesc == (((ulong)source.code << 20) | 1)) ||
                        (source.code == 51227866 && prompt.promptDesc == ((ulong)source.code << 20))) &&
                        DuelDeckStrategy.ShouldKeepMainMonsterZoneOpen(session, prompt.playerId) &&
                        DuelCombat.VisibleAttackTotal(session.Players[1 - prompt.playerId]) < session.Players[prompt.playerId].lp)
                        return BitConverter.GetBytes(0);
                    if (UsesTacticalRules && DuelTactics.ChooseResolutionYesNo(session, prompt) is int answer)
                        return BitConverter.GetBytes(answer);
                    return BitConverter.GetBytes(prompt.IsDirectAttackChoice && _plannedAttackTargetSequence != uint.MaxValue ? 0 : 1);

                case YgoConstants.MSG_SELECT_OPTION:
                    return BitConverter.GetBytes(DddTurnPlan.ChooseOption(session, prompt) ??
                        (UsesTacticalRules ? DuelTactics.ChooseOption(session, prompt) : null) ?? 0);

                case YgoConstants.MSG_ANNOUNCE_NUMBER:
                    return BitConverter.GetBytes(0);

                case YgoConstants.MSG_SELECT_CARD:
                    return BuildSelectCard(session, prompt);

                case YgoConstants.MSG_SELECT_UNSELECT_CARD:
                    return BuildSelectUnselect(session, prompt);

                case YgoConstants.MSG_SELECT_TRIBUTE:
                    return BuildSelectTribute(session, prompt);

                case YgoConstants.MSG_SELECT_PLACE:
                case YgoConstants.MSG_SELECT_DISFIELD:
                    return BuildSelectPlace(session, prompt);

                case YgoConstants.MSG_SELECT_POSITION:
                    return BitConverter.GetBytes((UsesTacticalRules ? DuelTactics.ChoosePosition(session, prompt) : null) ?? PickPosition(session, prompt));

                case YgoConstants.MSG_SELECT_SUM:
                    return BuildSelectSum(prompt);

                case YgoConstants.MSG_SELECT_COUNTER:
                    return BuildSelectCounter(prompt);

                case YgoConstants.MSG_SORT_CHAIN:
                case YgoConstants.MSG_SORT_CARD:
                    return new byte[] { unchecked((byte)-1) };

                case YgoConstants.MSG_ANNOUNCE_RACE:
                    return BitConverter.GetBytes(PickBits64(prompt.announceMask, (int)prompt.min));

                case YgoConstants.MSG_ANNOUNCE_ATTRIB:
                    return BitConverter.GetBytes((uint)PickBits64(prompt.announceMask, (int)prompt.min));

                case YgoConstants.MSG_ANNOUNCE_CARD:
                {
                    uint[] valid = YgoNativeBridge.FilterDeclarableCards(prompt.options.ToArray(), _crossoutDeclaration.HasValue ? 20000 : 256);
                    if (_crossoutDeclaration is uint target && Array.IndexOf(valid, target) >= 0)
                        return BitConverter.GetBytes((int)target);
                    uint pick = valid.Length > 0 ? valid[0] : 89631139u;
                    return BitConverter.GetBytes((int)pick);
                }

                case YgoConstants.MSG_ROCK_PAPER_SCISSORS:
                    return BitConverter.GetBytes(1 + Math.Abs(session.TurnNumber + session.Logs.Count) % 3);

                default:
                    return BitConverter.GetBytes(0);
            }
        }

        internal TacticalAI ForkForComboSearch()
        {
            var copy = new TacticalAI(_policy, _linkPolicy)
            {
                Difficulty = Difficulty,
                _lastTurn = _lastTurn,
                _actionsThisTurn = _actionsThisTurn,
                _linkDeck = _linkDeck,
                _comboProjection = true,
            };
            foreach (var pair in _effectCounts) copy._effectCounts.Add(pair.Key, pair.Value);
            copy._repositionedSeqs.UnionWith(_repositionedSeqs);
            return copy;
        }

        internal int[] ComboMainOptions(DuelSession session, DuelPrompt prompt)
        {
            bool planLinks = PlanLinks(session, prompt);
            bool preserveOpeningBoard = prompt.spSummonable.Count > 0 && FusionXyzTurnPlan.PreserveOpeningBoard(session, prompt.playerId);
            var choices = new List<(int action, int priority)>();
            var distinct = new HashSet<(int command, uint code, uint location, ulong description)>();

            void Add(List<SelectableCardItem> items, int command)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    if (item.controller != prompt.playerId || !CanUseMainAction(session, prompt, planLinks, command, item, projected: true) ||
                        (command == 1 && preserveOpeningBoard && item.location == YgoConstants.LOCATION_EXTRA) ||
                        !distinct.Add((command, item.code, item.location, item.description))) continue;
                    var card = CardDatabase.Get(item.code);
                    uint tags = card?.DeckEffectCategories ?? 0;
                    bool develops = (tags & ((1u << 5) | (1u << 6) | (1u << 7))) != 0;
                    int priority = command switch
                    {
                        1 => 550 + (card?.IsExtra == true ? 140 * (card.IsLink ? (int)card.level : 2) : 50),
                        5 => 400 + (item.location == YgoConstants.LOCATION_GRAVE ? 120 : 0),
                        _ => 500,
                    };
                    int developmentBonus = develops ? command == 5 ? 370 : 260 : 0;
                    choices.Add(((i << 16) | command, priority + developmentBonus));
                }
            }

            Add(prompt.spSummonable, 1);
            Add(prompt.activatable, 5);
            Add(prompt.summonable, 0);
            foreach (var summon in prompt.summonable)
                if (DuelTactics.ChooseDefensiveSet(session, prompt, summon, followupUnproven: true, compareWithBattle: true) is int setIndex &&
                    CanUseMainAction(session, prompt, planLinks, 3, prompt.mSetable[setIndex], projected: true) &&
                    !choices.Any(choice => choice.action == ((setIndex << 16) | 3)))
                    choices.Add(((setIndex << 16) | 3, 510));
            if (prompt.canBp && HasWorthwhileAttack(session, prompt.playerId)) choices.Add((6, 850));
            if (prompt.canEp && CountOwnMonsters(session, prompt.playerId) > 0 && prompt.sSetable.Count == 0)
                choices.Add((7, 900));
            return choices.OrderByDescending(choice => choice.priority).Take(10).Select(choice => choice.action).ToArray();
        }

        private bool PlanLinks(DuelSession session, DuelPrompt prompt) =>
            HasLinkPolicy && (_linkDeck ??= LinkComboPolicy.FitsDeck(session.Players[prompt.playerId]));

        private bool CanUseMainAction(DuelSession session, DuelPrompt prompt, bool planLinks, int command, SelectableCardItem item,
            bool projected = false) =>
            CanUseIdleAction(session, command, item) &&
            (command != 0 || projected || !UsesTacticalRules || (!ComboSearch.NeedsProvenNormalSummon(item.code) &&
                (_comboSearch == null || (!DuelTactics.RisksEndPhaseDestruction(session, prompt) &&
                    !DuelTactics.NeedsTributeProjection(session, prompt, item))))) &&
            (command is not (0 or 3) || !UsesTacticalRules || !DuelDeckStrategy.IsExodiaPiece(item.code) ||
                DuelDeckStrategy.For(session, prompt.playerId) != DuelDeckStyle.Lock) &&
            (command is not (0 or 3) || !UsesTacticalRules ||
                DuelDeckStrategy.CanFillMainMonsterZone(session, prompt, item, setting: command == 3)) &&
            (command != 1 || projected || !UsesTacticalRules || item.location == YgoConstants.LOCATION_EXTRA ||
                DuelDeckStrategy.CanFillMainMonsterZone(session, prompt, item, setting: false)) &&
            (command != 4 || !UsesTacticalRules || DuelDeckStrategy.CanSetBackrow(session, prompt.playerId, item.code)) &&
            (command != 1 || (CanUseSpecialSummon(session.Players[prompt.playerId], item, planLinks, projected) &&
                (!UsesTacticalRules || DuelTactics.CanSummonRedDragonSafely(session, prompt, item.code)))) &&
            (command != 5 || (CanActivate(session, prompt, item) && CanPlayFieldSpell(session, prompt, item) &&
                CanEndMainPhaseWithEffect(session, prompt, item)));

        private bool CanUseSpecialSummon(PlayerFieldSnapshot own, SelectableCardItem item, bool planLinks, bool projected)
        {
            if (UsesTacticalRules && UsesComboPlanning && LinkComboPolicy.NeedsMaterialProjection(own, item)) return projected;
            return !planLinks || LinkComboPolicy.CanUseSummon(own, item);
        }

        private bool CanEndMainPhaseWithEffect(DuelSession session, DuelPrompt prompt, SelectableCardItem effect)
        {
            if (!UsesTacticalRules || effect.code != MelffyFootsteps ||
                effect.location is not (YgoConstants.LOCATION_HAND or YgoConstants.LOCATION_SZONE)) return true;

            // Footsteps advances to the End Phase. Do the other usable plays before starting that effect.
            if (prompt.summonable.Count != 0 || (prompt.canBp && HasWorthwhileAttack(session, prompt.playerId))) return false;
            if (prompt.spSummonable.Any(item => CanUseIdleAction(session, 1, item) &&
                CardDatabase.Get(item.code) is CardInfo card && CardDatabase.ExtractSetcodes(card.setcode).Contains(MelffySet))) return false;
            return !prompt.sSetable.Any(item => CardDatabase.Get(item.code) is CardInfo card &&
                (card.IsTrap || (card.type & YgoConstants.TYPE_QUICKPLAY) != 0));
        }

        internal bool AdvanceComboPlanning(DuelSession session, DuelPrompt prompt)
        {
            if (!ReferenceEquals(_preparingCombo, prompt))
            {
                _comboPreparation?.Dispose();
                _comboPreparation = null;
                _preparingCombo = prompt;
                ResetTurnState(session.TurnNumber);
                if (prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD && CanSearchCombo(session, prompt, PlanLinks(session, prompt)))
                    _comboPreparation = _comboSearch.Prepare(session, prompt, this).GetEnumerator();
            }
            if (_comboPreparation == null) return true;
            if (_comboPreparation.MoveNext()) return false;
            _comboPreparation.Dispose();
            _comboPreparation = null;
            return true;
        }

        private bool CanSearchCombo(DuelSession session, DuelPrompt prompt, bool planLinks) =>
            _comboSearch != null && Difficulty == AIDifficulty.Smart && session.RetryCount == 0 && _actionsThisTurn < 48 &&
            !(prompt.canBp && DuelCombat.OpenLethal(session, prompt.playerId)) &&
            ChoosePriorityMainAction(session, prompt, planLinks) == null &&
            ReferenceEquals(session.ActivePrompt, prompt) && (IsRecovering(session, prompt) || planLinks ||
                DddTurnPlan.IsDddDeck(session.Players[prompt.playerId]) ||
                ComboSearch.CanDevelop(prompt) ||
                prompt.summonable.Any(item => DuelTactics.NeedsTributeProjection(session, prompt, item)));

        private bool IsRecovering(DuelSession session, DuelPrompt prompt) =>
            _comboSearch != null && UsesTacticalRules && session.TurnPlayer == prompt.playerId && session.TurnInterruptions > 0;

        private byte[] BuildIdleCmd(DuelSession s, DuelPrompt p, bool fallback, int? plannedMainAction)
        {
            bool planLinks = PlanLinks(s, p);
            int? preparation = plannedMainAction == null && UsesTacticalRules && !fallback
                ? ChoosePriorityMainAction(s, p, planLinks) : null;
            if (plannedMainAction == null && CanSearchCombo(s, p, planLinks))
                plannedMainAction = _comboSearch.Choose(s, p, this);

            _unselectCount = 0;
            _actionsThisTurn++;
            _linkSummon = 0;
            bool CanUse(int command, SelectableCardItem item) => CanUseMainAction(s, p, planLinks, command, item);

            if (plannedMainAction == null && UsesTacticalRules && p.canBp && DuelCombat.OpenLethal(s, p.playerId))
                return BitConverter.GetBytes(6);
            if (preparation is int ready && _actionsThisTurn <= 48)
                return EncodeIdleAction(s, p, ready & 0xffff, ready >> 16);
            if (fallback || _actionsThisTurn > 56)
            {
                if (p.canBp && HasWorthwhileAttack(s, p.playerId)) return StartBattle(s, p);
                if (p.canEp) return EndMainPhase(s, p);
            }
            else if (plannedMainAction is int action && UsePlannedMainAction(s, p, action,
                (command, item) => CanUseMainAction(s, p, planLinks, command, item, projected: true)) is byte[] response)
            {
                return response;
            }
            else if ((plannedMainAction == ComboSearch.KeepSetup || IsRecovering(s, p)) && (p.canEp || p.canBp))
            {
                if (IsRecovering(s, p) && s.Players[p.playerId].deckCount >= 2)
                {
                    int draw = p.activatable.FindIndex(effect => effect.code == PotOfGreed && CanUse(5, effect));
                    if (draw >= 0) return EncodeIdleAction(s, p, 5, draw);
                }
                return FinishMainPhase(s, p, p.canBp && HasWorthwhileAttack(s, p.playerId), preserveHand: true);
            }
            else if (((planLinks ? _linkPolicy.Choose(s, p, CanUse) : null) ?? ChoosePlannedIdle(s, p, CanUse)) is int planned)
            {
                int index = planned >> 16;
                if (!_comboProjection && UsesComboPlanning && (planned & 0xffff) == 0 &&
                    DuelTactics.ChooseDefensiveSet(s, p, p.summonable[index], followupUnproven: true) is int setIndex &&
                    CanUse(3, p.mSetable[setIndex]))
                    return EncodeIdleAction(s, p, 3, setIndex);
                return EncodeIdleAction(s, p, planned & 0xffff, planned >> 16);
            }

            bool canBattle = p.canBp && HasWorthwhileAttack(s, p.playerId);
            bool preserveBoard = p.spSummonable.Count > 0 && FusionXyzTurnPlan.PreserveOpeningBoard(s, p.playerId);
            var policy = UsesTrainedPolicy ? _policy.ForStyle(DuelDeckStrategy.For(s, p.playerId)) : null;
            bool usePolicy = policy?.UsesMainPhase == true && !fallback;
            if (usePolicy && canBattle && CanFinishWithAromaGarden(s, p))
            {
                int garden = p.activatable.FindIndex(item => item.code == AromaGarden && CanUse(5, item));
                if (garden >= 0) return EncodeIdleAction(s, p, 5, garden);
            }

            int? genericAction = usePolicy
                ? policy.ChooseMainAction(s, p, canBattle, preserveBoard, CanUse)
                : ChooseRuleMainAction(s, p, preserveBoard, CanUse);
            if (genericAction is int selected)
            {
                int command = selected & 0xffff;
                int index = selected >> 16;
                if (command == 6) return StartBattle(s, p);
                if (UsesTacticalRules && command == 0 &&
                    DuelTactics.ChooseDefensiveSet(s, p, p.summonable[index], followupUnproven: UsesComboPlanning && !_comboProjection) is int setIndex &&
                    CanUse(3, p.mSetable[setIndex]))
                    return EncodeIdleAction(s, p, 3, setIndex);
                return EncodeIdleAction(s, p, command, index);
            }

            return FinishMainPhase(s, p, canBattle, preserveHand: false);
        }

        private int? ChoosePriorityMainAction(DuelSession session, DuelPrompt prompt, bool planLinks)
        {
            if (DuelTactics.ChooseArchfiendEndPhaseUpgrade(session, prompt) is int upgrade &&
                CanUseMainAction(session, prompt, planLinks, 1, prompt.spSummonable[upgrade]))
                return (upgrade << 16) | 1;

            var own = session.Players[prompt.playerId];
            bool exposedOpening = session.TurnNumber <= 2 && own.lp > 1000 && own.mzone.All(card => card == null) &&
                prompt.summonable.Any(card => (CardDatabase.Get(card.code)?.atk ?? 0) <
                    DuelTactics.StrongestAttacker(session.Players[1 - prompt.playerId]));
            bool canSpendHandtrap = session.TurnNumber <= 2 && prompt.summonable.Any(card => ComboSearch.IsHandDisruption(card.code));
            bool usableRitualDraw = canSpendHandtrap &&
                CardDatabase.Get(session.GetObservedOwnDeckTop(prompt.playerId)) is { IsSpell: true } top &&
                (top.type & YgoConstants.TYPE_RITUAL) != 0 &&
                own.hand.Any(card => (card.type & YgoConstants.TYPE_RITUAL) != 0 && (card.type & YgoConstants.TYPE_MONSTER) != 0) &&
                own.mzone.All(card => card == null || card.code == SignatureTurnPlan.MagiciansSouls) &&
                own.szone.All(card => card == null || card is { code: 47222536, IsFaceUp: true });
            int best = -1, priority = 0;
            for (int i = 0; i < prompt.activatable.Count; i++)
            {
                var item = prompt.activatable[i];
                bool drawRitual = usableRitualDraw && item.location == YgoConstants.LOCATION_MZONE &&
                    item.code == SignatureTurnPlan.MagiciansSouls &&
                    item.description == (((ulong)SignatureTurnPlan.MagiciansSouls << 20) | 1);
                if (item.controller != prompt.playerId || (!drawRitual && item.location is not (YgoConstants.LOCATION_HAND or YgoConstants.LOCATION_SZONE)))
                    continue;
                int value = item.code switch
                {
                    DuelTactics.LightningStorm when DuelTactics.ShouldLeadWithLightningStorm(session, prompt) => 5,
                    PotOfGreed when own.deckCount >= 2 => 4,
                    GracefulCharity when own.deckCount >= 3 => 3,
                    SignatureTurnPlan.MagiciansSouls when drawRitual => 2,
                    _ when canSpendHandtrap && CardDatabase.Get(item.code) is { IsSpell: true } ritual &&
                        (ritual.type & YgoConstants.TYPE_RITUAL) != 0 => 2,
                    // See the result of the search before committing a vulnerable D/D to a Normal Summon or Set.
                    _ when exposedOpening && DddTurnPlan.IsContractSearch(item) => 2,
                    AHeroLives when session.TurnNumber == 1 && own.lp >= 4000 => 2,
                    Faris when session.TurnNumber <= 2 && own.hand.Any(card => card.code is ShadowMist or Malicious) => 2,
                    Scheiren when session.TurnNumber <= 2 && own.deckCount >= 3 &&
                        own.hand.Any(card => card.code is 74078255 or 73956664 or 37961969) => 2,
                    Tuning when session.TurnNumber <= 2 && prompt.summonable.Count != 0 => 1,
                    _ when DuelDeckStrategy.CanRevealAnkhForSummon(own, item) => 2,
                    _ => 0,
                };
                if (value <= priority || !CanUseMainAction(session, prompt, planLinks, 5, item)) continue;
                best = i;
                priority = value;
            }
            if (best >= 0) return (best << 16) | 5;
            if (DddTurnPlan.ChooseEarlyPendulumRecovery(session, prompt,
                (command, item) => CanUseMainAction(session, prompt, planLinks, command, item)) is int pendulum)
                return pendulum;
            if (DuelDeckStrategy.For(session, prompt.playerId) == DuelDeckStyle.Resource &&
                SignatureTurnPlan.ChooseResourceOpening(session, prompt,
                    (command, item) => CanUseMainAction(session, prompt, planLinks, command, item)) is int resource)
                return resource;
            if (session.TurnNumber <= 2 && !DddTurnPlan.IsDddDeck(own) &&
                DuelDeckStrategy.For(session, prompt.playerId) == DuelDeckStyle.Development)
                return PendulumTurnPlan.ChooseIdle(session, prompt,
                    (command, item) => CanUseMainAction(session, prompt, planLinks, command, item));
            return null;
        }

        private byte[] FinishMainPhase(DuelSession s, DuelPrompt p, bool canBattle, bool preserveHand)
        {
            // Prepare defenders for battle. Smart AI considers defensive changes when it ends its turn.
            if (p.repositionable.Count > 0 && s.CurrentPhase == YgoConstants.PHASE_MAIN1)
            {
                for (int i = 0; i < p.repositionable.Count; i++)
                {
                    var rep = p.repositionable[i];
                    if (_repositionedSeqs.Contains(rep.sequence)) continue;
                    var live = s.Players[p.playerId].mzone[rep.sequence];
                    if (live == null) continue;
                    int oppMax = MaxOpponentAtk(s, p.playerId);
                    if (live.IsDefense && live.attack >= oppMax && live.attack >= 1400 && p.canBp)
                        return EncodeIdleAction(s, p, 2, i);
                    if (!UsesTacticalRules && !live.IsDefense && live.attack < oppMax && live.defense > live.attack + 400)
                        return EncodeIdleAction(s, p, 2, i);
                }
            }

            if (canBattle) return StartBattle(s, p);

            // 6. Monster Set only if no Normal Summon was possible & monster has solid DEF
            if (p.mSetable.Count > 0 && CountOwnMonsters(s, p.playerId) == 0 &&
                (!preserveHand || MaxOpponentAtk(s, p.playerId) >= s.Players[p.playerId].lp))
            {
                int bestIdx = -1;
                int bestDef = -1;
                for (int i = 0; i < p.mSetable.Count; i++)
                {
                    if (UsesTacticalRules && !DuelDeckStrategy.CanFillMainMonsterZone(s, p, p.mSetable[i], setting: true)) continue;
                    if (UsesTacticalRules && DuelDeckStrategy.IsExodiaPiece(p.mSetable[i].code) &&
                        MaxOpponentAtk(s, p.playerId) < s.Players[p.playerId].lp) continue;
                    var ci = CardDatabase.Get(p.mSetable[i].code);
                    int df = ci != null ? ci.def : 0;
                    if (df > bestDef) { bestDef = df; bestIdx = i; }
                }
                if (bestIdx >= 0) return BitConverter.GetBytes((bestIdx << 16) | 3);
            }

            // 7. Set Spell/Trap cards cleanly (Traps/Quick-Plays) in S/T zones
            if (p.sSetable.Count > 0)
            {
                for (int i = 0; i < p.sSetable.Count; i++)
                {
                    var ci = CardDatabase.Get(p.sSetable[i].code);
                    if (ci != null && (ci.IsTrap || (ci.type & YgoConstants.TYPE_QUICKPLAY) != 0) &&
                        (!UsesTacticalRules || DuelDeckStrategy.CanSetBackrow(s, p.playerId, ci.code)))
                    {
                        return BitConverter.GetBytes((i << 16) | 4);
                    }
                }
            }

            if (p.canEp) return EndMainPhase(s, p);
            if (p.canBp) return StartBattle(s, p);
            return BitConverter.GetBytes(7);
        }

        private byte[] EndMainPhase(DuelSession session, DuelPrompt prompt)
        {
            if (UsesTacticalRules && !_comboProjection && prompt.canBp &&
                DuelCombat.CanCloseBattleFromMain(session, prompt.playerId))
                return BitConverter.GetBytes(6);

            // Apply end-of-turn positioning after the live AI has committed to ending the Main Phase.
            if (UsesTacticalRules &&
                DuelTactics.ChooseDefensiveReposition(session, prompt) is int index)
                return EncodeIdleAction(session, prompt, 2, index);
            return BitConverter.GetBytes(7);
        }

        private byte[] StartBattle(DuelSession session, DuelPrompt prompt)
        {
            // An attacker cannot change its position after battle. Close a threatened lethal lane first.
            if (UsesTacticalRules && DuelTactics.ChooseDefensiveReposition(session, prompt, beforeBattle: true) is int defender)
                return EncodeIdleAction(session, prompt, 2, defender);
            return BitConverter.GetBytes(6);
        }

        private byte[] UsePlannedMainAction(DuelSession session, DuelPrompt prompt, int action, Func<int, SelectableCardItem, bool> canUse)
        {
            if (action == 6 && prompt.canBp) return StartBattle(session, prompt);
            if (action == 7 && prompt.canEp) return EndMainPhase(session, prompt);
            int command = action & 0xffff;
            int index = action >> 16;
            var options = command switch
            {
                0 => prompt.summonable,
                1 => prompt.spSummonable,
                2 => prompt.repositionable,
                3 => prompt.mSetable,
                4 => prompt.sSetable,
                5 => prompt.activatable,
                _ => null,
            };
            if (options == null || index < 0 || index >= options.Count || !canUse(command, options[index])) return null;
            if (command == 0 && UsesTacticalRules && _comboSearch is { ClearsBattleLethal: false } &&
                DuelTactics.ChooseDefensiveSet(session, prompt, options[index], followupUnproven: true, lethalOnly: true) is int defender &&
                canUse(3, prompt.mSetable[defender]))
                return EncodeIdleAction(session, prompt, 3, defender);
            return EncodeIdleAction(session, prompt, command, index);
        }

        private static int? ChooseRuleMainAction(DuelSession session, DuelPrompt prompt, bool preserveBoard,
            Func<int, SelectableCardItem, bool> canUse)
        {
            int? bestChoice = null;
            int bestScore = int.MinValue;

            void Consider(List<SelectableCardItem> items, int command)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    if (!canUse(command, item) || (command == 1 && preserveBoard && item.location == YgoConstants.LOCATION_EXTRA))
                        continue;
                    var card = CardDatabase.Get(item.code);
                    int score = command switch
                    {
                        1 => (card?.atk ?? 1500) + (card?.IsExtra == true ? 1200 : 400),
                        5 => 500 + (card?.IsMonster == true ? 900 : card?.IsSpell == true ? 800 : card?.IsTrap == true ? 450 : 0) +
                            (card?.IsPendulum == true && item.location == YgoConstants.LOCATION_HAND ? 300 : 0),
                        _ => (card?.atk ?? 1000) + (card?.IsMonster == true && (card.type & YgoConstants.TYPE_EFFECT) != 0 ? 400 : 0),
                    };
                    if (score <= bestScore) continue;
                    bestScore = score;
                    bestChoice = (i << 16) | command;
                }
            }

            if (prompt.spSummonable.Count > 0 && CountOwnMonsters(session, prompt.playerId) < 5)
                Consider(prompt.spSummonable, 1);
            if (!bestChoice.HasValue) Consider(prompt.activatable, 5);
            if (!bestChoice.HasValue) Consider(prompt.summonable, 0);
            return bestChoice;
        }

        private static int? ChoosePlannedIdle(DuelSession session, DuelPrompt prompt, Func<int, SelectableCardItem, bool> canUse)
        {
            return DddTurnPlan.ChooseIdle(session, prompt, canUse)
                ?? LinkTurnPlan.ChooseIdle(session, prompt, canUse)
                ?? SynchroTurnPlan.ChooseIdle(session, prompt, canUse)
                ?? FusionXyzTurnPlan.ChooseIdle(session, prompt, canUse)
                ?? SignatureTurnPlan.ChooseIdle(session, prompt, canUse)
                ?? PendulumTurnPlan.ChooseIdle(session, prompt, canUse);
        }

        private bool CanActivate(DuelSession session, DuelPrompt prompt, SelectableCardItem item) =>
            SynchroTurnPlan.CanActivate(session, prompt, item) && (!UsesTacticalRules || DuelTactics.CanActivate(session, prompt, item));

        private bool CanPlayFieldSpell(DuelSession session, DuelPrompt prompt, SelectableCardItem item)
        {
            if (item.location != YgoConstants.LOCATION_HAND || item.controller != prompt.playerId ||
                CardDatabase.Get(item.code) is not CardInfo incoming || (incoming.type & YgoConstants.TYPE_FIELD) == 0)
                return true;

            var current = session.Players[prompt.playerId].szone[YgoConstants.FIELD_ZONE_SEQUENCE];
            if (current == null || current.effectsDisabled) return true;

            // Flip a set field or use an available effect of the existing field before replacing it.
            if (prompt.activatable.Any(effect => effect.controller == prompt.playerId &&
                YgoConstants.IsFieldZone(effect.location, effect.sequence) &&
                CanUseIdleAction(session, 5, effect))) return false;

            if (!current.IsFaceUp) return true;
            var existing = CardDatabase.Get(current.code);
            uint code = CardDatabase.GetArchetypeCollectionKey(incoming);
            uint currentCode = existing == null ? current.code : CardDatabase.GetArchetypeCollectionKey(existing);
            return code != currentCode || CanGainFromFieldReactivation(session, prompt, code);
        }

        private static bool CanGainFromFieldReactivation(DuelSession session, DuelPrompt prompt, uint code)
        {
            byte player = prompt.playerId;
            return code switch
            {
                // Keep Light Stage in play when it can be holding a set card inactive.
                TrickstarLightStage => !session.Players[1 - player].szone.Take(YgoConstants.FIELD_ZONE_SEQUENCE)
                    .Any(card => card is { IsFaceUp: false }) &&
                    session.HasCardInDeck(player, card => IsMonsterInSet(card, TrickstarSet)),
                PrimevalPlanetPerlereino => session.HasCardInDeck(player,
                    card => card.code == VisasStarfrost || IsMonsterInSet(card, TearlamentsSet)),
                TheHiddenCity => session.HasCardInDeck(player, card => IsMonsterInSet(card, SubterrorSet)),
                UnionHangar => session.HasCardInDeck(player, card => (card.type & YgoConstants.TYPE_UNION) != 0 &&
                    card.race == YgoConstants.RACE_MACHINE && card.attr == YgoConstants.ATTRIBUTE_LIGHT),
                AromaGarden => CanFinishWithAromaGarden(session, prompt),
                _ => false,
            };
        }

        private static bool CanFinishWithAromaGarden(DuelSession session, DuelPrompt prompt)
        {
            var own = session.Players[prompt.playerId];
            var rival = session.Players[1 - prompt.playerId];
            if (!prompt.canBp || rival.mzone.Any(card => card != null)) return false;

            var faceUp = own.mzone.Where(card => card?.IsFaceUp == true).ToArray();
            if (!faceUp.Any(card => CardDatabase.Get(card.code) is CardInfo info && IsMonsterInSet(info, AromaSet)))
                return false;

            var attackers = faceUp.Where(card => card.IsAttack).ToArray();
            int damage = attackers.Sum(card => Math.Max(0, card.attack));
            return damage < rival.lp && damage + 500 * attackers.Length >= rival.lp;
        }

        private static bool IsMonsterInSet(CardInfo card, ushort setcode) =>
            card.IsMonster && CardDatabase.ExtractSetcodes(card.setcode).Any(code => (code & 0xfff) == setcode);

        private static string IdleActionKey(DuelSession session, int command, SelectableCardItem item)
        {
            if (command == 1) return $"sp:{item.code}";
            if (YgoConstants.IsFieldZone(item.location, item.sequence) ||
                (item.location == YgoConstants.LOCATION_HAND && CardDatabase.Get(item.code) is CardInfo card &&
                    (card.type & YgoConstants.TYPE_FIELD) != 0))
            {
                // A replacement can have its own effect, and a second hand copy can move into the first copy's slot.
                int version = session.Players[item.controller].FieldZoneVersion;
                return $"field:{item.controller}:{version}:{item.code}:{item.description}:{item.location}:{item.sequence}";
            }
            return $"act:{item.code}:{item.description}:{item.location}:{item.sequence}";
        }

        private bool CanUseIdleAction(DuelSession session, int command, SelectableCardItem item) =>
            (command != 1 && command != 5) ||
            !_effectCounts.TryGetValue(IdleActionKey(session, command, item), out int count) || count < (command == 1 ? 2 : 1);

        private byte[] EncodeIdleAction(DuelSession session, DuelPrompt prompt, int command, int index)
        {
            if (command == 2) _repositionedSeqs.Add(prompt.repositionable[index].sequence);
            if (command == 1 || command == 5)
            {
                var item = command == 1 ? prompt.spSummonable[index] : prompt.activatable[index];
                if (UsesTacticalRules && (_linkDeck == true || UsesComboPlanning) && command == 1 &&
                    item.location == YgoConstants.LOCATION_EXTRA && LinkComboPolicy.Rank(item.code) > 0)
                    _linkSummon = item.code;
                BumpKey(IdleActionKey(session, command, item));
            }
            return BitConverter.GetBytes((index << 16) | command);
        }

        private byte[] BuildBattleCmd(DuelSession s, DuelPrompt p, bool fallback)
        {
            _battleActions++;
            uint previousAttacker = _attackerSequence;
            _attackerSequence = uint.MaxValue;
            _plannedAttackTargetSequence = uint.MaxValue;
            bool canLeaveBattle = p.canM2 || p.canEp;
            if (UsesTacticalRules && !fallback &&
                DuelCombat.FindLethal(s, p, _comboProjection ? uint.MaxValue : previousAttacker) is { } lethal)
            {
                int attacker = lethal.Attacker;
                if (_comboSearch != null && !_checkedDirectAttackOrder && lethal.Target == null &&
                    DuelCombat.VerifyDirectAttackOrder(s, p, attacker, this) is int verified)
                {
                    _checkedDirectAttackOrder = true;
                    attacker = verified;
                }
                _attackerSequence = p.attackable[attacker].sequence;
                _plannedAttackTargetSequence = lethal.Target ?? uint.MaxValue;
                return BitConverter.GetBytes((attacker << 16) | 1);
            }
            if (canLeaveBattle && (fallback || _battleActions > 25))
                return BitConverter.GetBytes(p.canM2 ? 2 : 3);

            if (p.activatable.Count > 0)
            {
                for (int i = 0; i < p.activatable.Count; i++)
                {
                    var act = p.activatable[i];
                    if (!CanActivate(s, p, act)) continue;
                    string key = $"bpact:{act.code}:{act.description}:{act.sequence}";
                    if (_effectCounts.ContainsKey(key)) continue;
                    BumpKey(key);
                    _attackerSequence = previousAttacker;
                    return BitConverter.GetBytes((i << 16) | 0);
                }
            }

            if (UsesTacticalRules)
            {
                int index = DuelCombat.ChooseAttacker(s, p, !canLeaveBattle);
                if (index < 0) return BitConverter.GetBytes(p.canM2 ? 2 : 3);
                if ((_comboProjection || _comboSearch != null) &&
                    DuelCombat.AllocateExtraBattleRemoval(s, p, index, previousAttacker) is { } allocation &&
                    // For the existing attacker, the target prompt can reject the alternative without changing the attack.
                    (_comboProjection || allocation.Attacker == index ||
                        DuelCombat.VerifyAllocatedTarget(s, p, allocation.Attacker, allocation.Target)))
                {
                    index = allocation.Attacker;
                    _plannedAttackTargetSequence = allocation.Target;
                }
                _attackerSequence = p.attackable[index].sequence;
                return BitConverter.GetBytes((index << 16) | 1);
            }

            if (p.attackable.Count > 0)
            {
                int opp = 1 - p.playerId;
                var oppMonsters = s.Players[opp].mzone.Where(m => m != null).ToList();

                // Pick attacker with guaranteed direct attack or favorable battle
                // A mandatory attacker still has to attack when no matchup is favorable.
                int bestIdx = canLeaveBattle ? -1 : 0;
                int bestGain = -1;

                for (int i = 0; i < p.attackable.Count; i++)
                {
                    var atkItem = p.attackable[i];
                    var live = s.Players[p.playerId].mzone[atkItem.sequence];
                    int myAtk = live != null ? live.attack : (CardDatabase.Get(atkItem.code)?.atk ?? 0);

                    if (atkItem.directAttackable)
                    {
                        int gain = myAtk + 500;
                        if (gain > bestGain)
                        {
                            bestGain = gain;
                            bestIdx = i;
                        }
                        continue;
                    }

                    foreach (var om in oppMonsters)
                    {
                        if (!om.IsFaceUp)
                        {
                            // Attack face-down conservatively with >= 1400 ATK or in Smart mode >= 1500
                            if (myAtk >= 1400)
                            {
                                int gain = myAtk - 800;
                                if (gain > bestGain) { bestGain = gain; bestIdx = i; }
                            }
                        }
                        else if (om.IsDefense)
                        {
                            if (myAtk > om.defense)
                            {
                                int gain = 600 + om.defense / 4;
                                if (gain > bestGain) { bestGain = gain; bestIdx = i; }
                            }
                        }
                        else
                        {
                            if (myAtk > om.attack)
                            {
                                int gain = (myAtk - om.attack) + 1200;
                                if (gain > bestGain) { bestGain = gain; bestIdx = i; }
                            }
                            else if (myAtk == om.attack && myAtk >= 1800 && CountOwnMonsters(s, p.playerId) > oppMonsters.Count)
                            {
                                int gain = 200;
                                if (gain > bestGain) { bestGain = gain; bestIdx = i; }
                            }
                        }
                    }
                }

                if (bestIdx >= 0)
                    return BitConverter.GetBytes((bestIdx << 16) | 1);
            }

            return BitConverter.GetBytes(p.canM2 ? 2 : 3);
        }

        private int PickChain(DuelSession s, DuelPrompt p, bool fallback)
        {
            if (p.candidates.Count == 0) return -1;
            if (p.forced) return 0;
            if (fallback || _actionsThisTurn > 48) return -1;

            // Avoid chaining a fast/quick effect to our own card if opponent hasn't responded,
            // UNLESS this is a simultaneous trigger chain (SEGOC: speCount == 0x7f, e.g. Tearlaments GY triggers).
            if (!p.IsTriggerChain && s.CurrentChain.Count > 0 && s.CurrentChain[s.CurrentChain.Count - 1].triggerPlayer == p.playerId)
                return -1;

            string ChainKey(SelectableCardItem effect) => $"ch:{s.TurnNumber}:{effect.code}:{effect.description}:{s.CurrentChain.Count}";
            if (UsesTacticalRules)
            {
                int winged = p.candidates.FindIndex(card => DuelTactics.ChooseWingedKuribohTiming(s, p, card) == true &&
                    !_effectCounts.ContainsKey(ChainKey(card)));
                if (winged >= 0)
                {
                    BumpKey(ChainKey(p.candidates[winged]));
                    return winged;
                }
            }
            if (UsesTacticalRules && !p.IsTriggerChain &&
                s.CurrentChain.LastOrDefault() is { negated: false, triggerLoc: YgoConstants.LOCATION_MZONE } opposing &&
                opposing.triggerPlayer != p.playerId)
            {
                var field = s.Players[opposing.triggerPlayer].mzone;
                const uint ashBlossom = 14558127;
                // Ash answers the Deck effect even after the monster leaves the field as a cost.
                if (opposing.triggerSeq < field.Length && field[opposing.triggerSeq]?.IsFaceUp != true)
                {
                    int ash = p.candidates.FindIndex(card => card.code == ashBlossom &&
                        !_effectCounts.ContainsKey(ChainKey(card)) && CanActivate(s, p, card));
                    if (ash >= 0)
                    {
                        BumpKey(ChainKey(p.candidates[ash]));
                        return ash;
                    }
                }
            }

            int preferred = -1;
            float bestPriority = 0;
            string preferredKey = null;
            var policy = UsesTrainedPolicy ? _policy.ForStyle(DuelDeckStrategy.For(s, p.playerId)) : null;
            for (int i = 0; i < p.candidates.Count; i++)
            {
                var c = p.candidates[i];
                if (!CanActivate(s, p, c)) continue;
                string key = ChainKey(c);
                if (_effectCounts.ContainsKey(key)) continue;
                if (policy?.UsesChains != true || p.IsTriggerChain || c.isTriggeredEffect)
                {
                    BumpKey(key);
                    return i;
                }

                float priority = policy.ChainPriority(s, p, c) + DuelTactics.ControlChainPriority(s, p, c);
                if (priority <= bestPriority) continue;
                bestPriority = priority;
                preferred = i;
                preferredKey = key;
            }
            if (preferredKey != null) BumpKey(preferredKey);
            return preferred;
        }

        private int DecideEffectYesNo(DuelSession s, DuelPrompt p)
        {
            if (p.IsDirectAttackChoice) return _plannedAttackTargetSequence == uint.MaxValue ? 1 : 0;
            if (UsesTacticalRules && !DuelTactics.CanActivate(s, p, new SelectableCardItem
                {
                    code = p.promptCardCode, controller = p.playerId, location = p.promptLoc, sequence = p.promptSeq,
                    description = p.promptEffectDesc != 0 ? p.promptEffectDesc : p.promptDesc,
                    isTriggeredEffect = p.IsTriggerActivationConfirmation,
                })) return 0;
            string key = $"eyn:{s.TurnNumber}:{p.promptCardCode}:{p.promptDesc}";
            if (_effectCounts.TryGetValue(key, out int cnt) && cnt >= 3) return 0;
            BumpKey(key);
            return 1;
        }

        private byte[] BuildSelectCard(DuelSession s, DuelPrompt p)
        {
            if (UsesTacticalRules && p.IsAttackTargetSelection)
            {
                if (_plannedAttackTargetSequence != uint.MaxValue)
                {
                    int plannedTarget = p.candidates.FindIndex(card => card.controller == 1 - p.playerId &&
                        card.location == YgoConstants.LOCATION_MZONE && card.sequence == _plannedAttackTargetSequence);
                    if (plannedTarget >= 0) return PackIndexList(new[] { plannedTarget });
                }
                int target = DuelCombat.ChooseTarget(s, p, _attackerSequence);
                if (target >= 0) return PackIndexList(new[] { target });
            }

            ulong hint = UsesTacticalRules ? p.SelectionHint : s.LastHintMsg;
            if (UsesTacticalRules && hint == DuelTactics.AddToHand)
            {
                int winged = DuelTactics.FindWingedKuribohSearch(s, p);
                if (winged >= 0) return PackIndexList(new[] { winged });
            }
            if (UsesTacticalRules && p.min == 1 && p.max == 1 && hint == DuelTactics.Negate)
            {
                int target = DuelTactics.ChooseHandtrapTarget(s, p);
                if (target >= 0) return PackIndexList(new[] { target });
            }

            if (UsesTacticalRules && DuelTactics.ChooseResourceCard(s, p) is int resource)
                return PackIndexList(new[] { resource });

            if (p.min == 1 && p.max == 1 &&
                ((_linkDeck == true ? LinkComboPolicy.ChooseSupport(s, p) : null) ??
                LinkComboPolicy.ChooseMaterial(s, p, _linkSummon, _linkDeck == true) ??
                DddTurnPlan.ChooseCard(s, p) ?? LinkTurnPlan.ChooseCard(s, p) ??
                SynchroTurnPlan.ChooseCard(s, p) ?? FusionXyzTurnPlan.ChooseCard(s, p) ??
                SignatureTurnPlan.ChooseCard(s, p)) is int planned)
                return PackIndexList(new[] { planned });

            if (UsesTacticalRules && p.min == 1 && p.max == 1 && hint == DuelTactics.AddToHand &&
                DuelDeckStrategy.For(s, p.playerId) == DuelDeckStyle.Lock)
            {
                int keyPiece = p.candidates.FindIndex(card => card.controller == p.playerId && DuelDeckStrategy.IsKeyPiece(card.code) &&
                    !s.Players[p.playerId].hand.Any(owned => owned.code == card.code));
                if (keyPiece >= 0) return PackIndexList(new[] { keyPiece });
            }
            if (hint == DuelTactics.Negate && p.min == 1 && p.max == 1 &&
                s.CurrentChain.LastOrDefault() is ChainLinkInfo opposingEffect && opposingEffect.triggerPlayer != p.playerId &&
                opposingEffect.triggerLoc == YgoConstants.LOCATION_MZONE)
            {
                int target = p.candidates.FindIndex(card => card.controller == opposingEffect.triggerPlayer &&
                    card.location == opposingEffect.triggerLoc && card.sequence == opposingEffect.triggerSeq &&
                    card.code == opposingEffect.code);
                if (target >= 0) return PackIndexList(new[] { target });
            }

            bool removalHint = UsesTacticalRules ? DuelTactics.IsRemoval(hint) : hint is 500 or 501 or 502 or 504 or DuelTactics.Negate;
            int opp = 1 - p.playerId;

            var ranked = p.candidates
                .Select((c, idx) => (idx, score: UsesTacticalRules
                    ? DuelTactics.SelectionPriority(s, p.playerId, c, hint) : ScoreSelectCandidate(c, p.playerId, opp, removalHint)))
                .OrderByDescending(x => x.score)
                .ToList();

            if (UsesTacticalRules && hint == DuelTactics.AddToHand && p.min == 1 && p.max == 1 && ranked.Count > 1 &&
                ChooseIdleRecovery(s, p, ranked[0].idx) is int recoveryPick)
                return PackIndexList(new[] { recoveryPick });

            int pickCount = (int)p.min;
            if (removalHint && p.max > p.min)
            {
                // Pick opponent targets up to p.max without touching own good cards
                int goodTargets = ranked.Count(r => r.score > 0);
                pickCount = Math.Max((int)p.min, Math.Min((int)p.max, goodTargets));
            }
            if (pickCount == 0 && p.cancelable && p.min == 0)
            {
                return BitConverter.GetBytes(-1);
            }
            pickCount = Math.Max(1, Math.Min(ranked.Count, pickCount));

            return PackIndexList(ranked.Take(pickCount).Select(x => x.idx));
        }

        private int? ChooseIdleRecovery(DuelSession duel, DuelPrompt prompt, int defaultPick)
        {
            if (_checkedIdleRecovery || _comboSearch == null || duel.TurnPlayer != prompt.playerId ||
                duel.CurrentPhase is not (YgoConstants.PHASE_MAIN1 or YgoConstants.PHASE_MAIN2) ||
                CardDatabase.Get(prompt.candidates[defaultPick].code)?.IsExtra != true)
                return null;

            int handPick = -1;
            int handValue = int.MinValue;
            for (int index = 0; index < prompt.candidates.Count; index++)
            {
                var item = prompt.candidates[index];
                if (item.controller != prompt.playerId ||
                    item.location is not (YgoConstants.LOCATION_GRAVE or YgoConstants.LOCATION_REMOVED) ||
                    (item.location == YgoConstants.LOCATION_REMOVED && (item.position & YgoConstants.POS_FACEUP) == 0) ||
                    CardDatabase.Get(item.code) is not CardInfo card)
                    return null;
                if (card.IsExtra) continue;
                int score = DuelTactics.SelectionPriority(duel, prompt.playerId, item, prompt.SelectionHint);
                if (score <= handValue) continue;
                handPick = index;
                handValue = score;
            }
            if (handPick < 0) return null;

            _checkedIdleRecovery = true;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            bool Expired() => clock.ElapsedMilliseconds >= IdleRecoveryBudgetMilliseconds;
            using var trial = duel.ReplayForPlanning(Expired);
            if (trial == null || Expired() || trial.ActivePrompt is not { } replay || replay.playerId != prompt.playerId ||
                replay.messageType != prompt.messageType || replay.SelectionHint != prompt.SelectionHint ||
                replay.min != prompt.min || replay.max != prompt.max || replay.cancelable != prompt.cancelable ||
                replay.candidates.Count != prompt.candidates.Count)
                return null;
            for (int index = 0; index < prompt.candidates.Count; index++)
            {
                var original = prompt.candidates[index];
                var copy = replay.candidates[index];
                if (original.code != copy.code || original.controller != copy.controller ||
                    original.location != copy.location || original.sequence != copy.sequence || original.position != copy.position)
                    return null;
            }

            // Prefer the recovery that reaches the hand only when the Extra Deck choice yields no immediate play.
            if (Expired()) return null;
            trial.BeginSpeculation(prompt.playerId, stopAfterTurn: true);
            trial.SubmitPlanningResponse(PackIndexList(new[] { defaultPick }));
            var next = trial.ActivePrompt;
            bool replace = !Expired() && !trial.SpeculationUncertain && !trial.IsFinished && trial.RetryCount == 0 &&
                trial.TurnNumber == duel.TurnNumber && trial.CurrentPhase == duel.CurrentPhase &&
                trial.CurrentChain.Count == 0 && next?.messageType == YgoConstants.MSG_SELECT_IDLECMD &&
                next.playerId == prompt.playerId && next.summonable.Count == 0 &&
                next.spSummonable.Count == 0 && next.activatable.Count == 0;
            return replace ? handPick : null;
        }

        private byte[] BuildSelectUnselect(DuelSession s, DuelPrompt p)
        {
            _unselectCount++;
            if (UsesTacticalRules && _unselectCount <= 4 && p.SelectionHint == 506 &&
                s.SelectionChainLink is { code: MelffyFootsteps } source && source.triggerPlayer == p.playerId &&
                p.candidates.Count > 0 && p.candidates.All(item => item.controller == p.playerId && item.location == YgoConstants.LOCATION_DECK))
            {
                int target = p.candidates.FindIndex(item => item.code is 98416533 or 20003027 or 93018428);
                return PackTwoInts(1, target >= 0 ? target : 0);
            }
            if (p.finishable && (p.unselectCandidates.Count > 0 || p.candidates.Count == 0 || _unselectCount >= p.candidates.Count + 2))
            {
                _unselectCount = 0;
                return BitConverter.GetBytes(-1);
            }
            if (p.cancelable && _unselectCount >= p.candidates.Count + 4)
            {
                _unselectCount = 0;
                return BitConverter.GetBytes(-1);
            }
            if (p.candidates.Count > 0)
            {
                if ((LinkComboPolicy.ChooseMaterial(s, p, _linkSummon, _linkDeck == true) ??
                    DddTurnPlan.ChooseScale(s, p) ?? LinkTurnPlan.ChooseLinkMaterial(s, p) ??
                    FusionXyzTurnPlan.ChooseLinkMaterial(s, p)) is int material)
                    return PackTwoInts(1, material);

                int bestIdx = 0;
                int bestScore = int.MinValue;
                bool removal = s.LastHintMsg == 500 || s.LastHintMsg == 501 || s.LastHintMsg == 502 || s.LastHintMsg == 504;
                for (int i = 0; i < p.candidates.Count; i++)
                {
                    int sc = UsesTacticalRules ? DuelTactics.SelectionPriority(s, p.playerId, p.candidates[i], p.SelectionHint) :
                        ScoreSelectCandidate(p.candidates[i], p.playerId, 1 - p.playerId, removal);
                    if (sc > bestScore) { bestScore = sc; bestIdx = i; }
                }
                return PackTwoInts(1, bestIdx);
            }
            return p.finishable ? BitConverter.GetBytes(-1) : PackTwoInts(1, 0);
        }

        private byte[] BuildSelectTribute(DuelSession s, DuelPrompt p)
        {
            var sorted = p.candidates
                .Select((c, idx) => (idx, rel: Math.Max(1, (int)c.releaseParam), value: UsesTacticalRules
                    ? DuelTactics.SelectionPriority(s, p.playerId, c, DuelTactics.Release) : -(CardDatabase.Get(c.code)?.atk ?? 1000)))
                .OrderByDescending(x => x.value)
                .ToList();

            var chosen = new List<int>();
            int accRel = 0;
            foreach (var item in sorted)
            {
                if (chosen.Count >= p.max) break;
                chosen.Add(item.idx);
                accRel += item.rel;
                if (accRel >= p.min) break;
            }
            return PackIndexList(chosen);
        }

        private byte[] BuildSelectSum(DuelPrompt p)
        {
            int n = p.candidates.Count;
            int minCnt = (int)p.min;
            int maxCnt = p.max > 0 ? Math.Min(n, (int)p.max) : n;
            int target = (int)(p.acc & 0xFFFF);

            var mustParams = p.mustSelect.Select(m => ((int)(m.sumParam & 0xFFFF), (int)((m.sumParam >> 16) & 0xFFFF))).ToList();
            var candParams = p.candidates.Select(c => ((int)(c.sumParam & 0xFFFF), (int)((c.sumParam >> 16) & 0xFFFF))).ToList();
            var picked = new List<int>();

            if (!p.sumExactCountZeroAny && p.max > 0)
            {
                if (SolveExactSum(mustParams, candParams, target, minCnt, maxCnt, picked))
                    return PackIndexList(picked);
            }
            else
            {
                if (SolveOverflowSum(mustParams, candParams, target, picked))
                    return PackIndexList(picked);
            }

            // Fallback subset
            var fallback = Enumerable.Range(0, Math.Min(n, Math.Max(1, minCnt)));
            return PackIndexList(fallback);
        }

        private static bool CanReachExact(List<(int o1, int o2)> items, int idx, int rem)
        {
            if (rem == 0 && idx == items.Count) return true;
            if (idx >= items.Count || rem < 0) return false;
            var (o1, o2) = items[idx];
            if (o1 > 0 && CanReachExact(items, idx + 1, rem - o1)) return true;
            if (o2 > 0 && CanReachExact(items, idx + 1, rem - o2)) return true;
            return false;
        }

        private static bool SolveExactSum(
            List<(int o1, int o2)> must, List<(int o1, int o2)> cands,
            int target, int minCnt, int maxCnt, List<int> outIndices)
        {
            var cur = new List<int>();
            bool Dfs(int pos)
            {
                if (cur.Count >= minCnt && cur.Count <= maxCnt)
                {
                    var all = new List<(int, int)>(must.Count + cur.Count);
                    all.AddRange(must);
                    foreach (int i in cur) all.Add(cands[i]);
                    if (all.Count > 0 && CanReachExact(all, 0, target))
                    {
                        outIndices.AddRange(cur);
                        return true;
                    }
                }
                if (cur.Count >= maxCnt || pos >= cands.Count) return false;
                for (int i = pos; i < cands.Count; i++)
                {
                    cur.Add(i);
                    if (Dfs(i + 1)) return true;
                    cur.RemoveAt(cur.Count - 1);
                }
                return false;
            }
            return Dfs(0);
        }

        private static bool SolveOverflowSum(
            List<(int o1, int o2)> must, List<(int o1, int o2)> cands,
            int target, List<int> outIndices)
        {
            int mSumMin = 0, mSumMax = 0, mSmallest = int.MaxValue;
            foreach (var (o1, o2) in must)
            {
                int lo = (o2 > 0 && o2 < o1) ? o2 : o1;
                int hi = o2 > o1 ? o2 : o1;
                mSumMin += lo;
                mSumMax += hi;
                if (lo < mSmallest) mSmallest = lo;
            }

            var cur = new List<int>();
            bool Dfs(int pos, int sumMin, int sumMax, int smallest)
            {
                if ((cur.Count > 0 || must.Count > 0) && sumMax >= target && (sumMin - smallest) < target)
                {
                    outIndices.AddRange(cur);
                    return true;
                }
                if (sumMax >= target) return false;
                for (int i = pos; i < cands.Count; i++)
                {
                    var (o1, o2) = cands[i];
                    int lo = (o2 > 0 && o2 < o1) ? o2 : o1;
                    int hi = o2 > o1 ? o2 : o1;
                    cur.Add(i);
                    if (Dfs(i + 1, sumMin + lo, sumMax + hi, Math.Min(smallest, lo))) return true;
                    cur.RemoveAt(cur.Count - 1);
                }
                return false;
            }
            return Dfs(0, mSumMin, mSumMax, mSmallest);
        }

        private byte[] BuildSelectPlace(DuelSession session, DuelPrompt p)
        {
            int needed = Math.Max(1, (int)p.min);
            uint avail = ~p.placeFlag;
            byte me = p.playerId;
            byte opp = (byte)(1 - me);
            if (UsesTacticalRules && needed == 1 && p.messageType == YgoConstants.MSG_SELECT_PLACE && (avail & 0x60) != 0 &&
                DuelDeckStrategy.For(session, me) == DuelDeckStyle.Resource)
                return new[] { me, (byte)YgoConstants.LOCATION_MZONE, (byte)((avail & 0x20) != 0 ? 5 : 6) };
            if (_linkDeck == true && session.TurnPlayer == me && needed == 1 && p.messageType == YgoConstants.MSG_SELECT_PLACE &&
                LinkComboPolicy.ChooseMonsterZone(session.Players[me], avail & 0x7f, _linkSummon) is int zone)
                return new[] { me, (byte)YgoConstants.LOCATION_MZONE, (byte)zone };
            var resp = new List<byte>(needed * 3);

            int[] mOrder = { 5, 6, 2, 1, 3, 0, 4 };
            int[] sOrder = { 2, 1, 3, 0, 4, 5, 6, 7 };

            void Scan(byte con, uint loc, int shift, int[] order)
            {
                foreach (int idx in order)
                {
                    if (resp.Count >= needed * 3) break;
                    if (((avail >> (shift + idx)) & 1u) != 0)
                    {
                        resp.Add(con);
                        resp.Add((byte)loc);
                        resp.Add((byte)idx);
                    }
                }
            }

            Scan(me, YgoConstants.LOCATION_MZONE, 0, mOrder);
            Scan(me, YgoConstants.LOCATION_SZONE, 8, sOrder);
            Scan(opp, YgoConstants.LOCATION_MZONE, 16, mOrder);
            Scan(opp, YgoConstants.LOCATION_SZONE, 24, sOrder);

            while (resp.Count < needed * 3)
            {
                resp.Add(me);
                resp.Add((byte)YgoConstants.LOCATION_MZONE);
                resp.Add(0);
            }
            return resp.ToArray();
        }

        private byte[] BuildSelectCounter(DuelPrompt p)
        {
            int rem = (int)p.min;
            byte[] buf = new byte[p.candidates.Count * 2];
            for (int i = 0; i < p.candidates.Count && rem > 0; i++)
            {
                int take = Math.Min(rem, (int)p.candidates[i].currentCounter);
                rem -= take;
                byte[] sbytes = BitConverter.GetBytes((short)take);
                buf[i * 2] = sbytes[0];
                buf[i * 2 + 1] = sbytes[1];
            }
            return buf;
        }

        private static int PickPosition(DuelSession s, DuelPrompt p)
        {
            var ci = CardDatabase.Get(p.promptCardCode);
            int oppMax = s != null ? MaxOpponentAtk(s, p.playerId) : 0;
            bool preferAtk = ci == null || (ci.atk >= oppMax && ci.atk >= ci.def);
            if (preferAtk && (p.allowedPositions & 0x1) != 0) return 0x1; // POS_FACEUP_ATTACK
            if (!preferAtk && (p.allowedPositions & 0x4) != 0) return 0x4; // POS_FACEUP_DEFENSE
            if ((p.allowedPositions & 0x1) != 0) return 0x1;
            if ((p.allowedPositions & 0x4) != 0) return 0x4;
            if ((p.allowedPositions & 0x8) != 0) return 0x8;
            return 0x2;
        }

        private static int ScoreSelectCandidate(SelectableCardItem item, byte self, int opp, bool isRemoval)
        {
            var ci = CardDatabase.Get(item.code);
            int atk = ci != null ? ci.atk : 1000;
            bool isOpp = item.controller == opp;
            if (isRemoval)
            {
                return (isOpp ? 10000 : -5000) + (isOpp ? atk : -atk);
            }
            return (isOpp ? -5000 : 10000) + atk + (item.location == YgoConstants.LOCATION_EXTRA ? 500 : 0);
        }

        private static bool HasWorthwhileAttack(DuelSession s, int playerId)
        {
            if (s.TurnNumber <= 1) return false;
            int opp = 1 - playerId;
            var oppMons = s.Players[opp].mzone.Where(m => m != null).ToList();
            foreach (var m in s.Players[playerId].mzone)
            {
                if (m == null || !m.IsAttack) continue;
                if (oppMons.Count == 0) return true;
                foreach (var om in oppMons)
                {
                    if (!om.IsFaceUp && m.attack >= 1400) return true;
                    if (om.IsDefense && m.attack > om.defense) return true;
                    if (!om.IsDefense && m.attack > om.attack) return true;
                }
            }
            return false;
        }

        private static int CountOwnMonsters(DuelSession s, int playerId)
        {
            int c = 0;
            foreach (var m in s.Players[playerId].mzone) if (m != null) c++;
            return c;
        }

        private static int MaxOpponentAtk(DuelSession s, int playerId)
        {
            int max = 0;
            foreach (var m in s.Players[1 - playerId].mzone)
            {
                if (m != null && m.IsFaceUp && m.attack > max) max = m.attack;
            }
            return max;
        }

        private static ulong PickBits64(ulong avail, int count)
        {
            ulong res = 0;
            int picked = 0;
            for (int b = 0; b < 64 && picked < count; b++)
            {
                if (((avail >> b) & 1UL) != 0)
                {
                    res |= 1UL << b;
                    picked++;
                }
            }
            return res;
        }

        private void BumpKey(string key)
        {
            _effectCounts[key] = _effectCounts.TryGetValue(key, out int c) ? c + 1 : 1;
        }

        private static byte[] PackTwoInts(int a, int b)
        {
            byte[] buf = new byte[8];
            Buffer.BlockCopy(BitConverter.GetBytes(a), 0, buf, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(b), 0, buf, 4, 4);
            return buf;
        }

        internal static byte[] EncodePlannedSelection(DuelPrompt prompt, int index) => prompt.messageType switch
        {
            YgoConstants.MSG_SELECT_CARD => DuelSession.PackCardSelection(index),
            YgoConstants.MSG_SELECT_UNSELECT_CARD => PackTwoInts(1, index),
            _ => BitConverter.GetBytes(index),
        };

        private static byte[] PackIndexList(IEnumerable<int> indices)
        {
            var list = indices.ToList();
            using (var ms = new MemoryStream(8 + list.Count * 4))
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(0); // format type 0
                bw.Write(list.Count);
                foreach (int idx in list) bw.Write(idx);
                return ms.ToArray();
            }
        }
    }
}
