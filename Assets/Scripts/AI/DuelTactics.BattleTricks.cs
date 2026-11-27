using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using YGOPortable.Core;

namespace YGOPortable.AI
{
    internal static partial class DuelTactics
    {
        private static readonly Regex EffectNumber = new(@"[①-⑳]\s*[:：]", RegexOptions.CultureInvariant);
        private static readonly Regex StatDelta = new(@"(\d+)\s*(?:만큼\s*)?(올린다|내린다|상승|감소)|(?:gains?|loses?)\s+(\d+)\s+(?:ATK|DEF)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly ConditionalWeakTable<CardInfo, BattleEffects> BattleProfiles = new();

        internal enum BattleChange { Unknown, Increase, Decrease, Double, Half, Zero, OpposingAttack, Pierce, DoubleDamage, ExtraAttack }

        internal readonly struct BattleTrick
        {
            internal readonly BattleChange Change;
            internal readonly int Amount;
            internal readonly bool DefenseOnly;
            internal readonly bool OpponentOnly;
            internal readonly ulong Race;
            internal readonly uint Attribute, MonsterType;
            internal readonly ushort Archetype;
            internal readonly bool EndPhaseDestruction;

            internal BattleTrick(BattleChange change, int amount, bool defenseOnly, bool opponentOnly, ulong race = 0,
                uint attribute = 0, uint monsterType = 0, ushort archetype = 0, bool endPhaseDestruction = false)
            {
                Change = change;
                Amount = amount;
                DefenseOnly = defenseOnly;
                OpponentOnly = opponentOnly;
                Race = race;
                Attribute = attribute;
                MonsterType = monsterType;
                Archetype = archetype;
                EndPhaseDestruction = endPhaseDestruction;
            }

            internal bool ReducesStat => Change is BattleChange.Decrease or BattleChange.Half or BattleChange.Zero;

            internal bool AppliesTo(LiveCardState card)
            {
                if (Race != 0 && (card.race & Race) == 0 || Attribute != 0 && (card.attribute & Attribute) == 0 ||
                    MonsterType != 0 && (card.type & MonsterType) == 0) return false;
                if (Archetype == 0) return true;
                ulong sets = CardDatabase.Get(card.code)?.setcode ?? 0;
                for (; sets != 0; sets >>= 16)
                    if ((sets & 0xfff) == Archetype) return true;
                return false;
            }
        }

        private sealed class BattleEffects
        {
            internal readonly BattleTrick?[] Tricks;
            internal readonly string[] Labels;

            internal BattleEffects(CardInfo card)
            {
                string rules = card.desc ?? string.Empty;
                var printed = EffectNumber.Split(rules);
                Tricks = printed.Length == 1 ? new[] { ReadBattleTrick(rules) } :
                    printed.Skip(1).Select(ReadBattleTrick).ToArray();
                Labels = card.strs ?? Array.Empty<string>();
            }

            internal BattleTrick? For(ulong description, uint code)
            {
                if (Tricks.Length == 1) return Tricks[0];
                if (Tricks.Length == 0) return null;
                if (CardDatabase.GetDescriptionCardCode(description) == code)
                {
                    int index = (int)(description & 0xfffff);
                    if (index < Labels.Length && !string.IsNullOrWhiteSpace(Labels[index]))
                    {
                        if (!DescribesCombat(Labels[index])) return null;
                        if (index < Tricks.Length && Tricks[index].HasValue) return Tricks[index];
                        var combatOnly = Tricks.Where(trick => trick.HasValue).Take(2).ToArray();
                        return combatOnly.Length == 1 ? combatOnly[0] : null;
                    }
                    if (index < Tricks.Length && Tricks.All(trick => trick.HasValue)) return Tricks[index];
                }
                // Unknown effect indices must not turn a mixed-purpose effect into a combat-only one.
                return Tricks.All(trick => trick.HasValue) ? Tricks[0] : null;
            }
        }

        internal static bool TryGetTemporaryBattleTrick(uint code, ulong description, out BattleTrick trick)
        {
            var info = CardDatabase.Get(code);
            var effect = info == null ? null : BattleProfiles.GetValue(info, card => new BattleEffects(card)).For(description, code);
            trick = effect.GetValueOrDefault();
            return effect.HasValue;
        }

        internal static bool CanSpendBattleTrick(DuelSession duel, DuelPrompt prompt, SelectableCardItem effect)
        {
            if (!TryGetTemporaryBattleTrick(effect.code, effect.description, out var trick)) return true;
            if (duel.TurnNumber == 1 || duel.CurrentPhase is YgoConstants.PHASE_MAIN2 or YgoConstants.PHASE_END)
                return false;
            return DuelCombat.BattleTrickImprovesOutcome(duel, prompt.playerId, trick);
        }

        internal static int ChooseBattleTrickTarget(DuelSession duel, DuelPrompt prompt)
        {
            if (!duel.HasActiveAttack || prompt.min != 1 || prompt.max != 1 ||
                duel.SelectionChainLink is not { } effect || effect.triggerPlayer != prompt.playerId ||
                !TryGetTemporaryBattleTrick(effect.code, effect.description, out var trick) ||
                prompt.candidates.Any(card => card.location != YgoConstants.LOCATION_MZONE) ||
                (IsRemoval(prompt.SelectionHint) || prompt.SelectionHint is Release or Discard or 511 or 512 or 513))
                return -1;

            bool chooseAttacker = (duel.AttackerCon == prompt.playerId) != trick.ReducesStat;
            uint controller = chooseAttacker ? duel.AttackerCon : duel.TargetCon;
            uint location = chooseAttacker ? duel.AttackerLoc : duel.TargetLoc;
            uint sequence = chooseAttacker ? duel.AttackerSeq : duel.TargetSeq;
            return prompt.candidates.FindIndex(card =>
                card.controller == controller && card.location == location && card.sequence == sequence);
        }

        private static bool DescribesCombat(string text) =>
            Has(text, "공격력", "수비력", "관통", "전투 데미지", "번 공격", "회 공격", "ATK", "DEF", "piercing",
                "battle damage", "attack twice", "additional attack", "can attack again");

        private static bool Has(string text, params string[] keywords)
        {
            foreach (string keyword in keywords)
                if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static BattleTrick? ReadBattleTrick(string text)
        {
            bool stat = Has(text, "공격력", "수비력", " ATK", " DEF");
            bool timed = Has(text, "턴 종료시까지", "턴 종료 시까지", "이 턴에", "이 턴 중", "until the end of this turn",
                "until the end phase", "this turn", "데미지 스텝 종료시까지", "배틀 페이즈 종료시까지",
                "until the end of the Damage Step", "until the end of the Battle Phase");
            bool piercing = Has(text, "관통", "수비력을 공격력이 넘은 만큼", "piercing");
            bool doubleDamage = Has(text, "전투 데미지", "battle damage") && Has(text, "배가", "배의", "doubled", "double");
            bool extraAttack = Has(text, "번 공격", "회 공격", "연속 공격", "attack twice", "additional attack", "attack again");
            if (!timed || !(stat || piercing || doubleDamage || extraAttack) ||
                Has(text, "무효", "효과를 받지", "특수 소환한다", "특수 소환할 수 있다", "패에 넣는다", "패에 넣을 수", "드로우",
                    "컨트롤을 얻", "대상으로 할 수 없", "negate", "unaffected", "special summon", "draw a card", "take control"))
                return null;

            // Ignore an End Phase penalty while identifying the attack effect.
            int penalty = text.IndexOf("이 턴의 엔드 페이즈", StringComparison.Ordinal);
            if (penalty < 0) penalty = text.IndexOf("during the End Phase", StringComparison.OrdinalIgnoreCase);
            string effect = penalty < 0 ? text : text[..penalty];
            int sentences = effect.IndexOf("발동할 수 있다.", StringComparison.Ordinal);
            if (sentences >= 0) effect = effect[(sentences + "발동할 수 있다.".Length)..];
            if (Has(effect, "제외한다", "묘지로 보낸다", "패로 되돌린다", "덱으로 되돌린다", "파괴한다",
                "뒷면 수비 표시로 한다", "recover", "destroy", "banish", "return it to the", "send it to the GY"))
                return null;

            bool defenseOnly = stat && Has(effect, "수비력", " DEF") && !Has(effect, "공격력", " ATK");
            bool opponent = Has(effect, "상대 몬스터의", "상대 필드의", "그 상대 몬스터", "opponent's monster") &&
                !Has(effect, "상대 몬스터의 공격력만큼", "ATK of the opponent");
            string targetAndEffect = penalty < 0 ? text : text[..penalty];
            ulong race = 0;
            uint attribute = 0;
            foreach (var entry in YgoConstants.RaceNamesKR)
                if (targetAndEffect.Contains(entry.Value + " 몬스터", StringComparison.Ordinal)) race |= entry.Key;
            foreach (var entry in YgoConstants.AttributeNamesKR)
                if (targetAndEffect.Contains(entry.Value + " 속성 몬스터", StringComparison.Ordinal)) attribute |= entry.Key;
            uint type = Has(targetAndEffect, "융합 몬스터", "Fusion Monster") ? YgoConstants.TYPE_FUSION : 0;
            ushort archetype = Has(targetAndEffect, "\"히어로\" 몬스터", "\"HERO\" monster") ? HeroSet : (ushort)0;
            bool endPhaseDestruction = penalty >= 0 && Has(text[penalty..], "파괴된다", "destroy");
            var delta = StatDelta.Match(effect);
            if (delta.Success && int.TryParse(delta.Groups[1].Success ? delta.Groups[1].Value : delta.Groups[3].Value, out int amount))
                return Read(Has(delta.Value, "내린다", "감소", "loses", "lose") ? BattleChange.Decrease : BattleChange.Increase, amount);

            BattleChange change = piercing ? BattleChange.Pierce : doubleDamage ? BattleChange.DoubleDamage :
                extraAttack ? BattleChange.ExtraAttack : Has(effect, "상대 몬스터의 공격력만큼", "ATK of the opponent") ? BattleChange.OpposingAttack :
                Has(effect, "절반", "halved", "half its") ? BattleChange.Half :
                Has(effect, "배가 된다", "becomes double", "double the ATK") ? BattleChange.Double :
                Has(effect, "0 이 된다", "0이 된다", "becomes 0") ? BattleChange.Zero : BattleChange.Unknown;
            return Read(change, 0);

            BattleTrick Read(BattleChange kind, int value) =>
                new(kind, value, defenseOnly, opponent, race, attribute, type, archetype, endPhaseDestruction);
        }
    }
}
