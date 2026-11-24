using System;
using System.Linq;
using UnityEngine;
using YGOPortable.Core;

namespace YGOPortable.Editor
{
    internal static partial class TestStoryCampaign
    {
        private static void VerifyVrainsScenarioEffects()
        {
            const uint sparkman = 20721928, raigeki = 12580477, upstart = 70368879, hellfire = 46918794,
                cyber = 70095154, spider = 98978921, cyclone = 5318639;
            using (var hanoi = new ScenarioDuel("hanoi_backdoor", rivalCard: 63528891))
            {
                hanoi.NextMain(2);
                hanoi.Activate(43839002, 33897356);
                Require(hanoi.Rival.banished.Any(card => card.code == 33897356) &&
                    hanoi.Rival.hand.Count(card => card.code == 63528891) == 6,
                    "Cynet Backdoor did not banish the real Link and search a lower-ATK Cyberse.");
                hanoi.NextMain(4);
                Require(hanoi.Rival.mzone.Any(card => card?.code == 33897356), "The Backdoor Link did not return at Standby.");
            }
            VerifyGoukiRevival();
            VerifyAltergeistProtocol();
            using (var poison = new ScenarioDuel("trickstar_poison", playerCard: upstart))
            {
                Require(poison.Player.lp == 8000, "The opening draw was incorrectly charged Trickstar damage.");
                poison.Activate(upstart);
                Require(poison.Player.lp == 7600 && poison.Rival.lp == 9000, "Light Stage did not charge 200 for activation and 200 for the extra draw.");
                poison.NextMain(3);
                Require(poison.Player.lp == 7400, "The next normal draw did not receive 200 Trickstar damage.");
            }
            VerifyStormEpisodes();
            using (var tree = new ScenarioDuel("sunavalon_fortress", playerCard: hellfire))
            {
                tree.Activate(hellfire);
                tree.Activate(hellfire);
                Require(tree.Rival.mzone.Any(card => card?.code == 92770064) && tree.Rival.lp == 8000 && tree.Player.lp == 7000,
                    "Spectre's tree did not restore its controller's damage repeatedly.");
            }
            VerifyTopologicTower();
            using (var lightning = new ScenarioDuel("lightning_link", playerCard: 14087893))
                Require(lightning.Rival.mzone.Any(card => card?.code == 63528891) &&
                    lightning.Duel.ActivePrompt.activatable.All(card => card.code != 14087893),
                    "The protected monster in Lightning's linked zone was a legal Book of Moon target.");
            using (var radar = new ScenarioDuel("drone_intercept", playerCard: upstart))
            {
                radar.Activate(upstart);
                Require(radar.Player.grave.Any(card => card.code == upstart) && radar.Player.hand.Count == 4 &&
                    radar.Rival.lp == 8000 && radar.Rival.szone.Any(card => card?.code == StoryGimmickRuntime.DroneRadar),
                    "The drone radar did not intercept the actual Spell Card before it drew or healed.");
            }
            using (var soulburner = new ScenarioDuel("salamangreat_reincarnation"))
            {
                soulburner.NextMain(2);
                Require(soulburner.Rival.grave.Any(card => card.code == 87871125) &&
                    soulburner.Rival.mzone.Any(card => card is { code: 87871125, attack: 3600 }),
                    "Sunlight Wolf did not use its real namesake to reincarnate at double ATK.");
                EnterBattle(soulburner);
                Attack(soulburner, 87871125, declineEffect: 1295111);
                Attack(soulburner, 87871125, declineEffect: 1295111);
                Require(soulburner.Player.lp == 800, $"The reincarnated Wolf did not make two 3,600 ATK attacks (LP {soulburner.Player.lp}): " +
                    string.Join(" / ", soulburner.Duel.Logs.TakeLast(15)));
            }
            using (var ocean = new ScenarioDuel("marincess_ocean", playerCard: cyclone))
            {
                Require(ocean.Rival.szone[5]?.code == 91027843 &&
                    ocean.Rival.mzone.Any(card => card is { code: 20934852, attack: 2500 }),
                    "Marincess Battle Ocean did not strengthen the physical Marincess.");
                ocean.Activate(cyclone, 91027843);
                Require(ocean.Rival.grave.Any(card => card.code == 91027843) &&
                    ocean.Rival.mzone.Any(card => card is { code: 20934852, attack: 2300 }),
                    "Destroying Battle Ocean did not remove its boost.");
            }
            using (var hydra = new ScenarioDuel("hydradrive"))
            {
                hydra.NormalSummon(sparkman);
                Require(hydra.Rival.mzone.Any(card => card is { code: 13203964, attribute: 31 }) &&
                    hydra.Player.mzone.Any(card => card is { code: sparkman, effectsDisabled: true }),
                    "The Hydradrive did not carry five Attributes and suppress a matching monster.");
            }
            using (var hydra = new ScenarioDuel("hydradrive", playerCard: 28985331))
            {
                hydra.NormalSummon(28985331);
                Require(hydra.Player.mzone.Any(card => card is { code: 28985331, effectsDisabled: false }),
                    "The sixth Attribute was incorrectly suppressed.");
            }
            foreach (bool humanFirst in new[] { true, false })
            {
                using var windy = new ScenarioDuel("storm_core", humanFirst: humanFirst);
                Require(windy.Duel.FieldDisabledMask == ((1u << 9) | (1u << 11)) << (windy.Duel.HumanPlayerIndex * 16),
                    "The Storm Core did not block exactly two of the player's Spell/Trap Zones.");
            }
            const uint sangan = 26202165;
            using (var appliances = new ScenarioDuel("appliancer_links", playerCard: sangan, humanFirst: false))
            {
                appliances.NextMain(2);
                appliances.NormalSummon(sangan);
                EnterBattle(appliances);
                Attack(appliances, sangan, acceptEffect: 77102944, declineEffect: 3875465);
                Require(appliances.Rival.mzone[5]?.code == 78225596 && appliances.Rival.mzone[1]?.code == 77102944 &&
                    appliances.Player.banished.Any(card => card.code == sangan),
                    "The co-linked Laundry Dragon did not resolve the co-link banish branch: " +
                    string.Join(" / ", appliances.Duel.Logs.TakeLast(18)));
            }
            using (var mirror = new ScenarioDuel("mirror_link", playerExtra: new[] { spider },
                rivalOpening: new DuelOpening(10000, 5, 0)))
            {
                mirror.NormalSummon(sparkman);
                SpecialSummon(mirror, spider);
                mirror.NextMain(2);
                Require(mirror.Rival.lp == 10000 && mirror.Rival.mzone.Any(card => card?.code == 13203964 && card.CurrentNameCode == spider),
                    "The Mirror Link God did not copy the player's actual Link Monster.");
                mirror.NextMain(3);
                Require(mirror.Rival.mzone.Any(card => card?.code == 13203964 && card.CurrentNameCode == 13203964),
                    "The mirror copy did not expire at the end of the turn.");
            }
            using (var arrival = new ScenarioDuel("ignister_arrival", playerCard: raigeki,
                rivalOpening: new DuelOpening(10000, 5, 11738489)))
            {
                int priorAttack = arrival.Rival.mzone.FirstOrDefault(card => card?.code == 11738489)?.attack ?? -1;
                arrival.Activate(raigeki);
                Require(arrival.Rival.szone[5]?.code == 59054773 &&
                    arrival.Rival.mzone.Any(card => card is { code: 11738489, attack: 6000 }),
                    $"Arrival lacked 6,000 ATK, AiLand, or native immunity. Before: {priorAttack}; field: {arrival.Rival.szone[5]?.code}; " +
                    "monsters: " + string.Join(", ", arrival.Rival.mzone.Where(card => card != null).Select(card => $"{card.code}:{card.attack}")) +
                    " / " + string.Join(" / ", arrival.Duel.Logs.TakeLast(12)));
            }
            using (var revolver = new ScenarioDuel("borreload_final"))
            {
                revolver.NormalSummon(sparkman);
                int before = revolver.Player.hand.Count;
                revolver.NextMain(2);
                EnterBattle(revolver);
                Attack(revolver, 31833038);
                Require(revolver.Player.mzone.All(card => card == null) && revolver.Player.hand.Count == before + 1 &&
                    revolver.Rival.mzone.All(card => card?.code != sparkman), "Borreload did not bounce the actual attack target to its owner's hand.");
            }
            using (var finale = new ScenarioDuel("into_the_vrains", rivalOpening: new DuelOpening(8000, 5, 86066372)))
            {
                finale.NormalSummon(sparkman);
                finale.NextMain(2);
                const uint decode = 1861629;
                int removal = finale.Duel.ActivePrompt.activatable.FindIndex(card => card.code == 86066372);
                Require(removal >= 0, "Accesscode's destruction effect was unavailable.");
                finale.Duel.SubmitResponseInt((removal << 16) | 5);
                finale.Drive(() => finale.Duel.ActivePrompt is { messageType: YgoConstants.MSG_SELECT_CARD } prompt &&
                    prompt.candidates.Any(card => card.code == decode));
                int cost = finale.Duel.ActivePrompt.candidates.FindIndex(card => card.code == decode);
                finale.Duel.SubmitResponse(DuelSession.PackCardSelection(cost));
                finale.Drive(() => InMain(finale.Duel, 2), target: sparkman);
                Require(finale.Rival.banished.Any(card => card.code == 1861629) && finale.Player.grave.Any(card => card.code == sparkman),
                    "Accesscode did not use the live Link in the Graveyard to destroy a card.");
                finale.Activate(28827503, 36211150);
                Require(finale.Rival.mzone.Any(card => card?.code == spider) && finale.Rival.grave.Any(card => card.code == 36211150),
                    "Into the VRAINS! did not put the prepared Cyberse through a real Link Summon.");
            }
            Debug.Log("[VERIFY VRAINS SCENARIOS] Backdoor, Gouki, trap chains, Trickstar, Storm Access, Sunavalon, linked-zone banish, Reincarnation, Attributes, boss immunities and finale passed.");
        }

        private static void VerifyGoukiRevival()
        {
            const uint greatOgre = 78437364, sparkman = 20721928, axe = 40619825, spider = 98978921, raigeki = 12580477;
            var deck = Enumerable.Range(0, 40).Select(i => i % 3 == 0 ? sparkman : axe).ToArray();
            for (ulong seed = 11; seed < 35; seed++)
            {
                using var gouki = new ScenarioDuel("gouki_showdown", playerDeck: deck, playerExtra: new[] { spider }, humanFirst: false, seed: seed);
                gouki.NextMain(2);
                if (gouki.Player.hand.Count(card => card.code == axe) < 2 || gouki.Player.hand.All(card => card.code != sparkman)) continue;
                gouki.NormalSummon(sparkman);
                SpecialSummon(gouki, spider);
                gouki.Activate(axe, spider);
                gouki.Activate(axe, spider);
                EnterBattle(gouki);
                Attack(gouki, spider);
                Require(gouki.Rival.lp == 8000 && gouki.Rival.mzone.Any(card => card is { code: greatOgre, attack: 3100 }),
                    "Defeating Gouki in battle did not prevent damage and revive it at +500 ATK.");
                using var removal = new ScenarioDuel("gouki_showdown", playerCard: raigeki);
                removal.Activate(raigeki);
                removal.Activate(raigeki);
                Require(removal.Rival.grave.Any(card => card.code == greatOgre), "The same Gouki revived more than once.");
                return;
            }
            Require(false, "The Gouki fixture did not draw two Equips.");
        }

        private static void VerifyAltergeistProtocol()
        {
            const uint knight = 28985331, protocol = 27541563;
            using var trapNet = new ScenarioDuel("altergeist_traps", playerCard: knight);
            trapNet.Duel.SubmitResponseInt(0);
            trapNet.Drive(() => trapNet.Duel.ActivePrompt is { } prompt &&
                (prompt.messageType == YgoConstants.MSG_SELECT_EFFECTYN && prompt.promptCardCode == knight ||
                    prompt.messageType == YgoConstants.MSG_SELECT_CHAIN && prompt.candidates.Any(card => card.code == knight)));
            var offered = trapNet.Duel.ActivePrompt;
            trapNet.Duel.SubmitResponseInt(offered.messageType == YgoConstants.MSG_SELECT_EFFECTYN
                ? 1 : offered.candidates.FindIndex(card => card.code == knight));
            trapNet.Drive(() => InMain(trapNet.Duel, 1), acceptEffect: protocol);
            Require(trapNet.Player.grave.Count == 1 && trapNet.Player.grave[0].code == knight &&
                trapNet.Rival.grave.Any(card => card.code is 42790071 or 89538537) &&
                trapNet.Rival.szone.Any(card => card?.code == protocol),
                "Altergeist Protocol did not pay its cost and negate the live monster effect: " + string.Join(" / ", trapNet.Duel.Logs.TakeLast(14)));
        }

        private static void VerifyStormEpisodes()
        {
            const uint damageSpell = 19523799;
            using (var damage = new ScenarioDuel("borreload_storm", humanFirst: false, rivalCard: damageSpell,
                playerOpening: new DuelOpening(2100, 5, 0)))
            {
                Require(damage.Duel.ReadyStoryCrisis == StoryCrisisSkill.None, "Storm Access lit above 2,000 LP.");
                damage.Activate(damageSpell);
                Require(damage.Player.lp < 2000 && damage.Duel.ReadyStoryCrisis == StoryCrisisSkill.StormAccess &&
                    damage.Duel.PendingStoryCrisis == StoryCrisisSkill.None,
                    "Real battle-engine damage did not light Storm Access before its own draw.");
            }
            foreach (string key in new[] { "borreload_storm", "mirror_link", "borreload_final" })
            {
                var aiOpening = key == "mirror_link" ? new DuelOpening(10000, 5, 0) : (DuelOpening?)null;
                using var storm = new ScenarioDuel(key, humanFirst: false, rivalOpening: aiOpening,
                    playerOpening: new DuelOpening(2000, 5, 0));
                Require(storm.Duel.ReadyStoryCrisis == StoryCrisisSkill.StormAccess, $"Storm Access was missing in {key}.");
                storm.Drive(() => storm.Duel.PendingStoryCrisis == StoryCrisisSkill.StormAccess, endTurn: true);
                storm.Duel.SubmitResponseInt(1);
                storm.Drive(() => InMain(storm.Duel, 2));
                Require(storm.Player.extra.Count == 1 && storm.Player.extra[0].code == 1861629 &&
                    storm.Duel.ReadyStoryCrisis == StoryCrisisSkill.None, $"Storm Access was not consumed exactly once in {key}.");
            }
        }

        private static void VerifyTopologicTower()
        {
            const uint zeroboros = 66403530, cyber = 70095154;
            bool fired = false, quiet = false;
            for (byte zone = 0; zone < 5 && (!fired || !quiet); zone++)
            {
                using var tower = new ScenarioDuel("topologic_tower", playerCard: cyber, rivalOpening: new DuelOpening(8000, 5, zeroboros));
                Require(tower.Rival.mzone[5]?.code == zeroboros, "Zeroboros did not start in the Extra Monster Zone.");
                int index = tower.Duel.ActivePrompt.spSummonable.FindIndex(card => card.code == cyber);
                Require(index >= 0, "The test could not start Cyber Dragon's native Special Summon.");
                tower.Duel.SubmitResponseInt((index << 16) | 1);
                tower.Drive(() => tower.Duel.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_PLACE, attackPosition: true);
                Require((tower.Duel.ActivePrompt.placeFlag & (1u << zone)) == 0, $"The expected Main Monster Zone {zone} was blocked.");
                tower.Duel.SubmitResponse(new[] { (byte)tower.Duel.HumanPlayerIndex, (byte)YgoConstants.LOCATION_MZONE, zone });
                tower.Drive(() => InMain(tower.Duel, 1), attackPosition: true);
                bool banished = tower.Rival.banished.Any(card => card.code == zeroboros) &&
                    tower.Player.banished.Any(card => card.code == cyber);
                if (banished)
                {
                    Require(tower.Rival.mzone.All(card => card == null) && tower.Player.mzone.All(card => card == null),
                        "The linked summon did not clear both fields.");
                    fired = true;
                }
                else
                {
                    Require(tower.Rival.mzone[5]?.code == zeroboros && tower.Player.mzone[zone]?.code == cyber,
                        "The unlinked summon did not leave both physical monsters in place.");
                    quiet = true;
                }
            }
            Require(fired && quiet, "Zeroboros did not distinguish linked and unlinked native placements.");
        }
    }
}
