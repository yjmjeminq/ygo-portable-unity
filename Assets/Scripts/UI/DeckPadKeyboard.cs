using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace YGOPortable.UI
{
    internal sealed class DeckPadKeyboard
    {
        private enum KeyboardPage { Hangul, Latin, Number }

        private const int Columns = 10;
        private const int Rows = 4;
        private const string HangulKeys = "ㄱㄴㄷㄹㅁㅂㅅㅇㅈㅊㅋㅌㅍㅎㄲㄸㅃㅆㅉㅏㅑㅓㅕㅗㅛㅜㅠㅡㅣㅐㅒㅔㅖㅘㅙㅚㅝㅞㅟㅢ";
        private const string LatinKeys = "abcdefghijklmnopqrstuvwxyz0123456789-._";
        private const string NumberKeys = "1234567890";
        private readonly RectTransform _overlay;
        private readonly Text _preview;
        private readonly Button[] _keys = new Button[Columns * Rows];
        private readonly Text[] _labels = new Text[Columns * Rows];
        private readonly Button _pageButton;
        private readonly Text _pageLabel;
        private readonly Button _shiftButton;
        private readonly Button _spaceButton;
        private readonly Action _onClosed;
        private readonly List<char> _hangul = new List<char>();
        private InputField _target;
        private KeyboardPage _page;
        private string _committed = "";
        private string _characters = "";
        private bool _numericOnly;
        private bool _uppercase;

        internal Transform Scope => _overlay;
        internal Selectable DefaultFocus => _keys[0];
        internal bool IsOpen => _overlay.gameObject.activeSelf;

        internal DeckPadKeyboard(Transform parent, Action onClosed = null, bool shopColors = false)
        {
            _onClosed = onClosed;
            _overlay = LotdUiKit.FillRect("PadKeyboard", parent);
            LotdUiKit.AddImage(_overlay.gameObject, LotdUiKit.WhiteSprite,
                shopColors ? PackShopStyle.Scrim : new Color(.012f, .021f, .05f, .88f));
            var min = new Vector2(.15f, .21f);
            var max = new Vector2(.85f, .79f);
            var panel = shopColors ? PackShopStyle.Panel(_overlay, "Window", min, max) :
                LotdUiKit.CreateMenuPanel(_overlay, "Window", min, max);
            var title = LotdUiKit.CreateRect("Title", panel, new Vector2(.08f, .865f), new Vector2(.92f, .957f),
                Vector2.zero, Vector2.zero);
            LotdUiKit.AddFittedText(title, "Text", "패드로 입력", 23, 14, TextAnchor.MiddleLeft,
                shopColors ? PackShopStyle.TextPrimary : LotdUiKit.CyanAccent, FontStyle.Bold);
            var preview = LotdUiKit.CreateRect("Entry", panel, new Vector2(.068f, .744f), new Vector2(.932f, .851f),
                Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(preview.gameObject, LotdUiKit.WhiteSprite,
                shopColors ? PackShopStyle.SurfaceInset : new Color(.03f, .09f, .17f));
            _preview = LotdUiKit.AddFittedText(preview, "Text", "", 25, 14, TextAnchor.MiddleLeft,
                shopColors ? PackShopStyle.TextPrimary : LotdUiKit.TextPrimary);
            _preview.rectTransform.offsetMin = new Vector2(8f, 0f);
            for (int i = 0; i < _keys.Length; i++)
            {
                float x = .068f + (i % Columns) * .087f;
                float y = .605f - (i / Columns) * .113f;
                var cell = LotdUiKit.CreateRect("Key" + i, panel, new Vector2(x, y), new Vector2(x + .080f, y + .093f),
                    Vector2.zero, Vector2.zero);
                int index = i;
                _keys[i] = CreateButton(cell, "", () => PressLetter(index), 20, false);
                _labels[i] = _keys[i].GetComponentInChildren<Text>();
            }

            Button ActionButton(string name, string label, float left, float right, Action action, bool accent = false)
            {
                var slot = LotdUiKit.CreateRect(name, panel, new Vector2(left, .137f), new Vector2(right, .237f),
                    Vector2.zero, Vector2.zero);
                return CreateButton(slot, label, action, 16, accent);
            }

            Button CreateButton(Transform slot, string label, Action action, int size, bool accent) => shopColors
                ? PackShopStyle.Button(slot, "Button", label, action, size, accent)
                : LotdUiKit.CreateLotdButton(slot, "Button", label, action, size, accent);

            _pageButton = ActionButton("Page", "", .068f, .225f, NextPage);
            _pageLabel = _pageButton.GetComponentInChildren<Text>();
            _shiftButton = ActionButton("Shift", "대소문자", .236f, .369f, ToggleCase);
            _spaceButton = ActionButton("Space", "띄어쓰기", .380f, .513f, () => Append(' '));
            ActionButton("Erase", "지우기", .524f, .657f, Erase);
            ActionButton("Cancel", "취소", .668f, .789f, Close);
            ActionButton("Apply", "입력 완료", .800f, .932f, Apply, true);
            var hint = LotdUiKit.CreateRect("Hint", panel, new Vector2(.08f, .048f), new Vector2(.92f, .118f),
                Vector2.zero, Vector2.zero);
            LotdUiKit.AddFittedText(hint, "Text", "방향키 이동   A 글자 선택   B 취소", 15, 10,
                TextAnchor.MiddleCenter, shopColors ? PackShopStyle.TextMuted : LotdUiKit.TextMuted);
            _overlay.gameObject.SetActive(false);
        }

        internal void Open(InputField target)
        {
            _target = target;
            _committed = target.text;
            _hangul.Clear();
            _numericOnly = target.contentType == InputField.ContentType.IntegerNumber ||
                target.contentType == InputField.ContentType.DecimalNumber || target.contentType == InputField.ContentType.Pin;
            _page = _numericOnly ? KeyboardPage.Number : KeyboardPage.Hangul;
            _uppercase = false;
            _overlay.SetAsLastSibling();
            _overlay.gameObject.SetActive(true);
            UpdateKeys();
            UpdatePreview();
        }

        internal void Close()
        {
            bool wasOpen = IsOpen;
            _target = null;
            _overlay.gameObject.SetActive(false);
            if (wasOpen) _onClosed?.Invoke();
        }

        private string Value => _committed + ComposeHangul(_hangul);
        private void Apply()
        {
            var input = _target;
            string value = Value;
            Close();
            if (input != null && input.isActiveAndEnabled) input.text = value;
        }

        private void NextPage()
        {
            FlushHangul();
            _page = _page == KeyboardPage.Number ? KeyboardPage.Hangul : (KeyboardPage)((int)_page + 1);
            UpdateKeys();
        }

        private void ToggleCase()
        {
            _uppercase = !_uppercase;
            UpdateKeys();
        }

        private void UpdateKeys()
        {
            _characters = _page switch
            {
                KeyboardPage.Hangul => HangulKeys,
                KeyboardPage.Latin => _uppercase ? LatinKeys.ToUpperInvariant() : LatinKeys,
                _ => NumberKeys + (_target.contentType == InputField.ContentType.DecimalNumber ? "." : ""),
            };
            for (int i = 0; i < _keys.Length; i++)
            {
                _keys[i].transform.parent.gameObject.SetActive(i < _characters.Length);
                if (i < _characters.Length) _labels[i].text = _characters[i].ToString();
            }
            _pageLabel.text = _page switch
            {
                KeyboardPage.Hangul => "ABC",
                KeyboardPage.Latin => "숫자",
                _ => "한글",
            };
            _pageButton.transform.parent.gameObject.SetActive(!_numericOnly);
            _shiftButton.transform.parent.gameObject.SetActive(_page == KeyboardPage.Latin);
            _spaceButton.transform.parent.gameObject.SetActive(!_numericOnly);
        }

        private void PressLetter(int index)
        {
            if (index >= _characters.Length) return;
            char key = _characters[index];
            if (_page != KeyboardPage.Hangul)
            {
                Append(key);
                return;
            }
            _hangul.Add(key);
            if (IsOverLimit(Value)) _hangul.RemoveAt(_hangul.Count - 1);
            UpdatePreview();
        }

        private void Append(char key)
        {
            FlushHangul();
            if (!IsOverLimit(_committed + key)) _committed += key;
            UpdatePreview();
        }

        private bool IsOverLimit(string value) => _target.characterLimit > 0 && value.Length > _target.characterLimit;

        private void Erase()
        {
            if (_hangul.Count > 0) _hangul.RemoveAt(_hangul.Count - 1);
            else if (_committed.Length > 0)
            {
                int[] positions = StringInfo.ParseCombiningCharacters(_committed);
                _committed = _committed.Substring(0, positions[positions.Length - 1]);
            }
            UpdatePreview();
        }

        private void FlushHangul()
        {
            _committed = Value;
            _hangul.Clear();
        }

        private void UpdatePreview() => _preview.text = Value;

        private const string Initials = "ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ";
        private const string Medials = "ㅏㅐㅑㅒㅓㅔㅕㅖㅗㅘㅙㅚㅛㅜㅝㅞㅟㅠㅡㅢㅣ";
        private const string Finals = " ㄱㄲㄳㄴㄵㄶㄷㄹㄺㄻㄼㄽㄾㄿㅀㅁㅂㅄㅅㅆㅇㅈㅊㅋㅌㅍㅎ";
        private const string JoinedMedials = "ㅗㅏㅘㅗㅐㅙㅗㅣㅚㅜㅓㅝㅜㅔㅞㅜㅣㅟㅡㅣㅢㅘㅣㅙㅝㅣㅞ";
        private const string JoinedFinals = "ㄱㅅㄳㄴㅈㄵㄴㅎㄶㄹㄱㄺㄹㅁㄻㄹㅂㄼㄹㅅㄽㄹㅌㄾㄹㅍㄿㄹㅎㅀㅂㅅㅄ";

        internal static string ComposeHangul(IReadOnlyList<char> keys)
        {
            var result = new StringBuilder(keys.Count);
            int onset = -1;
            int medial = -1;
            int final = 0;

            void Output()
            {
                if (onset >= 0)
                {
                    char syllable = medial < 0 ? Initials[onset] : (char)(0xac00 + (onset * 21 + medial) * 28 + final);
                    result.Append(syllable);
                }
                onset = medial = -1;
                final = 0;
            }

            foreach (char key in keys)
            {
                int vowel = Medials.IndexOf(key);
                if (vowel >= 0)
                {
                    if (final > 0)
                    {
                        char next = Finals[final];
                        for (int i = 0; i < JoinedFinals.Length; i += 3)
                        {
                            if (JoinedFinals[i + 2] != next) continue;
                            final = Finals.IndexOf(JoinedFinals[i]);
                            next = JoinedFinals[i + 1];
                            break;
                        }
                        if (next == Finals[final]) final = 0;
                        Output();
                        onset = Initials.IndexOf(next);
                    }
                    else if (medial >= 0)
                    {
                        int compound = Join(JoinedMedials, Medials, Medials[medial], key);
                        if (compound >= 0)
                        {
                            medial = compound;
                            continue;
                        }
                        Output();
                    }
                    if (onset < 0) onset = Initials.IndexOf('ㅇ');
                    medial = vowel;
                    continue;
                }

                int consonant = Initials.IndexOf(key);
                int trailing = Finals.IndexOf(key);
                if (medial >= 0 && trailing > 0)
                {
                    if (final > 0) trailing = Join(JoinedFinals, Finals, Finals[final], key);
                    if (trailing > 0)
                    {
                        final = trailing;
                        continue;
                    }
                }
                Output();
                if (consonant >= 0) onset = consonant;
                else result.Append(key);
            }
            Output();
            return result.ToString();
        }

        private static int Join(string table, string alphabet, char left, char right)
        {
            for (int i = 0; i < table.Length; i += 3)
                if (table[i] == left && table[i + 1] == right) return alphabet.IndexOf(table[i + 2]);
            return -1;
        }
    }
}
