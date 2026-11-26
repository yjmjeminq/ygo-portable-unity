using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    internal sealed partial class TagForceWorldUI
    {
        private string RpgMapArt => _rpgConfiguration?.assetConfig?.worldMapPath ?? "TagForce/5DS/Maps/world_map_5ds.png";
        private static readonly Vector2[] RpgNodePoints =
        {
            new(.642f, .536f), new(.173f, .281f), new(.066f, .22f), new(.230f, .587f),
            new(.610f, .340f), new(.838f, .453f), new(.323f, .373f), new(.143f, .763f),
            new(.441f, .560f), new(.722f, .676f), new(.837f, .872f),
        };

        private readonly List<(Image Image, Color Color, float Phase)> _rpgMapPulses = new();
        private float _rpgMapTime;

        private void BuildRpgMap()
        {
            BuildRpgMenuBackdrop();
            _header.gameObject.SetActive(false);
            var sky = Area(_body, "Atmosphere", .26f, -.36f, 1.24f, 1.24f);
            DuelUiKit.AddImage(sky.gameObject, RpgSoftLight, new Color(.34f, .36f, .67f, .28f), false, false);
            RpgUiStyle.Display(HudLabel(Area(_body, "MapTitle", .013f, 1.018f, .108f, 1.127f), "월드맵", 35,
                new Color(.93f, .97f, 1f)));
            RpgUiStyle.Reading(HudLabel(Area(_body, "MapArea", .113f, 1.022f, .61f, 1.115f),
                IsGxCampaign ? "듀얼 아카데미아 화산섬" : "새틀라이트 · 네오 도미노 시티", 23, new Color(.76f, .83f, .94f)));
            _rpgMapTime = 0f;
            _rpgMapPulses.Clear();
            int cleared = RpgClears;
            var episodes = _rpgCampaign?.episodes ?? Array.Empty<StoryEpisodeData>();
            int target = cleared < episodes.Length ? CampaignRegionForEpisode(cleared + 1) : -1;
            BuildRpgMapLegend();
            var mapWell = Area(_body, "Atlas", .006f, .019f, .730f, .989f);
            RpgUiStyle.Glass(mapWell);
            var crop = Area(mapWell, "MapInset", .014f, .041f, .987f, .957f);
            crop.gameObject.AddComponent<RectMask2D>();
            var atlas = DuelUiKit.FillRect("MapArt", crop);
            var fit = atlas.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fit.aspectRatio = 16f / 9f;
            var art = _images.Get(RpgMapArt);
            if (art == null) Tint(atlas, new Color(.051f, .142f, .226f));
            else DuelUiKit.AddImage(atlas.gameObject, art, new Color(.90f, .94f, 1f), false, false);

            var paths = DuelUiKit.FillRect("Routes", atlas).gameObject.AddComponent<RpgWorldRoutes>();
            paths.raycastTarget = false;
            foreach (var (from, _, to, _) in CampaignRoutes)
                paths.Add(CampaignMapPoint(from), CampaignMapPoint(to), CampaignRegions[from].IsOpen(cleared) &&
                    CampaignRegions[to].IsOpen(cleared));

            var dossier = Area(_body, "RegionPreview", .746f, .019f, .993f, .989f);
            RpgUiStyle.Glass(dossier);
            var thumb = DuelUiKit.AddImage(Area(dossier, "Thumbnail", .066f, .639f, .934f, .928f).gameObject,
                null, Color.white, true, false);
            var tag = Area(dossier, "RegionState", .073f, .565f, .80f, .621f);
            RpgUiStyle.Glass(tag);
            var indicator = DuelUiKit.AddImage(Anchor(tag, "Indicator", new(.079f, .5f), Vector2.one * 13f).gameObject,
                RpgUiStyle.Glyph(RpgUiStyle.Symbol.Dot), RpgUiStyle.Cyan, true, false);
            var state = HudLabel(Area(tag, "Status", .16f, .04f, .96f, .96f), "", 16, RpgUiStyle.Cyan);
            var title = HudLabel(Area(dossier, "RegionName", .073f, .461f, .937f, .555f), "", 34, Color.white);
            RpgUiStyle.Display(title);
            Tint(Area(dossier, "Divider", .079f, .451f, .926f, .453f), new Color(.50f, .64f, .80f, .31f));
            var mainMark = DuelUiKit.AddImage(Area(dossier, "StoryMark", .074f, .387f, .13f, .429f).gameObject,
                RpgUiStyle.Glyph(RpgUiStyle.Symbol.Alert), RpgUiStyle.Gold, true, false);
            var main = HudLabel(Area(dossier, "StoryEvent", .148f, .376f, .93f, .438f), "", 19, RpgUiStyle.Gold);
            var questMark = HudLabel(Area(dossier, "RequestMark", .074f, .312f, .13f, .371f), "?", 19, RpgUiStyle.Cyan,
                align: TextAnchor.MiddleCenter, heading: true);
            var request = HudLabel(Area(dossier, "RequestEvent", .148f, .312f, .93f, .371f), "", 18, RpgUiStyle.Cyan);
            var guests = HudLabel(Area(dossier, "Residents", .08f, .147f, .928f, .301f), "", 18,
                new Color(.85f, .91f, .96f), align: TextAnchor.UpperLeft);
            RpgUiStyle.Reading(main);
            RpgUiStyle.Reading(request);
            RpgUiStyle.Reading(guests);
            guests.lineSpacing = 1.18f;
            int selected = -1;
            var travel = RpgButton(Area(dossier, "Travel", .068f, .037f, .932f, .127f), "이동하기", () =>
            {
                if (selected < 0 || !CampaignRegions[selected].IsOpen(RpgClears) || _rpgPendingRegion >= 0) return;
                if (selected == _rpgLocation) Show(Page.Field);
                else BeginRpgTravel(selected, RpgArrival);
            }, 23, primary: true, prominent: true);
            var travelText = travel.GetComponentInChildren<Text>();
            travelText.alignment = TextAnchor.MiddleLeft;
            travelText.rectTransform.anchorMin = new(.092f, .02f);
            travelText.rectTransform.anchorMax = new(.81f, .98f);
            var travelIcon = DuelUiKit.AddImage(Area(travel.transform, "TravelMark", .836f, .24f, .943f, .76f).gameObject,
                RpgUiStyle.Glyph(RpgUiStyle.Symbol.Chevron), RpgUiStyle.Cyan, true, false);

            var selections = new Image[CampaignRegions.Length];
            var pins = new Button[CampaignRegions.Length];
            var nodes = DuelUiKit.FillRect("Nodes", atlas);
            for (int i = 0; i < CampaignRegions.Length; i++)
            {
                int index = i;
                var region = CampaignRegions[i];
                bool open = region.IsOpen(cleared), current = i == _rpgLocation, goal = i == target;
                Color color = goal ? RpgUiStyle.Gold : current ? RpgUiStyle.Cyan : open
                    ? new Color32(159, 209, 234, 255) : new Color32(163, 173, 198, 255);
                var pin = Anchor(nodes, region.Id, CampaignMapPoint(i), new Vector2(71f, 66f));
                var button = pin.gameObject.AddComponent<Button>();
                pins[i] = button;
                DuelUiKit.AddImage(pin.gameObject, DuelUiKit.WhiteSprite, Color.clear, false, true);
                var pinBody = Anchor(pin, "Body", new(.5f, .5f), Vector2.one * 49f);
                DuelUiKit.AddImage(pinBody.gameObject, RpgUiStyle.Glyph(RpgUiStyle.Symbol.Hexagon),
                    new Color(.076f, .124f, .223f, .97f), false, false);
                var ring = Anchor(pin, "Ring", new(.5f, .5f), Vector2.one * 42f);
                var rim = DuelUiKit.AddImage(ring.gameObject, RpgUiStyle.Glyph(RpgUiStyle.Symbol.Ring), color, false, false);
                var glyph = goal ? RpgUiStyle.Symbol.Alert : open ? RpgUiStyle.Symbol.Dot : RpgUiStyle.Symbol.Lock;
                var sign = Anchor(pin, open ? "Symbol" : "MapLock", new(.5f, .5f), Vector2.one * (open && !goal ? 15f : 25f));
                DuelUiKit.AddImage(sign.gameObject, RpgUiStyle.Glyph(glyph), color, false, false);
                var focus = Anchor(pin, "Selection", new(.5f, .5f), Vector2.one * 55f);
                selections[i] = DuelUiKit.AddImage(focus.gameObject, RpgUiStyle.Glyph(RpgUiStyle.Symbol.Ring), Color.white, false, false);
                selections[i].enabled = false;
                if (current) AddPulse("CurrentPulse", RpgUiStyle.Cyan, 0f, 71f);
                if (goal) AddPulse("GoalPulse", RpgUiStyle.Gold, 1.5f, 62f);
                if (current && goal)
                {
                    var currentMark = Anchor(pin, "Here", new(.16f, .81f), Vector2.one * 14f);
                    DuelUiKit.AddImage(currentMark.gameObject, RpgUiStyle.Glyph(RpgUiStyle.Symbol.Dot), RpgUiStyle.Cyan, false, false);
                }
                float labelCenter = i == RpgHideout ? .76f : .5f;
                var name = HudLabel(Anchor(pin, "Name", new(labelCenter, -.120f), new Vector2(141f, 26f)), region.Name,
                    15, color, align: TextAnchor.MiddleCenter, heading: true);
                name.horizontalOverflow = HorizontalWrapMode.Overflow;
                name.resizeTextForBestFit = false;
                name.raycastTarget = false;
                var shadow = name.gameObject.AddComponent<Outline>();
                shadow.effectColor = new Color(.018f, .033f, .057f, .95f);
                shadow.effectDistance = new Vector2(1.5f, -1.5f);
                button.targetGraphic = rim;
                button.colors = new ColorBlock { normalColor = Color.white, highlightedColor = new Color(1f, 1f, 1f, .82f),
                    selectedColor = new Color(1f, 1f, 1f, .67f), pressedColor = new Color(.65f, .88f, 1f),
                    disabledColor = Color.gray, colorMultiplier = 1f, fadeDuration = .12f };
                pin.gameObject.AddComponent<TagForceCursorSound>();
                MenuButtonMotion.Attach(button, ring, color, underline: false);
                button.onClick.AddListener(() => Select(index));

                void AddPulse(string id, Color glowColor, float phase, float size)
                {
                    var glowSlot = Anchor(pin, id, new(.5f, .5f), Vector2.one * size);
                    var glow = DuelUiKit.AddImage(glowSlot.gameObject, RpgUiStyle.Glyph(RpgUiStyle.Symbol.Ring), glowColor, false, false);
                    _rpgMapPulses.Add((glow, glowColor, phase));
                }
            }
            Select(_rpgLocation, false);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(pins[_rpgLocation].gameObject);
            UpdateRpgMapPulse(0f);

            void Select(int index, bool playSound = true)
            {
                if (selected == index) return;
                if (playSound) DuelAudio.PlayTagForceSfx(TagForceSfx.Confirm);
                if (selected >= 0) selections[selected].enabled = false;
                selected = index;
                selections[selected].enabled = true;
                var region = CampaignRegions[index];
                bool open = region.IsOpen(cleared), current = index == _rpgLocation;
                _images.BeginPage();
                _images.Get(RpgMapArt);
                thumb.sprite = GetRpgFieldArt(region);
                thumb.color = thumb.sprite == null ? new Color(.08f, .17f, .23f) : open ? Color.white : new Color(.40f, .46f, .56f);
                if (thumb.sprite == null) { thumb.sprite = DuelUiKit.WhiteSprite; thumb.preserveAspect = false; }
                else thumb.preserveAspect = true;
                _images.Trim();
                title.text = region.Name;
                state.text = !open ? "잠김 · 이야기 진행 시 개방" : current ? "현재 위치" : "이동 가능";
                tag.anchorMax = new Vector2(open ? .415f : .86f, tag.anchorMax.y);
                state.color = open ? RpgUiStyle.Cyan : new Color32(185, 193, 211, 255);
                indicator.color = state.color;
                var story = index == target ? episodes[cleared] : null;
                var quests = RpgSideQuest.All.Where(item => item.Region == region.Id);
                var quest = quests.FirstOrDefault();
                var requestHere = quests.FirstOrDefault(item =>
                {
                    var status = CollectionProgress.GetRpgQuestState(item.Id);
                    return RpgPendingDeliveries(item).Any() || IsRpgQuestOpen(item, cleared) &&
                        status != RpgQuestState.Complete && (item.Id != RpgSideQuest.GarageCouplerId || status != RpgQuestState.Available);
                });
                main.text = open && story != null ? story.isCutsceneOnly ? story.title : story.opponentName : "메인 사건 없음";
                request.text = IsGxCampaign ? _rpgConfiguration.spots[index].subtitle : open && requestHere != null ? requestHere.Title : "의뢰 없음";
                main.color = open && story != null ? (Color)RpgUiStyle.Gold : new Color(.59f, .66f, .75f);
                request.color = open && requestHere != null ? (Color)RpgUiStyle.Cyan : new Color(.59f, .66f, .75f);
                mainMark.color = main.color;
                questMark.color = request.color;
                var locals = RpgLocalResidents(episodes, index, cleared, story, quest).Select(actor => actor.opponentName);
                if (quest != null && quest.Giver != story?.charId) locals = locals.Prepend(RpgQuestName(quest));
                if (HasRpgMerchant(index, cleared)) locals = locals.Append(RpgShopkeeperName);
                string residentNames = string.Join(" · ", locals.Distinct().Take(4));
                guests.text = !open ? "새 지역은 메인 이야기를 진행하면 열립니다." :
                    residentNames.Length > 0 ? $"머무는 인물\n{residentNames}" : "머무는 인물 없음";
                travel.interactable = open;
                travelText.text = !open ? "이동 잠김" : current ? "필드로 돌아가기" : "이동하기";
                travelIcon.sprite = RpgUiStyle.Glyph(open ? RpgUiStyle.Symbol.Chevron : RpgUiStyle.Symbol.Lock);
                travelIcon.color = open ? RpgUiStyle.Cyan : new Color32(162, 178, 207, 255);
                if (playSound) UiEntranceMotion.Play(dossier.gameObject, .78f, .18f);
            }
        }

        private void BuildRpgMapLegend()
        {
            _rpgNavItems[0].gameObject.SetActive(false);
            _rpgNavigation.anchorMin = new(.55f, .039f);
            _rpgNavigation.anchorMax = new(.946f, .096f);
            for (int i = 1; i < _rpgNavItems.Length; i++)
            {
                _rpgNavItems[i].anchorMin = new((i - 1) * .34f, 0f);
                _rpgNavItems[i].anchorMax = new((i - 1) * .34f + .32f, 1f);
            }
            var legend = Area(_body, "MapLegend", .012f, -.186f, .50f, -.111f);
            Add(0, "현재 위치", RpgUiStyle.Symbol.Ring, RpgUiStyle.Cyan);
            Add(1, "메인 사건", RpgUiStyle.Symbol.Alert, RpgUiStyle.Gold);
            Add(2, "이동 잠김", RpgUiStyle.Symbol.Lock, new Color32(173, 183, 205, 255));

            void Add(int index, string text, RpgUiStyle.Symbol symbol, Color color)
            {
                float left = index * .305f;
                var icon = Anchor(legend, "Mark", new(left + .024f, .5f), Vector2.one * 19f);
                DuelUiKit.AddImage(icon.gameObject, RpgUiStyle.Glyph(symbol), color, false, false);
                RpgUiStyle.Reading(HudLabel(Area(legend, "Key", left + .065f, .02f, left + .27f, .98f), text, 17, color));
            }
        }

        private void UpdateRpgMapPulse(float delta)
        {
            _rpgMapTime += delta;
            foreach (var (image, color, phase) in _rpgMapPulses)
            {
                float grow = .5f + .5f * Mathf.Sin(_rpgMapTime * 4.2f + phase);
                image.rectTransform.localScale = Vector3.one * Mathf.Lerp(.84f, 1.14f, grow);
                image.color = new Color(color.r, color.g, color.b, Mathf.Lerp(.85f, .18f, grow));
            }
        }
    }

    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class RpgWorldRoutes : MaskableGraphic
    {
        private readonly List<(Vector2 A, Vector2 B, bool Open)> _edges = new();

        internal void Add(Vector2 start, Vector2 end, bool open)
        {
            _edges.Add((start, end, open));
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect = rectTransform.rect;
            foreach (var (start, end, open) in _edges)
            {
                var a = rect.min + Vector2.Scale(start, rect.size);
                var b = rect.min + Vector2.Scale(end, rect.size);
                var normal = Vector2.Perpendicular((b - a).normalized);
                AddLine(mesh, a, b, normal * (open ? 2.4f : 1.7f), new Color32(13, 29, 54, open ? (byte)199 : (byte)142));
                if (open) AddLine(mesh, a, b, normal, new Color32(172, 220, 239, 195));
                else for (int i = 0, n = Mathf.Max(1, Mathf.RoundToInt(Vector2.Distance(a, b) / 14f)); i < n; i++)
                    AddLine(mesh, Vector2.Lerp(a, b, (float)i / n), Vector2.Lerp(a, b, (i + .56f) / n),
                        normal * .75f, new Color32(181, 200, 225, 146));
            }
        }

        private static void AddLine(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 normal, Color32 color)
        {
            int i = mesh.currentVertCount;
            mesh.AddVert(a - normal, color, Vector2.zero);
            mesh.AddVert(a + normal, color, Vector2.zero);
            mesh.AddVert(b + normal, color, Vector2.zero);
            mesh.AddVert(b - normal, color, Vector2.zero);
            mesh.AddTriangle(i, i + 1, i + 2);
            mesh.AddTriangle(i, i + 2, i + 3);
        }
    }
}
