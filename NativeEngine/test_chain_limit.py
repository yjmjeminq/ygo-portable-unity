"""Exercise the native chain rule without starting Unity or the game."""

import argparse
import ctypes as ct
from pathlib import Path
import struct
from tempfile import TemporaryDirectory


PROJECT = Path(__file__).resolve().parent.parent
ZEUS = 90448279
MSG_RETRY = 1
MSG_SELECT_IDLECMD = 11
MSG_SELECT_EFFECTYN = 12
MSG_SELECT_CARD = 15
MSG_SELECT_CHAIN = 16
MSG_SELECT_PLACE = 18
MSG_CHAINED = 71
MSG_CHAIN_SOLVING = 72
MSG_CHAIN_END = 74

ZEUS_FIXTURE = """
Debug.AddCard(90448279,0,0,LOCATION_MZONE,0,POS_FACEUP_ATTACK,true)
for i=1,40 do
    Debug.AddCard(89631139,0,0,LOCATION_MZONE,0,POS_FACEUP_ATTACK)
end
Debug.AddCard(46986414,1,1,LOCATION_MZONE,0,POS_FACEUP_ATTACK)
"""

TRIGGER_FIXTURE = """
local watcher = Debug.AddCard(89631139,0,0,LOCATION_MZONE,0,POS_FACEUP_ATTACK)
Debug.AddCard(91152256,0,0,LOCATION_HAND,0,POS_FACEDOWN_ATTACK)
for i=1,18 do
    local e = Effect.CreateEffect(watcher)
    e:SetType(EFFECT_TYPE_FIELD|%s)
    e:SetProperty(EFFECT_FLAG_DELAY)
    e:SetCode(EVENT_SUMMON_SUCCESS)
    e:SetRange(LOCATION_MZONE)
    e:SetOperation(function() end)
    watcher:RegisterEffect(e)
end
"""


def make_fixture_pack(original, destination, fixture):
    source = original.read_bytes()
    magic, version, count = struct.unpack_from("<4sII", source)
    assert magic == b"YGOS" and version == 1
    directory = bytearray(source[:12 + count * 16])
    for offset in range(12, len(directory), 16):
        name, name_size, data, size = struct.unpack_from("<4I", directory, offset)
        if source[name:name + name_size] != b"utility.lua":
            continue
        extra = ("\n" + fixture + "\n").encode()
        utility = source[data:data + size] + extra
        struct.pack_into("<II", directory, offset + 8, len(source), len(utility))
        destination.write_bytes(directory + source[len(directory):] + utility)
        break
    else:
        raise AssertionError("utility.lua is missing from the script pack")


def load_engine(path):
    engine = ct.CDLL(str(path))
    signatures = {
        "Init": (ct.c_int32, ct.c_char_p, ct.c_char_p),
        "Shutdown": (None,),
        "CreateDuel": (ct.c_void_p, ct.POINTER(ct.c_uint64), ct.c_uint64, *([ct.c_int32] * 6)),
        "DestroyDuel": (None, ct.c_void_p),
        "LoadBaseScripts": (ct.c_int32, ct.c_void_p),
        "StartDuel": (None, ct.c_void_p),
        "Process": (ct.c_int32, ct.c_void_p),
        "GetMessage": (ct.c_void_p, ct.c_void_p, ct.POINTER(ct.c_uint32)),
        "SetResponse": (None, ct.c_void_p, ct.c_char_p, ct.c_uint32),
    }
    for name, (result, *arguments) in signatures.items():
        function = getattr(engine, "YGO_" + name)
        function.restype = result
        function.argtypes = arguments
    return engine


def uint(data, offset):
    return struct.unpack_from("<I", data, offset)[0]


def messages(engine, duel):
    size = ct.c_uint32()
    address = engine.YGO_GetMessage(duel, ct.byref(size))
    buffer = ct.string_at(address, size.value)
    offset = 0
    while offset < len(buffer):
        length = uint(buffer, offset)
        offset += 4
        assert 0 < length <= len(buffer) - offset
        yield buffer[offset:offset + length]
        offset += length


def choose_response(prompt, use_zeus):
    kind = prompt[0]
    if kind == MSG_SELECT_CHAIN:
        count = uint(prompt, 12)
        if use_zeus:
            for index in range(count):
                if uint(prompt, 16 + 23 * index) == ZEUS:
                    return struct.pack("<i", index)
            return struct.pack("<i", -1)
        return struct.pack("<i", 0 if count else -1)
    if kind == MSG_SELECT_CARD:
        assert use_zeus and uint(prompt, 3) == 2, "Expected Zeus's two-material cost"
        return struct.pack("<IIBB", 2, 2, 0, 1)
    if kind == MSG_SELECT_IDLECMD:
        if not use_zeus:
            assert uint(prompt, 2) == 1, "Expected one Normal Summon candidate"
            return struct.pack("<i", 0)
        offset = 2
        for size in (10, 10, 7, 10, 10):
            offset += 4 + uint(prompt, offset) * size
        for index in range(uint(prompt, offset)):
            if uint(prompt, offset + 4 + 19 * index) == ZEUS:
                return struct.pack("<i", 5 | (index << 16))
    if kind == MSG_SELECT_EFFECTYN and not use_zeus:
        return struct.pack("<i", 1)
    if kind == MSG_SELECT_PLACE and not use_zeus:
        assert prompt[2] == 1
        blocked = uint(prompt, 3)
        zone = next(i for i in range(5) if not (blocked & (1 << i)))
        return bytes((0, 0x04, zone))
    raise AssertionError(f"Unexpected duel prompt {kind}")


def check_chains(engine, card_data, script_pack, fixture, expected, use_zeus=False):
    with TemporaryDirectory() as temp:
        packed_fixture = Path(temp) / "fixture.pack"
        make_fixture_pack(script_pack, packed_fixture, fixture)
        assert engine.YGO_Init(str(card_data).encode(), str(packed_fixture).encode()) == 0
        duel = engine.YGO_CreateDuel((ct.c_uint64 * 4)(1, 2, 3, 4), 0x2E800, 8000, 0, 0, 8000, 0, 0)
        assert duel
        try:
            assert engine.YGO_LoadBaseScripts(duel)
            engine.YGO_StartDuel(duel)
            lengths, linked, resolved = [], [], []
            for _ in range(500):
                status = engine.YGO_Process(duel)
                packet = list(messages(engine, duel))
                for message in packet:
                    kind = message[0]
                    assert kind != MSG_RETRY, "Core rejected a test response"
                    if kind == MSG_CHAINED:
                        linked.append(message[1])
                        assert len(linked) <= 16, f"Chain extended to link {len(linked)}"
                    if kind == MSG_CHAIN_SOLVING:
                        resolved.append(message[1])
                    if kind == MSG_CHAIN_END and linked:
                        assert linked == list(range(1, len(linked) + 1))
                        assert resolved == linked[::-1]
                        lengths.append(len(linked))
                        linked.clear()
                        resolved.clear()
                        if len(lengths) == len(expected):
                            assert lengths == expected, f"Expected {expected}, got {lengths}"
                            return
                assert status != 0, "Duel ended before the expected chains"
                if status == 1:
                    assert packet
                    assert len(linked) < 16 or packet[-1][0] not in (MSG_SELECT_CHAIN, MSG_SELECT_EFFECTYN)
                    response = choose_response(packet[-1], use_zeus)
                    engine.YGO_SetResponse(duel, response, len(response))
            raise AssertionError(f"Timed out waiting for chains {expected}, got {lengths}")
        finally:
            engine.YGO_DestroyDuel(duel)
            engine.YGO_Shutdown()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dll", type=Path, default=PROJECT / "Assets/Plugins/x86_64/ygoengine.dll")
    parser.add_argument("--assets", type=Path, default=PROJECT / "Assets/StreamingAssets")
    args = parser.parse_args()
    engine = load_engine(args.dll.resolve())
    card_data = args.assets / "cards_prop.bin"
    script_pack = args.assets / "cardscripts.pack"
    check_chains(engine, card_data, script_pack, ZEUS_FIXTURE, [16], use_zeus=True)
    print("Zeus fast effects stop at chain link 16.")
    for trigger_type in ("EFFECT_TYPE_TRIGGER_F", "EFFECT_TYPE_TRIGGER_O"):
        check_chains(engine, card_data, script_pack, TRIGGER_FIXTURE % trigger_type, [16, 2])
        print(f"{trigger_type}: 18 simultaneous public triggers resolve in chains of 16 and 2.")


if __name__ == "__main__":
    main()
