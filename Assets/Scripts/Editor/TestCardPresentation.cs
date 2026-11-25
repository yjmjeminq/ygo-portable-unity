using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YGOPortable.Core;
using YGOPortable.UI;

namespace YGOPortable.Editor
{
    internal static class TestCardPresentation
    {
        private const uint BlueEyes = 89631139;
        private const uint AlternateBlueEyes = 89631134;
        private const uint SkullRedBird = 10202894;
        private const uint Masquerena = 65741786;
        private const uint DominusImpulse = 40366667;
        private const int DominusIllustration = 22823;

        internal static void VerifyInterfaceIcons()
        {
            var identityKeys = YgoConstants.RaceSpriteNames.Values.Concat(YgoConstants.AttributeSpriteNames.Values)
                .Concat(new[] { "attribute/spell", "attribute/trap" });
            foreach (string key in identityKeys)
            {
                var fallback = DuelUiIcons.Find(key);
                Require(fallback != null, $"Missing built-in identity icon for {key}.");
                VerifyIcon(key);
            }
            foreach (var (key, type) in new (string, uint)[]
            {
                ("quickplay", YgoConstants.TYPE_SPELL | YgoConstants.TYPE_QUICKPLAY),
                ("continuous", YgoConstants.TYPE_SPELL | YgoConstants.TYPE_CONTINUOUS),
                ("equip", YgoConstants.TYPE_SPELL | YgoConstants.TYPE_EQUIP),
                ("field", YgoConstants.TYPE_SPELL | YgoConstants.TYPE_FIELD),
                ("ritual", YgoConstants.TYPE_SPELL | YgoConstants.TYPE_RITUAL),
                ("counter", YgoConstants.TYPE_TRAP | YgoConstants.TYPE_COUNTER),
            })
                Require(DuelUiKit.GetSpellTrapSprite(type) == VerifyIcon("spelltrap/" + key),
                    $"The card did not display its {key} subtype icon.");
            Require(DuelUiKit.GetSpellTrapSprite(YgoConstants.TYPE_TRAP | YgoConstants.TYPE_CONTINUOUS) ==
                DuelUiIcons.FindCardIdentity("spelltrap/continuous"), "Continuous traps did not share the requested icon.");
            Require(DuelUiIcons.Find("phase/main2") != null && DuelUiIcons.Action("SUMMONSP") != null &&
                DuelUiKit.GetSpellTrapSprite(YgoConstants.TYPE_TRAP | YgoConstants.TYPE_COUNTER) != null,
                "A phase, command, or spell and trap icon did not resolve.");
            var commands = new[] { "ACTION", "ATTACK", "SUMMON", "SUMMONSP", "TURN_DEF", "SETMONST",
                "SET", "LOOK", "SURRENDER", "CANCEL" }.Select(DuelUiIcons.Action).ToArray();
            Require(commands.All(sprite => sprite != null) && commands.Distinct().Count() == commands.Length,
                "The duel commands did not each resolve to a distinct pictogram.");
            var suppliedSleeve = Resources.Load<Texture2D>(CardArtLoader.CardSleeveResource);
            var fileSleeve = CardArtLoader.GetDuelVisualsSprite(CardArtLoader.CardSleeveFile);
            var sleeve = CardArtLoader.GetCardBackSprite();
            Require(sleeve != null && CardArtLoader.GetArtSprite(0) == sleeve &&
                (fileSleeve != null ? sleeve == fileSleeve : suppliedSleeve == null || sleeve.texture == suppliedSleeve),
                "The card back ignored a supplied sleeve or rendered inconsistently for hidden cards.");
            if (fileSleeve != null)
                Debug.Log($"[VERIFY DUEL SLEEVE] File-backed card sleeve active at {sleeve.texture.width}×{sleeve.texture.height}.");
            VerifyDuelVisualsOverrides();
            Debug.Log("[VERIFY DUEL ICONS] All 41 packaged badges and override routes passed.");

            static Sprite VerifyIcon(string key)
            {
                string assetName = DuelUiIcons.IdentityAssetName(key);
                var packaged = DuelUiKit.LoadSprite("Duel/CardIcons/" + assetName);
                Require(packaged != null && packaged.rect.width == 128f && packaged.rect.height == 128f,
                    $"Missing or incorrectly sized packaged badge for {assetName}.");
                var supplied = CardArtLoader.GetDuelVisualsSprite("CardIcons/" + assetName + ".png") ??
                    CardArtLoader.GetDuelVisualsSprite("CardIcons/" + key + ".png");
                var shown = DuelUiIcons.FindCardIdentity(key);
                Require(shown == (supplied ?? packaged), $"Could not resolve the configured badge for {assetName}.");
                return shown;
            }
        }

        private static void VerifyDuelVisualsOverrides()
        {
            string testRoot = Path.Combine(Path.GetTempPath(), "ygo-duel-visuals-" + Guid.NewGuid().ToString("N"));
            string local = Path.Combine(testRoot, "Documents", "DuelVisuals");
            string streaming = Path.Combine(testRoot, "StreamingAssets");
            string packaged = Path.Combine(streaming, "DuelVisuals");
            string previous = CardArtLoader.ActiveDuelVisualsPath;
            try
            {
                CardArtLoader.EnsureLocalDuelVisualsFolder(local);
                Require(Directory.Exists(local), "The app could not create a Files-visible DuelVisuals folder.");
                WriteImage(local, CardArtLoader.CardSleeveFile, 16, 24);
                WriteImage(packaged, CardArtLoader.CardSleeveFile, 24, 36);
                WriteImage(local, "CardIcons/attribute/light.png", 18, 18);
                WriteImage(packaged, "CardIcons/attribute/light.png", 24, 24);
                WriteImage(packaged, "CardIcons/race/dragon.png", 24, 24);
                WriteImage(local, "CardIcons/race/soldier.png", 19, 19);
                WriteImage(packaged, "Effects/Summon.png", 14, 14);
                File.Copy(Path.Combine(Application.streamingAssetsPath, "cardart.idx"), Path.Combine(streaming, "cardart.idx"));

                CardArtLoader.Shutdown();
                CardArtLoader.Initialize(streaming, local);
                var sleeve = CardArtLoader.GetCardBackSprite();
                Require(sleeve.texture.width == 16 && CardArtLoader.GetArtSprite(0) == sleeve &&
                    DuelUiKit.GetAttributeSprite(YgoConstants.ATTRIBUTE_LIGHT)?.texture.width == 18,
                    "Local duel visuals did not override packaged artwork for the sleeve and attribute.");
                Require(DuelUiKit.GetRaceSprite(YgoConstants.RACE_DRAGON)?.texture.width == 24,
                    "The race icon did not fall back to the packaged visuals.");
                Require(DuelUiKit.GetRaceSprite(YgoConstants.RACE_WARRIOR)?.texture.width == 19,
                    "The renamed warrior icon did not preserve a user's existing override.");
                Require(!CardArtLoader.IsDuelArtActive && DuelVisualAssets.Effect(DuelEffectKind.Summon)?.texture.width == 14,
                    "The base visuals should work before the external art pack is installed.");

                string pack = Path.Combine(local, "cardart.pack");
                byte[] illustration = File.ReadAllBytes(Path.Combine(local, CardArtLoader.CardSleeveFile));
                using (var writer = new BinaryWriter(File.Create(pack)))
                    WriteArtPack(writer, illustration);
                WriteImage(local, "Effects/Summon.png", 18, 18);
                WriteImage(local, "Effects/Fusion_Vortex.png", 20, 20);
                string video = Path.Combine(packaged, "Effects", "Fusion.mp4");
                File.WriteAllBytes(video, new byte[] { 1 });
                File.Delete(Path.Combine(local, CardArtLoader.CardSleeveFile));
                CardArtLoader.Reload();
                Require(CardArtLoader.GetCardBackSprite().texture.width == 24,
                    "Reloading with a removed local sleeve did not restore the packaged one.");
                var installed = CardArtLoader.RetainArtwork(BlueEyes, 0);
                CardArtLoader.ReleaseArtwork(BlueEyes, 0);
                Require(CardArtLoader.IsDuelArtActive && CardArtLoader.HasExternalDuelArt &&
                    CardArtLoader.ActiveDuelArtCount == 1 && installed.texture.width == 16,
                    "Reloading did not index and decode the installed artwork.");
                Require(DuelVisualAssets.Effect(DuelEffectKind.Summon)?.texture.width == 18 &&
                    DuelVisualAssets.SummonTexture(DuelEffectKind.Fusion)?.texture.width == 20 &&
                    DuelVisualAssets.FindSummonVideo(DuelEffectKind.Fusion, local, streaming) == video,
                    "The runtime did not pick up the local effect or bundled summon video.");

                string nested = Path.Combine(local, "DuelVisuals");
                Directory.CreateDirectory(nested);
                string nestedPack = Path.Combine(nested, "cardart.pack");
                string localEffect = Path.Combine(local, "Effects", "Summon.png");
                string nestedEffect = Path.Combine(nested, "Effects", "Summon.png");
                Directory.CreateDirectory(Path.GetDirectoryName(nestedEffect));
                File.Move(pack, nestedPack);
                File.Move(localEffect, nestedEffect);
                CardArtLoader.Reload();
                Require(CardArtLoader.HasExternalDuelArt && CardArtLoader.HasExternalDuelArtFile &&
                    CardArtLoader.ActiveDuelVisualsPath == nested &&
                    DuelVisualAssets.Effect(DuelEffectKind.Summon)?.texture.width == 18,
                    "Reloading did not find the pack and matching effects inside the iOS extraction folder.");

                File.Move(nestedPack, pack);
                File.Move(nestedEffect, localEffect);
                CardArtLoader.Reload();
                Require(CardArtLoader.HasExternalDuelArt && CardArtLoader.ActiveDuelVisualsPath == local,
                    "Reloading continued using the nested path after the pack moved to the standard location.");
                File.Delete(pack);
                File.Delete(localEffect);
                CardArtLoader.Reload();
                Require(!CardArtLoader.IsDuelArtActive && !CardArtLoader.HasExternalDuelArtFile &&
                    DuelVisualAssets.Effect(DuelEffectKind.Summon)?.texture.width == 14,
                    "Removing the external pack did not clear the cached artwork and effects.");
                File.WriteAllText(pack, "invalid fixture");
                CardArtLoader.Reload();
                Require(!CardArtLoader.HasExternalDuelArt && CardArtLoader.HasExternalDuelArtFile,
                    "A damaged local pack could not be distinguished from a missing file.");

                string zipPath = Path.Combine(testRoot, "DuelVisuals.zip");
                using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                {
                    using (var writer = new BinaryWriter(zip.CreateEntry("DuelVisuals/cardart.pack").Open()))
                        WriteArtPack(writer, illustration);
                    using var effectEntry = zip.CreateEntry("DuelVisuals/Effects/Summon.png").Open();
                    effectEntry.Write(illustration, 0, illustration.Length);
                }
                DuelVisualsZipImport.Install(zipPath, local);
                CardArtLoader.Reload();
                Require(File.ReadAllText(pack) == "invalid fixture" && CardArtLoader.HasExternalDuelArt &&
                    CardArtLoader.ActiveDuelArtCount == 1 &&
                    DuelVisualAssets.Effect(DuelEffectKind.Summon)?.texture.width == 16,
                    "The selected ZIP was not used, or the manually installed file was overwritten.");

                string badZip = Path.Combine(testRoot, "MissingCardArt.zip");
                using (var zip = ZipFile.Open(badZip, ZipArchiveMode.Create))
                using (var entry = zip.CreateEntry("DuelVisuals/Field.png").Open())
                    entry.WriteByte(1);
                bool rejected = false;
                try { DuelVisualsZipImport.Install(badZip, local); }
                catch (InvalidDataException) { rejected = true; }
                CardArtLoader.Reload();
                Require(rejected && CardArtLoader.HasExternalDuelArt && CardArtLoader.ActiveDuelArtCount == 1,
                    "A failed ZIP import replaced the previously installed artwork.");
            }
            finally
            {
                CardArtLoader.Shutdown();
                CardArtLoader.Initialize(Application.streamingAssetsPath, previous);
                if (Directory.Exists(testRoot)) Directory.Delete(testRoot, recursive: true);
            }
            Debug.Log("[VERIFY DUEL VISUALS] Folder creation, nested packs, ZIP import and recovery, and live reload passed.");

            static void WriteArtPack(BinaryWriter writer, byte[] artwork)
            {
                writer.Write(0x5241444Du);
                writer.Write(1u);
                writer.Write(1u);
                writer.Write(BlueEyes);
                writer.Write(0);
                writer.Write(28u);
                writer.Write((uint)artwork.Length);
                writer.Write(artwork);
            }

            static void WriteImage(string directory, string relative, int width, int height)
            {
                string path = Path.Combine(directory, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var texture = new Texture2D(width, height);
                try { File.WriteAllBytes(path, texture.EncodeToPNG()); }
                finally { UnityEngine.Object.DestroyImmediate(texture); }
            }
        }

        internal static void Run()
        {
            var canvas = new GameObject("Card Presentation Verification", typeof(Canvas)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(1600f, 900f);
            try
            {
                var card = CompositeCardView.Create(canvas.transform, "Card", true);
                VerifyFoil(card);
                VerifySummonMotion(card);
                VerifyExpandedDuelCard(canvas.transform);
                VerifyChainSourceIdentity();
                var animator = CardAnimationManager.Ensure(canvas);
                VerifyFeaturedCards(animator);
                VerifySummonReadout(animator);
                VerifyBattleComparisonProtocol();
                VerifyBattleVisibility(animator);
                VerifyLinkDirectAttack(animator);
                VerifyAttackSpacing(animator);
                VerifyAttackAfterLayoutChange(animator);
                VerifyLifeFeedbackRouting(animator);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvas.gameObject);
            }
            Debug.Log("[VERIFY CARD PRESENTATION] Foil geometry, hidden and reused cards, signature reveals and distinct summon paths passed.");
        }

        internal static void VerifyOverframePaths()
        {
            const uint magician = 46986414, baronne = 84815190, excel = 9674034, unrelated = 20721928;
            var installed = Path.Combine(CardArtLoader.ActiveDuelVisualsPath, "CutIns");
            if (Directory.Exists(installed))
            {
                int verified = 0, fellBack = 0;
                foreach (string file in Directory.EnumerateFiles(installed, "*.png"))
                {
                    var parts = Path.GetFileNameWithoutExtension(file).Split('-');
                    if (parts.Length != 2 || !uint.TryParse(parts[0], out uint code) ||
                        !int.TryParse(parts[1], out int variant)) continue;
                    var source = CardArtLoader.GetArtworkSource(code, variant);
                    Require(source == (code, variant), $"The installed portrait {code}-{variant} had no matching card art.");
                    var portrait = DuelVisualAssets.RetainCutIn(source.Code, source.Variant);
                    if (portrait == null && code == baronne && variant == 0)
                    {
                        var artwork = CardArtLoader.RetainArtwork(code, variant);
                        Require(artwork != null && artwork != CardArtLoader.GetFallbackArt(),
                            "The incomplete Baronne cut-in did not have its illustration available for fallback.");
                        CardArtLoader.ReleaseArtwork(code, variant);
                        fellBack++;
                        continue;
                    }
                    Require(portrait != null && portrait.rect.width > 0, $"The installed cut-in {code}-{variant} did not decode.");
                    if (variant == 0)
                        Require(DuelVisualAssets.CanDisplayCutIn(code, -1), $"Bundled art skipped the installed {code}-0 cut-in.");
                    DuelVisualAssets.ReleaseCutIn(portrait);
                    verified++;
                }
                Debug.Log($"[VERIFY OVERFRAME LIBRARY] Verified {verified} installed cut-ins and {fellBack} illustration fallbacks.");
            }

            string folder = Path.Combine(Path.GetTempPath(), "ygo-overframe-art-" + Guid.NewGuid().ToString("N"));
            string directory = Path.Combine(folder, "CutIns");
            Directory.CreateDirectory(directory);
            var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            texture.SetPixels32(Enumerable.Range(0, 64).Select(i => i % 8 < 3
                ? new Color32(0, 0, 0, 0) : new Color32(72, 110, 180, 255)).ToArray());
            texture.Apply();
            byte[] portraitBytes = texture.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(texture);
            foreach (uint code in new[] { BlueEyes, magician, baronne, excel, SkullRedBird, Masquerena })
                File.WriteAllBytes(Path.Combine(directory, $"{code}-0.png"), portraitBytes);
            File.WriteAllBytes(Path.Combine(directory, $"{BlueEyes}-23.png"), portraitBytes);

            string previous = CardArtLoader.ActiveDuelVisualsPath;
            var canvas = new GameObject("Overframe Verification", typeof(Canvas)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(1600, 900);
            CardArtLoader.Shutdown();
            CardArtLoader.Initialize(Application.streamingAssetsPath, folder);
            try
            {
                var zero = DuelVisualAssets.RetainCutIn(BlueEyes, 0);
                var bundled = DuelVisualAssets.RetainCutIn(BlueEyes, -1);
                var alternate = DuelVisualAssets.RetainCutIn(BlueEyes, 23);
                Require(zero != null && bundled == zero && alternate != null && alternate != zero &&
                    !DuelVisualAssets.CanDisplayCutIn(BlueEyes, 24), "Default and exact alternate artwork did not resolve independently.");
                Require(zero.rect == new Rect(3f, 0f, 5f, 8f) && !zero.texture.isReadable,
                    "Transparent padding still shrank the portrait, or the decoded pixels remained in CPU memory.");
                DuelVisualAssets.ReleaseCutIn(alternate);
                DuelVisualAssets.ReleaseCutIn(bundled);
                DuelVisualAssets.ReleaseCutIn(zero);

                var holder = DuelUiKit.CreateRect("View", canvas.transform, new Vector2(.25f, .16f), new Vector2(.52f, .83f),
                    Vector2.zero, Vector2.zero);
                var card = CompositeCardView.Create(holder, "Detailed", detailedOverlay: true);
                card.EnableOverframe();
                CardOverframeInput.Attach(holder, card, false);
                card.SetCard(BlueEyes);
                Canvas.ForceUpdateCanvases();
                var aspect = card.transform.Find("AspectBox");
                var layer = aspect.Find("Overframe");
                var title = aspect.Find("CardTitle");
                var header = aspect.Find("OverframeHeader").GetComponent<Image>();
                var bounds = (RectTransform)layer.Find("PortraitBounds");
                var image = bounds.Find("Depth/LocalCutIn").GetComponent<Image>();
                Require(card.HasOverframe && image.sprite == zero && image.maskable && !image.raycastTarget &&
                    layer.GetSiblingIndex() > aspect.Find("Frame").GetSiblingIndex() &&
                    header.enabled && header.color.a > .85f && !header.raycastTarget &&
                    layer.GetSiblingIndex() < header.transform.GetSiblingIndex() && header.transform.GetSiblingIndex() < title.GetSiblingIndex() &&
                    bounds.GetComponent<RectMask2D>() == null &&
                    layer.Find("OverframeTrim").GetSiblingIndex() < bounds.GetSiblingIndex() &&
                    layer.Find("FaceArtwork").GetComponent<RectMask2D>() != null &&
                    bounds.anchorMin.x < 0f && bounds.anchorMax.x > 1f && bounds.anchorMax.y > 1f &&
                    aspect.Find("Rules/Text").GetComponent<Text>().color.r > .8f,
                    "Extended artwork did not cover the face behind readable title and rules layers.");
                var input = holder.GetComponent<CardOverframeInput>();
                typeof(CardOverframeInput).GetMethod("ShowDepth", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(input, new object[] { new Vector2(.6f, .3f), .21f });
                var tilt = (RectTransform)bounds.Find("Depth");
                var background = layer.Find("FaceArtwork/ExpandedArtwork").GetComponent<Image>();
                Require(tilt.anchoredPosition.sqrMagnitude > 1f && tilt.localRotation != Quaternion.identity &&
                    card.transform.localRotation != Quaternion.identity &&
                    background.sprite == DuelStyle.OverframeBackdrop && background.sprite != card.ArtworkSprite &&
                    background.color.r > .8f && bounds.anchorMin.y >= .20f &&
                    Vector2.Dot(background.rectTransform.anchoredPosition, tilt.anchoredPosition) < 0f,
                    "The portrait and frame did not move independently of the subject-free background.");
                typeof(CardOverframeInput).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(input, null);
                Require(holder.anchorMin == new Vector2(.25f, .16f) && holder.anchorMax == new Vector2(.52f, .83f),
                    "The detail card shifted when its overframe became visible.");
                card.SetCard(BlueEyes, faceDown: true);
                Require(!card.HasOverframe && image.sprite == null && !header.enabled, "The face-down card exposed its portrait or title backing.");
                card.SetCard(unrelated);
                Require(!card.HasOverframe && aspect.Find("Rules/Text").GetComponent<Text>().color.r < .2f,
                    "The next regular card kept the extended artwork or ink.");
                Require(CardFaceFrame.Get(YgoConstants.TYPE_EFFECT) != CardFaceFrame.Get(YgoConstants.TYPE_SPELL) &&
                    CardFaceFrame.Get(YgoConstants.TYPE_XYZ) != CardFaceFrame.Get(YgoConstants.TYPE_XYZ | YgoConstants.TYPE_PENDULUM),
                    "Distinct card types reused the same face.");

                var deck = new DeckEditorUI(null, canvas.transform, mobileLayout: false);
                const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(DeckEditorUI).GetMethod("InspectCard", instance).Invoke(deck, new object[] { BlueEyes });
                var preview = (CompositeCardView)typeof(DeckEditorUI).GetField("_previewCard", instance).GetValue(deck);
                Require(!preview.HasOverframe, "The regular deck preview borrowed the special edition portrait.");
                var selected = (Dictionary<uint, DeckCardAppearance>)typeof(DeckEditorUI)
                    .GetField("_trunkAppearances", instance).GetValue(deck);
                selected[BlueEyes] = new DeckCardAppearance { overframe = true };
                typeof(DeckEditorUI).GetMethod("InspectCard", instance).Invoke(deck, new object[] { BlueEyes });
                Require(!preview.HasOverframe && preview.Appearance?.overframe == true &&
                    preview.transform.Find("AspectBox/DeckEdition").gameObject.activeSelf,
                    "The deck preview should show the printed face with its edition badge.");
                typeof(DeckEditorUI).GetMethod("OpenExpandedDeckCard", instance).Invoke(deck, new object[] { BlueEyes });
                var expanded = (CompositeCardView)typeof(DeckEditorUI).GetField("_expandedDeckArt", instance).GetValue(deck);
                Require(!expanded.HasOverframe && expanded.ArtworkSprite == preview.ArtworkSprite,
                    "The expanded deck viewer did not show the printed alternate artwork.");
                var thumbnail = CompositeCardView.Create(holder, "Compact");
                var special = new DeckCardAppearance { overframe = true };
                thumbnail.SetCard(BlueEyes, live: new LiveCardState { code = BlueEyes, Appearance = special });
                Require(thumbnail.HasOverframe && thumbnail.transform.Find("AspectBox/DeckEdition").gameObject.activeSelf,
                    "The compact extended preview did not show its portrait and edition marker.");
                thumbnail.SetReadoutFocus(1f);
                Require(thumbnail.transform.Find("AspectBox/Overframe/PortraitBounds/Depth").localScale.x > 1f,
                    "Focusing the extended preview did not lift the artwork.");
                thumbnail.SetDeckCard(unrelated, special);
                var fallback = thumbnail.transform.Find("AspectBox/Overframe");
                Require(thumbnail.HasOverframe && !fallback.Find("PortraitBounds/Depth/LocalCutIn").GetComponent<Image>().enabled &&
                    fallback.Find("FaceArtwork/ExpandedArtwork").GetComponent<Image>().sprite == thumbnail.ArtworkSprite,
                    "An owned variant without a portrait did not fall back to its artwork.");
                thumbnail.SetDeckCard(unrelated, special, faceDown: true);
                Require(!thumbnail.HasOverframe && !fallback.gameObject.activeSelf,
                    "The fallback artwork was visible through a face-down card.");
                deck.Close();

                var crafting = new CardCraftingUI(canvas.transform, null);
                crafting.Show(BlueEyes);
                var shopCard = (CompositeCardView)typeof(CardCraftingUI).GetField("_art", instance).GetValue(crafting);
                var toggle = (Button)typeof(CardCraftingUI).GetField("_editionPreview", instance).GetValue(crafting);
                Require(shopCard.CurrentCode == BlueEyes && !shopCard.HasOverframe && toggle.gameObject.activeInHierarchy,
                    "The crafting screen did not open on the standard card or offer its alternate preview.");
                int inventory = CollectionProgress.GetOwnedOverframeCount(BlueEyes);
                decimal dust = CollectionProgress.CraftDust;
                toggle.onClick.Invoke();
                Require(shopCard.HasOverframe && CollectionProgress.CraftDust == dust &&
                    CollectionProgress.GetOwnedOverframeCount(BlueEyes) == inventory,
                    "Previewing the edition changed its stock or crafting dust.");
                toggle.onClick.Invoke();
                Require(!shopCard.HasOverframe, "The crafting preview did not return to the regular face.");
                VerifyOverframeCraftAvailability(canvas.transform, crafting, unrelated);
                crafting.Hide();

                VerifyOverframeOffsets(canvas.transform, folder, magician);
                VerifyPackFaces(canvas.transform);
                var animator = CardAnimationManager.Ensure(canvas);
                VerifyWideCutIn(animator, unrelated);
                VerifyOverframeAnimation(animator);
                VerifyPhysicalDeckAppearances();
                Debug.Log("[VERIFY OVERFRAME] Subject-free backdrops, artwork fallback, hidden cards, summon finish and chain animation passed.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvas.gameObject);
                CardArtLoader.Shutdown();
                CardArtLoader.Initialize(Application.streamingAssetsPath, previous);
                foreach (string file in Directory.EnumerateFiles(directory)) File.Delete(file);
                Directory.Delete(directory);
                Directory.Delete(folder);
            }
            VerifyPackagedOverframe(portraitBytes);
        }

        private static void VerifyOverframeCraftAvailability(Transform parent, CardCraftingUI crafting, uint ordinary)
        {
            var actions = crafting.Root.transform.Find("Main/Details");
            var standardCraft = actions.Find("Craft");
            var overframeCraft = actions.Find("CraftOverframe");
            Require(overframeCraft.gameObject.activeInHierarchy, "A supported card hid Overframe crafting.");
            crafting.Search.text = ordinary.ToString();
            Require(standardCraft.gameObject.activeInHierarchy && !overframeCraft.gameObject.activeSelf,
                "An ordinary card still displayed Overframe crafting in the catalog.");
            crafting.Show(ordinary, CardTradeAction.CraftOverframe);
            Require(crafting.ActiveRoot == actions.parent.gameObject && !overframeCraft.gameObject.activeSelf,
                "An unsupported Overframe shortcut opened the crafting confirmation.");

            foreach (bool compact in new[] { false, true })
            {
                var holder = DuelUiKit.FillRect("TradeButtons", parent);
                var shortcuts = new CardTradeControls(holder, (_, _) => { }, compact, deckEditor: compact);
                var standard = (RectTransform)holder.Find("Craft");
                var special = holder.Find("CraftOverframe");
                var specialDisassemble = holder.Find("DisassembleOverframe");
                shortcuts.Bind(BlueEyes);
                Require(special.gameObject.activeInHierarchy && specialDisassemble.gameObject.activeInHierarchy,
                    "A supported card hid the Overframe shortcuts.");
                Vector2 supportedSize = standard.anchorMax - standard.anchorMin;
                shortcuts.Bind(ordinary);
                Vector2 regularSize = standard.anchorMax - standard.anchorMin;
                Require(!special.gameObject.activeSelf && !specialDisassemble.gameObject.activeSelf &&
                    standard.gameObject.activeInHierarchy && regularSize.x * regularSize.y > supportedSize.x * supportedSize.y,
                    "The ordinary card's shortcuts left an unavailable edition visible or an empty slot.");
                shortcuts.Bind(BlueEyes);
                Require(special.gameObject.activeInHierarchy &&
                    standard.anchorMax - standard.anchorMin == supportedSize,
                    "Switching back to a supported card did not restore its Overframe crafting layout.");
                UnityEngine.Object.DestroyImmediate(holder.gameObject);
            }
            Debug.Log("[VERIFY OVERFRAME CRAFT] Catalog and card shortcuts hid unavailable Overframe actions.");
        }

        private static void VerifyOverframeOffsets(Transform parent, string directory, uint magician)
        {
            var masquerenaOffset = new Vector2(-136f, -145f);
            Require(OverframeOffsetCatalog.GetOffset(Masquerena) == (masquerenaOffset, 1f) &&
                OverframeOffsetCatalog.GetOffset(54603525) == (new Vector2(210f, 0f), 1f) &&
                OverframeOffsetCatalog.GetOffset(76072561) == (new Vector2(235f, 0f), 1f) &&
                OverframeOffsetCatalog.GetOffset(magician) == (Vector2.zero, 1f),
                "Startup did not load the bundled card corrections or default an unlisted card.");

            var holder = DuelUiKit.CreateRect("OffsetVerification", parent, Vector2.zero, Vector2.zero,
                Vector2.zero, new Vector2(300f, 420f));
            var view = CompositeCardView.Create(holder, "Detail", detailedOverlay: true);
            var cinematic = CompositeCardView.Create(holder, "Cinematic", detailedOverlay: true, cinematicOverframe: true);
            var appearance = new DeckCardAppearance { overframe = true };
            view.SetDeckCard(Masquerena, appearance);
            cinematic.SetDeckCard(Masquerena, appearance);
            Canvas.ForceUpdateCanvases();
            var portrait = view.transform.Find("AspectBox/Overframe/PortraitBounds/Depth/LocalCutIn").GetComponent<Image>();
            var projection = cinematic.transform.Find("Overframe/Depth/LocalCutIn").GetComponent<Image>();
            VerifyPlacement(portrait, masquerenaOffset, 1f);
            VerifyPlacement(projection, masquerenaOffset, 1f);
            Vector2 original = portrait.rectTransform.anchoredPosition;
            holder.sizeDelta *= .5f;
            Canvas.ForceUpdateCanvases();
            VerifyPlacement(portrait, masquerenaOffset, 1f);
            Require(Vector2.Distance(portrait.rectTransform.anchoredPosition, original * .5f) < .02f,
                "Resizing a retained card did not adapt its correction to the portrait's new size.");
            view.SetDeckCard(magician, appearance);
            VerifyPlacement(portrait, Vector2.zero, 1f);

            string path = Path.Combine(directory, OverframeOffsetCatalog.FileName);
            try
            {
                OverframeOffsetCatalog.Load(directory);
                Require(OverframeOffsetCatalog.GetOffset(Masquerena) == (Vector2.zero, 1f),
                    "A missing file kept a stale correction.");

                File.WriteAllText(path, "{\"65741786\":{\"offsetX\":2,\"offsetY\":-3,\"scale\":1.25}}");
                OverframeOffsetCatalog.Load(directory);
                File.WriteAllText(path, "{\"65741786\":");
                Require(OverframeOffsetCatalog.GetOffset(Masquerena) == (new Vector2(2f, -3f), 1.25f),
                    "A lookup did not use the startup cache after the file changed.");
                view.SetDeckCard(Masquerena, appearance);
                VerifyPlacement(portrait, new Vector2(2f, -3f), 1.25f);
                Vector2 neutral = portrait.rectTransform.anchoredPosition;
                var tilt = (RectTransform)portrait.transform.parent;
                view.PoseOverframe(1f, 1.16f, new Vector2(.55f, -.22f), .2f);
                Require(portrait.rectTransform.anchoredPosition == neutral &&
                    portrait.rectTransform.localScale == Vector3.one * 1.25f &&
                    tilt.anchoredPosition != Vector2.zero && tilt.localRotation != Quaternion.identity &&
                    tilt.localScale == Vector3.one * 1.16f,
                    "Animated tilt overwrote the catalog position or scale.");

                foreach (string malformed in new[] { "{\"65741786\":", "[]", "{\"65741786\":{\"scale\":0}}",
                    "{\"65741786\":{\"offsetX\":-136,\"scale\":\"1\"}}",
                    "{\"65741786\":{\"offsetX\":-136,\"offsetY\":1e999}}",
                    "{\"65741786\":{\"offsetX\":-136},\"bad-key\":{}}",
                    "{\"65741786\":{\"offsetX\":-136}} junk" })
                {
                    File.WriteAllText(path, malformed);
                    OverframeOffsetCatalog.Load(directory);
                    Require(OverframeOffsetCatalog.GetOffset(Masquerena) == (Vector2.zero, 1f),
                        "Malformed catalog data did not safely restore the defaults.");
                }
                view.SetDeckCard(magician, appearance);
                view.SetDeckCard(Masquerena, appearance);
                VerifyPlacement(portrait, Vector2.zero, 1f);
            }
            finally
            {
                File.Delete(path);
                OverframeOffsetCatalog.Load(Application.streamingAssetsPath);
                UnityEngine.Object.DestroyImmediate(holder.gameObject);
            }
            Debug.Log("[VERIFY OVERFRAME OFFSETS] Startup and cache, missing/malformed files, recycled cards, responsive positioning and tilt passed.");

            static void VerifyPlacement(Image image, Vector2 pixels, float scale)
            {
                Require(image.sprite != null, "The offset test did not bind a portrait.");
                Vector2 box = image.GetPixelAdjustedRect().size;
                Vector2 art = image.sprite.rect.size;
                float fit = Mathf.Min(box.x / art.x, box.y / art.y);
                Require(fit > 0f && Vector2.Distance(image.rectTransform.anchoredPosition, pixels * fit) < .02f &&
                    image.rectTransform.localScale == Vector3.one * scale, "The portrait did not use its configured offset and scale.");
            }
        }

        private static void VerifyWideCutIn(CardAnimationManager animator, uint withoutPortrait)
        {
            var from = new CardPose(new Vector2(-280f, -280f), new Vector2(85f, 123f));
            var to = new CardPose(new Vector2(132f, -74f), new Vector2(96f, 139f));
            var queue = (Queue<IEnumerator>)typeof(CardAnimationManager).GetField("_steps",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(animator);
            int landings = 0;
            animator.StageFieldArrival(new CardFlight(Masquerena, from, to, true, false, new DeckCardAppearance()),
                CardMotion.ExtraDeckSummon, 0, YgoConstants.LOCATION_MZONE, 2, null, null, () => landings++,
                () => { }, wideCutIn: true);
            animator.CompleteFieldArrivals();
            var step = queue.Dequeue();
            Require(step.MoveNext() && step.Current is IEnumerator, "The queued summon did not start its cinematic.");
            var cinematic = animator.GetComponentInChildren<DuelCutInCinematic>();
            Require(cinematic != null, "The Masquerena summon did not use the widescreen stage.");
            var playback = (IEnumerator)step.Current;
            Require(playback.MoveNext() && playback.Current is IEnumerator, "The cinematic did not prepare its landing.");
            var timeline = (IEnumerator)playback.Current;
            Require(timeline.MoveNext(), "The cinematic ended before showing a frame.");
            Canvas.ForceUpdateCanvases();
            cinematic.Pose(.68f);
            var stage = (RectTransform)cinematic.transform.Find("WideStage");
            var portrait = stage.Find("Portrait/SourceCanvas").GetComponent<RawImage>();
            var title = stage.Find("MonsterReadout/MonsterName/Text").GetComponent<Text>();
            Require(Mathf.Abs(stage.rect.width / stage.rect.height - 16f / 9f) < .001f &&
                cinematic.GetComponent<Image>().color.a > .68f && portrait.texture != null &&
                portrait.rectTransform.localScale == Vector3.one &&
                stage.Find("Portrait").localScale.x > 1f &&
                title.text == CardDatabase.GetName(Masquerena) &&
                stage.Find("MonsterReadout/Arrows/LinkDirections").gameObject.activeInHierarchy,
                "The cinematic lost its widescreen fit, slide, or Link monster readout.");
            ExecuteEvents.Execute(cinematic.gameObject, new PointerEventData(EventSystem.current)
                { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerDownHandler);
            Require(!timeline.MoveNext() && !playback.MoveNext() && !step.MoveNext() && landings == 1 &&
                animator.GetComponentInChildren<DuelCutInCinematic>() == null,
                "Clicking the widescreen reveal did not immediately release the stage and land once.");

            animator.QueueMove(new CardFlight(withoutPortrait, from, to, false, false),
                CardMotion.SpecialSummon, () => landings++, wideCutIn: true);
            var fallback = queue.Dequeue();
            Require(fallback.MoveNext() && animator.GetComponentInChildren<DuelCutInCinematic>() == null,
                "A monster without artwork started an empty widescreen stage.");
            while (fallback.MoveNext()) { }
            Require(landings == 2, "The summon without artwork did not complete.");
            animator.Clear();
            Debug.Log("[VERIFY WIDE CUT-IN] Queued Masquerena, 16:9 portrait, Link stats, click skip, landing and no-art fallback passed.");
        }

        private static void VerifyPackFaces(Transform parent)
        {
            var opening = new PackOpeningView(parent, null);
            opening.Show(DuelUiKit.WhiteSprite, "카드 프레임 검증",
                new IReadOnlyList<PackPull>[] { new[] { new PackPull(BlueEyes, true, 1), new PackPull(BlueEyes, false, 2) } });
            const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(PackOpeningView).GetMethod("ShowResults", instance).Invoke(opening, null);
            opening.AdvanceResults(1f);
            var slot = parent.Find("Screen_PackOpening/Results/Cards/Card_0");
            var select = (RectTransform)slot.Find("Select");
            var tilt = select.GetComponent<CardOverframeInput>();
            var face = slot.Find("Select/Face/View").GetComponent<CompositeCardView>();
            var edition = (RectTransform)slot.Find("OverframeBadge");
            var rarity = (RectTransform)slot.Find("Rarity");
            Require(!face.HasOverframe && !tilt.enabled && !edition.gameObject.activeSelf && !rarity.gameObject.activeSelf,
                "A sealed card in the pack exposed its edition or rarity.");
            slot.GetComponentInChildren<Button>().onClick.Invoke();
            opening.AdvanceResults(.241f);
            var portrait = face.transform.Find("AspectBox/Overframe/PortraitBounds/Depth");
            Require(face.HasOverframe && portrait.localScale.x > 1.07f && edition.gameObject.activeSelf &&
                edition.anchorMax.y < select.anchorMin.y &&
                rarity.gameObject.activeSelf && rarity.anchorMax.y < select.anchorMin.y &&
                !tilt.enabled && face.transform.Find("AspectBox/AtkDef").gameObject.activeInHierarchy,
                "The pack's special portrait did not lift or its captions obscured the card face.");
            opening.AdvanceResults(1f);
            Require(tilt.enabled && portrait.localScale.x <= 1.04f, "The opened card did not settle after flipping.");
            var focus = select.GetComponent<StarterRowFocus>();
            var pointer = new PointerEventData(EventSystem.current) { pointerId = 3, button = PointerEventData.InputButton.Left };
            focus.OnSelect(pointer);
            opening.AdvanceResults(.15f);
            Canvas.ForceUpdateCanvases();
            pointer.position = RectTransformUtility.WorldToScreenPoint(null, select.TransformPoint(select.rect.center));
            tilt.OnPointerDown(pointer);
            pointer.position += Vector2.right * select.rect.width * .19f;
            tilt.OnDrag(pointer);
            tilt.AdvancePose(.15f, 1f);
            var background = (RectTransform)face.transform.Find("AspectBox/Overframe/FaceArtwork/ExpandedArtwork");
            Require(Quaternion.Angle(face.transform.localRotation, Quaternion.identity) > 2f &&
                ((RectTransform)portrait).anchoredPosition.x > 0f && background.anchoredPosition.x < 0f &&
                portrait.localScale.x > 1.03f,
                "Dragging the revealed card did not animate its three layers while retaining selection lift.");
            tilt.OnPointerUp(pointer);
            focus.OnDeselect(pointer);
            opening.AdvanceResults(.15f);
            tilt.AdvancePose(1f, 2f);
            Require(Quaternion.Angle(face.transform.localRotation, Quaternion.identity) < .1f &&
                Mathf.Approximately(portrait.localScale.x, 1f), "The tilted card did not reset after release.");
            opening.Hide();
            Debug.Log("[VERIFY OVERFRAME PACK] Captions, flip lift, drag parallax, selection and release passed.");
            VerifyPackRarityReveals(opening, parent.Find("Screen_PackOpening/Results"));
        }

        private static void VerifyPackRarityReveals(PackOpeningView opening, Transform results)
        {
            const uint macroCosmos = 30241314, acidGolem = 23995346, sparkman = 20721928;
            opening.Show(DuelUiKit.WhiteSprite, "팩 등급 검증", new IReadOnlyList<PackPull>[]
            {
                new[] { new PackPull(BlueEyes, false, 1), new PackPull(macroCosmos, false, 1),
                    new PackPull(acidGolem, false, 1), new PackPull(sparkman, false, 1) },
                new[] { new PackPull(sparkman, false, 2) },
            });
            opening.Back();
            opening.AdvanceResults(1f);
            Canvas.ForceUpdateCanvases();

            var cards = results.Find("Cards");
            var slots = Enumerable.Range(0, 4).Select(index => cards.Find($"Card_{index}")).ToArray();
            var rims = slots.Select(slot => slot.Find("Select/Face/View/AspectBox/RarityRim")
                .GetComponent<PackPullRarityGraphic>()).ToArray();
            var summary = results.Find("Summary/Text").GetComponent<Text>();
            Require(rims.All(rim => !rim.gameObject.activeSelf) && !summary.text.Contains("UR"),
                "A face-down pack result exposed the rare card or its effect.");

            using var mesh = new VertexHelper();
            var populate = typeof(PackPullRarityGraphic).GetMethod("OnPopulateMesh",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            int CountVertices(PackPullRarityGraphic rim)
            {
                populate.Invoke(rim, new object[] { mesh });
                return mesh.currentVertCount;
            }

            slots[0].GetComponentInChildren<Button>().onClick.Invoke();
            opening.AdvanceResults(.19f);
            int revealVertices = CountVertices(rims[0]);
            Require(rims[0].isActiveAndEnabled && !rims[1].isActiveAndEnabled &&
                slots[0].Find("Rarity/Text").GetComponent<Text>().text == "UR",
                "Flipping the UR card did not show its own result badge and highlight.");
            opening.AdvanceResults(1f);
            int steadyVertices = CountVertices(rims[0]);
            Require(steadyVertices > 0 && revealVertices > steadyVertices,
                "The UR reveal did not leave a steady frame after its expanding rim.");

            slots[1].GetComponentInChildren<Button>().onClick.Invoke();
            opening.AdvanceResults(.18f);
            Require(rims[1].isActiveAndEnabled && CountVertices(rims[1]) > steadyVertices,
                "Flipping an SR card did not show its own highlight animation.");
            opening.Back();
            Require(!rims[2].isActiveAndEnabled && !rims[3].isActiveAndEnabled &&
                CountVertices(rims[1]) == steadyVertices && summary.text.Contains("UR 1장") && summary.text.Contains("SR 1장"),
                "Fast reveal did not settle the effects or report the correct rarity totals.");

            opening.ChangePage(1);
            opening.Back();
            Require(rims.All(rim => !rim.isActiveAndEnabled), "Changing packs kept a previous rare card's highlight.");
            opening.Back();
            Require(!summary.text.Contains("UR") && !summary.text.Contains("SR"), "The next pack reused the earlier rarity totals.");
            opening.ChangePage(-1);
            Require(rims[0].isActiveAndEnabled && CountVertices(rims[0]) == steadyVertices && summary.text.Contains("UR 1장"),
                "Returning to a revealed pack did not restore its steady highlights and totals.");
            opening.Hide();
            Debug.Log("[VERIFY PACK RARITY] Individual reveals, persistent UR/SR frames, fast reveal, and pack navigation passed.");
        }

        private static void VerifyPhysicalDeckAppearances()
        {
            const uint Sparkman = 20721928;
            var deck = new UserDeck();
            for (int i = 0; i < 40; i++)
                deck.InsertCard(deck.main, i, Sparkman, new DeckCardAppearance { overframe = i % 2 == 0 });
            var opponent = Enumerable.Repeat(Sparkman, 40).ToArray();
            foreach (bool special in new[] { false, true })
            {
                using var session = new DuelSession();
                IReadOnlyList<DeckCardAppearance?> drawn = null;
                CardMoveEvent summon = null;
                session.OnStyledCardsDrawn += (player, _, editions) =>
                {
                    if (player == session.HumanPlayerIndex) drawn = editions;
                };
                session.OnCardMoved += move =>
                {
                    if (move.toLoc == YgoConstants.LOCATION_MZONE) summon = move;
                };
                Require(session.StartNewDuel(deck.main, deck.extra, opponent, Array.Empty<uint>(),
                    seed: new ulong[] { 718, 391, 27182, 31 }, playerDeckAppearance: deck),
                    "Could not start the physical card edition fixture.");
                if (session.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_CHAIN)
                    session.SubmitResponseInt(-1);
                var hand = session.Players[session.HumanPlayerIndex].hand;
                int index = session.ActivePrompt.summonable.FindIndex(option =>
                    hand[(int)option.sequence].Appearance?.overframe == special);
                Require(index >= 0 && hand.All(card => card.code == Sparkman) &&
                    drawn != null && drawn.SequenceEqual(hand.Select(card => card.Appearance)),
                    "Shuffling or drawing lost the edition of identical card codes.");
                session.SubmitResponseInt(index << 16);
                if (session.ActivePrompt?.messageType == YgoConstants.MSG_SELECT_PLACE)
                    session.SubmitResponse(new byte[] { (byte)session.HumanPlayerIndex, (byte)YgoConstants.LOCATION_MZONE, 2 });
                Require(summon?.Appearance?.overframe == special &&
                    session.Players[session.HumanPlayerIndex].mzone.Any(card => card?.Appearance?.overframe == special) &&
                    session.RetryCount == 0, "The move or resulting field card inherited the edition of another copy.");
            }
            Debug.Log("[VERIFY PHYSICAL CARDS] Regular and special copies retained separate identities through shuffle, draw and summon.");
        }

        private static void VerifyPackagedOverframe(byte[] image)
        {
            string freshApp = Path.Combine(Path.GetTempPath(), "ygo-packaged-visuals-" + Guid.NewGuid().ToString("N"));
            string visuals = Path.Combine(freshApp, "DuelVisuals");
            string cutIns = Path.Combine(visuals, "CutIns");
            Directory.CreateDirectory(cutIns);
            string portrait = Path.Combine(cutIns, $"{BlueEyes}-0.png");
            string alternatePortrait = Path.Combine(cutIns, $"{BlueEyes}-1.png");
            string pack = Path.Combine(visuals, "cardart.pack");
            string sourcePack = Path.Combine(visuals, "overframeart.pack");
            File.WriteAllBytes(portrait, image);
            File.WriteAllBytes(alternatePortrait, image);
            using (var writer = new BinaryWriter(File.Create(pack)))
            {
                writer.Write(0x5241444Du);
                writer.Write(1u);
                var artworks = new[] { (BlueEyes, 0), (BlueEyes, 1), (DominusImpulse, DominusIllustration) };
                writer.Write((uint)artworks.Length);
                int offset = 12 + 16 * artworks.Length;
                foreach (var (code, variant) in artworks)
                {
                    writer.Write(code);
                    writer.Write(variant);
                    writer.Write((uint)offset);
                    writer.Write((uint)image.Length);
                    offset += image.Length;
                }
                foreach (var artwork in artworks) writer.Write(image);
            }

            string previous = CardArtLoader.ActiveDuelVisualsPath;
            const string artworkKey = "DuelArtwork.89631139";
            bool hadPreference = PlayerPrefs.HasKey(artworkKey);
            int previousPreference = PlayerPrefs.GetInt(artworkKey);
            string familyKey = $"DuelArtwork.Family.{BlueEyes}";
            string previousFamily = PlayerPrefs.HasKey(familyKey) ? PlayerPrefs.GetString(familyKey) : null;
            PlayerPrefs.DeleteKey(familyKey);
            var canvas = new GameObject("Fresh install CutIn", typeof(Canvas));
            CardArtLoader.Shutdown();
            CardArtLoader.Initialize(freshApp, Path.Combine(freshApp, "EmptyProfile"));
            try
            {
                CardArtLoader.SetDuelArtworkVariant(BlueEyes, 0);
                var view = CompositeCardView.Create(canvas.transform, "Portrait");
                view.EnableOverframe();
                view.SetCard(BlueEyes);
                Require(CardArtLoader.GetArtworkSource(BlueEyes, 0) == (BlueEyes, 0) &&
                    view.HasOverframe && view.ArtworkSprite.texture.width == 8 &&
                    view.transform.Find("AspectBox/Overframe/PortraitBounds/Depth/LocalCutIn").GetComponent<Image>().sprite.texture.width == 8,
                    "An empty app profile could not use the artwork and cut-in from packaged StreamingAssets.");
                Debug.Log("[VERIFY PACKAGED OVERFRAME] Fresh install loaded its matching artwork and portrait from the IPA resource layout.");

                var detail = CompositeCardView.Create(canvas.transform, "Details", detailedOverlay: true);
                var hand = CompositeCardView.Create(canvas.transform, "Hand");
                CardArtLoader.SetDuelArtworkVariant(BlueEyes, 1);
                detail.SetCard(BlueEyes);
                hand.SetCard(BlueEyes);
                Require(detail.ArtworkSprite == hand.ArtworkSprite &&
                    detail.transform.Find("AspectBox/Frame").GetComponent<Image>().sprite ==
                    hand.transform.Find("AspectBox/Frame").GetComponent<Image>().sprite &&
                    detail.ArtworkSprite != view.ArtworkSprite,
                    "The card gallery and the duel used different default artwork or frames.");
                hand.SetDeckCard(BlueEyes, new DeckCardAppearance { artwork = 1 });
                Require(hand.ArtworkSprite == view.ArtworkSprite,
                    "The shared card face overrode the artwork selected for a specific copy.");
                Debug.Log("[VERIFY COMMON CARD FACE] Compact and detailed views share artwork and frames and honor explicit printings.");
                VerifyDuelArtworkChoice(canvas.GetComponent<Canvas>(), view.ArtworkSprite, detail.ArtworkSprite);
                VerifySourceOverframeFace(canvas.GetComponent<Canvas>(), freshApp, sourcePack);
            }
            finally
            {
                if (hadPreference) PlayerPrefs.SetInt(artworkKey, previousPreference);
                else PlayerPrefs.DeleteKey(artworkKey);
                if (previousFamily == null) PlayerPrefs.DeleteKey(familyKey);
                else PlayerPrefs.SetString(familyKey, previousFamily);
                PlayerPrefs.Save();
                UnityEngine.Object.DestroyImmediate(canvas);
                CardArtLoader.Shutdown();
                CardArtLoader.Initialize(Application.streamingAssetsPath, previous);
                File.Delete(portrait);
                File.Delete(alternatePortrait);
                File.Delete(pack);
                File.Delete(sourcePack);
                Directory.Delete(cutIns);
                Directory.Delete(visuals);
                Directory.Delete(freshApp);
            }
        }

        private static void VerifySourceOverframeFace(Canvas canvas, string streaming, string sourcePack)
        {
            var mask = new Texture2D(4, 8, TextureFormat.RGBA32, false);
            var rgbUnderMask = new Color32(213, 68, 147, 0);
            mask.SetPixels32(Enumerable.Repeat(rgbUnderMask, 32).ToArray());
            mask.Apply();
            byte[] image = mask.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(mask);
            using (var writer = new BinaryWriter(File.Create(sourcePack)))
            {
                writer.Write(0x464F444Du);
                writer.Write(1u);
                writer.Write(1u);
                writer.Write(DominusImpulse);
                writer.Write(DominusIllustration);
                writer.Write(28u);
                writer.Write((uint)image.Length);
                writer.Write(image);
            }

            CardArtLoader.Shutdown();
            CardArtLoader.Initialize(streaming, Path.Combine(streaming, "EmptyProfile"));
            Require(CardArtLoader.SupportsOverframe(DominusImpulse, DominusIllustration) &&
                CardArtLoader.FirstOverframeVariant(DominusImpulse) == DominusIllustration &&
                !CardArtLoader.SupportsOverframe(BlueEyes) && DuelVisualAssets.CanDisplayCutIn(BlueEyes, 0),
                "The source registry did not keep the Overframe face and the summon portrait independent.");

            var deck = new UserDeck();
            var edition = new DeckCardAppearance { overframe = true, artwork = DominusIllustration + 1 };
            for (int i = 0; i < 40; i++) deck.InsertCard(deck.main, i, DominusImpulse, edition);
            using var session = new DuelSession();
            Require(session.StartNewDuel(deck.main, deck.extra, Enumerable.Repeat(SkullRedBird, 40).ToArray(),
                Array.Empty<uint>(), playerDeckAppearance: deck), "Could not start the source Overframe fixture.");
            var ui = new DuelFieldUI(null, canvas.transform, new DuelPromptModals(canvas.transform, null, null));
            ui.BindSession(session, null, null);
            ui.Refresh();
            var hand = ui.Root.transform.Find("PlayerHand").GetComponentInChildren<CompositeCardView>();
            var details = CompositeCardView.Create(canvas.transform, "SourceEdition", detailedOverlay: true);
            details.UsePrintedFaceOnly();
            details.SetDeckCard(DominusImpulse, edition);
            var front = hand.transform.Find("AspectBox/OverframeCardFace").GetComponent<Image>();
            Require(front.enabled && !front.preserveAspect && hand.ArtworkSprite == details.ArtworkSprite &&
                !hand.HasOverframe && hand.transform.Find("AspectBox/DeckEdition") == null &&
                ((Color32)front.sprite.texture.GetPixel(0, 0)).Equals(new Color32(213, 68, 147, 255)),
                "The hand or editor did not display the original colors under the transparent render mask.");
            var detailFace = details.transform.Find("AspectBox/OverframeCardFace");
            var layers = detailFace.parent;
            Require(detailFace.GetSiblingIndex() > layers.Find("CardTitle").GetSiblingIndex() &&
                detailFace.GetSiblingIndex() > layers.Find("Rules").GetSiblingIndex() &&
                detailFace.GetSiblingIndex() < layers.Find("CardBack").GetSiblingIndex(),
                "The complete source face drew behind the composed card labels or above the sleeve.");

            details.UsePrintedFaceOnly(regularEditionOnly: true);
            details.SetDeckCard(DominusImpulse, edition);
            Require(details.Appearance?.overframe == true && details.ArtworkSprite != front.sprite &&
                details.ArtworkSprite != CardArtLoader.GetFallbackArt() &&
                !detailFace.GetComponent<Image>().enabled && !layers.Find("DeckEdition").gameObject.activeSelf &&
                layers.Find("Frame").GetComponent<Image>().enabled,
                "The regular preview still exposed a packaged full Overframe face or its edition badge.");
            details.UsePrintedFaceOnly();
            details.SetDeckCard(DominusImpulse, edition);
            Require(details.ArtworkSprite == front.sprite && detailFace.GetComponent<Image>().enabled,
                "Reusing the preview left the next source Overframe hidden.");

            details.SetDeckCard(DominusImpulse, default(DeckCardAppearance));
            Require(details.ArtworkSprite != front.sprite &&
                details.transform.Find("AspectBox/Frame").GetComponent<Image>().enabled,
                "Switching to the regular edition kept the Overframe front.");
            details.SetDeckCard(DominusImpulse, edition, faceDown: true);
            Require(!details.transform.Find("AspectBox/OverframeCardFace").GetComponent<Image>().enabled,
                "Turning over the card did not conceal its front.");
            ui.UnbindSession();
            Debug.Log("[VERIFY SOURCE OVERFRAME] Packaged registry, printed hand and detail faces, original RGB and hidden cards passed.");
        }

        private static void VerifyDuelArtworkChoice(Canvas canvas, Sprite original, Sprite alternate)
        {
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(1600f, 900f);
            var fixture = new UserDeck();
            var appearance = new DeckCardAppearance { artwork = 1, overframe = true };
            for (int i = 0; i < 40; i++) fixture.InsertCard(fixture.main, i, BlueEyes, appearance);
            using var session = new DuelSession();
            Require(session.StartNewDuel(fixture.main, fixture.extra, Enumerable.Repeat(SkullRedBird, 40).ToArray(),
                Array.Empty<uint>(), playerDeckAppearance: fixture), "Could not start the artwork preference fixture.");
            session.Players[session.HumanPlayerIndex].mzone[2] = new LiveCardState
            {
                code = BlueEyes, controller = (byte)session.HumanPlayerIndex, location = YgoConstants.LOCATION_MZONE,
                sequence = 2, position = YgoConstants.POS_FACEUP_ATTACK, Appearance = appearance,
            };
            var ui = new DuelFieldUI(null, canvas.transform, new DuelPromptModals(canvas.transform, null, null));
            ui.BindSession(session, null, null);
            ui.Refresh();
            typeof(DuelFieldUI).GetMethod("ShowCardZoomModal", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(LiveCardState) }, null).Invoke(ui,
                new object[] { session.Players[session.HumanPlayerIndex].hand[0] });
            var zoom = ui.Root.transform.Find("Overlay_CardZoom/ZoomWin");
            var option = zoom.Find("ArtworkOptions/Next/Button").GetComponent<Button>();
            var enlarged = zoom.Find("CardHolder/ZoomCard").GetComponent<CompositeCardView>();
            Require(option.gameObject.activeInHierarchy, "A deck with an existing artwork assignment could not open the artwork picker.");
            Require(enlarged.ArtworkSprite == original, "The opened duel card did not show its existing assignment.");
            option.onClick.Invoke();
            var hand = ui.Root.transform.Find("PlayerHand").GetComponentInChildren<CompositeCardView>();
            var field = ui.Root.transform.Find("DuelBoard/Zone_4_3/Card/CardView").GetComponent<CompositeCardView>();
            Require(enlarged.ArtworkSprite == alternate && hand.ArtworkSprite == alternate && field.ArtworkSprite == alternate &&
                !hand.HasOverframe && !field.HasOverframe && hand.CurrentCode == BlueEyes && field.CurrentCode == BlueEyes,
                "The selected artwork was not immediately shared by the duel detail, hand and field.");
            ui.UnbindSession();

            var animator = CardAnimationManager.Ensure(canvas);
            var pose = new CardPose(Vector2.zero, new Vector2(96f, 139f));
            animator.QueueMove(new CardFlight(BlueEyes, pose, pose, true, false, appearance),
                CardMotion.SpecialSummon, null, wideCutIn: true);
            var queue = (Queue<IEnumerator>)typeof(CardAnimationManager).GetField("_steps",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(animator);
            Require(queue.Dequeue().MoveNext(), "The selected artwork had no queued summon.");
            var stage = animator.GetComponentInChildren<DuelCutInCinematic>();
            var exact = DuelVisualAssets.RetainCutIn(BlueEyes, 1);
            Require(stage != null && stage.GetComponentInChildren<RawImage>().texture == exact?.texture,
                "The summon did not use the selected illustration's own cut-in.");
            DuelVisualAssets.ReleaseCutIn(exact);
            animator.Clear();
            Debug.Log("[VERIFY ARTWORK PREFERENCE] Duel detail cycled an existing deck assignment and updated hand, field and the exact summon portrait.");
        }

        private static void VerifyOverframeAnimation(CardAnimationManager animator)
        {
            var start = new CardPose(new Vector2(-80f, -160f), new Vector2(92f, 134f));
            var end = new CardPose(new Vector2(220f, -80f), new Vector2(92f, 134f));
            var queue = (Queue<IEnumerator>)typeof(CardAnimationManager).GetField("_steps",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(animator);
            bool landed = false;
            animator.QueueMove(new CardFlight(BlueEyes, start, end, true, false), CardMotion.SpecialSummon, () => landed = true);
            var move = queue.Dequeue();
            Require(move.MoveNext() && move.Current is IEnumerator, "The ace did not play its summon reveal.");
            var reveal = (IEnumerator)move.Current;
            Require(reveal.MoveNext(), "The overframe summon did not start.");
            var enter = SampleAnimation((IEnumerator)reveal.Current);
            var view = animator.transform.Find("MovingCard");
            var fullscreen = animator.transform.Find("SummonPortrait");
            var name = animator.transform.Find("SummonName").GetComponent<CanvasGroup>();
            var stats = name.transform.Find("SummonStats").GetComponent<CanvasGroup>();
            var framing = animator.transform.Find("SummonShade/SummonFraming").GetComponent<CanvasGroup>();
            Require(view.GetComponent<CompositeCardView>().CurrentCode == 0 && view.Find("Overframe") == null,
                "The summon revealed the subject before its card was turned up.");
            Require(fullscreen != null && fullscreen.GetComponent<CanvasGroup>().alpha == 0f &&
                ((RectTransform)fullscreen).rect.height >= ((RectTransform)animator.transform).rect.height * .70f &&
                name.alpha == 0f && stats.alpha == 0f && framing.alpha == 0f,
                "The large summon subject was missing or showed before the card was public.");
            enter(.69f);
            var light = fullscreen.Find("Light").GetComponent<Image>().color;
            Require(framing.alpha > 0f && name.alpha == 0f && stats.alpha == 0f && light.r > light.g && light.g > light.b,
                "The portrait did not enter with its attribute tint before the name and stats.");
            enter(.51f);
            Canvas.ForceUpdateCanvases();
            var layer = view.Find("Overframe");
            var tilt = layer.Find("Depth");
            var fixedRect = (RectTransform)view;
            Vector2 initialSize = fixedRect.sizeDelta;
            TrackStableOverframe(layer, enter, .53f, 1f);
            Require(fixedRect.sizeDelta == initialSize && Mathf.Approximately(fixedRect.localScale.y, 1f),
                "The summoned card did not reach its display size without changing its layout.");
            Require(layer.gameObject.activeSelf && Mathf.Approximately(tilt.localScale.x, 1.25f) &&
                animator.transform.Find("ChainImpactWave").GetComponent<DuelEffectGraphic>().Tint == DuelStyle.ChainGold &&
                animator.transform.Find("Chain").GetComponent<DuelEffectGraphic>().Tint == DuelStyle.ChainGold &&
                fullscreen.GetComponent<CanvasGroup>().alpha > .95f && view.GetComponent<CanvasGroup>().alpha < .05f,
                "The summon did not produce gold feedback or enlarge the overframe at impact.");
            Require(name.alpha > .95f && stats.alpha == 0f, "The stats covered the portrait entrance.");
            Require(reveal.MoveNext(), "The summon omitted the overframe landing.");
            var settle = SampleAnimation((IEnumerator)reveal.Current);
            var statsRect = (RectTransform)stats.transform;
            float statsStartX = statsRect.anchoredPosition.x;
            settle(.31f);
            Require(stats.alpha is > 0f and < 1f && statsRect.anchoredPosition.x < statsStartX,
                "The stat strip did not slide into view during the hold.");
            settle(1f);
            Require(Mathf.Approximately(tilt.localScale.x, 1f) && stats.alpha == 1f,
                "The subject or stat strip did not settle before the card travelled to the field.");
            Require(reveal.MoveNext(), "The ace did not travel to the field.");
            TrackStableOverframe(layer, SampleAnimation((IEnumerator)reveal.Current), .05f, 1f);
            float fittedHeight = Mathf.Min(end.Size.y, end.Size.x / DuelUiKit.CardAspectRatio);
            Require(fixedRect.sizeDelta == initialSize && Mathf.Abs(fixedRect.rect.height * fixedRect.localScale.y - fittedHeight) < .1f &&
                Vector2.Distance(fixedRect.anchoredPosition, end.Position) < .1f, "The fixed summon layout missed its destination.");
            Require(!reveal.MoveNext() && !move.MoveNext() && landed && (view == null || !view.gameObject.activeSelf) &&
                animator.transform.Find("ChainImpactWave") == null && animator.transform.Find("SummonPortrait") == null &&
                animator.transform.Find("SummonShade") == null && animator.transform.Find("SummonName") == null,
                "The overframe cut-in outlived the summon.");
            Debug.Log("[VERIFY SUMMON STAGING] Attribute portrait, framing, name, stats and field handoff passed.");

            landed = false;
            Vector2 liveEnd = end.Position;
            var responsiveEnd = end.FollowLayout(() => new CardPose(liveEnd, end.Size));
            animator.QueueMove(new CardFlight(BlueEyes, start, responsiveEnd, false, false,
                new DeckCardAppearance { overframe = true }), CardMotion.SpecialSummon, () => landed = true);
            move = queue.Dequeue();
            Require(move.MoveNext() && move.Current.GetType().Name.StartsWith("<RevealSummon>", StringComparison.Ordinal),
                "An ace that left the field lost its overframe on the next summon of the same duel.");
            reveal = (IEnumerator)move.Current;
            Require(reveal.MoveNext(), "The resummon cut-in did not start.");
            SampleAnimation((IEnumerator)reveal.Current)(1f);
            Require(animator.transform.Find("MovingCard").GetComponent<CompositeCardView>().HasOverframe &&
                animator.transform.Find("ChainImpactWave") != null, "The resummoned ace did not show its subject and shockwave.");
            while (reveal.MoveNext()) SampleAnimation((IEnumerator)reveal.Current)(1f);
            Require(move.MoveNext() && landed && animator.transform.Find("ChainImpactWave") == null,
                "The field card was not published before the landing wave.");
            var landing = (IEnumerator)move.Current;
            Require(landing.MoveNext(), "The special edition did not produce a landing wave.");
            var pulse = SampleAnimation((IEnumerator)landing.Current);
            liveEnd += new Vector2(36f, 12f);
            pulse(.5f);
            Require(Vector2.Distance(((RectTransform)animator.transform.Find("OverframeLanding")).anchoredPosition, liveEnd) < .1f,
                "The wave missed the slot after the layout changed.");
            pulse(1f);
            Require(!landing.MoveNext() && !move.MoveNext() && animator.transform.Find("OverframeLanding") == null,
                "The landing wave outlived the animation.");

            animator.QueueMove(new CardFlight(BlueEyes, start, end, false, false, default(DeckCardAppearance)),
                CardMotion.SpecialSummon, null);
            move = queue.Dequeue();
            Require(move.MoveNext() && move.Current.GetType().Name.StartsWith("<RevealSummon>", StringComparison.Ordinal),
                "The regular copy lost its portrait after a special copy of the same card was summoned.");
            reveal = (IEnumerator)move.Current;
            Require(reveal.MoveNext(), "The regular copy's reveal did not start.");
            SampleAnimation((IEnumerator)reveal.Current)(1f);
            Require(animator.transform.Find("SummonPortrait").GetComponent<CanvasGroup>().alpha > .95f &&
                !animator.transform.Find("MovingCard").GetComponent<CompositeCardView>().HasOverframe,
                "The regular copy borrowed the special edition finish.");
            while (reveal.MoveNext()) SampleAnimation((IEnumerator)reveal.Current)(1f);
            Require(!move.MoveNext() && animator.transform.Find("OverframeLanding") == null,
                "The regular copy borrowed the special edition landing.");

            animator.QueueMove(new CardFlight(BlueEyes, start, end, true, true), CardMotion.SpecialSummon, null);
            move = queue.Dequeue();
            Require(move.MoveNext() && !move.Current.GetType().Name.StartsWith("<RevealSummon>", StringComparison.Ordinal) &&
                animator.transform.Find("MovingCard/Overframe") == null, "A face-down summon exposed the ace's identity.");
            Debug.Log("[VERIFY OVERFRAME RESUMMON] The same ace showed its cut-in on a second face-up summon and stayed hidden on a face-down summon.");

            animator.Clear();
            const uint baronne = 84815190;
            animator.QueueMove(new CardFlight(baronne, start, end, true, false), CardMotion.ExtraDeckSummon, null);
            move = queue.Dequeue();
            Require(move.MoveNext() && move.Current is IEnumerator, "The Synchro ace skipped its signature reveal.");
            reveal = (IEnumerator)move.Current;
            Require(reveal.MoveNext(), "The Synchro ace did not start its entrance.");
            enter = SampleAnimation((IEnumerator)reveal.Current);
            enter(.96f);
            var synchro = animator.transform.Find("Synchro").GetComponent<DuelEffectGraphic>();
            var subject = animator.transform.Find("MovingCard/Overframe").GetComponent<CanvasGroup>();
            Require(synchro.SummonFinale && subject.alpha > .9f && animator.transform.Find("SummonPeakFlash") == null,
                "The signature finale did not flow into the ace's overframe.");
            while (reveal.MoveNext()) SampleAnimation((IEnumerator)reveal.Current)(1f);
            Require(!move.MoveNext() && animator.transform.Find("Synchro") == null,
                "The signature ace cut-in left its arena overlay active.");
            Debug.Log("[VERIFY SIGNATURE OVERFRAME] The Synchro impact flowed into the supported artwork and cleared on arrival.");

            animator.Clear();
            VerifyGroupedOverframe(animator, queue);
            animator.Clear();
            animator.QueueChain(start, 9674034, 1, false, appearance: new DeckCardAppearance { overframe = true });
            var chain = queue.Dequeue();
            Require(chain.MoveNext(), "The ace activation did not play its cut-in.");
            var sample = SampleAnimation((IEnumerator)chain.Current);
            sample(.49f);
            Canvas.ForceUpdateCanvases();
            var cutIn = animator.transform.Find("MovingCard/Overframe");
            Require(cutIn != null && cutIn.Find("Depth/LocalCutIn").gameObject.activeInHierarchy,
                "The ace activation omitted its overframe subject.");
            TrackStableOverframe(cutIn, sample, .51f, .98f);
            Require(cutIn.Find("OverframeTrim").GetComponent<CardOverframeGraphic>().canvasRenderer.GetAlpha() > .4f,
                "The special edition activation did not show its frame flash.");
            animator.Clear();
            Debug.Log("[VERIFY OVERFRAME MESH] Summon and activation reused their subject and shimmer meshes while scaling.");
            VerifyStandardCutIn(animator, queue, start, end);
            VerifyOverframeActivationProjection(animator, queue, start);
            VerifySummonArtworkFallback(animator, queue, start, end);
        }

        private static void VerifySummonArtworkFallback(CardAnimationManager animator, Queue<IEnumerator> queue,
            CardPose start, CardPose end)
        {
            const uint sparkman = 20721928;
            animator.QueueMove(new CardFlight(sparkman, start, end, true, false,
                new DeckCardAppearance { overframe = true }), CardMotion.Summon, null);
            var move = queue.Dequeue();
            Require(move.MoveNext() && move.Current is IEnumerator, "The special card without a cut-in skipped its reveal.");
            var reveal = (IEnumerator)move.Current;
            Require(reveal.MoveNext(), "The special card did not enter its reveal.");
            var portrait = animator.transform.Find("SummonPortrait");
            Require(portrait != null && portrait.GetComponent<CanvasGroup>().alpha == 0f &&
                portrait.Find("ArtworkPanel/Projection/Inset/Subject").GetComponent<Image>().sprite == CardArtLoader.GetArtSprite(sparkman),
                "The fallback did not wait for the reveal or use the selected copy's artwork.");
            SampleAnimation((IEnumerator)reveal.Current)(1f);
            Require(portrait.GetComponent<CanvasGroup>().alpha > .95f, "The fallback artwork did not become visible.");
            while (reveal.MoveNext()) SampleAnimation((IEnumerator)reveal.Current)(1f);
            Require(animator.transform.Find("SummonPortrait") == null, "The projection remained over the duel after the reveal.");
            animator.Clear();
            Debug.Log("[VERIFY OVERFRAME SUMMON ART] A special edition without a portrait revealed its own artwork and cleared on arrival.");
        }

        private static void VerifyStandardCutIn(CardAnimationManager animator, Queue<IEnumerator> queue, CardPose start, CardPose end)
        {
            var flight = new CardFlight(SkullRedBird, start, end, true, false, default(DeckCardAppearance));
            foreach (var motion in new[] { CardMotion.Summon, CardMotion.SpecialSummon })
            {
                animator.QueueMove(flight, motion, null);
                var move = queue.Dequeue();
                Require(move.MoveNext() && move.Current.GetType().Name.StartsWith("<RevealSummon>", StringComparison.Ordinal),
                    $"A supported standard monster missed its {motion} cut-in after appearing in a group.");
                var reveal = (IEnumerator)move.Current;
                Require(reveal.MoveNext(), "The standard reveal did not start.");
                var portrait = animator.transform.Find("SummonPortrait").GetComponent<CanvasGroup>();
                Require(portrait.alpha == 0f, "The standard portrait appeared before the card turned up.");
                SampleAnimation((IEnumerator)reveal.Current)(1f);
                Require(portrait.alpha > .95f &&
                    !animator.transform.Find("MovingCard").GetComponent<CompositeCardView>().HasOverframe,
                    "The standard cut-in was absent or received the special card finish.");
                while (reveal.MoveNext()) SampleAnimation((IEnumerator)reveal.Current)(1f);
                Require(!move.MoveNext() && animator.transform.Find("OverframeLanding") == null,
                    "The standard copy used the special edition landing.");
            }
            animator.Clear();
            Debug.Log("[VERIFY STANDARD CUT-IN] A regular copy replayed its portrait on normal and special summons while keeping its regular finish.");
        }

        private static void VerifyOverframeActivationProjection(CardAnimationManager animator, Queue<IEnumerator> queue, CardPose start)
        {
            const uint pot = 55144522, mirrorForce = 44095762, fissure = 66788016;
            foreach (uint code in new[] { pot, mirrorForce, fissure })
            {
                animator.QueueChain(start, code, 1, code == mirrorForce, appearance: new DeckCardAppearance { overframe = true });
                var link = queue.Dequeue();
                Require(link.MoveNext(), "The spell or trap activation did not animate.");
                var lift = SampleAnimation((IEnumerator)link.Current);
                lift(.64f);
                var card = animator.transform.Find("MovingCard").GetComponent<CompositeCardView>();
                var artwork = card.transform.Find("AspectBox/Artwork");
                var foil = artwork.Find("CinematicFinish").GetComponent<CardFinishGraphic>();
                Canvas.ForceUpdateCanvases();
                int changedVertices = 0;
                foil.RegisterDirtyVerticesCallback(() => changedVertices++);
                lift(.83f);
                lift(.94f);
                var projection = animator.transform.Find("OverframeArtProjection");
                var flare = animator.transform.Find("OverframeActivation").GetComponent<DuelEffectGraphic>();
                Require(card.HasOverframe && !card.HasOverframePortrait &&
                    projection.GetComponent<CanvasGroup>().alpha > .7f &&
                    projection.Find("Artwork").GetComponent<Image>().sprite == card.ArtworkSprite &&
                    flare.Tint == DuelStyle.ChainCyan,
                    "The special spell or trap did not project the matching artwork with its own flare.");
                Require(foil.Rarity == CardRarity.UltraRare && foil.canvasRenderer.GetAlpha() > .7f && changedVertices == 0 &&
                    artwork.Find("OverframeShade").GetComponent<Image>().canvasRenderer.GetAlpha() < .35f &&
                    (code != fissure || artwork.Find("RarityFinish") == null),
                    "The edition reflection hid the source art, depended on rarity, or rebuilt its mesh during activation.");
                Require(link.MoveNext(), "The projected artwork did not return to the field.");
                SampleAnimation((IEnumerator)link.Current)(1f);
                Require(foil.canvasRenderer.GetAlpha() < .01f, "The edition reflection stayed visible on return.");
                Require(!link.MoveNext() && animator.transform.Find("OverframeArtProjection") == null &&
                    animator.transform.Find("OverframeActivation") == null, "The edition flare was not cleaned up.");
            }
            animator.QueueChain(start, 0, 1, true, appearance: new DeckCardAppearance { overframe = true });
            var concealed = queue.Dequeue();
            Require(concealed.MoveNext() && animator.transform.Find("OverframeArtProjection") == null &&
                animator.transform.Find("OverframeActivation") == null, "The projection exposed an unidentified card.");
            animator.Clear();
            Debug.Log("[VERIFY OVERFRAME ACTIVATION] Spell and trap artwork, rarity-independent foil, cyan flare and concealment passed.");
        }

        private static void TrackStableOverframe(Transform overframe, Action<float> frame, float from, float to)
        {
            var graphics = overframe.Find("Depth").GetComponentsInChildren<Graphic>();
            Require(graphics.Length > 0, "The overframe has no active subject.");
            int dirty = 0;
            void CountRebuild() => dirty++;
            foreach (var graphic in graphics) graphic.RegisterDirtyVerticesCallback(CountRebuild);
            for (int i = 0; i < 20; i++)
            {
                frame(Mathf.Lerp(from, to, i / 19f));
                Canvas.ForceUpdateCanvases();
            }
            foreach (var graphic in graphics) graphic.UnregisterDirtyVerticesCallback(CountRebuild);
            Require(dirty == 0, $"Transform animation requested {dirty} unnecessary overframe mesh rebuilds.");
        }

        private static void VerifyGroupedOverframe(CardAnimationManager animator, Queue<IEnumerator> queue)
        {
            var cardSize = new Vector2(92f, 134f);
            var targets = new[]
            {
                new CardPose(new Vector2(-210f, -110f), cardSize),
                new CardPose(new Vector2(-100f, -110f), cardSize),
                new CardPose(new Vector2(10f, -110f), cardSize),
                new CardPose(new Vector2(120f, -110f), cardSize),
            };
            uint[] cards = { BlueEyes, 9674034, SkullRedBird, 46986414 };
            int departed = 0, landed = 0, refreshed = 0;
            for (int i = 0; i < cards.Length; i++)
            {
                var from = new CardPose(new Vector2(-80f + i * 100f, -235f), cardSize);
                var flight = new CardFlight(cards[i], from, targets[i], true, i == 3,
                    new DeckCardAppearance { overframe = i != 2 });
                animator.StageFieldArrival(flight, CardMotion.SpecialSummon, 0, YgoConstants.LOCATION_MZONE, (uint)i,
                    null, () => departed++, () => landed++, () => refreshed++);
            }
            animator.CompleteFieldArrivals();
            var arrival = queue.Dequeue();
            Require(arrival.MoveNext() && arrival.Current is IEnumerator, "The simultaneous arrivals did not enter a shared cut-in.");
            var reveal = (IEnumerator)arrival.Current;
            Require(reveal.MoveNext() && departed == cards.Length && landed == 0, "The group left the origin at different times.");
            var pop = SampleAnimation((IEnumerator)reveal.Current);
            Require(animator.GetComponentsInChildren<CompositeCardView>().Count(view => view.CurrentCode == 0) == cards.Length &&
                animator.transform.Find("GroupSummonPortrait").GetComponent<CanvasGroup>().alpha == 0f,
                "A grouped card showed its identity before turning over.");
            pop(.52f);
            Canvas.ForceUpdateCanvases();
            var views = animator.GetComponentsInChildren<CompositeCardView>();
            var ace = views.Single(view => view.CurrentCode == BlueEyes);
            var excel = views.Single(view => view.CurrentCode == 9674034);
            var standard = views.Single(view => view.CurrentCode == SkullRedBird);
            var hidden = views.Single(view => view.CurrentCode == 0);
            TrackStableOverframe(ace.transform.Find("Overframe"), pop, .54f, 1f);
            TrackStableOverframe(excel.transform.Find("Overframe"), pop, .54f, 1f);
            var left = (RectTransform)ace.transform;
            var right = (RectTransform)excel.transform;
            Require(ace.HasOverframe && excel.HasOverframe && !standard.HasOverframe && !hidden.HasOverframe &&
                animator.transform.Find("GroupSummonPortrait").GetComponent<CanvasGroup>().alpha > .95f &&
                views.Length == cards.Length && left.anchoredPosition.x < right.anchoredPosition.x &&
                right.anchoredPosition.x - left.anchoredPosition.x > left.rect.width * 1.48f &&
                Mathf.Approximately(ace.transform.Find("Overframe/Depth").localScale.x, 1.25f) &&
                animator.transform.Find("ChainImpactWave").GetComponent<DuelEffectGraphic>().Tint == DuelStyle.ChainGold,
                "The grouped aces overlapped, lost their bounce, or revealed the face-down card.");
            Require(reveal.MoveNext(), "The grouped cut-in omitted the settling stage.");
            SampleAnimation((IEnumerator)reveal.Current)(1f);
            Require(!reveal.MoveNext() && animator.transform.Find("GroupSummonShade") == null &&
                animator.transform.Find("GroupSummonPortrait") == null &&
                landed == 0 && refreshed == 0, "The grouped cut-in published the field too early.");
            Require(arrival.MoveNext(), "The group omitted its movement to the field.");
            var land = SampleAnimation((IEnumerator)arrival.Current);
            land(.5f);
            Require(landed == 0 && refreshed == 0, "One card became live while the remaining group was still moving.");
            land(1f);
            Require(Vector2.Distance(left.anchoredPosition, targets[0].Position) < .1f &&
                Vector2.Distance(right.anchoredPosition, targets[1].Position) < .1f &&
                Vector2.Distance(((RectTransform)standard.transform).anchoredPosition, targets[2].Position) < .1f &&
                Vector2.Distance(((RectTransform)hidden.transform).anchoredPosition, targets[3].Position) < .1f,
                "One grouped card did not finish in its original target slot.");
            Require(arrival.MoveNext() && landed == cards.Length && refreshed == 1 &&
                animator.GetComponentsInChildren<CompositeCardView>().Length == 0,
                "The engine's group did not land together or left a visible proxy on the field.");
            var landing = (IEnumerator)arrival.Current;
            Require(landing.MoveNext(), "The group skipped the special edition landing waves.");
            var waves = animator.GetComponentsInChildren<DuelEffectGraphic>()
                .Where(effect => effect.name == "OverframeLanding").ToArray();
            Require(waves.Length == 2 && targets.Take(2).All(target =>
                waves.Any(wave => Vector2.Distance(wave.rectTransform.anchoredPosition, target.Position) < .1f)),
                "The group failed to mark the public slots selectively.");
            SampleAnimation((IEnumerator)landing.Current)(1f);
            Require(!landing.MoveNext() && !arrival.MoveNext(), "The group landing did not finish.");
            Debug.Log("[VERIFY OVERFRAME GROUP] Standard and special portraits shared the reveal, the hidden card stayed covered, and all four arrived together.");
        }

        private static void VerifyFoil(CompositeCardView card)
        {
            const uint macroCosmos = 30241314;
            const uint sparkman = 20721928;
            card.SetCard(BlueEyes);
            Canvas.ForceUpdateCanvases();
            var finish = card.GetComponentInChildren<CardFinishGraphic>();
            Require(finish != null && finish.Rarity == CardRarity.UltraRare && !finish.raycastTarget,
                "A visible UR card did not get a non-interactive foil surface.");
            using var mesh = new VertexHelper();
            var populate = typeof(CardFinishGraphic).GetMethod("OnPopulateMesh",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            populate.Invoke(finish, new object[] { mesh });
            int ultraVertices = mesh.currentVertCount;
            UIVertex reflection = default;
            Require(ultraVertices > 20, "The foil did not build its reflection and bevel mesh.");
            mesh.PopulateUIVertex(ref reflection, 9);
            float before = reflection.position.x;
            card.SetRarityHighlight(1f);
            populate.Invoke(finish, new object[] { mesh });
            mesh.PopulateUIVertex(ref reflection, 9);
            Require(reflection.position.x > before, "Highlighting the UR card did not move the foil light.");

            card.SetCard(BlueEyes, faceDown: true);
            Require(!finish.gameObject.activeSelf, "A face-down UR card exposed its foil.");
            card.SetCard(macroCosmos);
            populate.Invoke(finish, new object[] { mesh });
            Require(finish.gameObject.activeSelf && finish.Rarity == CardRarity.SuperRare && mesh.currentVertCount < ultraVertices,
                "An SR card did not replace the UR finish with its own foil.");
            card.SetCard(sparkman);
            Require(!finish.gameObject.activeSelf, "Reusing a card view left the foil on a normal card.");
            Require(card.transform.Find("AspectBox/AtkDef/Attack/Digits/Value").GetComponent<Text>().text == "1600" &&
                card.transform.Find("AspectBox/AtkDef/Defense/Digits/Value").GetComponent<Text>().text == "1400",
                "The common card face did not display printed stats outside a duel.");
        }

        private static void VerifyExpandedDuelCard(Transform parent)
        {
            const uint accesscode = 86066372, utopia = 84013237, oddEyes = 16178681;
            var card = CompositeCardView.Create(parent, "Expanded Duel Card", detailedOverlay: true);
            try
            {
                var face = card.transform.Find("AspectBox");
                var badge = face.Find("LevelInfo/Rating");
                var number = badge.Find("Number").GetComponent<Text>();
                var symbol = badge.Find("Symbol").GetComponent<Image>();
                var attack = face.Find("AtkDef/Attack/Digits/Value").GetComponent<Text>();
                var defense = face.Find("AtkDef/Defense");
                card.SetCard(accesscode);
                Require(number.text == "4" && symbol.sprite == DuelStatSymbols.Rating(DuelRatingKind.Link) &&
                    attack.text == "2300" && attack.font == DuelTypography.NumberFont && !defense.gameObject.activeSelf,
                    "The enlarged Link card did not use its Link rating and attack alone.");

                card.SetCard(utopia, live: new LiveCardState
                {
                    code = utopia, type = CardDatabase.Get(utopia).type, rank = 0,
                    position = YgoConstants.POS_FACEUP_ATTACK, attack = 2500, defense = 2000,
                });
                Require(number.text == "0" && badge.gameObject.activeSelf &&
                    symbol.sprite == DuelStatSymbols.Rating(DuelRatingKind.Rank) && defense.gameObject.activeSelf,
                    "A reused enlarged card lost Rank 0 or kept its Link layout.");

                card.SetCard(oddEyes);
                var pendulumFrame = face.Find("Frame").GetComponent<Image>();
                var printedFrame = pendulumFrame.sprite;
                var left = face.Find("LeftScale/Value").GetComponent<Text>();
                var right = face.Find("RightScale/Value").GetComponent<Text>();
                Require(left.text == "4" && right.text == "4" && left.font == DuelTypography.NumberFont &&
                    right.font == DuelTypography.NumberFont && symbol.sprite == DuelStatSymbols.Rating(DuelRatingKind.Level),
                    "The displayed Pendulum monster lost its pair of printed scales.");

                card.SetCard(oddEyes, live: new LiveCardState
                {
                    code = oddEyes, type = YgoConstants.TYPE_SPELL | YgoConstants.TYPE_PENDULUM,
                    location = YgoConstants.LOCATION_SZONE, sequence = 4, position = YgoConstants.POS_FACEUP_ATTACK,
                    lscale = 4, rscale = 7,
                });
                Require(number.text == "7" && symbol.sprite == DuelStatSymbols.Rating(DuelRatingKind.Scale) &&
                    !left.gameObject.activeInHierarchy && !right.gameObject.activeInHierarchy &&
                    !face.Find("AtkDef").gameObject.activeSelf && pendulumFrame.sprite == printedFrame,
                    "The enlarged card did not show the active scale in the right Pendulum Zone.");
                card.SetCard(0, faceDown: true);
                Require(!badge.gameObject.activeSelf && !face.Find("AtkDef").gameObject.activeSelf &&
                    face.Find("CardBack").gameObject.activeSelf,
                    "A face-down expanded card kept a live combat or rating badge.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(card.gameObject);
            }
        }

        private static void VerifySummonMotion(CompositeCardView card)
        {
            var motion = new CardMoveEvent
            {
                code = 5148778, // Fusion/Pendulum monster
                reason = YgoConstants.REASON_SPSUMMON,
                fromLoc = YgoConstants.LOCATION_EXTRA,
                fromPos = YgoConstants.POS_FACEUP_ATTACK,
            };
            Require(CardAnimationManager.SummonEffect(motion.code, DuelFieldUI.MonsterArrivalMotion(motion)).kind == DuelEffectKind.Pendulum,
                "A face-up Extra Deck Pendulum monster was shown as a Fusion Summon.");
            motion.fromPos = YgoConstants.POS_FACEDOWN_DEFENSE;
            Require(CardAnimationManager.SummonEffect(motion.code, DuelFieldUI.MonsterArrivalMotion(motion)).kind == DuelEffectKind.Fusion,
                "A Fusion Summon lost its own visual treatment.");
            motion.fromLoc = YgoConstants.LOCATION_GRAVE;
            Require(DuelFieldUI.MonsterArrivalMotion(motion) == CardMotion.SpecialSummon,
                "Reviving a Fusion monster triggered the Fusion Summon effect.");

            var from = new CardPose(new Vector2(-300f, -120f), new Vector2(90f, 130f));
            var to = new CardPose(Vector2.zero, new Vector2(220f, 319f));
            var flight = new CardFlight(BlueEyes, from, to, false, false);
            var rect = (RectTransform)card.transform;
            foreach (var kind in new[] { DuelEffectKind.Fusion, DuelEffectKind.Synchro, DuelEffectKind.Xyz, DuelEffectKind.Link })
            {
                CardAnimationManager.ApplySummonEntrance(card, flight, kind, .5f);
                Require(Vector2.Distance(rect.anchoredPosition, Vector2.Lerp(from.Position, to.Position, .5f)) < .1f &&
                    Quaternion.Angle(rect.localRotation, Quaternion.identity) < .1f,
                    "A signature summon veered away from the reveal or twisted the card.");
            }
            CardAnimationManager.ApplySummonEntrance(card, flight, DuelEffectKind.Link, 1f);
            Require(Vector2.Distance(rect.anchoredPosition, to.Position) < .1f, "The reveal did not settle at its target pose.");
        }

        private static void VerifyChainSourceIdentity()
        {
            const uint breaker = 71413901, zone = YgoConstants.LOCATION_MZONE;
            const uint faceUp = YgoConstants.POS_FACEUP_ATTACK;
            using var duel = new DuelSession();
            var first = new ChainLinkInfo { code = breaker, PublicFieldSource = ((byte)0, zone, 0u) };
            var second = new ChainLinkInfo { code = breaker, PublicFieldSource = ((byte)1, zone, 2u) };
            duel.CurrentChain.Add(first);
            duel.CurrentChain.Add(second);
            var notify = typeof(DuelSession).GetMethod("NotifyCardMoved", BindingFlags.Instance | BindingFlags.NonPublic);

            void Notify(CardMoveEvent move, CardMoveEvent partner = null) => notify.Invoke(duel, new object[] { move, partner });

            Notify(new CardMoveEvent
                {
                    code = breaker, fromCon = 0, fromLoc = zone, fromSeq = 0, fromPos = faceUp,
                    toCon = 1, toLoc = zone, toSeq = 2, toPos = faceUp,
                },
                new CardMoveEvent
                {
                    code = breaker, fromCon = 1, fromLoc = zone, fromSeq = 2, fromPos = faceUp,
                    toCon = 0, toLoc = zone, toSeq = 0, toPos = faceUp,
                });
            Require(first.PublicFieldSource == ((byte)1, zone, 2u) && second.PublicFieldSource == ((byte)0, zone, 0u),
                "Simultaneous moves assigned both chain sources to the same copy.");
            Notify(new CardMoveEvent { code = breaker, fromCon = 1, fromLoc = zone, fromSeq = 2, fromPos = faceUp,
                toCon = 1, toLoc = YgoConstants.LOCATION_GRAVE });
            Notify(new CardMoveEvent { code = breaker, fromCon = 1, fromLoc = YgoConstants.LOCATION_HAND,
                toCon = 1, toLoc = zone, toSeq = 2, toPos = faceUp });
            Require(first.PublicFieldSource == null && second.PublicFieldSource == ((byte)0, zone, 0u),
                "A departed chain source was rebound to a newly summoned copy.");
        }

        private static void VerifyFeaturedCards(CardAnimationManager animator)
        {
            var mine = new CardPose(Vector2.zero, Vector2.one);
            var opponent = new CardPose(Vector2.zero, Vector2.one, 180f);
            Require(!animator.TakeFeaturedSummon(new CardFlight(BlueEyes, mine, mine, false, true)) &&
                animator.TakeFeaturedSummon(new CardFlight(BlueEyes, mine, mine, true, false)) &&
                !animator.TakeFeaturedSummon(new CardFlight(AlternateBlueEyes, mine, mine, false, false)) &&
                animator.TakeFeaturedSummon(new CardFlight(BlueEyes, opponent, opponent, false, false)),
                "The portrait was exposed while hidden, repeated for alternate art, or suppressed for the other player.");
            animator.Clear();
            Require(animator.TakeFeaturedSummon(new CardFlight(BlueEyes, mine, mine, false, false)),
                "Starting a fresh duel did not clear the featured summons.");
        }

        private static void VerifySummonReadout(CardAnimationManager animator)
        {
            const uint accesscode = 86066372;
            var pose = new CardPose(Vector2.zero, new Vector2(160, 230));
            var (plate, _) = animator.SummonPlate(new CardFlight(accesscode, pose, pose, false, false), "링크 소환", Vector2.zero);
            try
            {
                var root = plate.transform;
                Require(root.Find("Caption/Value").GetComponent<Text>().text == "링크 소환" &&
                    root.Find("Caption/Rating/Number")?.GetComponent<Text>().text == "4" &&
                    root.Find("Caption/Rating/Symbol").GetComponent<Image>().sprite == DuelStatSymbols.Rating(DuelRatingKind.Link) &&
                    root.Find("SummonStats/Attack/Digits/Value").GetComponent<Text>().text == "2300" &&
                    !root.Find("SummonStats/Defense").gameObject.activeSelf,
                    "A Link summon omitted its rating or added a defense value.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(plate.gameObject);
            }
        }

        private static void VerifyBattleComparisonProtocol()
        {
            using var duel = new DuelSession();
            var events = new List<(BattleCardStats Attacker, BattleCardStats Defender)>();
            duel.OnBattleResolved += (attacker, defender) => events.Add((attacker, defender));
            var parse = typeof(DuelSession).GetMethod("ParseMessages", BindingFlags.Instance | BindingFlags.NonPublic);
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            void WriteComparison()
            {
                writer.Write(15u);
                writer.Write(YgoConstants.MSG_CUSTOM_MSG);
                writer.Write(0x31424759u);
                writer.Write((byte)BattleStatSource.Effect);
                writer.Write(uint.MaxValue);
                writer.Write((byte)BattleStatSource.Defense);
                writer.Write(2000u);
            }

            void WriteBattle()
            {
                writer.Write(39u);
                writer.Write(YgoConstants.MSG_BATTLE);
                for (byte player = 0; player < 2; player++)
                {
                    writer.Write(player);
                    writer.Write((byte)YgoConstants.LOCATION_MZONE);
                    writer.Write(0u);
                    writer.Write(YgoConstants.POS_FACEUP_DEFENSE);
                    writer.Write(1200);
                    writer.Write(2000);
                    writer.Write((byte)0);
                }
            }

            WriteComparison();
            writer.Write(2u);
            writer.Write(YgoConstants.MSG_CUSTOM_MSG);
            writer.Write((byte)0);
            WriteBattle();
            WriteBattle();
            WriteComparison(); // An incomplete native step cannot supply values to a later message buffer.
            parse.Invoke(duel, new object[] { stream.ToArray() });
            stream.SetLength(0);
            WriteBattle();
            parse.Invoke(duel, new object[] { stream.ToArray() });
            Require(events.Count == 3 &&
                events[0].Attacker.ComparedValue?.Source == BattleStatSource.Effect &&
                events[0].Attacker.ComparedValue?.Value == uint.MaxValue &&
                events[0].Defender.ComparedValue?.Source == BattleStatSource.Defense &&
                events[0].Defender.ComparedValue?.Value == 2000 &&
                events.All(item => item.Attacker.Attack == 1200 && item.Defender.Defense == 2000) &&
                events.Skip(1).All(item => item.Attacker.ComparedValue == null && item.Defender.ComparedValue == null),
                "Battle comparison data leaked between attacks or replaced their original stats.");
        }

        private static void VerifyBattleVisibility(CardAnimationManager animator)
        {
            var link = new BattleCardStats(1, YgoConstants.LOCATION_MZONE, 0, YgoConstants.POS_FACEUP_ATTACK, 2300, 0);
            var hidden = new BattleCardStats(0, YgoConstants.LOCATION_MZONE, 2, YgoConstants.POS_FACEDOWN_DEFENSE, 800, 2000);
            var direct = animator.CreateBattleReadout(DuelFieldUI.BattleStatsArea, link, default, 0, true, false);
            var unrevealed = animator.CreateBattleReadout(DuelFieldUI.BattleStatsArea, link, hidden, 0, true, false);
            try
            {
                Require(direct.transform.Find("Attacker/Role/Text").GetComponent<Text>().text == "상대 직접 공격" &&
                    !direct.transform.Find("Attacker/Values/Defense").gameObject.activeSelf && direct.transform.Find("Defender") == null,
                    "A direct attack showed a defender or a Link defense value.");
                Require(unrevealed.transform.Find("Attacker/Role/Text").GetComponent<Text>().text == "상대 공격" &&
                    unrevealed.transform.Find("Defender") == null && unrevealed.alpha == 0f && !unrevealed.blocksRaycasts &&
                    Array.TrueForAll(unrevealed.GetComponentsInChildren<Graphic>(), graphic => !graphic.raycastTarget),
                    "The queued readout exposed a hidden defender, claimed an attack was direct, or intercepted input.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(direct.gameObject);
                UnityEngine.Object.DestroyImmediate(unrevealed.gameObject);
            }
        }

        private static void VerifyLinkDirectAttack(CardAnimationManager animator)
        {
            const uint accesscode = 86066372;
            var link = new BattleCardStats(1, YgoConstants.LOCATION_MZONE, 0, YgoConstants.POS_FACEUP_ATTACK, 5300, 0);
            var card = new DuelCardReadout(CardDatabase.Get(accesscode), battleStats: link);
            var start = new CardPose(new Vector2(-80f, 90f), new Vector2(76f, 110f), 180f);
            var flight = new CardFlight(accesscode, start, start, false, false);
            var comparison = animator.CreateBattleReadout(DuelFieldUI.BattleStatsArea, link, default, 0, true, false);
            int arrivals = 0;
            animator.QueueAttack(flight, new Vector2(100f, -280f), true, true, comparison, card,
                new Vector2(120f, 115f), () => arrivals++);
            VerifyAttackFlight(animator, "5300", null, "4");
            Require(arrivals == 1, "The direct attack did not finish once.");
        }

        private static void VerifyAttackSpacing(CardAnimationManager animator)
        {
            const uint wagon = 34496660, mysticalElf = 15025844;
            foreach (var (start, end, opponent) in new[]
            {
                (new Vector2(48f, -70f), new Vector2(48f, 32f), false),
                (new Vector2(-182f, 62f), new Vector2(20f, -61f), true),
            })
            {
                animator.Clear();
                var resting = new CardPose(opponent ? start : start - Vector2.up * 7f, new Vector2(70f, 102f), opponent ? 90f : 0f);
                var from = new CardPose(start, resting.Size * (opponent ? 1f : 1.05f), resting.Angle);
                var defender = new CardPose(end, new Vector2(70f, 102f), -90f);
                var targetArt = DuelUiKit.CreateRect("Target Card", animator.transform, Vector2.one * .5f,
                    Vector2.one * .5f, Vector2.zero, Vector2.zero);
                targetArt.anchoredPosition = end;
                targetArt.sizeDelta = defender.Size;
                targetArt.localRotation = Quaternion.Euler(0f, 0f, defender.Angle);

                var sourceCard = CardDatabase.Get(opponent ? wagon : BlueEyes);
                byte player = opponent ? (byte)1 : (byte)0;
                var attack = new BattleCardStats(player, YgoConstants.LOCATION_MZONE, 1,
                    opponent ? YgoConstants.POS_FACEUP_DEFENSE : YgoConstants.POS_FACEUP_ATTACK, sourceCard.atk, sourceCard.def);
                var defense = new BattleCardStats((byte)(1 - player), YgoConstants.LOCATION_MZONE, 1,
                    YgoConstants.POS_FACEUP_DEFENSE, 800, 2000);
                var target = new DuelCardReadout(CardDatabase.Get(mysticalElf), battleStats: defense);
                var comparison = animator.CreateBattleReadout(DuelFieldUI.BattleStatsArea, attack, defense, 0, false, false);
                var queued = new CardPose(start - Vector2.one * 14f, from.Size, from.Angle);
                var flight = new CardFlight(sourceCard.code, queued, resting, false, false);
                var size = new Vector2(118f, opponent ? 108f : 113f);
                int arrivals = 0;
                animator.QueueAttack(flight, end + Vector2.one * 24f, false, opponent, comparison,
                    new DuelCardReadout(sourceCard, battleStats: attack), size, () => arrivals++,
                    () => (from, size, defender,
                        animator.CreateBattleCardReadout("DefendingCardReadout", target, !opponent, end, new Vector2(110f, 100f))));

                bool firstFrame = true;
                VerifyAttackFlight(animator, sourceCard.atk.ToString(), sourceCard.def.ToString(),
                    sourceCard.level.ToString(), () =>
                    {
                        var art = (RectTransform)animator.transform.Find("MovingCard");
                        if (firstFrame)
                        {
                            Require(art.anchoredPosition == from.Position, "The attack skipped the current displayed pose.");
                            var labels = (RectTransform)animator.transform.Find("AttackingCardReadout/CardReadout");
                            Require(opponent || labels.anchoredPosition.y < 0f,
                                "The raised card's readout did not keep its clearance on the first frame.");
                            firstFrame = false;
                        }
                        Require(!Bounds(art).Overlaps(Bounds(targetArt)), "The raised attack covered the target card.");
                        var impact = (RectTransform)animator.transform.Find("Strike");
                        Require(impact == null || impact.anchoredPosition == end,
                            "The impact used the old queued point instead of the defender's current position.");
                    });
                Require(arrivals == 1, "The spaced attack did not finish once.");
            }
            Debug.Log("[VERIFY DUEL ATTACK SPACING] A focused adjacent attack and a diagonal defense-position attack kept both cards and stat plates separate through windup, contact and return.");
        }

        private static void VerifyAttackAfterLayoutChange(CardAnimationManager animator)
        {
            animator.Clear();
            var attackerPose = new CardPose(new Vector2(-155f, -64f), new Vector2(68f, 99f));
            var defenderPose = new CardPose(new Vector2(95f, 70f), new Vector2(68f, 99f), 90f);
            var attackArea = new Vector2(112f, 105f);
            var defenseArea = new Vector2(106f, 100f);
            Vector2 scale = Vector2.one, offset = Vector2.zero;
            float artScale = 1f;
            CardPose ReadPose(CardPose pose) =>
                new(Vector2.Scale(pose.Position, scale) + offset, pose.Size * artScale, pose.Angle);
            var attacker = attackerPose.FollowLayout(() => ReadPose(attackerPose));
            var defender = defenderPose.FollowLayout(() => ReadPose(defenderPose));
            Vector2 ReadHit() => ReadPose(defenderPose).Position;
            var steps = (Queue<IEnumerator>)typeof(CardAnimationManager)
                .GetField("_steps", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(animator);

            animator.QueueAttackArrow(attacker, ReadHit, false);
            var arrow = steps.Dequeue();
            Require(arrow.MoveNext(), "The queued attack arrow did not begin.");
            var arrowFrame = SampleAnimation((IEnumerator)arrow.Current);
            offset = new Vector2(76f, -15f);
            arrowFrame(.66f);
            var track = (RectTransform)animator.transform.Find("AttackPath");
            Require(Vector2.Distance(track.anchoredPosition, (ReadPose(attackerPose).Position + ReadHit()) * .5f) < .08f,
                "The declared attack arrow stayed behind after moving both field anchors.");
            arrowFrame(1f);
            Require(!arrow.MoveNext(), "The responsive attack arrow did not finish.");
            offset = Vector2.zero;

            const uint mysticalElf = 15025844;
            var attack = new BattleCardStats(0, YgoConstants.LOCATION_MZONE, 1, YgoConstants.POS_FACEUP_ATTACK, 3000, 2500);
            var defense = new BattleCardStats(1, YgoConstants.LOCATION_MZONE, 1, YgoConstants.POS_FACEUP_DEFENSE, 800, 2000);
            var comparison = animator.CreateBattleReadout(DuelFieldUI.MobileBattleStatsArea, attack, defense, 0, false, false, mobileLayout: true);
            var attackCard = new DuelCardReadout(CardDatabase.Get(BlueEyes), battleStats: attack);
            var defenseCard = new DuelCardReadout(CardDatabase.Get(mysticalElf), battleStats: defense);
            var flight = new CardFlight(BlueEyes, attacker, attacker, false, false);
            int arrivals = 0;
            animator.QueueAttack(flight, ReadHit(), false, false, comparison, attackCard, attackArea, () => arrivals++,
                () => (attacker, attackArea, defender, animator.CreateBattleCardReadout("DefendingCardReadout",
                    defenseCard, true, defenderPose.Position, defenseArea)),
                () => (ReadHit(), Vector2.Scale(attackArea, scale), Vector2.Scale(defenseArea, scale)));
            var step = steps.Dequeue();
            Require(step.MoveNext(), "The responsive attack did not begin.");
            var windup = SampleAnimation((IEnumerator)step.Current);
            scale = new Vector2(.79f, .94f);
            offset = new Vector2(-66f, 28f);
            artScale = .86f;
            windup(0f);
            var art = (RectTransform)animator.transform.Find("MovingCard");
            var attackingStats = (RectTransform)animator.transform.Find("AttackingCardReadout");
            var defendingStats = (RectTransform)animator.transform.Find("DefendingCardReadout");
            Require(Vector2.Distance(art.anchoredPosition, ReadPose(attackerPose).Position) < .08f &&
                Vector2.Distance(attackingStats.sizeDelta, Vector2.Scale(attackArea, scale)) < .08f &&
                Vector2.Distance(defendingStats.anchoredPosition, ReadHit()) < .08f &&
                Vector2.Distance(defendingStats.sizeDelta, Vector2.Scale(defenseArea, scale)) < .08f,
                "The attack path or one of its stat plates kept the old position or width.");
            windup(1f);
            Require(step.MoveNext(), "The resized attack did not reach its target.");
            SampleAnimation((IEnumerator)step.Current)(1f);
            Require(!Bounds(attackingStats).Overlaps(Bounds(defendingStats)),
                "The resized lunge crossed into the defender's live readout.");
            Require(step.MoveNext(), "The resized attack did not enter its impact and return.");
            var returning = SampleAnimation((IEnumerator)step.Current);
            var impact = (RectTransform)animator.transform.Find("Strike");
            offset = new Vector2(34f, -19f);
            returning(.43f);
            Require(Vector2.Distance(impact.anchoredPosition, ReadHit()) < .08f &&
                Vector2.Distance(defendingStats.anchoredPosition, ReadHit()) < .08f &&
                defendingStats.Find("CardReadout/Combat/Defense/Digits/Value").GetComponent<Text>().text == "2000",
                "The impact or live defense value drifted after a second layout change.");
            returning(1f);
            Require(Vector2.Distance(art.anchoredPosition, ReadPose(attackerPose).Position) < .08f &&
                !step.MoveNext() && arrivals == 1, "The attacker did not return to the resized slot.");
            Debug.Log("[VERIFY DUEL RESPONSIVE ATTACK] The declared path, stat plates and impact followed layout changes through the complete attack.");
        }

        private static void VerifyLifeFeedbackRouting(CardAnimationManager animator)
        {
            animator.Clear();
            var own = DuelUiKit.CreateRect("Own LP", animator.transform.parent, new Vector2(.05f, .04f),
                new Vector2(.19f, .075f), Vector2.zero, Vector2.zero);
            var rival = DuelUiKit.CreateRect("Rival LP", animator.transform.parent, new Vector2(.77f, .93f),
                new Vector2(.93f, .965f), Vector2.zero, Vector2.zero);
            var pose = new CardPose(Vector2.zero, new Vector2(70f, 102f));
            var card = new DuelCardReadout(CardDatabase.Get(BlueEyes));
            var updates = new List<int>();
            animator.QueueAttack(new CardFlight(BlueEyes, pose, pose, false, false), new Vector2(30f, 200f),
                true, false, null, card, new Vector2(118f, 108f), null);
            animator.QueueLifeChange(own, -800, LifePointChangeKind.BattleDamage, () => updates.Add(-800));
            animator.QueueLifeChange(rival, -500, LifePointChangeKind.BattleDamage, () => updates.Add(-500));
            animator.QueueLifeChange(own, -1000, LifePointChangeKind.Payment, () => updates.Add(-1000));
            animator.QueueLifeChange(rival, -300, LifePointChangeKind.EffectDamage, () => updates.Add(-300));

            VerifyAttackFlight(animator, "3000", "2500", "8", () =>
            {
                var feedback = animator.transform.Cast<Transform>().Where(node => node.name == "LPChange").ToArray();
                if (animator.transform.Find("Strike") == null)
                {
                    Require(feedback.Length == 0 && updates.Count == 0, "LP changed before the strike.");
                    return;
                }
                Require(feedback.Length == 2 && updates.SequenceEqual(new[] { -800, -500 }),
                    "Two battle damage events did not appear together at contact.");
                CheckFeedback(feedback[0], own, "전투 피해", "-800");
                CheckFeedback(feedback[1], rival, "전투 피해", "-500");
            });
            Require(animator.transform.Find("LPChange") == null, "Battle damage remained after the strike.");
            var queue = (Queue<IEnumerator>)typeof(CardAnimationManager)
                .GetField("_steps", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(animator);
            var standalone = queue.Where(step => step.GetType().Name.StartsWith("<PlayLifeChanges>", StringComparison.Ordinal)).ToArray();
            Require(standalone.Length == 2, "An unrelated effect or payment joined the preceding attack.");
            Require(standalone[0].MoveNext(), "The LP payment did not render.");
            CheckFeedback(animator.transform.Find("LPChange"), own, "LP 지불", "-1000");
            Require(!standalone[0].MoveNext() && standalone[1].MoveNext(), "The effect damage was not shown after the payment.");
            CheckFeedback(animator.transform.Find("LPChange"), rival, "효과 피해", "-300");
            Require(!standalone[1].MoveNext() && updates.SequenceEqual(new[] { -800, -500, -1000, -300 }),
                "The displayed LP changes were applied in the wrong order.");

            void CheckFeedback(Transform feedback, RectTransform anchor, string kind, string value)
            {
                var rect = (RectTransform)feedback;
                Require(rect.anchoredPosition == animator.GetPoint(anchor, Vector2.one * .5f) &&
                    rect.sizeDelta == animator.GetAreaSize(anchor) &&
                    rect.Find("Kind/Text").GetComponent<Text>().text == kind &&
                    rect.Find("Value/Text").GetComponent<Text>().text == value &&
                    rect.GetComponentsInChildren<Graphic>().All(graphic => !graphic.raycastTarget),
                    "The LP feedback had the wrong owner, reason, amount or boundary.");
            }
            UnityEngine.Object.DestroyImmediate(own.gameObject);
            UnityEngine.Object.DestroyImmediate(rival.gameObject);
            Debug.Log("[VERIFY DUEL LP FEEDBACK] Two targets received damage at one impact. A later payment and effect used their own HUD steps.");
        }

        internal static void VerifyAttackFlight(CardAnimationManager animator, string attack, string defense, string rating,
            Action inspectFrame = null)
        {
            var steps = (Queue<IEnumerator>)typeof(CardAnimationManager)
                .GetField("_steps", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(animator);
            var step = steps.Single(candidate => candidate.GetType().Name.StartsWith("<Attack>", StringComparison.Ordinal));
            Vector2 previous = Vector2.zero;
            float unitsPerCardHeight = 0f;
            for (int stage = 0; stage < 3; stage++)
            {
                Require(step.MoveNext() && step.Current is IEnumerator, "The attack omitted an animation stage.");
                var sample = SampleAnimation((IEnumerator)step.Current);
                var art = (RectTransform)animator.transform.Find("MovingCard");
                var stats = (RectTransform)animator.transform.Find("AttackingCardReadout");
                var strip = stats.Find("CardReadout/Combat");
                float ratio = stats.sizeDelta.x / art.sizeDelta.y;
                if (stage == 0) unitsPerCardHeight = ratio;
                Require(art.gameObject.activeInHierarchy && stats.anchoredPosition == art.anchoredPosition &&
                    stats.localRotation == Quaternion.identity && Mathf.Approximately(unitsPerCardHeight, ratio) &&
                    (stage == 0 || (stats.anchoredPosition - previous).sqrMagnitude > 1f),
                    "The attacking monster's stat plate did not follow its pose.");
                Require(strip.Find("Attack/Digits/Value").GetComponent<Text>().text == attack &&
                    (defense == null ? !strip.Find("Defense").gameObject.activeSelf :
                        strip.Find("Defense/Digits/Value").GetComponent<Text>().text == defense) &&
                    stats.Find("CardReadout/Labels/Rating/Value").GetComponent<Text>().text == rating &&
                    stats.GetComponentsInChildren<Graphic>().All(graphic => !graphic.raycastTarget),
                    "The flying card omitted its current stats, rank, link or level, or blocked input.");
                previous = stats.anchoredPosition;
                var defender = (RectTransform)animator.transform.Find("DefendingCardReadout");
                var content = (RectTransform)stats.Find("CardReadout");
                for (int frame = 0; frame <= 16; frame++)
                {
                    sample(frame / 16f);
                    Require(stats.anchoredPosition == art.anchoredPosition &&
                        (defender == null || !Bounds(content).Overlaps(Bounds(defender))),
                        "The battle readouts separated from their card or overlapped each other during the attack.");
                    inspectFrame?.Invoke();
                }
            }
            Require(!step.MoveNext() && animator.transform.Find("AttackingCardReadout") == null &&
                animator.transform.Find("BattleReadout") == null && !animator.transform.Find("MovingCard").gameObject.activeSelf,
                "The attack left a transient card or stats on screen.");
        }

        internal static Action<float> SampleAnimation(IEnumerator frames)
        {
            Require(frames.MoveNext(), "The animation did not render its first frame.");
            var sample = (Action<float>)frames.GetType().GetField("apply",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(frames);
            Require(sample != null, "The renderer did not expose its interpolated frames.");
            return sample;
        }

        private static Rect Bounds(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners.Min(point => point.x), corners.Min(point => point.y),
                corners.Max(point => point.x), corners.Max(point => point.y));
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
