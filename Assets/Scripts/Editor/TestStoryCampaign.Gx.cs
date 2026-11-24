using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using YGOPortable.AI;
using YGOPortable.Core;

namespace YGOPortable.Editor
{
    internal static partial class TestStoryCampaign
    {
        private static void VerifyGxScenarioEffects()
        {
            const uint sparkman = 20721928, raigeki = 12580477, darkHole = 53129443;
            using (var ancientGear = new ScenarioDuel("ancient_gear", playerCard: sparkman))
            {
                ancientGear.Duel.SubmitResponseInt(3);
                ancientGear.Drive(() => InMain(ancientGear.Duel, 1));
                ancientGear.NextMain(2);
                Require(ancientGear.Rival.mzone.Any(card => card?.code == 83104731), "Ancient Gear Golem did not appear.");
                ancientGear.Duel.SubmitResponseInt(6);
                ancientGear.Drive(() => ancientGear.Duel.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_BATTLECMD);
                ancientGear.Duel.SubmitResponseInt(1);
                ancientGear.Drive(() => ancientGear.Duel.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_BATTLECMD);
                Require(ancientGear.Player.lp == 6400 && ancientGear.Player.mzone.All(card => card == null),
                    "Ancient Gear Golem did not inflict piercing damage against 1400 defense.");
            }
            using (var ojama = new ScenarioDuel("ojama_trio", playerCard: darkHole))
            {
                Require(new[] { 0, 2, 4 }.All(i => ojama.Player.mzone[i] is { code: >= 29843092 and <= 29843094 } card &&
                    card.IsDefense), "Three Ojama Tokens did not occupy the player's zones.");
                ojama.Activate(darkHole);
                Require(ojama.Player.mzone.All(card => card == null) && ojama.Player.lp == 7100,
                    "Destroying the Ojamas did not free the three zones and inflict the normal penalty.");
            }
            using (var elements = new ScenarioDuel("six_attributes", playerCard: 13039848))
            {
                Require(elements.Duel.ActivePrompt.summonable.Count == 0 && elements.Duel.ActivePrompt.mSetable.Count == 0,
                    "Earth summons and sets were legal on the first turn.");
                elements.NextMain(3);
                Require(elements.Duel.ActivePrompt.summonable.Count == 6,
                    "The second player's turn still blocked Earth after the element changed to Water.");
            }
            using (var helios = new ScenarioDuel("helios_alchemy"))
            {
                Require(helios.Rival.mzone.Any(card => card is { code: 54493213, attack: 300 }) &&
                    helios.Player.banished.Count == 1, "Helios did not gain 300 ATK from the first banished monster.");
                helios.NextMain(2);
                Require(helios.Rival.mzone.Any(card => card is { code: 54493213, attack: 600 }) &&
                    helios.Rival.banished.Count == 1, "Helios did not grow with the next banished monster.");
            }
            using (var vampires = new ScenarioDuel("vampire_pact", rivalCard: darkHole, humanFirst: false))
            {
                vampires.Activate(darkHole);
                Require(vampires.Rival.grave.Any(card => card.code == 53839837), "Dark Hole did not destroy its controller's Vampire Lord.");
                vampires.NextMain(3);
                Require(vampires.Rival.mzone.Any(card => card?.code == 53839837),
                    "The scenario did not revive its own destroyed Vampire Lord.");
            }
            using (var amazoness = new ScenarioDuel("amazoness_colosseum", humanFirst: false))
            {
                amazoness.NextMain(2);
                Require(amazoness.Duel.ActivePrompt.mSetable.Count == 0, "The arena allowed a face-down Defense Position set.");
                amazoness.NormalSummon();
                Require(!amazoness.Duel.ActivePrompt.canEp && amazoness.Duel.ActivePrompt.canBp,
                    "The arena allowed the player to skip an available attack.");
            }
            var ravielOpening = new DuelOpening(8000, 5, 69890967);
            using (var beasts = new ScenarioDuel("sacred_beasts", playerCard: raigeki, rivalOpening: ravielOpening))
            {
                beasts.Activate(raigeki);
                Require(beasts.Rival.mzone.Count(card => card != null) == 3, "Each Sacred Beast must survive its first destruction.");
                beasts.NextMain(3);
                beasts.Activate(raigeki);
                Require(beasts.Rival.mzone.All(card => card == null) && beasts.Rival.grave.Count == 3,
                    "The Sacred Beasts regained their one-time protection after the turn changed.");
            }
            var arcanaFaces = (Upright: false, Reversed: false);
            for (ulong seed = 11; seed < 30 && (!arcanaFaces.Upright || !arcanaFaces.Reversed); seed++)
            {
                using var arcana = new ScenarioDuel("arcana_fate", seed: seed);
                string toss = arcana.Duel.Logs.FirstOrDefault(log => log.Contains("코인 토스"));
                Require(toss != null, "The native Arcana coin toss did not occur.");
                bool upright = toss.EndsWith("앞면", StringComparison.Ordinal);
                Require((upright ? arcana.Rival : arcana.Player).mzone.Any(card => card?.code == 34568403),
                    "The Chariot did not stay with its owner upright or change sides reversed.");
                arcanaFaces = (arcanaFaces.Upright || upright, arcanaFaces.Reversed || !upright);
            }
            Require(arcanaFaces.Upright && arcanaFaces.Reversed, "The seeds did not exercise both results of the Arcana coin toss.");
            using (var plasma = new ScenarioDuel("plasma", playerCard: 28985331))
            {
                plasma.NormalSummon();
                Require(plasma.Player.mzone.Single(card => card != null).effectsDisabled,
                    "The player's on-field monster effect remained enabled under Plasma.");
            }
            const uint crow = 24508238, upstart = 70368879;
            using (var plasma = new ScenarioDuel("plasma", playerCard: crow, rivalCard: upstart, humanFirst: false))
            {
                plasma.Activate(upstart);
                plasma.NextMain(2);
                plasma.Activate(crow, upstart);
                Require(plasma.Player.grave.Any(card => card.code == crow) &&
                    plasma.Rival.grave.Any(card => card.code == upstart) && plasma.Rival.banished.Count == 0,
                    "Plasma did not negate the effect activated in hand after the player paid its discard cost.");
            }
            using (var shock = new ScenarioDuel("shock_collar", playerCard: raigeki,
                rivalOpening: new DuelOpening(8000, 5, sparkman)))
            {
                shock.Activate(raigeki);
                Require(shock.Rival.lp == 7700 && shock.Player.lp == 8000,
                    "The collar did not shock the controller of the destroyed monster.");
            }
            using (var venom = new ScenarioDuel("venom_counters"))
            {
                venom.NormalSummon();
                var poisoned = venom.Player.mzone.Single(card => card != null);
                Require(poisoned.attack == 1100 && poisoned.Counters.Any(counter => counter is { Type: 0x1009, Count: 1 }),
                    "The summoned monster did not get a native Venom counter and lose 500 ATK.");
            }
            VerifyFossilFusion();
            using (var crystals = new ScenarioDuel("crystal_beasts", playerCard: 5318639))
            {
                const uint continuousSpell = YgoConstants.TYPE_SPELL | YgoConstants.TYPE_CONTINUOUS;
                var opening = crystals.Rival.szone.Where(card => card != null).ToArray();
                Require(opening.Length == 3 && opening.All(card => card is { type: continuousSpell, location: YgoConstants.LOCATION_SZONE } &&
                    card.IsFaceUp) && crystals.Rival.mzone.All(card => card == null),
                    "Three Crystal Beasts were not live continuous spells in the back row.");
                crystals.Activate(5318639, opening[0].code);
                Require(crystals.Rival.grave.Any(card => card.code == opening[0].code),
                    "Mystical Space Typhoon could not destroy the crystallized monster card.");
            }
            using (var martel = new ScenarioDuel("possessed_beasts"))
            {
                martel.NextMain(2);
                Require(martel.Rival.mzone.Count(card => card != null) == 1 &&
                    martel.Rival.mzone.Any(card => card?.code == 43378048) && martel.Rival.banished.Count == 3,
                    "Martel did not use the three actual Sacred Beasts to summon Armityle.");
            }
            using (var disrupted = new ScenarioDuel("possessed_beasts", playerCard: raigeki))
            {
                disrupted.Activate(raigeki);
                disrupted.NextMain(2);
                Require(disrupted.Rival.mzone.All(card => card?.code != 43378048),
                    "Martel still fused after the player destroyed the materials.");
            }
            using (var king = new ScenarioDuel("supreme_king_fusion", playerCard: 87910978,
                rivalOpening: new DuelOpening(8000, 5, 86165817)))
                Require(king.Duel.ActivePrompt.activatable.All(card => card.code != 87910978),
                    "Brain Control could target the Supreme King's face-up Fiend.");
            using (var yubel = new ScenarioDuel("super_polymerization_countdown"))
            {
                yubel.Drive(() => yubel.Duel.IsFinished, endTurn: true);
                Require(yubel.Duel.TurnNumber == 10 && yubel.Duel.Winner == yubel.Duel.AiPlayerIndex &&
                    yubel.Duel.WinReason == StoryGimmickRuntime.CountdownWinReason, "Yubel's ten-turn limit did not end the duel.");
            }
            VerifyDarknessCombo();
            using (var graduation = new ScenarioDuel("graduation_duel",
                rivalOpening: new DuelOpening(8000, 5, 31111109)))
            {
                graduation.NextMain(2);
                Require(graduation.Rival.grave.Count == 7 &&
                    graduation.Duel.ActivePrompt.activatable.Any(card => card.code == 31111109),
                    "Divine Neos did not have the seven physical cards available for its copy effect.");
            }
            Debug.Log("[VERIFY GX SCENARIOS] Native field restrictions, monster effects, physical counters and tokens, finale summons and timers passed.");
        }

        private static void VerifyFossilFusion()
        {
            const uint skullKing = 96897184, fossilFusion = 59419719, burial = 81439173, dragon = 89631139, darkHole = 53129443;
            var opening = Enumerable.Range(0, 40).Select(index => (index % 3) switch
            {
                0 => burial,
                1 => dragon,
                _ => darkHole,
            }).ToArray();
            for (ulong seed = 11; seed < 35; seed++)
            {
                using var fossils = new ScenarioDuel("fossil_fusion", playerDeck: opening, rivalExtra: new[] { skullKing }, seed: seed);
                Require(fossils.Rival.hand.Any(card => card.code == fossilFusion) &&
                    fossils.Rival.mzone.Any(card => card?.code == skullKing), "The Fossil duel did not guarantee its opening.");
                if (!fossils.Duel.ActivePrompt.activatable.Any(card => card.code == burial) ||
                    !fossils.Duel.ActivePrompt.activatable.Any(card => card.code == darkHole)) continue;
                fossils.Activate(burial, dragon);
                fossils.Activate(darkHole);
                fossils.NextMain(2);
                fossils.Activate(fossilFusion);
                Require(fossils.Player.banished.Any(card => card.code == dragon) &&
                    fossils.Rival.banished.Any(card => card.code == skullKing) &&
                    fossils.Rival.mzone.Any(card => card?.code == skullKing),
                    "Fossil Fusion did not consume a physical monster from each graveyard to summon Skull King.");
                return;
            }
            Require(false, "The Fossil Fusion fixture did not draw both setup spells.");
        }

        private static void VerifyDarknessCombo()
        {
            for (ulong seed = 11; seed < 35; seed++)
            {
                using var darkness = new ScenarioDuel("darkness_zero_infinity", seed: seed,
                    rivalOpening: new DuelOpening(8000, 5, 60417395));
                var pieces = darkness.Rival.szone.Take(5).ToArray();
                Require(pieces.All(card => card is { code: >= 511090101 and <= 511090105 } && !card.IsFaceUp),
                    "Darkness did not set five different story Traps in its native back row.");
                int zero = Array.FindIndex(pieces, card => card.code == StoryGimmickRuntime.DarknessZero);
                int infinity = Array.FindIndex(pieces, card => card.code == StoryGimmickRuntime.DarknessInfinity);
                int count = Math.Abs(zero-infinity)-1;
                if (count == 0 || pieces[zero + Math.Sign(infinity-zero)].code != 511090105) continue;
                darkness.NextMain(2);
                Require(darkness.Player.lp == 8000 - 1000 * count &&
                    darkness.Rival.szone.Count(card => card?.IsFaceUp == true) == count+2,
                    "Zero and Infinity did not reveal only their intervening Traps and deal the resulting damage.");
                darkness.NextMain(3);
                Require(darkness.Rival.szone.Take(5).All(card => card != null && !card.IsFaceUp),
                    "The revealed Darkness Traps did not set themselves again at the end of the turn.");
                darkness.NextMain(4);
                Require(darkness.Player.lp == 8000 - 2000 * count, "The re-set Zero could not start the second Darkness combo.");
                return;
            }
            Require(false, "No deterministic seed exercised the Darkness 3 branch.");
        }

        private static bool InMain(DuelSession duel, int turn) =>
            duel.TurnNumber == turn && duel.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_IDLECMD;

        private sealed class ScenarioDuel : IDisposable
        {
            internal DuelSession Duel { get; }
            internal PlayerFieldSnapshot Player => Duel.Players[Duel.HumanPlayerIndex];
            internal PlayerFieldSnapshot Rival => Duel.Players[Duel.AiPlayerIndex];
            private readonly TacticalAI _ai = new() { Difficulty = AIDifficulty.Easy };
            private readonly string _name;

            internal ScenarioDuel(string name, uint playerCard = 20721928, uint rivalCard = 20721928,
                bool humanFirst = true, DuelOpening? rivalOpening = null, ulong seed = 41,
                IReadOnlyList<uint> playerDeck = null, IReadOnlyList<uint> rivalExtra = null,
                IReadOnlyList<uint> playerExtra = null, bool recordReplay = false, DuelOpening? playerOpening = null)
            {
                _name = name;
                Duel = new DuelSession(recordPlanningReplay: recordReplay);
                Require(Duel.StartNewDuel(playerDeck ?? Enumerable.Repeat(playerCard, 40).ToArray(), playerExtra ?? Array.Empty<uint>(),
                    Enumerable.Repeat(rivalCard, 40).ToArray(), rivalExtra ?? Array.Empty<uint>(), humanFirst,
                    seed: new ulong[] { seed, unchecked(32 + (seed ^ 41UL) * 0x9E3779B97F4A7C15UL), 7, 11 },
                    opponentOpening: rivalOpening, playerOpening: playerOpening, storyGimmick: name), $"Could not start {name}.");
                Drive(() => InMain(Duel, 1));
            }

            internal void NormalSummon(uint code = 0)
            {
                int index = Duel.ActivePrompt.summonable.FindIndex(card => code == 0 || card.code == code);
                Require(index >= 0, $"The test had no legal normal summon in {_name}.");
                int turn = Duel.TurnNumber;
                Duel.SubmitResponseInt(index << 16);
                Drive(() => InMain(Duel, turn));
            }

            internal void NextMain(int turn) => Drive(() => InMain(Duel, turn), endTurn: true);

            internal void Activate(uint code, uint target = 0, uint acceptEffect = 0)
            {
                int index = Duel.ActivePrompt.activatable.FindIndex(card => card.code == code);
                Require(index >= 0, $"Could not activate {code} in {_name}.");
                int turn = Duel.TurnNumber;
                Duel.SubmitResponseInt((index << 16) | 5);
                Drive(() => InMain(Duel, turn), target: target, acceptEffect: acceptEffect);
            }

            internal void Drive(Func<bool> stop, bool endTurn = false, uint target = 0, uint acceptEffect = 0,
                bool attackPosition = false, uint declineEffect = 0)
            {
                for (int step = 0; step < 170 && !stop() && !Duel.IsFinished && Duel.RetryCount == 0; step++)
                {
                    var prompt = Duel.ActivePrompt;
                    if (prompt == null) { Duel.StepUntilPrompt(); continue; }
                    if (prompt.messageType == YgoConstants.MSG_SELECT_CARD && target != 0)
                    {
                        int index = prompt.candidates.FindIndex(card => card.code == target);
                        if (index >= 0) { Duel.SubmitResponse(DuelSession.PackCardSelection(index)); continue; }
                    }
                    if (attackPosition && prompt.messageType == YgoConstants.MSG_SELECT_POSITION &&
                        (prompt.allowedPositions & YgoConstants.POS_FACEUP_ATTACK) != 0)
                        Duel.SubmitResponseInt((int)YgoConstants.POS_FACEUP_ATTACK);
                    else if (acceptEffect != 0 && prompt.messageType == YgoConstants.MSG_SELECT_EFFECTYN && prompt.promptCardCode == acceptEffect)
                        Duel.SubmitResponseInt(1);
                    else if (declineEffect != 0 && prompt.messageType == YgoConstants.MSG_SELECT_EFFECTYN && prompt.promptCardCode == declineEffect)
                        Duel.SubmitResponseInt(0);
                    else if (prompt.messageType == YgoConstants.MSG_SELECT_CHAIN && !prompt.forced)
                        Duel.SubmitResponseInt(acceptEffect == 0 ? -1 : prompt.candidates.FindIndex(card => card.code == acceptEffect));
                    else if (endTurn && prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD && prompt.canEp)
                        Duel.SubmitResponseInt(7);
                    else Duel.SubmitResponse(_ai.ComputeResponse(Duel, prompt));
                }
                Require(stop() && Duel.RetryCount == 0, $"The {_name} test missed its checkpoint in turn {Duel.TurnNumber} " +
                    $"(phase {Duel.CurrentPhase}, prompt {Duel.ActivePrompt?.messageType}, retries {Duel.RetryCount}): " +
                    string.Join(" / ", Duel.Logs.TakeLast(14)));
            }

            public void Dispose() => Duel.Dispose();
        }
    }
}
