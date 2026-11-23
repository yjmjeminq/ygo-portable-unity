using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
            foreach (uint code in new[] { BlueEyes, magician, baronne, excel, SkullRedBird })
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

                var holder = LotdUiKit.CreateRect("View", canvas.transform, new Vector2(.25f, .16f), new Vector2(.52f, .83f),
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
                Require(preview.HasOverframe, "The selected special edition preview did not show the decoded portrait.");
                typeof(DeckEditorUI).GetMethod("OpenExpandedDeckCard", instance).Invoke(deck, new object[] { BlueEyes });
                var expanded = (CompositeCardView)typeof(DeckEditorUI).GetField("_expandedDeckArt", instance).GetValue(deck);
                Require(expanded.HasOverframe, "The expanded deck viewer did not show the decoded portrait.");
                var thumbnail = CompositeCardView.Create(holder, "Compact");
                var special = new DeckCardAppearance { overframe = true };
                thumbnail.SetCard(BlueEyes, live: new LiveCardState { code = BlueEyes, Appearance = special });
                Require(thumbnail.HasOverframe && thumbnail.transform.Find("AspectBox/DeckEdition").gameObject.activeSelf,
                    "The live hand or field card did not show its extended face and edition marker.");
                thumbnail.SetReadoutFocus(1f);
                Require(thumbnail.transform.Find("AspectBox/Overframe/PortraitBounds/Depth").localScale.x > 1f,
                    "Focusing a live copy did not lift the artwork.");
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
                crafting.Hide();

                VerifyPackFaces(canvas.transform);
                VerifyOverframeAnimation(CardAnimationManager.Ensure(canvas));
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

        private static void VerifyPackFaces(Transform parent)
        {
            var opening = new PackOpeningView(parent, null);
            opening.Show(LotdUiKit.WhiteSprite, "카드 프레임 검증",
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
            string pack = Path.Combine(visuals, "cardart.pack");
            File.WriteAllBytes(portrait, image);
            using (var writer = new BinaryWriter(File.Create(pack)))
            {
                writer.Write(0x5241444Du);
                writer.Write(1u);
                writer.Write(2u);
                for (int variant = 0; variant < 2; variant++)
                {
                    writer.Write(BlueEyes);
                    writer.Write(variant);
                    writer.Write((uint)(44 + variant * image.Length));
                    writer.Write((uint)image.Length);
                }
                writer.Write(image);
                writer.Write(image);
            }

            string previous = CardArtLoader.ActiveDuelVisualsPath;
            const string artworkKey = "DuelArtwork.89631139";
            bool hadPreference = PlayerPrefs.HasKey(artworkKey);
            int previousPreference = PlayerPrefs.GetInt(artworkKey);
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
            }
            finally
            {
                if (hadPreference) PlayerPrefs.SetInt(artworkKey, previousPreference);
                else PlayerPrefs.DeleteKey(artworkKey);
                UnityEngine.Object.DestroyImmediate(canvas);
                CardArtLoader.Shutdown();
                CardArtLoader.Initialize(Application.streamingAssetsPath, previous);
                File.Delete(portrait);
                File.Delete(pack);
                Directory.Delete(cutIns);
                Directory.Delete(visuals);
                Directory.Delete(freshApp);
            }
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
            float fittedHeight = Mathf.Min(end.Size.y, end.Size.x / LotdUiKit.CardAspectRatio);
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
            Require(synchro.SummonFinale && subject.alpha > .9f && animator.transform.Find("SummonPeakFlash") != null,
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
            CardAnimationManager.ApplySummonEntrance(card, flight, DuelEffectKind.Synchro, .5f);
            Vector2 synchro = rect.anchoredPosition;
            CardAnimationManager.ApplySummonEntrance(card, flight, DuelEffectKind.Xyz, .5f);
            Vector2 xyz = rect.anchoredPosition;
            Require(synchro.y > xyz.y + 50f && xyz.x > synchro.x + 30f, "The special summons used the same entrance path.");
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
                var targetArt = LotdUiKit.CreateRect("Target Card", animator.transform, Vector2.one * .5f,
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
            var own = LotdUiKit.CreateRect("Own LP", animator.transform.parent, new Vector2(.05f, .04f),
                new Vector2(.19f, .075f), Vector2.zero, Vector2.zero);
            var rival = LotdUiKit.CreateRect("Rival LP", animator.transform.parent, new Vector2(.77f, .93f),
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
