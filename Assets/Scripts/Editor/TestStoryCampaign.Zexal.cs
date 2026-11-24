using System;
using System.Linq;
using UnityEngine;
using YGOPortable.Core;

namespace YGOPortable.Editor
{
    internal static partial class TestStoryCampaign
    {
        private static void VerifyZexalScenarioEffects()
        {
            const uint raigeki = 12580477, cyber = 70095154, spark = 20721928, tachyon = 88177324;
            using (var numbers = new ScenarioDuel("numbers_protection", playerCard: raigeki))
            {
                Require(numbers.Rival.mzone.Any(card => card?.code == 31801517 && card.overlayCodes.Count == 2),
                    "The opposing Number did not get its physical Xyz materials.");
                numbers.Activate(raigeki);
                Require(numbers.Rival.grave.Any(card => card.code == 31801517),
                    "Number battle protection incorrectly blocked effect destruction.");
            }
            VerifyNumberBattleProtection();
            using (var chronomaly = new ScenarioDuel("chronomaly_colosseum"))
            {
                chronomaly.NextMain(2);
                Require(chronomaly.Rival.mzone.Any(card => card?.code == 39139935 && card.overlayCodes.Count == 2) &&
                    chronomaly.Player.lp == 7500, "The Machu Mech Xyz Summon did not trigger the colosseum damage.");
            }
            foreach (var (rule, code) in new[] { ("gimmick_puppet_show", 88120966u), ("ark_knight_material", 48739166u) })
            {
                using var duel = new ScenarioDuel(rule, playerCard: cyber);
                SpecialSummon(duel, cyber);
                duel.NextMain(2);
                duel.Activate(code, cyber);
                var xyz = duel.Rival.mzone.Single(card => card?.code == code);
                Require(duel.Player.mzone.All(card => card?.code != cyber) && (rule == "gimmick_puppet_show"
                    ? duel.Player.grave.Any(card => card.code == cyber) && xyz.overlayCodes.Count == 1
                    : xyz.overlayCodes.SequenceEqual(new[] { cyber })),
                    $"The native removal or material absorption failed for {rule}.");
            }
            using (var cannon = new ScenarioDuel("heartland_cannon", rivalOpening: new DuelOpening(8000, 5, 23998625)))
            {
                Require(cannon.Player.lp == 7500, "The first Heartland Cannon hit was missing.");
                cannon.NextMain(2);
                Require(cannon.Player.lp == 7000 && cannon.Rival.lp == 8000, "Heartland Cannon did not hit once per turn.");
            }
            foreach (bool humanFirst in new[] { true, false })
            {
                using var hand = new ScenarioDuel("barian_hand", humanFirst: humanFirst);
                Require(hand.Duel.FieldDisabledMask == 1u << (16 * hand.Duel.HumanPlayerIndex + 2),
                    "Barian Hand disabled the wrong player's field or more than one zone.");
            }
            using (var lightning = new ScenarioDuel("star_seraph_xyz"))
            {
                lightning.NextMain(2);
                Require(lightning.Rival.mzone.Any(card => card?.code == 49678559 &&
                    new[] { 38331564u, 91110378u, 70668285u }.All(card.overlayCodes.Contains)),
                    "Glorious Halo was not Xyz Summoned with the three Star Seraphs.");
            }
            using (var vector = new ScenarioDuel("barian_chaos"))
            {
                vector.NextMain(2);
                Require(vector.Rival.mzone.Any(card => card?.code == 2061963 &&
                    card.Counters.Any(counter => counter is { Type: 0x0013, Count: 1 })), "The first native Chaos Counter was missing.");
                vector.NextMain(4);
                Require(vector.Rival.mzone.Any(card => card?.code == 49456901 &&
                    card.overlayCodes.Contains(2061963)), "The full Barian gauge did not rank up the existing Number.");
            }
            using (var boxing = new ScenarioDuel("boxing_ring"))
            {
                Require(boxing.Duel.ActivePrompt.mSetable.Count == 0, "The boxing ring allowed a Defense Position set.");
                boxing.NextMain(2);
                EnterBattle(boxing);
                Attack(boxing, 59627393);
                Require(boxing.Player.lp == 3000, "Star Cestus did not deal twice its 2500 direct battle damage.");
            }
            using (var rewind = new ScenarioDuel("tachyon_rewind"))
            {
                rewind.NextMain(2);
                EnterBattle(rewind);
                Attack(rewind, tachyon);
                Require(rewind.Player.lp == 5000, "The Tachyon attack did not cause the expected battle damage.");
                rewind.Duel.SubmitResponseInt(2);
                rewind.Drive(() => InMain(rewind.Duel, 2));
                Require(rewind.Player.lp == 8000, "Battle damage was not rewound at the end of the Battle Phase.");
            }
            using (var mist = new ScenarioDuel("dark_mist", humanFirst: false))
            {
                mist.NextMain(2);
                mist.NormalSummon(spark);
                EnterBattle(mist);
                Attack(mist, spark, acceptEffect: 55727845);
                Require(mist.Rival.mzone.Any(card => card is { code: 55727845, attack: 900 }) &&
                    mist.Player.grave.Any(card => card.code == spark) && mist.Player.lp == 7900,
                    "Dark Mist did not halve Sparkman's 1600 ATK and absorb the remaining 800.");
            }
            using (var gorgonic = new ScenarioDuel("gorgonic_ghost"))
            {
                Require(gorgonic.Player.grave.Count == 3, "The first Gorgonic Standby did not mill three cards.");
                gorgonic.NextMain(2);
                Require(gorgonic.Player.grave.Count == 6 && gorgonic.Rival.grave.Count == 0,
                    "Gorgonic milling did not repeat exclusively on the player's deck.");
            }
            VerifyNumerounia();
            using (var barian = new ScenarioDuel("barian_emperors", rivalOpening: new DuelOpening(10000, 5, 12744567)))
                Require(barian.Rival.lp == 10000 && barian.Rival.mzone.Any(card => card?.code == 12744567 &&
                    card.overlayCodes.Contains(48739166)), "Barian Emperor Nash was missing 10,000 LP or ARK as Xyz material.");
            using (var astral = new ScenarioDuel("hundred_numbers"))
                Require(astral.Rival.extra.Count == 3 && astral.Rival.extra.Select(card => card.code).Distinct().Count() == 3 &&
                    astral.Rival.extra.All(card => CardDatabase.GetName(card.code).StartsWith("No.", StringComparison.Ordinal)),
                    "Astral's ritual did not add three distinct Numbers to the native Extra Deck.");
            using (var roots = new ScenarioDuel("hope_roots", humanFirst: false))
            {
                roots.NextMain(2);
                roots.NormalSummon(spark);
                EnterBattle(roots);
                Attack(roots, spark, acceptEffect: 84124261);
                Require(roots.Rival.mzone.Any(card => card?.code == 84124261 && card.overlayCodes.Count == 0),
                    "Utopia Roots did not use its material to negate the first attack.");
                roots.NextMain(4);
                EnterBattle(roots);
                Attack(roots, spark, acceptEffect: 84124261);
                Require(roots.Rival.mzone.Any(card => card?.code == 84124261 && card.overlayCodes.Count == 0) &&
                    roots.Rival.lp == 8000, "Utopia Roots was not recharged for the next attack.");
            }
            using (var finale = new ScenarioDuel("uturpic_finale", rivalOpening: new DuelOpening(8000, 5, 65305468)))
                Require(finale.Rival.mzone.Any(card => card?.code == 65305468 &&
                    new[] { 12014404u, 91499077u }.All(card.overlayCodes.Contains)), "The finale was missing the original Utopic Future and its materials.");
            Debug.Log("[VERIFY ZEXAL SCENARIOS] Live Numbers, Xyz effects, disabled zones, counter rank-up, battle rules, deck milling, Numerounia and Shining Draw passed.");
        }

        private static void VerifyNumberBattleProtection()
        {
            const uint cyber = 70095154, limiter = 23171610;
            var deck = Enumerable.Range(0, 40).Select(i => i % 2 == 0 ? cyber : limiter).ToArray();
            using var numbers = new ScenarioDuel("numbers_protection", humanFirst: false, playerDeck: deck);
            numbers.NextMain(2);
            SpecialSummon(numbers, cyber);
            numbers.Activate(limiter);
            Require(numbers.Player.mzone.Any(card => card is { code: cyber, attack: 4200 }), "The non-Number attacker never exceeded 4000 ATK.");
            EnterBattle(numbers);
            Attack(numbers, cyber);
            Require(numbers.Rival.mzone.Any(card => card?.code == 31801517) && numbers.Rival.lp == 7800,
                "The Number was destroyed by battle with a stronger non-Number monster.");
        }

        private static void VerifyNumerounia()
        {
            const uint numerounia = 15862758, darkHole = 53129443, raigeki = 12580477;
            using var duel = new ScenarioDuel("numeronius", playerCard: darkHole,
                rivalOpening: new DuelOpening(10000, 5, numerounia));
            var boss = duel.Rival.mzone.SingleOrDefault(card => card?.code == numerounia);
            Require(boss?.attack == 100000 && duel.Player.lp == 2000 && duel.Duel.ReadyStoryCrisis == StoryCrisisSkill.ShiningDraw,
                $"The boss did not open at 100,000 ATK and unlock Shining Draw (ATK {boss?.attack}, LP {duel.Player.lp}, {duel.Duel.ReadyStoryCrisis}).");
            duel.Activate(darkHole);
            Require(duel.Rival.mzone.Any(card => card?.code == numerounia), "The boss could be removed before Shining Draw.");
            duel.Drive(() => duel.Duel.PendingStoryCrisis == StoryCrisisSkill.ShiningDraw, endTurn: true);
            duel.Duel.SubmitResponseInt(0);
            duel.Drive(() => InMain(duel.Duel, 3));
            Require(duel.Duel.ReadyStoryCrisis == StoryCrisisSkill.ShiningDraw &&
                duel.Rival.mzone.Any(card => card?.code == numerounia), "Skipping the draw used the skill or unlocked the boss.");
            duel.Drive(() => duel.Duel.PendingStoryCrisis == StoryCrisisSkill.ShiningDraw, endTurn: true);
            var outCard = duel.Duel.ShiningDrawChoices().Single(card => card.code == raigeki);
            duel.Duel.SubmitResponseInt((int)outCard.sequence + 1);
            duel.Drive(() => InMain(duel.Duel, 5));
            Require(duel.Player.hand.Any(card => card.code == raigeki) && duel.Duel.ReadyStoryCrisis == StoryCrisisSkill.None,
                "Shining Draw did not draw the real rescue card or clear availability.");
            duel.Activate(raigeki);
            Require(duel.Rival.grave.Any(card => card.code == numerounia),
                "After Shining Draw, the physical boss remained immune to normal removal.");
        }

        private static void SpecialSummon(ScenarioDuel duel, uint code)
        {
            int index = duel.Duel.ActivePrompt.spSummonable.FindIndex(card => card.code == code);
            int turn = duel.Duel.TurnNumber;
            Require(index >= 0, $"Special Summon {code} was unavailable.");
            duel.Duel.SubmitResponseInt((index << 16) | 1);
            duel.Drive(() => InMain(duel.Duel, turn), attackPosition: true);
        }

        private static bool InBattle(ScenarioDuel duel) => duel.Duel.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_BATTLECMD;

        private static void EnterBattle(ScenarioDuel duel)
        {
            Require(duel.Duel.ActivePrompt.canBp, "Missing the native Battle Phase.");
            duel.Duel.SubmitResponseInt(6);
            duel.Drive(() => InBattle(duel));
        }

        private static void Attack(ScenarioDuel duel, uint code, uint acceptEffect = 0, uint declineEffect = 0)
        {
            int index = duel.Duel.ActivePrompt.attackable.FindIndex(card => card.code == code);
            Require(index >= 0, $"Native attack {code} was unavailable.");
            duel.Duel.SubmitResponseInt((index << 16) | 1);
            duel.Drive(() => InBattle(duel), acceptEffect: acceptEffect, declineEffect: declineEffect);
        }
    }
}
