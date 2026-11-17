using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace YGOPortable.UI
{
    [Flags]
    internal enum DeckPadButtons : ushort
    {
        None = 0,
        Up = 0x0001,
        Down = 0x0002,
        Left = 0x0004,
        Right = 0x0008,
        Save = 0x0010,
        Reorder = 0x0080,
        PreviousArea = 0x0100,
        NextArea = 0x0200,
        Submit = 0x1000,
        Cancel = 0x2000,
        Alternate = 0x4000,
        Search = 0x8000,
    }

    internal readonly struct DeckPadFrame
    {
        private const DeckPadButtons Actions = DeckPadButtons.Submit | DeckPadButtons.Cancel | DeckPadButtons.Alternate |
            DeckPadButtons.Search | DeckPadButtons.PreviousArea | DeckPadButtons.NextArea | DeckPadButtons.Save | DeckPadButtons.Reorder;
        private readonly DeckPadButtons _pressed;

        internal DeckPadFrame(Vector2Int direction, DeckPadButtons pressed, float descriptionScroll = 0f)
        {
            Direction = direction;
            _pressed = pressed;
            DescriptionScroll = descriptionScroll;
        }

        internal Vector2Int Direction { get; }
        internal float DescriptionScroll { get; }
        internal bool HasInput => Direction != Vector2Int.zero || (_pressed & Actions) != 0 || DescriptionScroll != 0f;
        internal bool Submit => (_pressed & DeckPadButtons.Submit) != 0;
        internal bool Cancel => (_pressed & DeckPadButtons.Cancel) != 0;
        internal bool Alternate => (_pressed & DeckPadButtons.Alternate) != 0;
        internal bool Search => (_pressed & DeckPadButtons.Search) != 0;
        internal bool PreviousArea => (_pressed & DeckPadButtons.PreviousArea) != 0;
        internal bool NextArea => (_pressed & DeckPadButtons.NextArea) != 0;
        internal bool Save => (_pressed & DeckPadButtons.Save) != 0;
        internal bool Reorder => (_pressed & DeckPadButtons.Reorder) != 0;
    }

    internal sealed class DeckPadInput
    {
        private const float StickPress = .57f;
        private const float StickRelease = .39f;
        private const float InitialRepeatDelay = .34f;
        private const float NextRepeatDelay = .10f;

        private Vector2Int _heldDirection;
        private DeckPadButtons _heldButtons;
        private bool _sampledButtons;
        private float _nextDirection;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private int _xinputSlot = -1;
        private float _nextXInputProbe;
        private bool _xinputUnavailable;
#endif

        internal void Reset()
        {
            _heldDirection = Vector2Int.zero;
            _heldButtons = DeckPadButtons.None;
            _sampledButtons = false;
            _nextDirection = 0f;
        }

        internal DeckPadFrame Read(float now)
        {
            if (!Application.isFocused)
            {
                Reset();
                return default;
            }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (TryReadXInput(now, out var pad))
            {
                var stick = new Vector2(pad.LeftThumbX / 32768f, pad.LeftThumbY / 32768f);
                return Advance(stick, pad.Buttons, now, pad.RightThumbY / 32768f);
            }
#endif

            var legacyStick = new Vector2(Input.GetAxisRaw("DeckPadStickHorizontal"), Input.GetAxisRaw("DeckPadStickVertical"));
            var buttons = LegacyButton(KeyCode.JoystickButton0, DeckPadButtons.Submit) |
                LegacyButton(KeyCode.JoystickButton1, DeckPadButtons.Cancel) |
                LegacyButton(KeyCode.JoystickButton2, DeckPadButtons.Alternate) |
                LegacyButton(KeyCode.JoystickButton3, DeckPadButtons.Search) |
                LegacyButton(KeyCode.JoystickButton4, DeckPadButtons.PreviousArea) |
                LegacyButton(KeyCode.JoystickButton5, DeckPadButtons.NextArea) |
                LegacyButton(KeyCode.JoystickButton7, DeckPadButtons.Save) |
                LegacyButton(KeyCode.JoystickButton9, DeckPadButtons.Reorder) |
                LegacyDirection("DeckPadDpadHorizontal", DeckPadButtons.Left, DeckPadButtons.Right) |
                LegacyDirection("DeckPadDpadVertical", DeckPadButtons.Down, DeckPadButtons.Up);
            return Advance(legacyStick, buttons, now, Input.GetAxisRaw("DeckPadLookVertical"));
        }

        internal DeckPadFrame Advance(Vector2 stick, DeckPadButtons held, float now, float descriptionStick = 0f)
        {
            // A button already held while entering the editor belongs to the previous screen.
            var pressed = _sampledButtons ? held & ~_heldButtons : DeckPadButtons.None;
            _heldButtons = held;
            _sampledButtons = true;

            var dpad = new Vector2Int(
                ((held & DeckPadButtons.Right) != 0 ? 1 : 0) - ((held & DeckPadButtons.Left) != 0 ? 1 : 0),
                ((held & DeckPadButtons.Up) != 0 ? 1 : 0) - ((held & DeckPadButtons.Down) != 0 ? 1 : 0));
            Vector2Int direction;
            if (dpad != Vector2Int.zero)
                direction = dpad.x != 0 ? new Vector2Int(dpad.x, 0) : new Vector2Int(0, dpad.y);
            else
            {
                float deadZone = _heldDirection == Vector2Int.zero ? StickPress : StickRelease;
                float x = Mathf.Abs(stick.x);
                float y = Mathf.Abs(stick.y);
                direction = Mathf.Max(x, y) < deadZone ? Vector2Int.zero :
                    x >= y ? new Vector2Int(stick.x > 0f ? 1 : -1, 0) :
                    new Vector2Int(0, stick.y > 0f ? 1 : -1);
            }

            Vector2Int move = Vector2Int.zero;
            if (_heldDirection != direction)
            {
                _heldDirection = direction;
                _nextDirection = now + InitialRepeatDelay;
                move = direction;
            }
            else if (direction != Vector2Int.zero && now >= _nextDirection)
            {
                _nextDirection = now + NextRepeatDelay;
                move = direction;
            }

            float descriptionScroll = Mathf.Sign(descriptionStick) * Mathf.InverseLerp(.28f, 1f, Mathf.Abs(descriptionStick));
            return new DeckPadFrame(move, pressed, descriptionScroll);
        }

        private static DeckPadButtons LegacyButton(KeyCode key, DeckPadButtons action) =>
            Input.GetKey(key) ? action : DeckPadButtons.None;

        private static DeckPadButtons LegacyDirection(string axis, DeckPadButtons negative, DeckPadButtons positive)
        {
            float value = Input.GetAxisRaw(axis);
            return value < -.6f ? negative : value > .6f ? positive : DeckPadButtons.None;
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [StructLayout(LayoutKind.Sequential)]
        private struct XInputState
        {
            public uint PacketNumber;
            public DeckPadButtons Buttons;
            public byte LeftTrigger;
            public byte RightTrigger;
            public short LeftThumbX;
            public short LeftThumbY;
            public short RightThumbX;
            public short RightThumbY;
        }

        [DllImport("xinput1_4.dll")]
        private static extern uint XInputGetState(uint userIndex, out XInputState state);

        private bool TryReadXInput(float now, out XInputState state)
        {
            state = default;
            if (_xinputUnavailable) return false;

            try
            {
                if (_xinputSlot >= 0 && XInputGetState((uint)_xinputSlot, out state) == 0)
                    return true;
                _xinputSlot = -1;
                if (now < _nextXInputProbe) return false;
                _nextXInputProbe = now + 1f;
                for (uint i = 0; i < 4; i++)
                {
                    if (XInputGetState(i, out state) != 0) continue;
                    _xinputSlot = (int)i;
                    return true;
                }
            }
            catch (Exception error) when (error is DllNotFoundException || error is EntryPointNotFoundException)
            {
                _xinputUnavailable = true;
            }

            return false;
        }
#endif
    }
}
