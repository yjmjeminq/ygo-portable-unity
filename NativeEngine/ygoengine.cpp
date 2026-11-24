#include <algorithm>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <filesystem>
#include <mutex>
#include <stack>
#include <string>
#include <unordered_map>
#include <vector>

#ifdef _WIN32
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#define YGO_API extern "C" __declspec(dllexport)
#else
#define YGO_API extern "C" __attribute__((visibility("default")))
#endif

#include "ocgapi.h"
#include "ocgapi_constants.h"
#include "card.h"
#include "duel.h"
#include "effect.h"
#include "field.h"
#include "interpreter.h"
#include <lua.h>

namespace {

#pragma pack(push, 1)
struct PackedCard {
    uint32_t code;
    uint32_t alias;
    uint32_t type;
    uint32_t level;
    uint32_t attribute;
    uint32_t link_marker;
    uint64_t race;
    int32_t  attack;
    int32_t  defense;
    uint32_t lscale;
    uint32_t rscale;
    uint16_t setcodes[8];
};

struct ScriptPackEntry {
    uint32_t name_off;
    uint32_t name_len;
    uint32_t data_off;
    uint32_t data_len;
};
#pragma pack(pop)

static_assert(sizeof(PackedCard) == 64, "PackedCard must be 64 bytes");
static_assert(sizeof(ScriptPackEntry) == 16, "ScriptPackEntry must be 16 bytes");

typedef void (*YGO_LogFn)(int32_t logType, const char* msg);

struct EngineState {
    std::mutex mu;
    std::vector<PackedCard> cards;
    std::vector<uint8_t> script_blob;
    std::unordered_map<std::string, std::pair<uint32_t, uint32_t>> scripts;
    std::filesystem::path fallback_script_dir;
    YGO_LogFn log_cb = nullptr;
};

EngineState g_state;

// Scenario-only cards are outside the regular deck catalogue.
constexpr PackedCard story_cards[] = {
    { 511090001, 0, 0x80002, 0, 0, 0, 0, 0, 0, 0, 0, {} },
    { 511090002, 0, 0x2,     0, 0, 0, 0, 0, 0, 0, 0, {} },
    { 511090101, 0, 0x20004, 0, 0, 0, 0, 0, 0, 0, 0, {} },
    { 511090102, 0, 0x20004, 0, 0, 0, 0, 0, 0, 0, 0, {} },
    { 511090103, 0, 0x20004, 0, 0, 0, 0, 0, 0, 0, 0, {} },
    { 511090104, 0, 0x20004, 0, 0, 0, 0, 0, 0, 0, 0, {} },
    { 511090105, 0, 0x20004, 0, 0, 0, 0, 0, 0, 0, 0, {} },
};

std::string story_script_file(const std::string& key) {
    if (key == "c511090101.lua" || key == "c511090102.lua" || key == "c511090103.lua" ||
        key == "c511090104.lua" || key == "c511090105.lua")
        return "story_darkness_trap.lua";
    if (key == "c511090001.lua" || key == "c511090002.lua" || key == "story_gimmicks.lua" || key == "story_gx.lua")
        return key;
    return {};
}

std::string to_lower_ascii(const char* s) {
    std::string out;
    if (!s) return out;
    out.reserve(64);
    for (const unsigned char* p = reinterpret_cast<const unsigned char*>(s); *p; ++p) {
        unsigned char c = *p;
        if (c >= 'A' && c <= 'Z') c = static_cast<unsigned char>(c - 'A' + 'a');
        out.push_back(static_cast<char>(c));
    }
    return out;
}

bool read_all_bytes(const char* utf8_path, std::vector<uint8_t>& out) {
    out.clear();
    if (!utf8_path || !*utf8_path) return false;
#ifdef _WIN32
    int wlen = MultiByteToWideChar(CP_UTF8, 0, utf8_path, -1, nullptr, 0);
    if (wlen <= 0) return false;
    std::wstring wpath(static_cast<size_t>(wlen), L'\0');
    MultiByteToWideChar(CP_UTF8, 0, utf8_path, -1, wpath.data(), wlen);
    FILE* f = _wfopen(wpath.c_str(), L"rb");
#else
    FILE* f = std::fopen(utf8_path, "rb");
#endif
    if (!f) return false;
    std::fseek(f, 0, SEEK_END);
    long sz = std::ftell(f);
    std::fseek(f, 0, SEEK_SET);
    if (sz <= 0) {
        std::fclose(f);
        return false;
    }
    out.resize(static_cast<size_t>(sz));
    size_t nread = std::fread(out.data(), 1, static_cast<size_t>(sz), f);
    std::fclose(f);
    return nread == static_cast<size_t>(sz);
}

const PackedCard* find_card(uint32_t code) {
    const auto& cards = g_state.cards;
    auto it = std::lower_bound(cards.begin(), cards.end(), code,
        [](const PackedCard& c, uint32_t k) { return c.code < k; });
    if (it != cards.end() && it->code == code) {
        return &(*it);
    }
    for (const auto& story : story_cards) {
        if (story.code == code) return &story;
    }
    return nullptr;
}

void read_card_cb(void* /*payload*/, uint32_t code, OCG_CardData* data) {
    std::memset(data, 0, sizeof(OCG_CardData));
    const PackedCard* pc = find_card(code);
    if (!pc) return;
    data->code = pc->code;
    data->alias = pc->alias;
    data->type = pc->type;
    data->level = pc->level;
    data->attribute = pc->attribute;
    data->race = pc->race;
    data->attack = pc->attack;
    data->defense = pc->defense;
    data->lscale = pc->lscale;
    data->rscale = pc->rscale;
    data->link_marker = pc->link_marker;
    data->setcodes = const_cast<uint16_t*>(pc->setcodes);
}

void read_card_done_cb(void* /*payload*/, OCG_CardData* /*data*/) {
}

int read_script_cb(void* /*payload*/, OCG_Duel duel, const char* name) {
    if (!name || !*name) return 0;
    std::string key = to_lower_ascii(name);
    auto it = g_state.scripts.find(key);
    if (it != g_state.scripts.end()) {
        const uint32_t off = it->second.first;
        const uint32_t len = it->second.second;
        if (off + len <= g_state.script_blob.size()) {
            const char* ptr = reinterpret_cast<const char*>(g_state.script_blob.data() + off);
            return OCG_LoadScript(duel, ptr, len, name) ? 1 : 0;
        }
    }
    auto storyFile = story_script_file(key);
    if (!storyFile.empty() && !g_state.fallback_script_dir.empty()) {
        std::vector<uint8_t> script;
        auto path = g_state.fallback_script_dir / storyFile;
        if (read_all_bytes(path.u8string().c_str(), script))
            return OCG_LoadScript(duel, reinterpret_cast<const char*>(script.data()),
                                  static_cast<uint32_t>(script.size()), name) ? 1 : 0;
    }
    return 0;
}

void log_cb(void* /*payload*/, const char* string, int type) {
    if (g_state.log_cb && string) {
        g_state.log_cb(type, string);
    }
}

} // namespace

YGO_API int32_t YGO_Init(const char* cardPropPath, const char* scriptPackPath) {
    std::lock_guard<std::mutex> lock(g_state.mu);
    std::vector<uint8_t> cbuf;
    if (!read_all_bytes(cardPropPath, cbuf) || cbuf.size() < 12) {
        return -1;
    }
    if (std::memcmp(cbuf.data(), "YGOP", 4) != 0) {
        return -2;
    }
    uint32_t ver = 0, count = 0;
    std::memcpy(&ver, cbuf.data() + 4, 4);
    std::memcpy(&count, cbuf.data() + 8, 4);
    if (ver != 1 || cbuf.size() < 12 + static_cast<size_t>(count) * sizeof(PackedCard)) {
        return -3;
    }
    g_state.cards.resize(count);
    std::memcpy(g_state.cards.data(), cbuf.data() + 12, static_cast<size_t>(count) * sizeof(PackedCard));
    std::sort(g_state.cards.begin(), g_state.cards.end(),
        [](const PackedCard& a, const PackedCard& b) { return a.code < b.code; });

    std::vector<uint8_t> sbuf;
    if (!read_all_bytes(scriptPackPath, sbuf) || sbuf.size() < 12) {
        return -4;
    }
    if (std::memcmp(sbuf.data(), "YGOS", 4) != 0) {
        return -5;
    }
    uint32_t sver = 0, scount = 0;
    std::memcpy(&sver, sbuf.data() + 4, 4);
    std::memcpy(&scount, sbuf.data() + 8, 4);
    if (sver != 1 || sbuf.size() < 12 + static_cast<size_t>(scount) * sizeof(ScriptPackEntry)) {
        return -6;
    }
    g_state.scripts.clear();
    g_state.scripts.reserve(scount);
    g_state.script_blob = std::move(sbuf);
    const ScriptPackEntry* entries = reinterpret_cast<const ScriptPackEntry*>(g_state.script_blob.data() + 12);
    for (uint32_t i = 0; i < scount; ++i) {
        const auto& e = entries[i];
        if (static_cast<size_t>(e.name_off) + e.name_len > g_state.script_blob.size() ||
            static_cast<size_t>(e.data_off) + e.data_len > g_state.script_blob.size()) {
            return -7;
        }
        std::string key(reinterpret_cast<const char*>(g_state.script_blob.data() + e.name_off), e.name_len);
        for (char& ch : key) {
            if (ch >= 'A' && ch <= 'Z') ch = static_cast<char>(ch - 'A' + 'a');
        }
        g_state.scripts[key] = { e.data_off, e.data_len };
    }
    g_state.fallback_script_dir = std::filesystem::u8path(scriptPackPath).parent_path() / "StoryRules";
    return 0;
}

YGO_API void YGO_Shutdown() {
    std::lock_guard<std::mutex> lock(g_state.mu);
    g_state.cards.clear();
    g_state.scripts.clear();
    g_state.script_blob.clear();
    g_state.fallback_script_dir.clear();
    g_state.log_cb = nullptr;
}

YGO_API int32_t YGO_GetCardCount() {
    return static_cast<int32_t>(g_state.cards.size());
}

YGO_API int32_t YGO_GetScriptCount() {
    return static_cast<int32_t>(g_state.scripts.size());
}

YGO_API void YGO_SetLogCallback(YGO_LogFn cb) {
    g_state.log_cb = cb;
}

YGO_API int32_t YGO_HasCard(uint32_t code) {
    return find_card(code) ? 1 : 0;
}

YGO_API int32_t YGO_HasScript(const char* name) {
    if (!name) return 0;
    auto key = to_lower_ascii(name);
    if (g_state.scripts.find(key) != g_state.scripts.end()) return 1;
    auto storyFile = story_script_file(key);
    if (storyFile.empty() || g_state.fallback_script_dir.empty()) return 0;
    std::error_code error;
    return std::filesystem::is_regular_file(g_state.fallback_script_dir / storyFile, error) ? 1 : 0;
}

YGO_API void* YGO_CreateDuel(
    const uint64_t seed[4],
    uint64_t flags,
    int32_t team1LP, int32_t team1Draw, int32_t team1DrawPerTurn,
    int32_t team2LP, int32_t team2Draw, int32_t team2DrawPerTurn)
{
    OCG_DuelOptions opts{};
    if (seed) {
        std::memcpy(opts.seed, seed, sizeof(opts.seed));
    } else {
        opts.seed[0] = 0x123456789abcdef0ULL;
        opts.seed[1] = 0x0fedcba987654321ULL;
        opts.seed[2] = 0xdeadbeefcafebabeULL;
        opts.seed[3] = 0x3141592653589793ULL;
    }
    opts.flags = flags;
    opts.team1.startingLP = static_cast<uint32_t>(team1LP);
    opts.team1.startingDrawCount = static_cast<uint32_t>(team1Draw);
    opts.team1.drawCountPerTurn = static_cast<uint32_t>(team1DrawPerTurn);
    opts.team2.startingLP = static_cast<uint32_t>(team2LP);
    opts.team2.startingDrawCount = static_cast<uint32_t>(team2Draw);
    opts.team2.drawCountPerTurn = static_cast<uint32_t>(team2DrawPerTurn);
    opts.cardReader = read_card_cb;
    opts.scriptReader = read_script_cb;
    opts.logHandler = log_cb;
    opts.cardReaderDone = read_card_done_cb;
    opts.enableUnsafeLibraries = 0;

    OCG_Duel duel = nullptr;
    if (OCG_CreateDuel(&duel, &opts) != OCG_DUEL_CREATION_SUCCESS) {
        return nullptr;
    }
    return duel;
}

YGO_API void YGO_DestroyDuel(void* duel) {
    if (duel) OCG_DestroyDuel(static_cast<OCG_Duel>(duel));
}

YGO_API int32_t YGO_LoadBaseScripts(void* duel) {
    if (!duel) return 0;
    auto d = static_cast<OCG_Duel>(duel);
    if (!read_script_cb(nullptr, d, "constant.lua")) return 0;
    if (!read_script_cb(nullptr, d, "utility.lua")) return 0;
    return 1;
}

YGO_API void YGO_AddCard(void* duel, uint8_t team, uint8_t duelist, uint32_t code,
                         uint8_t con, uint32_t loc, uint32_t seq, uint32_t pos)
{
    if (!duel) return;
    OCG_NewCardInfo info{};
    info.team = team;
    info.duelist = duelist;
    info.code = code;
    info.con = con;
    info.loc = loc;
    info.seq = seq;
    info.pos = pos;
    OCG_DuelNewCard(static_cast<OCG_Duel>(duel), &info);
}

YGO_API int32_t YGO_ConfigureStoryGimmick(void* handle, const char* rule, uint8_t human) {
    if (!handle || !rule || !*rule || human > 1) return -1;
    auto* instance = static_cast<duel*>(handle);
    if (!read_script_cb(nullptr, instance, "story_gimmicks.lua")) {
        instance->handle_message("Could not load story_gimmicks.lua", OCG_LOG_TYPE_ERROR);
        return -1;
    }
    auto* state = instance->lua->current_state;
    lua_getglobal(state, "YgoStoryConfigure");
    lua_pushstring(state, rule);
    lua_pushinteger(state, human);
    const int status = lua_pcall(state, 2, 1, 0);
    if (status != LUA_OK)
        instance->handle_message(lua_tostring(state, -1), OCG_LOG_TYPE_ERROR);
    const int result = status == LUA_OK ? lua_toboolean(state, -1) : -1;
    lua_pop(state, 1);
    return result;
}

YGO_API void YGO_StartDuel(void* duel) {
    if (duel) OCG_StartDuel(static_cast<OCG_Duel>(duel));
}

YGO_API int32_t YGO_Process(void* duel) {
    if (!duel) return OCG_DUEL_STATUS_END;
    return OCG_DuelProcess(static_cast<OCG_Duel>(duel));
}

YGO_API const void* YGO_GetMessage(void* duel, uint32_t* outLen) {
    if (!duel) {
        if (outLen) *outLen = 0;
        return nullptr;
    }
    return OCG_DuelGetMessage(static_cast<OCG_Duel>(duel), outLen);
}

YGO_API uint64_t YGO_GetSingleTriggerDescription(void* handle, uint8_t player, uint32_t code, uint64_t hint) {
    if (!handle || (hint != 0 && hint != 221)) return 0;
    auto& core = static_cast<duel*>(handle)->game_field->core;
    if (core.units.size() < 2 || core.select_chains.size() != 1) return 0;

    auto unit = core.units.begin();
    const auto* prompt = Processors::get_opt_variant<Processors::SelectEffectYesNo>(*unit++);
    if (!prompt || prompt->step != 1 || prompt->playerid != player || prompt->description != hint ||
        !prompt->pcard || prompt->pcard->data.code != code) return 0;

    // The stock messages for a lone optional phase or event trigger contain only a generic prompt.
    if (hint == 0 && !Processors::get_opt_variant<Processors::PhaseEvent>(*unit)) return 0;
    if (hint == 221 && !Processors::get_opt_variant<Processors::PointEvent>(*unit)) return 0;

    const auto* effect = core.select_chains.front().triggering_effect;
    return effect && effect->get_handler() == prompt->pcard ? effect->description : 0;
}

YGO_API int32_t YGO_GetChainTriggerFlags(void* handle, uint8_t player, uint8_t* flags, uint32_t count) {
    if (!handle || !flags) return 0;
    auto& core = static_cast<duel*>(handle)->game_field->core;
    if (core.units.empty() || core.select_chains.size() != count) return 0;
    const auto* prompt = Processors::get_opt_variant<Processors::SelectChain>(core.units.front());
    if (!prompt || prompt->step != 1 || prompt->playerid != player) return 0;

    // SelectChain sorts this list before sending the candidates, so these flags
    // use the same indices as both the wire message and the submitted response.
    constexpr uint16_t trigger_types = EFFECT_TYPE_TRIGGER_O | EFFECT_TYPE_TRIGGER_F | EFFECT_TYPE_CONTINUOUS;
    constexpr uint8_t triggered_effect = 1 << 0;
    constexpr uint8_t event_specific_effect = 1 << 1;
    for (const auto& option : core.select_chains) {
        const auto* effect = option.triggering_effect;
        uint8_t kind = 0;
        if ((option.flag & CHAIN_HAND_TRIGGER) || (effect->type & trigger_types)) kind |= triggered_effect;
        if (effect->code != EVENT_FREE_CHAIN) kind |= event_specific_effect;
        *flags++ = kind;
    }
    return 1;
}

YGO_API void YGO_SetResponse(void* duel, const void* buf, uint32_t len) {
    if (duel) OCG_DuelSetResponse(static_cast<OCG_Duel>(duel), buf, len);
}

YGO_API uint32_t YGO_QueryCount(void* duel, uint8_t team, uint32_t loc) {
    if (!duel) return 0;
    return OCG_DuelQueryCount(static_cast<OCG_Duel>(duel), team, loc);
}

YGO_API uint32_t YGO_QueryCardIdentity(void* handle, uint8_t controller, uint32_t location, uint32_t sequence) {
    if (!handle || controller > 1) return 0;
    auto* card = static_cast<duel*>(handle)->game_field->get_field_card(controller, location, sequence);
    return card ? card->cardid + 1 : 0;
}

YGO_API int32_t YGO_MoveDeckCardToTop(void* handle, uint8_t controller, uint32_t sequence) {
    if (!handle || controller > 1) return 0;
    auto& field = *static_cast<duel*>(handle)->game_field;
    auto& deck = field.player[controller].list_main;
    if (sequence >= deck.size()) return 0;
    auto* selected = deck[sequence];
    deck.erase(deck.begin() + sequence);
    deck.push_back(selected);
    field.reset_sequence(controller, LOCATION_DECK);
    return 1;
}

YGO_API const void* YGO_QueryCard(void* duel, uint32_t flags, uint8_t con, uint32_t loc,
                                  uint32_t seq, uint32_t overlaySeq, uint32_t* outLen)
{
    if (!duel) {
        if (outLen) *outLen = 0;
        return nullptr;
    }
    OCG_QueryInfo q{ flags, con, loc, seq, overlaySeq };
    return OCG_DuelQuery(static_cast<OCG_Duel>(duel), outLen, &q);
}

YGO_API const void* YGO_QueryLocation(void* duel, uint32_t flags, uint8_t con, uint32_t loc, uint32_t* outLen) {
    if (!duel) {
        if (outLen) *outLen = 0;
        return nullptr;
    }
    OCG_QueryInfo q{ flags, con, loc, 0, 0 };
    return OCG_DuelQueryLocation(static_cast<OCG_Duel>(duel), outLen, &q);
}

YGO_API const void* YGO_QueryField(void* duel, uint32_t* outLen) {
    if (!duel) {
        if (outLen) *outLen = 0;
        return nullptr;
    }
    return OCG_DuelQueryField(static_cast<OCG_Duel>(duel), outLen);
}

namespace {
bool eval_declarable(const PackedCard& cd, const uint64_t* opcodes, int32_t count) {
    std::vector<int64_t> st;
    st.reserve(16);
    bool allow_alias = false;
    bool allow_token = false;
    for (int32_t i = 0; i < count; ++i) {
        uint64_t op = opcodes[i];
        switch (op) {
            case OPCODE_ADD: if (st.size() >= 2) { auto r = st.back(); st.pop_back(); st.back() += r; } break;
            case OPCODE_SUB: if (st.size() >= 2) { auto r = st.back(); st.pop_back(); st.back() -= r; } break;
            case OPCODE_MUL: if (st.size() >= 2) { auto r = st.back(); st.pop_back(); st.back() *= r; } break;
            case OPCODE_DIV: if (st.size() >= 2) { auto r = st.back(); st.pop_back(); st.back() = r ? (st.back() / r) : 0; } break;
            case OPCODE_AND: if (st.size() >= 2) { auto r = st.back(); st.pop_back(); st.back() = (st.back() && r) ? 1 : 0; } break;
            case OPCODE_OR:  if (st.size() >= 2) { auto r = st.back(); st.pop_back(); st.back() = (st.back() || r) ? 1 : 0; } break;
            case OPCODE_NEG: if (!st.empty()) st.back() = -st.back(); break;
            case OPCODE_NOT: if (!st.empty()) st.back() = !st.back(); break;
            case OPCODE_BAND: if (st.size() >= 2) { auto r = st.back(); st.pop_back(); st.back() &= r; } break;
            case OPCODE_BOR:  if (st.size() >= 2) { auto r = st.back(); st.pop_back(); st.back() |= r; } break;
            case OPCODE_BXOR: if (st.size() >= 2) { auto r = st.back(); st.pop_back(); st.back() ^= r; } break;
            case OPCODE_BNOT: if (!st.empty()) st.back() = ~st.back(); break;
            case OPCODE_LSHIFT: if (st.size() >= 2) { auto r = st.back(); st.pop_back(); st.back() <<= r; } break;
            case OPCODE_RSHIFT: if (st.size() >= 2) { auto r = st.back(); st.pop_back(); st.back() >>= r; } break;
            case OPCODE_ISCODE: if (!st.empty()) st.back() = (cd.code == static_cast<uint32_t>(st.back())) ? 1 : 0; break;
            case OPCODE_ISTYPE: if (!st.empty()) st.back() = (cd.type & static_cast<uint64_t>(st.back())) ? 1 : 0; break;
            case OPCODE_ISRACE: if (!st.empty()) st.back() = (cd.race & static_cast<uint64_t>(st.back())) ? 1 : 0; break;
            case OPCODE_ISATTRIBUTE: if (!st.empty()) st.back() = (cd.attribute & static_cast<uint64_t>(st.back())) ? 1 : 0; break;
            case OPCODE_GETCODE: st.push_back(cd.code); break;
            case OPCODE_GETTYPE: st.push_back(cd.type); break;
            case OPCODE_GETRACE: st.push_back(static_cast<int64_t>(cd.race)); break;
            case OPCODE_GETATTRIBUTE: st.push_back(cd.attribute); break;
            case OPCODE_ISSETCARD:
                if (!st.empty()) {
                    int32_t sc = static_cast<int32_t>(st.back());
                    uint16_t stype = static_cast<uint16_t>(sc & 0xfff);
                    uint16_t ssub = static_cast<uint16_t>(sc & 0xf000);
                    bool hit = false;
                    for (int s = 0; s < 8 && cd.setcodes[s] != 0; ++s) {
                        if ((cd.setcodes[s] & 0xfff) == stype && (cd.setcodes[s] & 0xf000 & ssub) == ssub) {
                            hit = true;
                            break;
                        }
                    }
                    st.back() = hit ? 1 : 0;
                }
                break;
            case OPCODE_ALLOW_ALIASES: allow_alias = true; break;
            case OPCODE_ALLOW_TOKENS:  allow_token = true; break;
            default: st.push_back(static_cast<int64_t>(op)); break;
        }
    }
    if (st.size() != 1 || st.back() == 0) return false;
    return cd.code == 78734254u || cd.code == 13857930u
        || ((allow_alias || !cd.alias) && (allow_token || ((cd.type & (TYPE_MONSTER | TYPE_TOKEN)) != (TYPE_MONSTER | TYPE_TOKEN))));
}
} // namespace

YGO_API int32_t YGO_FilterDeclarableCards(const uint64_t* opcodes, int32_t opCount, uint32_t* outCodes, int32_t maxOut) {
    if (!opcodes || opCount <= 0 || !outCodes || maxOut <= 0) return 0;
    int32_t found = 0;
    for (const auto& cd : g_state.cards) {
        if (cd.code == 0) continue;
        if (eval_declarable(cd, opcodes, opCount)) {
            outCodes[found++] = cd.code;
            if (found >= maxOut) break;
        }
    }
    return found;
}
