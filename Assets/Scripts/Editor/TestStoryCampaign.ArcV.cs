using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YGOPortable.Core;
using YGOPortable.UI;

namespace YGOPortable.Editor
{
    internal static partial class TestStoryCampaign
    {
        private static void VerifyArcVScenarioEffects()
        {
            const uint raigeki = 12580477, sparkman = 20721928, inferno = 46918794, darkHole = 53129443;
            VerifyActionCards();
            using (var sawatari = new ScenarioDuel("showmanship"))
            {
                sawatari.NextMain(2);
                EnterBattle(sawatari);
                Attack(sawatari, 25629622);
                Require(sawatari.Rival.mzone.Any(card => card is { code: 25629622, attack: 2900 }) && sawatari.Player.lp == 5100,
                    "Showmanship did not add 400 ATK to the live attacker.");
            }
            using (var circus = new ScenarioDuel("frightfur_circus", playerCard: raigeki))
            {
                circus.Activate(raigeki);
                Require(circus.Rival.grave.Any(card => card.code == 78778375) && circus.Player.lp == 7500,
                    "Destroying a Frightfur did not apply the 500-damage circus effect.");
            }
            using (var ddd = new ScenarioDuel("action_ddd"))
            {
                ddd.NextMain(2);
                Require(new[] { 74583607u, 987311u, 3758046u }.All(code => ddd.Rival.mzone.Any(card => card?.code == code)) &&
                    new[] { 45206713u, 72291412u }.All(code => ddd.Rival.banished.Any(card => card.code == code)) &&
                    ddd.Rival.mzone.Any(card => card?.code == 3758046 && card.overlayCodes.Count == 2),
                    "DDD did not perform its real Fusion, Synchro and Xyz opening: " + string.Join(" / ", ddd.Duel.Logs.TakeLast(22)));
            }
            using (var interrupted = new ScenarioDuel("action_ddd", playerCard: raigeki))
            {
                interrupted.Activate(raigeki);
                interrupted.NextMain(2);
                Require(interrupted.Rival.mzone.Any(card => card?.code == 74583607) &&
                    interrupted.Rival.mzone.All(card => card?.code is not (987311 or 3758046)),
                    "DDD still produced a Synchro or Xyz monster after losing its field materials.");
            }
            using (var raidraptors = new ScenarioDuel("raidraptor_resistance", playerCard: inferno))
            {
                for (int i = 0; i < 3; i++) raidraptors.Activate(inferno);
                Require(raidraptors.Rival.mzone.Any(card => card is { code: 73887236, attack: 100 }),
                    "The last stand activated above the set LP threshold.");
                raidraptors.Activate(inferno);
                Require(raidraptors.Rival.lp == 4000 && raidraptors.Rival.mzone.Any(card => card is { code: 73887236, attack: 1600 }),
                    "The Raidraptor last stand did not increase ATK at 4,000 LP.");
            }
            using (var speedroid = new ScenarioDuel("action_speedroid"))
            {
                speedroid.NextMain(2);
                Require(speedroid.Rival.mzone.Any(card => card?.code == 82044279) &&
                    new[] { 16725505u, 81275020u, 53932291u }.All(code => speedroid.Rival.grave.Any(card => card.code == code)),
                    "Clear Wing was not Synchro Summoned with the actual Speedroid materials.");
            }
            using (var heavy = new ScenarioDuel("superheavy_defense"))
            {
                heavy.NextMain(2);
                Require(heavy.Rival.mzone.Any(card => card?.code == 3117804 && card.IsDefense), "Big Ben-K did not start in Defense Position.");
                EnterBattle(heavy);
                Attack(heavy, 3117804);
                Require(heavy.Player.lp == 4500, "Big Ben-K did not deal its 3500 DEF as attack damage.");
            }
            using (var rebellion = new ScenarioDuel("action_dark_rebellion"))
            {
                rebellion.NormalSummon(sparkman);
                rebellion.NextMain(2);
                rebellion.Activate(16195942, sparkman);
                Require(rebellion.Rival.mzone.Any(card => card is { code: 16195942, attack: 3300 } && card.overlayCodes.Count == 0) &&
                    rebellion.Player.mzone.Any(card => card is { code: sparkman, attack: 800 }),
                    "Dark Rebellion did not detach its materials and steal half the target's ATK.");
            }
            using (var moon = new ScenarioDuel("lunalight_dance", playerCard: 14087893))
            {
                Require(moon.Duel.ActivePrompt.activatable.All(card => card.code != 14087893),
                    "Book of Moon could target the protected dancer.");
                moon.NextMain(2);
                EnterBattle(moon);
                Attack(moon, 51777272);
                int firstHit = moon.Player.lp;
                Attack(moon, 51777272);
                Require(firstHit < 8000 && moon.Player.lp < firstHit, "Lunalight did not attack twice in one battle phase.");
            }
            using (var windwitch = new ScenarioDuel("windwitch_barrier", playerCard: inferno))
            {
                windwitch.Activate(inferno);
                Require(windwitch.Rival.lp == 7500 && windwitch.Player.lp == 7500,
                    "The Windwitch barrier did not halve only its controller's effect damage.");
            }
            using (var king = new ScenarioDuel("action_king"))
            {
                king.NormalSummon(sparkman);
                king.NextMain(2);
                king.Activate(16172067);
                Require(king.Rival.mzone.Any(card => card?.code == 16172067) && king.Player.mzone.All(card => card == null) &&
                    king.Player.szone.All(card => card == null), "Tyrant did not clear the other physical cards, including the Action Field.");
            }
            using (var predator = new ScenarioDuel("predator_counters"))
            {
                predator.NormalSummon(sparkman);
                Require(PredatorCounters(predator) == 1, "The first native Predator Counter was missing.");
                predator.NextMain(3);
                Require(PredatorCounters(predator) == 2, "The Predator Counter did not accumulate on the next Standby.");
            }
            using (var destiny = new ScenarioDuel("destiny_hero_return", playerCard: raigeki))
            {
                destiny.Activate(raigeki, acceptEffect: 60461804);
                Require(destiny.Rival.grave.Any(card => card.code == 60461804), "Phoenix Enforcer could not be destroyed.");
                destiny.Drive(() => InMain(destiny.Duel, 2), endTurn: true, acceptEffect: 60461804);
                Require(destiny.Rival.mzone.Any(card => card?.code == 60461804), "Phoenix Enforcer did not revive at the next Standby.");
            }
            VerifyZarcBarrier();
            using (var venom = new ScenarioDuel("starving_venom", playerCard: 70095154))
            {
                SpecialSummon(venom, 70095154);
                venom.NextMain(2);
                venom.Activate(41209827, 70095154);
                Require(venom.Rival.mzone.Any(card => card?.code == 41209827 && card.CurrentNameCode == 70095154),
                    "Starving Venom did not copy the Level 5 monster's active identity and effects.");
            }
            using (var trial = new ScenarioDuel("ddd_final_trial"))
            {
                trial.NextMain(2);
                Require(trial.Rival.mzone.Any(card => card?.code == 18897163 &&
                    new[] { 16006416u, 44852429u }.All(card.overlayCodes.Contains)),
                    "The final DDD trial did not use the Fusion and Synchro kings to Xyz Summon Darkness Armageddon.");
            }
            using (var dragons = new ScenarioDuel("four_dragons", playerCard: darkHole))
            {
                var waves = new[] { 96733134u, 42160203u, 70771599u, 43387895u };
                for (int wave = 0; wave < waves.Length; wave++)
                {
                    Require(dragons.Rival.mzone.Any(card => card?.code == waves[wave]),
                        $"Supreme King Dragon {waves[wave]} did not appear in order.");
                    if (wave + 1 == waves.Length) break;
                    if (wave > 0) dragons.NextMain(wave * 2 + 1);
                    dragons.Activate(darkHole);
                    dragons.NextMain(wave * 2 + 2);
                }
            }
            using (var smile = new ScenarioDuel("smile_universe", rivalOpening: new DuelOpening(8000, 5, 16178681)))
            {
                Require(smile.Rival.hand.Any(card => card.code == 35259350) &&
                    new[] { 26270847u, 47075569u }.All(code => smile.Rival.extra.Any(card => card.code == code && card.IsFaceUp)),
                    "The finale did not offer Smile Universe and a face-up Pendulum Extra Deck.");
                smile.NextMain(2);
                smile.Activate(35259350);
                Require(smile.Rival.mzone.Any(card => card?.code is 26270847 or 47075569) && smile.Player.lp > 8000,
                    "Smile Universe did not actually summon a Pendulum Monster or heal the player.");
            }
            Debug.Log("[VERIFY ARC-V SCENARIOS] Live Action Cards, native DDD combos, destroyable field rules, battle and counters, Z-ARC lock, Dragon waves and Smile Universe passed.");

            static int PredatorCounters(ScenarioDuel duel) => duel.Player.mzone.Single(card => card?.code == 20721928)
                .Counters.SingleOrDefault(counter => counter.Type == 0x1041).Count;
        }

        private static void VerifyActionCards()
        {
            var witnessed = (Evasion: false, Miracle: false);
            for (ulong seed = 11; seed < 32 && (!witnessed.Evasion || !witnessed.Miracle); seed++)
            {
                using var action = new ScenarioDuel("action_opening", seed: seed, recordReplay: true);
                Require(action.Player.szone[5]?.code == StoryGimmickRuntime.ActionField && action.Duel.StoryActionSearchIndex >= 0,
                    "The Action Field did not offer a legal search.");
                var canvas = new GameObject("Action search", typeof(Canvas));
                var input = new GameObject("Action input", typeof(EventSystem));
                var previousEvents = EventSystem.current;
                var events = input.GetComponent<EventSystem>();
                if (EventSystem.current == null)
                    typeof(EventSystem).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(events, null);
                EventSystem.current = events;
                ((RectTransform)canvas.transform).sizeDelta = new Vector2(1600, 900);
                var modals = new DuelPromptModals(canvas.transform, null, action.Duel.SubmitResponse);
                var field = new DuelFieldUI(null, canvas.transform, modals, mobileLayout: seed % 2 == 0);
                try
                {
                    field.BindSession(action.Duel, null, null);
                    field.Refresh();
                    var search = field.Root.GetComponentsInChildren<Button>()
                        .Single(button => button.transform.parent.name == "ActionSearch");
                    Require(search.IsInteractable(), "The legal search was missing from the duel HUD.");
                    if (seed % 2 == 0)
                    {
                        events.SetSelectedGameObject(search.gameObject);
                        Require(EventSystem.current == events && events.currentSelectedGameObject == search.gameObject,
                            "The headless EventSystem did not focus the Action search button.");
                        field.HandleDuelPad(new DeckPadFrame(Vector2Int.zero, DeckPadButtons.Submit));
                    }
                    else
                    {
                        search.onClick.Invoke();
                    }
                }
                finally
                {
                    field.UnbindSession();
                    if (EventSystem.current == events)
                        typeof(EventSystem).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(events, null);
                    if (previousEvents != null) EventSystem.current = previousEvents;
                    UnityEngine.Object.DestroyImmediate(input);
                    UnityEngine.Object.DestroyImmediate(canvas);
                }
                action.Drive(() => InMain(action.Duel, 1));
                var found = action.Player.hand.SingleOrDefault(card => card.code is StoryGimmickRuntime.ActionEvasion or StoryGimmickRuntime.ActionMiracle);
                Require(found != null, $"Action search input did not add a card (seed {seed}): " +
                    string.Join(" / ", action.Duel.Logs.TakeLast(12)));
                Require(action.Duel.StoryActionSearchIndex < 0, "A second Action Card search was available in the same turn.");
                using (var replay = action.Duel.ReplayForPlanning(() => false))
                    Require(replay != null && replay.Players[replay.HumanPlayerIndex].hand.Any(card => card.code == found.code) &&
                        replay.StoryActionSearchIndex < 0, "Replaying the duel changed the discovered Action Card or reset its limit.");
                action.NormalSummon(20721928);
                action.NextMain(2);
                EnterBattle(action);
                Attack(action, 16178681, acceptEffect: found.code);
                bool evade = found.code == StoryGimmickRuntime.ActionEvasion;
                Require(action.Player.grave.Any(card => card.code == found.code) &&
                    action.Player.mzone.Any(card => card?.code == 20721928) && action.Player.lp == (evade ? 8000 : 7100),
                    $"The native Action Magic {found.code} did not save the attacked monster (LP {action.Player.lp}).");
                action.NextMain(3);
                Require(action.Duel.StoryActionSearchIndex >= 0, "A spent Action Card prevented searching on the next turn.");
                witnessed = (witnessed.Evasion || evade, witnessed.Miracle || !evade);
            }
            Require(witnessed.Evasion && witnessed.Miracle, "No seed exercised both native Action Magics.");
        }

        private static void VerifyZarcBarrier()
        {
            const uint teleport = 67723438, tuner = 59575539, sparkman = 20721928, brionac = 50321796, rulers = 54693926;
            var deck = Enumerable.Range(0, 40)
                .Select(i => new[] { teleport, tuner, sparkman, rulers }[i % 4]).ToArray();
            for (ulong seed = 11; seed < 42; seed++)
            {
                using var zarc = new ScenarioDuel("zarc_barrier", playerDeck: deck, playerExtra: new[] { brionac },
                    rivalOpening: new DuelOpening(10000, 5, 13331639), seed: seed);
                if (!new[] { teleport, sparkman, rulers }.All(code => zarc.Player.hand.Any(card => card.code == code))) continue;
                zarc.Activate(teleport, tuner);
                zarc.NormalSummon(sparkman);
                Require(zarc.Rival.lp == 10000 && zarc.Duel.ActivePrompt.spSummonable.All(card => card.code != brionac),
                    "Z-ARC allowed an otherwise legal Extra Deck summon.");
                zarc.Activate(rulers);
                SpecialSummon(zarc, brionac);
                Require(zarc.Player.mzone.Any(card => card?.code == brionac),
                    "Disabling the physical Z-ARC did not restore the legal Synchro Summon.");
                return;
            }
            Require(false, "The Z-ARC fixture did not draw the Synchro tools.");
        }
    }
}
