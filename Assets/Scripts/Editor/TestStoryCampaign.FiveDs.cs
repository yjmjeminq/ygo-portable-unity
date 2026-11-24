using System;
using System.Linq;
using UnityEngine;
using YGOPortable.Core;

namespace YGOPortable.Editor
{
    internal static partial class TestStoryCampaign
    {
        private static void Verify5DsScenarioEffects()
        {
            const uint darkHole = 53129443, raigeki = 12580477, sparks = 20721928, inferno = 46918794;
            using (var cuffs = new ScenarioDuel("handcuff_tokens", playerCard: darkHole))
            {
                Require(new[] { 1, 3 }.All(zone => cuffs.Player.mzone[zone] is
                    { code: StoryGimmickRuntime.HandcuffToken, defense: 0 } token && token.IsDefense &&
                    (token.type & YgoConstants.TYPE_TOKEN) != 0), "The two handcuffs were not physical tokens in the player's zones.");
                cuffs.Activate(darkHole);
                Require(cuffs.Player.mzone.All(card => card == null), "Destroying the handcuffs did not free their zones.");
            }
            VerifyPrisonPenalty();
            foreach (var (scenario, source, damage) in new[]
            {
                ("reactor_barrage", 89493368u, 800),
                ("black_rose_thorns", 73580471u, 300),
            })
            {
                using var duel = new ScenarioDuel(scenario);
                Require(duel.Rival.mzone.Any(card => card?.code == source), $"The source of {scenario} was missing.");
                duel.NormalSummon();
                Require(duel.Player.lp == 8000 - damage && duel.Rival.lp == 8000,
                    $"The player's summon did not trigger one {damage}-damage hit in {scenario} (LP {duel.Player.lp}/{duel.Rival.lp}): " +
                    string.Join(" / ", duel.Duel.Logs.TakeLast(10)));
            }
            VerifySynchroPrisonAndAbsorption();
            using (var ccapac = new ScenarioDuel("riding_ccapac_apu", playerCard: raigeki))
            {
                Require(ccapac.Rival.mzone.Any(card => card?.code == 46263076) &&
                    ccapac.Rival.szone[5]?.code == StoryGimmickRuntime.SpeedWorld,
                    "Ccapac Apu did not coexist with the physical riding field.");
                ccapac.Activate(raigeki);
                Require(ccapac.Rival.grave.Any(card => card.code == 46263076),
                    "The preplaced Earthbound Immortal was not destructible by an ordinary card effect.");
            }
            using (var fortune = new ScenarioDuel("riding_fortune_lady"))
            {
                fortune.NextMain(2);
                var every = fortune.Rival.mzone.Single(card => card?.code == 70252926);
                Require(every.level == 8 && every.attack == 3200, "Fortune Lady Every did not increase its level and ATK during Standby.");
            }
            using (var misty = new ScenarioDuel("riding_petrification"))
            {
                misty.NormalSummon();
                Require(misty.Player.mzone.Any(card => card is { code: sparks, attack: 0 }),
                    "Misty's petrification did not set a newly summoned monster's ATK to zero.");
            }
            using (var altar = new ScenarioDuel("riding_underworld_altar", playerCard: raigeki,
                rivalOpening: new DuelOpening(10000, 5, 39823987)))
            {
                Require(altar.Rival.lp == 10000 && altar.Rival.grave.Any(card => card.code == 66818682) &&
                    altar.Rival.mzone.Any(card => card?.code == 39823987), "Godwin's Sun and Moon were not in their native opening zones.");
                altar.Activate(raigeki);
                Require(altar.Rival.grave.Any(card => card.code == 39823987), "Inti could not be destroyed by Raigeki.");
                altar.Drive(() => InMain(altar.Duel, 2), endTurn: true, acceptEffect: 39823987);
                Require(altar.Rival.mzone.Any(card => card?.code == 66818682),
                    "Inti's printed Standby effect could not revive the correctly prepared Quilla.");
            }
            foreach (bool playerStarts in new[] { true, false })
            {
                using var unicorn = new ScenarioDuel("riding_unicorn", humanFirst: playerStarts);
                Require(unicorn.Duel.ActivePrompt.canBp && unicorn.Rival.mzone.Any(card => card?.code == 77506119),
                    "Team Unicorn did not allow the first seat to battle on turn one.");
                if (!playerStarts)
                {
                    unicorn.Duel.SubmitResponseInt(6);
                    unicorn.Drive(() => unicorn.Duel.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_BATTLECMD);
                    Require(unicorn.Duel.ActivePrompt.attackable.Any(card => card.code == 77506119 && card.directAttackable),
                        "Team Unicorn could enter battle but could not declare its legal turn-one direct attack.");
                }
            }
            using (var standard = new ScenarioDuel("riding_speed_world", humanFirst: false))
                Require(!standard.Duel.ActivePrompt.canBp, "The turn-one battle rule leaked into an ordinary riding duel.");
            using (var odin = new ScenarioDuel("riding_odin", playerCard: raigeki))
            {
                odin.Activate(raigeki);
                Require(odin.Rival.mzone.Any(card => card?.code == 93483212), "Raigeki destroyed the spell-immune Odin.");
            }
            using (var granel = new ScenarioDuel("riding_granel"))
            {
                Require(granel.Rival.mzone.Any(card => card is { code: 4545683, attack: >= 8000 }),
                    "Granel's opening ATK was below 8000.");
                granel.NextMain(3);
                Require(granel.Rival.mzone.Any(card => card is { code: 4545683, attack: 4000 }),
                    "Granel did not return to its printed half-LP ATK after the opening round.");
            }
            using (var delta = new ScenarioDuel("riding_delta_accel"))
            {
                delta.NextMain(2);
                Require(delta.Rival.mzone.Any(card => card?.code == 97836203) &&
                    new[] { 98558751u, 62560742u, 24943456u }.All(code => delta.Rival.grave.Any(card => card.code == code)),
                    "Delta Accel did not Synchro Summon Halberd Cannon with its three real Synchro materials.");
            }
            using (var interrupted = new ScenarioDuel("riding_delta_accel", playerCard: raigeki))
            {
                interrupted.Activate(raigeki);
                interrupted.NextMain(2);
                Require(interrupted.Rival.mzone.All(card => card?.code != 97836203) &&
                    interrupted.Rival.extra.Any(card => card.code == 97836203),
                    "Halberd Cannon was summoned despite the player removing its materials.");
            }
            using (var timelord = new ScenarioDuel("riding_timelords", playerCard: inferno,
                rivalOpening: new DuelOpening(10000, 5, 8967776)))
            {
                timelord.Activate(inferno);
                Require(timelord.Rival.lp == 10000 && timelord.Player.lp == 7500,
                    "The time reversal did not restore only the opponent's first nonlethal damage.");
                timelord.Activate(inferno);
                Require(timelord.Rival.lp == 9000 && timelord.Player.lp == 7000, "The time reversal was used twice.");
            }
            using (var finale = new ScenarioDuel("riding_stardust_finale", rivalOpening: new DuelOpening(8000, 5, 44508094)))
            {
                finale.NextMain(2);
                Require(finale.Rival.mzone.Any(card => card?.code == 24696097) &&
                    new[] { 44508094u, 50091196u }.All(code => finale.Rival.grave.Any(card => card.code == code)),
                    "Yusei did not Accel Synchro Summon Shooting Star Dragon with Stardust and Formula Synchron.");
            }
            Debug.Log("[VERIFY 5DS SCENARIOS] Handcuffs, penalties, Synchro lock and absorption, riding openers, first battle, ATK effects, native Accel summons and LP reversal passed.");
        }

        private static void VerifyPrisonPenalty()
        {
            const uint upstart = 70368879, sparkman = 20721928;
            var deck = Enumerable.Range(0, 40).Select(index => index % 2 == 0 ? upstart : sparkman).ToArray();
            using var prison = new ScenarioDuel("prison_penalty", playerDeck: deck);
            SetFirst(prison.Duel.ActivePrompt.mSetable.FindIndex(card => card.code == sparkman), 3);
            Require(prison.Player.lp == 7800, "The native monster set did not incur a 200 LP penalty.");
            SetFirst(prison.Duel.ActivePrompt.sSetable.FindIndex(card => card.code == upstart), 4);
            Require(prison.Player.lp == 7600, "The native spell set did not incur a 200 LP penalty.");
            prison.Activate(upstart);
            Require(prison.Player.lp == 7400 && prison.Rival.lp == 9000, "The activation penalty was not applied to the player.");

            void SetFirst(int index, int action)
            {
                Require(index >= 0, "The fixture drew no legal card to set.");
                prison.Duel.SubmitResponseInt((index << 16) | action);
                prison.Drive(() => InMain(prison.Duel, 1));
            }
        }

        private static void VerifySynchroPrisonAndAbsorption()
        {
            const uint teleport = 67723438, krebons = 59575539, sparkman = 20721928, brionac = 50321796;
            var deck = Enumerable.Range(0, 40).Select(index => (index % 3) switch
            {
                0 => teleport,
                1 => krebons,
                _ => sparkman,
            }).ToArray();
            ulong validSeed = 0;
            for (ulong seed = 11; seed < 35; seed++)
            {
                using var control = new ScenarioDuel(null, playerDeck: deck, playerExtra: new[] { brionac }, seed: seed);
                if (control.Duel.ActivePrompt.activatable.All(card => card.code != teleport) ||
                    control.Duel.ActivePrompt.summonable.All(card => card.code != sparkman)) continue;
                SetMaterials(control);
                Require(control.Duel.ActivePrompt.spSummonable.Any(card => card.code == brionac),
                    "The fixture could not offer a Synchro Summon outside the prison.");
                validSeed = seed;
                break;
            }
            Require(validSeed != 0, "No deterministic seed drew both setup cards.");
            using (var prison = new ScenarioDuel("monkey_prison", playerDeck: deck, playerExtra: new[] { brionac }, seed: validSeed))
            {
                SetMaterials(prison);
                Require(prison.Duel.ActivePrompt.spSummonable.All(card => card.code != brionac),
                    "The prison allowed the same Synchro Summon.");
            }
            foreach (var (scenario, absorber) in new[] { ("riding_wisel", 68140974u), ("riding_mashinicle", 63468625u) })
            {
                using var riding = new ScenarioDuel(scenario, playerDeck: deck, playerExtra: new[] { brionac }, seed: validSeed);
                SetMaterials(riding);
                int summon = riding.Duel.ActivePrompt.spSummonable.FindIndex(card => card.code == brionac);
                Require(summon >= 0, "Missing the material-ready Synchro Summon in the riding duel.");
                riding.Duel.SubmitResponseInt((summon << 16) | 1);
                riding.Drive(() => InMain(riding.Duel, 1));
                riding.NextMain(2);
                riding.Activate(absorber, brionac);
                Require(riding.Player.mzone.All(card => card?.code != brionac) &&
                    riding.Rival.szone.Take(5).Any(card => card?.code == brionac),
                    $"{scenario} failed to absorb the live Synchro Monster as an Equip Card.");
            }

            static void SetMaterials(ScenarioDuel duel)
            {
                duel.Activate(teleport, krebons);
                duel.NormalSummon(sparkman);
                Require(duel.Player.mzone.Any(card => card?.code == krebons), "Emergency Teleport did not summon its Psychic Tuner.");
            }
        }
    }
}
