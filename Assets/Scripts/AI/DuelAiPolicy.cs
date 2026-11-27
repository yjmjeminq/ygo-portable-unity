using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;
using YGOPortable.Core;

namespace YGOPortable.AI
{
    internal sealed class DuelAiPolicy
    {
        internal const string ResourceName = "DuelAiPolicy";
        private const int Version = 2;
        private const int StyleCount = DuelDeckStrategy.StyleCount;
        private const uint Development = (1u << 4) | (1u << 5) | (1u << 6) | (1u << 14) | (1u << 18);
        private const uint Disruption = (1u << 0) | (1u << 1) | (1u << 2) | (1u << 3) | (1u << 8) | (1u << 13);
        private const uint Negation = 1u << 8;
        internal const uint BrandedFusion = 44362883, Albion = 87746184, Lubellion = 70534340, Mirrorjade = 44146295;
        internal const uint FusionDestiny = 52947044, DestroyerPhoenix = 60461804, MiracleFusion = 45906428;
        internal const uint MaskChange = 21143940, DarkLaw = 58481572, PowerBond = 37630732, SuperPoly = 48130397;
        internal const uint MachineAngelRitual = 39996157, MeteonisDrytron = 22398665, Benten = 77235086;
        private const uint MachineAngelAbsoluteRitual = 11398951;
        internal const uint Dakini = 39618799, Nasateiya = 99427357, Vishnu = 78316184, Izana = 91668401;
        internal const uint HeraldOfUltimateness = 48546368, HeraldOfPerfection = 44665365;
        internal const uint MercifulMachineAngel = 64442155, MagnificentMachineAngel = 27331568;
        internal const uint MeteoroaDrytron = 24166324;
        private const uint DrytronDad = 56863746, DrytronDra = 69815951, DrytronQua = 95209656, DrytronMuBeta = 1174075;
        private const uint FallenOfAlbaz = 68468459, Granguignol = 24915933, Sanctifire = 38811586;
        private const uint Masquerade = 6855503, Quaeritis = 72272462;
        private const ushort CyberAngelSet = 0x2093;
        private const uint PhantomOfYubel = 80453041;
        private const uint Garura = 11765832, Mudragon = 54757758, StarvingVenom = 41209827, LovingDefender = 47172959;
        private const uint CyberTwin = 74157028, ChimeratechRampage = 84058253, ChimeratechOverdragon = 64599569,
            WakeUpHero = 32828466;

        [Flags]
        internal enum Decisions { MainPhase = 1, Chains = 2, Both = MainPhase | Chains }
        internal const int FirstChainFeature = (int)Feature.Chain;

        private enum Feature
        {
            NormalSummon, SpecialSummon, SummonAttack, SummonStarter, SummonControl, ExtraSummon, ExtraCommitment,
            Activate, ActivateStarter, ActivateExisting, ActivateControl, ActivateThreat,
            Battle, BattleOpen, BattleDamage,
            Chain, ChainResponse, ChainDisruption, ChainNegate, ChainWindow, ChainEnd, ChainDevelopment,
            Count,
        }

        [Serializable]
        private sealed class Model
        {
            public int version;
            public float[] weights;
            public Decisions decisions;
            public StyleModel[] styles;
        }

        [Serializable]
        private sealed class StyleModel
        {
            public DuelDeckStyle style;
            public float[] weights;
            public Decisions decisions;
        }

        private readonly float[] _weights;
        private readonly DuelAiPolicy[] _styles;
        internal Decisions EnabledDecisions { get; }
        internal bool UsesMainPhase => (EnabledDecisions & Decisions.MainPhase) != 0;
        internal bool UsesChains => (EnabledDecisions & Decisions.Chains) != 0;

        internal DuelAiPolicy(float[] weights, Decisions decisions) : this(weights, decisions, null) { }

        private DuelAiPolicy(float[] weights, Decisions decisions, DuelAiPolicy[] styles)
        {
            if ((decisions & Decisions.Both) == 0 || (decisions & ~Decisions.Both) != 0 ||
                weights == null || weights.Length != (int)Feature.Count ||
                weights.Any(weight => float.IsNaN(weight) || Math.Abs(weight) > 8))
                throw new ArgumentException("Invalid duel policy weights.", nameof(weights));
            _weights = (float[])weights.Clone();
            _styles = styles;
            EnabledDecisions = decisions;
        }

        internal static DuelAiPolicy LoadIncluded()
        {
            var asset = Resources.Load<TextAsset>(ResourceName);
            if (asset == null) return null;
            try
            {
                var model = JsonUtility.FromJson<Model>(asset.text);
                if (model?.version != Version) return null;
                if (model.styles == null || model.styles.Length == 0)
                    return new DuelAiPolicy(model.weights, model.decisions);
                var styles = new DuelAiPolicy[StyleCount];
                foreach (var profile in model.styles)
                {
                    if (profile == null || (uint)profile.style >= StyleCount || styles[(int)profile.style] != null)
                        throw new ArgumentException("Invalid duel policy style.");
                    styles[(int)profile.style] = new DuelAiPolicy(profile.weights, profile.decisions);
                }
                return new DuelAiPolicy(model.weights, model.decisions, styles);
            }
            catch (ArgumentException exception)
            {
                Debug.LogWarning($"[Duel AI] Could not load the included policy. {exception.Message}");
                return null;
            }
        }

        internal string ToJson() => JsonUtility.ToJson(new Model
        {
            version = Version,
            weights = _weights,
            decisions = EnabledDecisions,
            styles = _styles?.Select((policy, index) => (policy, index)).Where(entry => entry.policy != null)
                .Select(entry => new StyleModel { style = (DuelDeckStyle)entry.index, weights = entry.policy._weights,
                    decisions = entry.policy.EnabledDecisions }).ToArray(),
        }, true);

        internal DuelAiPolicy ForStyle(DuelDeckStyle style) => _styles?[(int)style] ?? this;

        internal DuelAiPolicy WithStyle(DuelDeckStyle style, DuelAiPolicy policy)
        {
            var styles = _styles == null ? new DuelAiPolicy[StyleCount] : (DuelAiPolicy[])_styles.Clone();
            styles[(int)style] = policy.ForStyle(style);
            return new DuelAiPolicy(_weights, EnabledDecisions, styles);
        }

        internal float[] CopyWeights() => (float[])_weights.Clone();

        internal static float[] InitialWeights()
        {
            var weights = new float[(int)Feature.Count];
            weights[(int)Feature.NormalSummon] = 1;
            weights[(int)Feature.SpecialSummon] = 1.5f;
            weights[(int)Feature.SummonAttack] = 1;
            weights[(int)Feature.SummonStarter] = .65f;
            weights[(int)Feature.SummonControl] = .5f;
            weights[(int)Feature.ExtraSummon] = .1f;
            weights[(int)Feature.ExtraCommitment] = -.65f;
            weights[(int)Feature.Activate] = 1.25f;
            weights[(int)Feature.ActivateStarter] = .55f;
            weights[(int)Feature.ActivateExisting] = .25f;
            weights[(int)Feature.ActivateControl] = -.35f;
            weights[(int)Feature.ActivateThreat] = .85f;
            weights[(int)Feature.Battle] = .15f;
            weights[(int)Feature.BattleOpen] = .25f;
            weights[(int)Feature.BattleDamage] = 1.15f;
            weights[(int)Feature.Chain] = .2f;
            weights[(int)Feature.ChainResponse] = .8f;
            weights[(int)Feature.ChainDisruption] = -.35f;
            weights[(int)Feature.ChainNegate] = .8f;
            weights[(int)Feature.ChainWindow] = .6f;
            weights[(int)Feature.ChainEnd] = .35f;
            weights[(int)Feature.ChainDevelopment] = .45f;
            return weights;
        }

        internal int? ChooseMainAction(DuelSession session, DuelPrompt prompt, bool canBattle, bool preserveOpeningBoard,
            Func<int, SelectableCardItem, bool> canUse)
        {
            var own = session.Players[prompt.playerId];
            var rival = session.Players[1 - prompt.playerId];
            var style = DuelDeckStrategy.For(session, prompt.playerId);
            int bodies = 0, ownAttack = 0, strongestOwn = 0, strongestRival = 0, directDamage = 0, opposition = 0;
            foreach (var card in own.mzone)
            {
                if (card == null) continue;
                bodies++;
                if (!card.IsFaceUp) continue;
                ownAttack += Math.Max(0, card.attack);
                strongestOwn = Math.Max(strongestOwn, card.attack);
                if (card.IsAttack) directDamage += Math.Max(0, card.attack);
            }
            foreach (var card in rival.mzone)
            {
                if (card == null) continue;
                opposition++;
                if (card.IsFaceUp) strongestRival = Math.Max(strongestRival, card.IsDefense ? card.defense : card.attack);
            }
            bool openBoard = opposition == 0;
            foreach (var card in rival.szone)
                if (card != null) opposition++;
            float threat = Math.Max(Math.Min(4, opposition) * .25f, Math.Clamp((strongestRival - strongestOwn) / 3000f, 0, 1));

            int? choice = canBattle ? 6 : null;
            float best = canBattle ? Weight(Feature.Battle) + (openBoard ? Weight(Feature.BattleOpen) +
                Weight(Feature.BattleDamage) * Math.Min(1, (float)directDamage / Math.Max(rival.lp, 1)) : 0) : float.NegativeInfinity;

            void Consider(List<SelectableCardItem> items, int command)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    bool extra = command == 1 && item.location == YgoConstants.LOCATION_EXTRA;
                    if ((extra && preserveOpeningBoard) || !canUse(command, item)) continue;

                    var card = CardDatabase.Get(item.code);
                    uint flags = card?.DeckEffectCategories ?? 0;
                    float score;
                    if (command == 5)
                    {
                        score = Weight(Feature.Activate);
                        if ((flags & Development) != 0) score += Weight(Feature.ActivateStarter);
                        if (item.location != YgoConstants.LOCATION_HAND) score += Weight(Feature.ActivateExisting);
                        if ((flags & Disruption) != 0) score += Weight(Feature.ActivateControl) + threat * Weight(Feature.ActivateThreat);
                        if (style == DuelDeckStyle.Lock && ((flags & (1u << 4)) != 0 || DuelDeckStrategy.IsKeyPiece(item.code)))
                            score += 1.25f;
                        if (style == DuelDeckStyle.Resource && card?.IsSpell == true && (flags & Development) != 0)
                            score += .8f;
                        if (item.location is YgoConstants.LOCATION_HAND or YgoConstants.LOCATION_SZONE)
                        {
                            score += item.code switch
                            {
                                BrandedFusion when !own.mzone.Any(monster => monster?.code == Mirrorjade) => 1.1f,
                                FusionDestiny when !own.mzone.Any(monster => monster?.code == DestroyerPhoenix) &&
                                    !prompt.spSummonable.Any(option => option.location == YgoConstants.LOCATION_EXTRA &&
                                        CardDatabase.Get(option.code)?.attr != YgoConstants.ATTRIBUTE_DARK) => .8f,
                                MachineAngelRitual or MeteonisDrytron when bodies == 0 => .9f,
                                SuperPoly when HasOpposingFusionMaterial(own, rival) => 2f,
                                _ => 0f,
                            };
                        }
                        if (score <= 0 && prompt.canEp) continue;
                    }
                    else
                    {
                        float attack = Math.Max(0, card?.atk ?? 1000);
                        score = Weight(command == 0 ? Feature.NormalSummon : Feature.SpecialSummon) +
                            Weight(Feature.SummonAttack) * Math.Min(1, attack / 4000);
                        if ((flags & Development) != 0) score += Weight(Feature.SummonStarter);
                        if ((flags & Disruption) != 0) score += Weight(Feature.SummonControl);
                        if (extra) score += Weight(Feature.ExtraSummon) +
                            Weight(Feature.ExtraCommitment) * Math.Clamp((ownAttack - attack) / 5000, 0, 1);
                        if (extra && item.code == PhantomOfYubel && bodies <= 2 &&
                            !own.mzone.Any(monster => monster?.code == PhantomOfYubel))
                            score += 1.4f;
                        if (style != DuelDeckStyle.Development && !canBattle && (flags & Disruption) == 0 &&
                            !DuelDeckStrategy.IsKeyPiece(item.code) && attack < strongestRival)
                            score -= .95f;
                        if (extra && bodies > 0 && score <= 0 && prompt.canEp) continue;
                    }

                    if (score <= best) continue;
                    best = score;
                    choice = (i << 16) | command;
                }
            }

            if (bodies < 5) Consider(prompt.spSummonable, 1);
            Consider(prompt.activatable, 5);
            Consider(prompt.summonable, 0);
            return choice;
        }

        internal float ChainPriority(DuelSession session, DuelPrompt prompt, SelectableCardItem effect)
        {
            uint flags = CardDatabase.Get(effect.code)?.DeckEffectCategories ?? 0;
            float score = Weight(Feature.Chain);
            if ((flags & Disruption) != 0) score += Weight(Feature.ChainDisruption);
            if ((flags & Development) != 0) score += Weight(Feature.ChainDevelopment);
            if (session.CurrentChain.Count > 0 && session.CurrentChain[^1] is { negated: false } last && last.triggerPlayer != prompt.playerId)
            {
                score += Weight(Feature.ChainResponse);
                if ((flags & Negation) != 0) score += Weight(Feature.ChainNegate);
            }

            const uint interaction = YgoConstants.TIMING_SUMMON | YgoConstants.TIMING_SPSUMMON |
                YgoConstants.TIMING_FLIPSUMMON | YgoConstants.TIMING_ATTACK;
            if (effect.isEventSpecificEffect || (prompt.hintTimingOther & interaction) != 0) score += Weight(Feature.ChainWindow);
            if (session.TurnPlayer != prompt.playerId && (prompt.hintTimingOther & YgoConstants.TIMING_END_PHASE) != 0)
                score += Weight(Feature.ChainEnd);
            if (effect.code == DuelTactics.MaxxC && DuelTactics.IsOpponentSummoningEffect(session.CurrentChain.LastOrDefault(), prompt.playerId))
                score += 1.25f;
            if (effect.code == DuelTactics.SolemnStrike && session.CurrentChain.Count == 0 &&
                session.HasPendingSpecialSummon((byte)(1 - prompt.playerId)))
                score += 4f;
            if (effect.code == SuperPoly && HasOpposingFusionMaterial(session.Players[prompt.playerId], session.Players[1 - prompt.playerId]))
                score += 2.5f;
            return score;
        }

        private static readonly ConditionalWeakTable<CardInfo, BoardRoles> BoardRoleCache = new();

        private sealed class BoardRoles
        {
            internal readonly bool Interacts, NeedsDiscard, SpendsOverlay, FairyCost, Recurs, OwnStatsChange;
            internal readonly bool GraveTrigger, SelfSpecialFromHand;
            internal readonly string RequiredFieldMonster;

            internal BoardRoles(CardInfo card)
            {
                var text = card.desc ?? string.Empty;
                bool quickWindow = Has("상대 턴에도", "자신 / 상대 턴", "자신 또는 상대 턴", "상대의 턴", "자신 / 상대의 턴",
                    "퀵 이펙트", "Quick Effect", "during either player's turn");
                bool responds = (Has("상대가", "발동했을 때", "발동하였을 때", "when your opponent") &&
                    Has("발동을 무효", "효과를 무효", "소환을 무효", "효과는 「", "효과를 「", "효과가 된다",
                        "negate the activation", "negate that effect", "becomes"));
                bool restriction = Has("상대는 특수 소환할 수 없", "상대는 묘지", "대신 제외", "발동할 수 없", "지불해야",
                    "your opponent cannot", "banished instead", "cannot activate", "must pay");
                bool interferes = (card.DeckEffectCategories & Disruption) != 0 ||
                    Has("효과를 무효", "발동을 무효", "카드를 파괴", "카드를 제외", "몬스터를 제외", "뒷면 수비 표시로",
                        "패로 되돌", "덱으로 되돌", "묘지로 보", "컨트롤을 얻", "뒷면 표시로 제외",
                        "링크 소환을 실행", "융합 소환한다", "싱크로 소환을 실행", "엑시즈 소환을 실행",
                        "negate", "destroy", "banish", "return", "shuffle", "take control",
                        "Link Summon", "Fusion Summon", "Synchro Summon");
                bool trap = card.IsTrap || (card.type & YgoConstants.TYPE_QUICKPLAY) != 0;
                Interacts = restriction || responds || interferes && (quickWindow || trap);
                if ((card.type & YgoConstants.TYPE_COUNTER) != 0)
                    RequiredFieldMonster = ReadNamedFieldRequirement(text, "자신 필드에 \"", "\" 몬스터가 존재하고,") ??
                        ReadNamedFieldRequirement(text, "if you control a \"", "\" monster,");
                NeedsDiscard = Has("패에서", "discard") && Has("버리고", "묘지로 보내고", "discard");
                SpendsOverlay = card.IsXyz && Has("엑시즈 소재를", "엑시즈 소재 1개", "detach");
                FairyCost = NeedsDiscard && Has("천사족 몬스터", "Fairy monster");
                Recurs = Has("묘지의 이 카드", "이 카드가 묘지에", "이 카드가 묘지에 존재", "this card is in your GY",
                    "this card from your GY") && Has("특수 소환", "패에 넣", "패로 되돌", "Special Summon", "add it to your hand");
                GraveTrigger = Recurs || Has("이 카드가 묘지로 보내졌을 경우", "묘지로 보내졌을 경우", "묘지의 이 카드를 제외하고",
                    "묘지에서 제외하고", "if this card is sent to the GY", "banish this card from your GY");
                SelfSpecialFromHand = Has("패에서 특수 소환할 수 있다", "이 카드를 패에서 특수 소환한다",
                    "Special Summon this card from your hand", "Special Summon this card (from your hand)");
                OwnStatsChange = Has("이 카드의 공격력", "이 카드의 원래 공격력", "this card gains ATK", "this card's ATK") &&
                    !Has("턴 종료시까지", "until the end of this turn");

                bool Has(params string[] needles)
                {
                    foreach (string needle in needles)
                        if (text.Contains(needle, StringComparison.OrdinalIgnoreCase)) return true;
                    return false;
                }
            }

            private static string ReadNamedFieldRequirement(string text, string startMarker, string endMarker)
            {
                int start = text.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase);
                if (start < 0) return null;
                start += startMarker.Length;
                int end = text.IndexOf('"', start);
                if (end <= start || !text.AsSpan(end).StartsWith(endMarker.AsSpan(), StringComparison.OrdinalIgnoreCase)) return null;
                return text.Substring(start, end - start);
            }
        }

        internal static bool? BackrowFieldCondition(PlayerFieldSnapshot field, CardInfo card) =>
            BoardRoleCache.GetValue(card, info => new BoardRoles(info)).RequiredFieldMonster is { } name
                ? HasNamedFieldMonster(field, name) : null;

        private static bool HasNamedFieldMonster(PlayerFieldSnapshot field, string name) => field.mzone.Any(monster =>
            monster is { IsFaceUp: true } && CardDatabase.Get(monster.code)?.name?.Contains(name, StringComparison.OrdinalIgnoreCase) == true);

        internal static int StableAttack(LiveCardState monster, CardInfo info, bool canBattle)
        {
            if (canBattle || monster.attack <= Math.Max(monster.baseAttack, info.atk)) return Math.Max(0, monster.attack);
            return BoardRoleCache.GetValue(info, card => new BoardRoles(card)).OwnStatsChange
                ? Math.Max(0, monster.attack) : Math.Max(0, Math.Max(monster.baseAttack, info.atk));
        }

        internal static int ReadyInteractionCount(DuelSession duel, byte player, bool inspectPrivateCards)
        {
            var field = duel.Players[player];
            int count = 0;
            foreach (var monster in field.mzone)
            {
                if (monster is not { IsFaceUp: true, effectsDisabled: false } ||
                    CardDatabase.Get(monster.code) is not { } info || duel.HasSpentBaronneNegate(monster)) continue;
                var roles = BoardRoleCache.GetValue(info, card => new BoardRoles(card));
                if (!roles.Interacts || roles.SpendsOverlay && monster.overlayCodes.Count == 0 ||
                    roles.NeedsDiscard && field.hand.Count == 0 ||
                    roles.FairyCost && inspectPrivateCards && !field.hand.Any(card => (card.race & YgoConstants.RACE_FAIRY) != 0))
                    continue;
                count++;
            }
            foreach (var card in field.szone)
            {
                if (card == null || card.effectsDisabled || !inspectPrivateCards && !card.IsFaceUp ||
                    CardDatabase.Get(card.code) is not { } info) continue;
                var roles = BoardRoleCache.GetValue(info, data => new BoardRoles(data));
                if (!roles.Interacts || roles.RequiredFieldMonster is { } name && !HasNamedFieldMonster(field, name))
                    continue;
                if (info.IsTrap || (info.type & YgoConstants.TYPE_QUICKPLAY) != 0) count++;
            }
            return count;
        }

        internal static int ReadyHandInterruptions(PlayerFieldSnapshot player)
        {
            int distinct = 0;
            bool emptyField = !player.mzone.Any(card => card != null) && !player.szone.Any(card => card != null);
            for (int index = 0; index < player.hand.Count; index++)
            {
                var card = player.hand[index];
                if (!ComboSearch.IsHandDisruption(card.code) || card.code == DuelTactics.MaxxC && player.deckCount <= 2)
                    continue;
                if (card.code == DuelTactics.CalledBy ||
                    card.code == DuelTactics.InfiniteImpermanence && !emptyField) continue;
                bool duplicate = false;
                for (int previous = 0; previous < index; previous++)
                    if (player.hand[previous].code == card.code)
                    {
                        duplicate = true;
                        break;
                    }
                if (!duplicate) distinct++;
            }
            return distinct;
        }

        internal static bool HasGraveyardValue(CardInfo card) => card != null &&
            BoardRoleCache.GetValue(card, info => new BoardRoles(info)).GraveTrigger;

        internal static bool CanSelfSpecialFromHand(CardInfo card) => card != null &&
            BoardRoleCache.GetValue(card, info => new BoardRoles(info)).SelfSpecialFromHand;

        internal static int GraveyardFollowUps(PlayerFieldSnapshot field)
        {
            int count = 0;
            foreach (var card in field.grave)
            {
                if (CardDatabase.Get(card.code) is not { } info ||
                    !BoardRoleCache.GetValue(info, item => new BoardRoles(item)).Recurs) continue;
                if (++count >= 3) break;
            }
            return count;
        }

        internal static bool IsRitualSpell(uint code) => IsMachineAngelRitual(code) || code is MeteonisDrytron or MeteoroaDrytron;

        internal static bool IsMachineAngelRitual(uint code) => code is MachineAngelRitual or MachineAngelAbsoluteRitual;

        internal static bool IsMachineAngelTribute(uint code) => code is MercifulMachineAngel or MagnificentMachineAngel;

        internal static bool CanSpendForMachineAngel(PlayerFieldSnapshot own) =>
            !own.mzone.Any(IsEstablishedRitualAce) ||
            own.hand.Concat(own.mzone.Where(card => card != null && !IsEstablishedRitualAce(card))).Any(card =>
                CardDatabase.Get(card.code) is { } info && (info.type & YgoConstants.TYPE_RITUAL) != 0 &&
                CardDatabase.ExtractSetcodes(info.setcode).Contains(CyberAngelSet));

        internal static bool IsRitualAce(uint code) => code is Vishnu or Dakini or Izana or
            HeraldOfUltimateness or HeraldOfPerfection or DrytronDad or DrytronDra or DrytronQua;

        internal static bool IsEstablishedRitualAce(LiveCardState card) => card != null && IsRitualAce(card.code);

        internal static bool SpentEstablishedFieldAce(CardMoveEvent move, ChainLinkInfo effect) =>
            move.fromLoc == YgoConstants.LOCATION_MZONE && (move.fromPos & YgoConstants.POS_FACEUP) != 0 &&
            (move.toCon != move.fromCon || move.toLoc != YgoConstants.LOCATION_MZONE) &&
            (effect == null || effect.triggerPlayer == move.fromCon && !effect.negated) &&
            DuelTactics.IsEstablishedFieldAce(move.SourceReadout) &&
            ((move.reason & (YgoConstants.REASON_RELEASE | YgoConstants.REASON_MATERIAL | YgoConstants.REASON_COST)) != 0 ||
                effect != null && (move.reason & YgoConstants.REASON_DESTROY) != 0);

        internal static bool NeedsAceMaterialProjection(PlayerFieldSnapshot own, SelectableCardItem summon) =>
            summon.location == YgoConstants.LOCATION_EXTRA && CardDatabase.Get(summon.code)?.IsExtra == true &&
            // A spare monster might not have the level, Type or name required by the offered summon.
            own.mzone.Any(card => card?.IsFaceUp == true && DuelTactics.IsEstablishedFieldAce(card));

        internal static bool NeedsAceFusionProjection(PlayerFieldSnapshot own, SelectableCardItem effect)
        {
            if (!own.mzone.Any(card => card?.IsFaceUp == true && DuelTactics.IsEstablishedFieldAce(card)) ||
                CardDatabase.Get(effect.code) is not { desc: { } rules } ||
                !rules.Contains("융합 소재", StringComparison.Ordinal) ||
                !own.extra.Any(card => CardDatabase.Get(card.code)?.IsFusion == true)) return false;
            string text = CardDatabase.GetDescriptionCardCode(effect.description) == effect.code
                ? CardDatabase.ExtractIndexedEffectDescription(effect.code, (int)(effect.description & 0xfffff)) ?? rules : rules;
            int material = text.IndexOf("융합 소재", StringComparison.Ordinal);
            return material >= 0 && text.Contains("융합 소환", StringComparison.Ordinal) &&
                text[..material].Contains("필드", StringComparison.Ordinal);
        }

        internal static bool SpentOwnRitualAce(CardMoveEvent move, ChainLinkInfo effect) =>
            move.fromLoc == YgoConstants.LOCATION_MZONE && move.toLoc != YgoConstants.LOCATION_MZONE &&
            IsRitualAce(move.code) &&
            (effect == null || effect.triggerPlayer == move.fromCon) &&
            ((move.reason & (YgoConstants.REASON_RELEASE | YgoConstants.REASON_MATERIAL | YgoConstants.REASON_COST)) != 0 ||
                effect != null && !effect.negated && (move.reason & YgoConstants.REASON_DESTROY) != 0);

        internal static bool IsDrytronReleaseEffect(uint code) => code is 97148796 or 96026108 or 60037599 or 33543890 or 22420202;

        internal static bool IsDrytronMonster(uint code) => IsDrytronReleaseEffect(code) ||
            code is 22435424 or DrytronMuBeta or DrytronDad or DrytronDra or DrytronQua;

        internal static bool CanSpendForDrytron(PlayerFieldSnapshot own, SelectableCardItem source) =>
            !own.mzone.Any(IsEstablishedRitualAce) ||
            own.hand.Concat(own.mzone.Where(card => card != null && !IsEstablishedRitualAce(card))).Any(card =>
                (source.code != card.code || source.location == YgoConstants.LOCATION_GRAVE) &&
                (IsDrytronMonster(card.code) ||
                    CardDatabase.Get(card.code) is { IsMonster: true } info && (info.type & YgoConstants.TYPE_RITUAL) != 0));

        internal static bool IsProtectedRitualMaterial(PlayerFieldSnapshot own, byte player, SelectableCardItem card) =>
            card.controller == player && card.location == YgoConstants.LOCATION_MZONE &&
            card.sequence < own.mzone.Length && IsEstablishedRitualAce(own.mzone[card.sequence]);

        internal static bool CanActivateRitual(PlayerFieldSnapshot own, SelectableCardItem spell)
        {
            if (!IsRitualSpell(spell.code) || spell.location is not (YgoConstants.LOCATION_HAND or YgoConstants.LOCATION_SZONE) ||
                spell.code == MeteoroaDrytron && spell.description != (((ulong)MeteoroaDrytron << 20) | 1) ||
                !own.mzone.Any(IsEstablishedRitualAce)) return true;
            return own.hand.Any(target => CanPayWithoutRitualAce(own, spell.code, target)) ||
                spell.code is MeteonisDrytron or MeteoroaDrytron && own.grave.Any(target => CanPayWithoutRitualAce(own, spell.code, target));
        }

        private static bool CanPayWithoutRitualAce(PlayerFieldSnapshot own, uint spell, LiveCardState target)
        {
            var info = CardDatabase.Get(target.code);
            if (info == null || (info.type & YgoConstants.TYPE_RITUAL) == 0 || info.level is 0 or > 31) return false;
            if (spell is MeteonisDrytron or MeteoroaDrytron)
            {
                if (spell == MeteoroaDrytron && info.race != YgoConstants.RACE_MACHINE) return false;
                var machines = own.hand.Where(card => card != target)
                    .Concat(own.mzone.Where(card => card != null && !IsEstablishedRitualAce(card)));
                int attack = machines.Where(card => card.race == YgoConstants.RACE_MACHINE).Sum(card =>
                    card is { code: DrytronMuBeta, IsFaceUp: true, effectsDisabled: false }
                        ? Math.Max(card.attack, card.overlayCodes.Sum(code =>
                            CardDatabase.Get(code) is { race: YgoConstants.RACE_MACHINE } material ? Math.Max(0, material.atk) : 0))
                        : Math.Max(0, card.attack));
                return info.atk > 0 && attack >= info.atk;
            }
            bool exact = spell == MachineAngelAbsoluteRitual;
            if (exact && !CardDatabase.ExtractSetcodes(info.setcode).Contains(CyberAngelSet)) return false;

            var materials = own.hand.Where(card => card != target)
                .Concat(own.mzone.Where(card => card != null && !IsEstablishedRitualAce(card)));
            if (exact)
                materials = materials.Concat(own.grave.Where(card => card.race is YgoConstants.RACE_FAIRY or YgoConstants.RACE_WARRIOR));
            ulong reachable = 1;
            int total = 0;
            foreach (var card in materials)
            {
                // Hand and face-down cards may carry their printed level only in the database.
                if ((card.type & (YgoConstants.TYPE_LINK | YgoConstants.TYPE_XYZ)) != 0) continue;
                uint level = card.level > 0 ? card.level : CardDatabase.Get(card.code)?.level ?? 0;
                if (level is 0 or > 31) continue;
                reachable |= reachable << (int)level;
                total += (int)level;
                if (exact ? (reachable & (1UL << (int)info.level)) != 0 : total >= info.level) return true;
            }
            return false;
        }

        internal static bool CanRiskPowerBond(DuelSession duel, DuelPrompt prompt)
        {
            var own = duel.Players[prompt.playerId];
            // The Extra Deck lists owned cards, not the engine's legal Fusion targets.
            // An unavailable low-ATK card cannot make the activation safe.
            return own.extra.Select(card => CardDatabase.Get(card.code)).Where(card =>
                card != null && (card.type & YgoConstants.TYPE_FUSION) != 0 && card.race == YgoConstants.RACE_MACHINE)
                .All(card => PowerBondBurnCeiling(own, card) < own.lp);
        }

        private static int PowerBondBurnCeiling(PlayerFieldSnapshot own, CardInfo fusion)
        {
            if (fusion.atk >= 0) return fusion.atk;
            if (fusion.code != ChimeratechOverdragon) return int.MaxValue;
            // Include the GY in case Cyberdark Chimera has enabled it as Fusion material.
            return 800 * own.hand.Concat(own.mzone).Concat(own.grave)
                .Count(card => card?.race == YgoConstants.RACE_MACHINE);
        }

        internal static bool HasOpposingFusionMaterial(PlayerFieldSnapshot own, PlayerFieldSnapshot rival)
        {
            var facing = rival.mzone.Where(card => card?.IsFaceUp == true && CanUseAsFusionMaterial(card)).ToArray();
            if (facing.Length == 0) return false;
            var board = own.mzone.Where(card => CanUseAsFusionMaterial(card) && !DuelTactics.IsEstablishedFieldAce(card))
                .Concat(facing).ToArray();
            return own.extra.Any(card => OpposingMaterialCount(card.code, board, facing) > 0);
        }

        private static bool CanUseAsFusionMaterial(LiveCardState card) => card != null &&
            CardDatabase.Get(card.code)?.desc?.Contains("이 카드는 융합 소재로 할 수 없다", StringComparison.Ordinal) != true;

        internal static bool CanFuseWithCartesia(PlayerFieldSnapshot own)
        {
            if (!own.mzone.Any(card => card?.code == Mirrorjade)) return true;
            var materials = own.hand.Concat(own.mzone.Where(card => card != null && card.code != Mirrorjade))
                .Where(card => CanUseAsFusionMaterial(card) && CardDatabase.Get(card.code)?.IsMonster == true).ToArray();
            const uint lightOrDark = YgoConstants.ATTRIBUTE_LIGHT | YgoConstants.ATTRIBUTE_DARK;
            return own.extra.Any(fusion => fusion.code switch
            {
                Granguignol => Pair(card => card.code == DuelTactics.Cartesia, card => (card.attribute & lightOrDark) != 0),
                Sanctifire => Pair(card => card.code == FallenOfAlbaz,
                    card => card.attribute == YgoConstants.ATTRIBUTE_LIGHT && card.race == YgoConstants.RACE_SPELLCASTER),
                Albion => Pair(card => card.code == FallenOfAlbaz, card => card.attribute == YgoConstants.ATTRIBUTE_LIGHT),
                Lubellion => Pair(card => card.code == FallenOfAlbaz, card => card.attribute == YgoConstants.ATTRIBUTE_DARK),
                Masquerade or Quaeritis => Pair(card => CardDatabase.Get(card.code) is { } info &&
                    CardDatabase.ExtractSetcodes(info.setcode).Contains((ushort)0x166), card => (card.attribute & lightOrDark) != 0),
                _ => false,
            });

            bool Pair(Func<LiveCardState, bool> first, Func<LiveCardState, bool> second) =>
                materials.Any(a => first(a) && materials.Any(b => a != b && second(b)));
        }

        private static int OpposingMaterialCount(uint target, LiveCardState[] board, LiveCardState[] facing)
        {
            bool Match(LiveCardState first, LiveCardState second) => target switch
            {
                Garura => first.attribute == second.attribute && first.race == second.race && first.CurrentNameCode != second.CurrentNameCode,
                Mudragon => first.attribute == second.attribute && first.race != second.race,
                StarvingVenom => first.attribute == YgoConstants.ATTRIBUTE_DARK && second.attribute == YgoConstants.ATTRIBUTE_DARK &&
                    ((first.type | second.type) & YgoConstants.TYPE_TOKEN) == 0,
                LovingDefender => DuelCombat.IsYubel(first.code) && (second.type & YgoConstants.TYPE_EFFECT) != 0 ||
                    DuelCombat.IsYubel(second.code) && (first.type & YgoConstants.TYPE_EFFECT) != 0,
                13243124 => first.race == second.race && first.attribute != second.attribute,
                87758525 => first.IsFaceUp && (second.type & YgoConstants.TYPE_FUSION) != 0 ||
                    second.IsFaceUp && (first.type & YgoConstants.TYPE_FUSION) != 0,
                69946549 => (first.type & YgoConstants.TYPE_FUSION) != 0 && second.attribute == YgoConstants.ATTRIBUTE_DARK ||
                    (second.type & YgoConstants.TYPE_FUSION) != 0 && first.attribute == YgoConstants.ATTRIBUTE_DARK,
                _ => false,
            };
            if (facing.Any(first => facing.Any(second => first != second && Match(first, second)))) return 2;
            return facing.Any(first => board.Any(second => first != second && Match(first, second))) ? 1 : 0;
        }

        internal static int? ChooseSummonTarget(DuelSession duel, DuelPrompt prompt, uint source)
        {
            if (prompt.min != 1 || prompt.max != 1 || prompt.candidates.Count < 2 ||
                prompt.candidates.Any(card => card.controller != prompt.playerId)) return null;
            var own = duel.Players[prompt.playerId];
            var rival = duel.Players[1 - prompt.playerId];
            if (prompt.candidates.All(item => item.location == YgoConstants.LOCATION_EXTRA))
            {
                if (source == SuperPoly)
                {
                    var facing = rival.mzone.Where(card => card?.IsFaceUp == true && CanUseAsFusionMaterial(card)).ToArray();
                    var board = own.mzone.Where(card => CanUseAsFusionMaterial(card) && !DuelTactics.IsEstablishedFieldAce(card))
                        .Concat(facing).ToArray();
                    return Best(item => OpposingMaterialCount(item.code, board, facing));
                }
                return Best(item => source switch
                {
                    BrandedFusion or Lubellion or Albion => item.code switch
                    {
                        Mirrorjade => 6,
                        Lubellion when own.hand.Count > 1 && own.extra.Any(card => card.code == Mirrorjade) => 4,
                        Albion => 3,
                        _ => 0,
                    },
                    FusionDestiny => item.code == DestroyerPhoenix ? 5 : 0,
                    MaskChange or 21143941 or 93600443 => item.code == DarkLaw ? 5 : 0,
                    MiracleFusion => item.code == WakeUpHero ? 4 : item.code == 22908820 ? 3 : 0,
                    DuelTactics.Cartesia => item.code is Granguignol or Sanctifire ? 5 :
                        item.code is Masquerade or Quaeritis or Albion or Lubellion ? 3 : 0,
                    PowerBond => PowerBondTarget(item),
                    _ => 0,
                });
            }
            if (IsRitualSpell(source) && prompt.SelectionHint == 509 &&
                prompt.candidates.All(item => (CardDatabase.Get(item.code)?.type & YgoConstants.TYPE_RITUAL) != 0))
            {
                bool preserveAce = own.mzone.Any(IsEstablishedRitualAce);
                return Best(item => (item.code switch
                {
                    Dakini or Vishnu when rival.mzone.Any(card => card != null) => 5,
                    DrytronDad => 6,
                    DrytronQua when rival.szone.Count(card => card != null) >= 2 => 5,
                    DrytronDra => 4,
                    HeraldOfUltimateness or HeraldOfPerfection when own.hand.Any(card =>
                        card.code != item.code && CardDatabase.Get(card.code)?.race == YgoConstants.RACE_FAIRY) => 5,
                    HeraldOfUltimateness or HeraldOfPerfection => 1,
                    Nasateiya when rival.mzone.Any(card => card != null) => 2,
                    Benten => 1,
                    _ => 3,
                }) + (preserveAce &&
                    (item.location == YgoConstants.LOCATION_GRAVE ? own.grave : own.hand).FirstOrDefault(card => card.code == item.code) is { } target &&
                    CanPayWithoutRitualAce(own, source, target) ? 10 : 0));
            }
            return null;

            int PowerBondTarget(SelectableCardItem item)
            {
                var fusion = CardDatabase.Get(item.code);
                if (fusion == null || PowerBondBurnCeiling(own, fusion) >= own.lp) return 0;
                return item.code is CyberTwin or ChimeratechRampage ? 6 : 1;
            }

            int? Best(Func<SelectableCardItem, int> value)
            {
                int best = 0;
                int? choice = null;
                for (int i = 0; i < prompt.candidates.Count; i++)
                {
                    int score = value(prompt.candidates[i]);
                    if (score <= best) continue;
                    best = score;
                    choice = i;
                }
                return choice;
            }
        }

        private float Weight(Feature feature) => _weights[(int)feature];
    }
}
