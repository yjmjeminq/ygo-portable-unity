using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    internal sealed class BeyondDestinyField
    {
        private const string ArtRoot = "TagForce/5DS/";
        private const string WalkerPath = ArtRoot + "Characters_SD/hero.png";
        private readonly TagForceWorldImages _images = new();
        private readonly RectTransform _root, _map, _walker, _marker;
        private readonly Image _picture, _character;
        private readonly Text _title;
        private readonly Button _continue;
        private readonly List<Vector2> _path = new();
        private RpgFieldTerrain _terrain;
        private Sprite[] _poses;
        private Action _enter;
        private Vector2 _position, _destination;
        private int _step, _facing = 3;
        private float _walkTime, _clock;
        private bool _enterOnArrival;

        internal GameObject Root => _root.gameObject;
        internal bool IsOpen => Root.activeSelf;

        internal BeyondDestinyField(Transform parent)
        {
            _root = DuelUiKit.FillRect("FiveDsField", parent);
            _map = DuelUiKit.FillRect("FieldProjection", _root);
            var aspect = _map.gameObject.AddComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            aspect.aspectRatio = 16f / 9f;
            _picture = DuelUiKit.AddImage(_map.gameObject, null, Color.white, false);
            var pointer = _map.gameObject.AddComponent<EventTrigger>();
            var tap = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
            tap.callback.AddListener(data => PanToward((PointerEventData)data));
            pointer.triggers.Add(tap);

            _marker = DuelUiKit.CreateRect("StoryMarker", _map, Vector2.zero, Vector2.zero,
                new Vector2(-25f, 0f), new Vector2(25f, 45f));
            var alert = DuelUiKit.AddFittedText(_marker, "Alert", "!", 32, 16, TextAnchor.MiddleCenter,
                DuelUiKit.GoldAccent, FontStyle.Bold);
            alert.raycastTarget = false;

            _walker = DuelUiKit.CreateRect("Protagonist", _map, Vector2.zero, Vector2.zero,
                new Vector2(-48f, 0f), new Vector2(48f, 112f));
            _walker.pivot = new Vector2(.5f, 0f);
            _walker.anchoredPosition = Vector2.zero;
            _character = DuelUiKit.AddImage(_walker.gameObject, null, Color.white, true, false);
            var banner = DuelUiKit.CreateMenuPanel(_map, "Location", new Vector2(.04f, .85f), new Vector2(.64f, .96f));
            RpgUiStyle.Panel(banner);
            _title = DuelUiKit.AddFittedText(banner, "Title", "", 27, 15, TextAnchor.MiddleCenter,
                DuelUiKit.TextPrimary, FontStyle.Bold);
            var dock = DuelUiKit.CreateRect("StoryDock", _map, new Vector2(.73f, .051f), new Vector2(.96f, .138f),
                Vector2.zero, Vector2.zero);
            _continue = DuelUiKit.CreateButton(dock, "Approach", "이야기 시작", MoveToStory, 23, true);
            Close();
        }

        internal bool Open(string path, string title, Action enter)
        {
            var terrain = Terrain(path);
            if (terrain == null) return false;
            _images.BeginPage();
            _picture.sprite = _images.Get(ArtRoot + path);
            _poses = _images.GetSlices(WalkerPath, 3, 4, fitRows: true);
            _images.Trim();
            if (_picture.sprite == null || _poses?.Length != 12) return false;
            _terrain = terrain;
            _position = terrain.Ground(new Vector2(.32f, .21f));
            _destination = terrain.Ground(new Vector2(.69f, .37f));
            _step = 0;
            _facing = 3;
            _walkTime = _clock = 0f;
            _enterOnArrival = false;
            _path.Clear();
            _title.text = title;
            _enter = enter;
            Root.SetActive(true);
            Render(false);
            EventSystem.current?.SetSelectedGameObject(_continue.gameObject);
            return true;
        }

        internal void Close()
        {
            Root.SetActive(false);
            _enter = null;
            _character.sprite = _picture.sprite = null;
            _poses = null;
            _images.BeginPage();
            _images.Trim();
        }

        internal void Update(float delta, Vector2 direction)
        {
            if (!IsOpen) return;
            _clock += delta;
            Vector2 from = _position;
            if (direction.sqrMagnitude > .01f)
            {
                _path.Clear();
                _enterOnArrival = false;
                _position = _terrain.Slide(_position, _position + Vector2.ClampMagnitude(direction, 1f) * (.36f * delta));
            }
            else if (_step < _path.Count)
            {
                _position = Vector2.MoveTowards(_position, _path[_step], delta * .36f);
                if ((_position - _path[_step]).sqrMagnitude < .0000001f) ++_step;
            }
            Vector2 travelled = _position - from;
            bool moving = travelled.sqrMagnitude > .00000001f;
            if (moving)
            {
                _facing = Mathf.Abs(travelled.x) > Mathf.Abs(travelled.y)
                    ? travelled.x < 0f ? 1 : 2 : travelled.y < 0f ? 0 : 3;
                _walkTime += delta;
            }
            else _walkTime = 0f;
            Render(moving);
            if (_enterOnArrival && NearStory()) Finish();
        }

        private void Render(bool moving)
        {
            int frame = moving ? 1 + Mathf.FloorToInt(_walkTime * 7f) % 2 : 0;
            _character.sprite = _poses[_facing * 3 + frame];
            _walker.anchorMin = _walker.anchorMax = _position;
            _walker.localScale = Vector3.one * _terrain.ScaleAt(_position.y);
            _marker.anchorMin = _marker.anchorMax = _destination;
            float bob = Mathf.Sin(_clock * 4f) * 4f;
            _marker.anchoredPosition = new Vector2(0f, 54f + bob);
        }

        private bool NearStory() => (_position - _destination).sqrMagnitude < .0021f;

        internal void MoveToStory()
        {
            if (NearStory()) Finish();
            else MoveTo(_destination, enter: true);
        }

        private void MoveTo(Vector2 point, bool enter)
        {
            _terrain.BuildPath(_position, _terrain.Ground(point), _path);
            _step = 0;
            _enterOnArrival = enter;
        }

        private void PanToward(PointerEventData pointer)
        {
            if (!IsOpen || !RectTransformUtility.ScreenPointToLocalPointInRectangle(_map,
                    pointer.position, pointer.pressEventCamera, out var local)) return;
            var bounds = _map.rect;
            Vector2 point = new(Mathf.InverseLerp(bounds.xMin, bounds.xMax, local.x),
                Mathf.InverseLerp(bounds.yMin, bounds.yMax, local.y));
            bool enter = (point - _destination).sqrMagnitude < .011f;
            MoveTo(enter ? _destination : point, enter);
        }

        private void Finish()
        {
            var enter = _enter;
            Close();
            enter?.Invoke();
        }

        private static RpgFieldTerrain Terrain(string path)
        {
            (float x, float y)[] floor = path switch
            {
                "Maps/spot_neodomino_rain_alley.png" => new[] { (.14f,.08f), (.82f,.08f), (.86f,.52f), (.68f,.52f), (.29f,.31f) },
                "Maps/spot_kaiba_dome_plaza.png" => new[] { (.17f,.09f), (.94f,.09f), (.94f,.43f), (.45f,.43f), (.22f,.31f) },
                "Maps/spot_arcadia_collapse.png" => new[] { (.22f,.08f), (.70f,.08f), (.92f,.39f), (.82f,.40f), (.52f,.26f) },
                "Maps/spot_team5ds_garage.png" => new[] { (.12f,.08f), (.85f,.08f), (.86f,.34f), (.69f,.49f), (.27f,.41f) },
                "Maps/spot_night_circuit.png" => new[] { (.12f,.10f), (.81f,.10f), (.90f,.36f), (.75f,.53f), (.28f,.42f) },
                "Maps/spot_wrgp_stadium.png" => new[] { (.12f,.09f), (.84f,.09f), (.96f,.42f), (.67f,.49f), (.19f,.37f) },
                "Maps/spot_ark_cradle.png" => new[] { (.21f,.13f), (.71f,.09f), (.64f,.34f), (.74f,.43f), (.49f,.44f), (.28f,.29f) },
                _ => null,
            };
            if (floor == null) return null;
            var polygon = new Vector2[floor.Length];
            for (int index = 0; index < polygon.Length; index++)
                polygon[index] = new Vector2(floor[index].x, floor[index].y);
            return new RpgFieldTerrain(new TagForceSpot { walkPolygon = polygon, nearScale = 1.28f, farScale = .73f });
        }
    }
}
