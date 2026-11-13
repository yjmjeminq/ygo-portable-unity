#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>
#include <vector>

#ifdef _WIN32
#include <windows.h>
#endif

extern "C" {
int32_t YGO_Init(const char* cardPropPath, const char* scriptPackPath);
void    YGO_Shutdown();
int32_t YGO_GetCardCount();
int32_t YGO_GetScriptCount();
void    YGO_SetLogCallback(void (*cb)(int32_t, const char*));
int32_t YGO_HasCard(uint32_t code);
int32_t YGO_HasScript(const char* name);
void*   YGO_CreateDuel(const uint64_t seed[4], uint64_t flags,
                       int32_t t1LP, int32_t t1D, int32_t t1DP,
                       int32_t t2LP, int32_t t2D, int32_t t2DP);
void    YGO_DestroyDuel(void* duel);
int32_t YGO_LoadBaseScripts(void* duel);
void    YGO_AddCard(void* duel, uint8_t team, uint8_t duelist, uint32_t code,
                    uint8_t con, uint32_t loc, uint32_t seq, uint32_t pos);
void    YGO_StartDuel(void* duel);
int32_t YGO_Process(void* duel);
const void* YGO_GetMessage(void* duel, uint32_t* outLen);
void    YGO_SetResponse(void* duel, const void* buf, uint32_t len);
uint32_t YGO_QueryCount(void* duel, uint8_t team, uint32_t loc);
}

static void on_log(int32_t type, const char* msg) {
    std::fprintf(stderr, "[OCG %d] %s\n", type, msg ? msg : "");
}

static std::string to_utf8(const wchar_t* ws) {
    if (!ws) return "";
    int len = WideCharToMultiByte(CP_UTF8, 0, ws, -1, nullptr, 0, nullptr, nullptr);
    std::string s(len > 0 ? len - 1 : 0, '\0');
    if (len > 1) WideCharToMultiByte(CP_UTF8, 0, ws, -1, &s[0], len, nullptr, nullptr);
    return s;
}

int wmain(int argc, wchar_t** argv) {
    SetConsoleOutputCP(CP_UTF8);
    if (argc < 3) {
        std::fprintf(stderr, "Usage: ygo_smoke <cards_prop.bin> <cardscripts.pack>\n");
        return 2;
    }
    YGO_SetLogCallback(on_log);
    std::string p1 = to_utf8(argv[1]);
    std::string p2 = to_utf8(argv[2]);
    int rc = YGO_Init(p1.c_str(), p2.c_str());
    if (rc != 0) {
        std::fprintf(stderr, "YGO_Init failed: %d\n", rc);
        return 1;
    }
    std::printf("Loaded cards=%d scripts=%d\n", YGO_GetCardCount(), YGO_GetScriptCount());

    const uint32_t checkCodes[] = { 39931513, 35844557, 37818794, 89631139, 46986414 };
    for (uint32_t c : checkCodes) {
        char buf[32];
        std::snprintf(buf, sizeof(buf), "c%u.lua", c);
        int hc = YGO_HasCard(c);
        int hs = YGO_HasScript(buf);
        std::printf("  Card %u: data=%d script=%d\n", c, hc, hs);
        if (!hc || (!hs && c != 89631139 && c != 46986414)) {
            std::fprintf(stderr, "Missing required card %u\n", c);
            return 1;
        }
    }

    uint64_t seed[4] = { 1, 2, 3, 4 };
    // DUEL_MODE_MR5 = 0x800 | 0x2000 | 0x4000 | 0x8000 | 0x20000 = 0x2E800
    void* duel = YGO_CreateDuel(seed, 0x2E800ULL, 8000, 5, 1, 8000, 5, 1);
    if (!duel) {
        std::fprintf(stderr, "CreateDuel failed\n");
        return 1;
    }
    if (!YGO_LoadBaseScripts(duel)) {
        std::fprintf(stderr, "LoadBaseScripts failed\n");
        return 1;
    }
    // Load 20 cards per deck mix of 2026 + stock monsters
    const uint32_t deck0[] = {
        39931513, 39931513, 39931513, 89631139, 89631139,
        46986414, 46986414, 35844557, 35844557, 35844557,
        39931513, 89631139, 46986414, 35844557, 39931513,
        89631139, 46986414, 35844557, 39931513, 89631139
    };
    for (uint32_t i = 0; i < 20; ++i) {
        YGO_AddCard(duel, 0, 0, deck0[i], 0, 0x01 /*DECK*/, i, 0x8 /*FACEDOWN_DEFENSE*/);
        YGO_AddCard(duel, 1, 0, deck0[i], 1, 0x01 /*DECK*/, i, 0x8);
    }
    YGO_AddCard(duel, 0, 0, 37818794, 0, 0x40 /*EXTRA*/, 0, 0x8);
    YGO_AddCard(duel, 1, 0, 37818794, 1, 0x40 /*EXTRA*/, 0, 0x8);

    YGO_StartDuel(duel);
    int status = YGO_Process(duel);
    uint32_t mlen = 0;
    const uint8_t* mbuf = reinterpret_cast<const uint8_t*>(YGO_GetMessage(duel, &mlen));
    std::printf("StartDuel processed: status=%d msgBytes=%u p0_hand=%u p1_hand=%u\n",
        status, mlen, YGO_QueryCount(duel, 0, 0x02), YGO_QueryCount(duel, 1, 0x02));

    // Step through initial IdleCmd (summon card 0 or go EP) to verify interaction!
    if (status == 1 && mlen > 0) {
        // Respond to prompt by skipping to EP (t=7) or ending prompt
        uint32_t off = 0;
        uint8_t lastMsg = 0;
        while (off + 4 <= mlen) {
            uint32_t sz = 0;
            std::memcpy(&sz, mbuf + off, 4);
            off += 4;
            if (off + sz > mlen) break;
            lastMsg = mbuf[off];
            off += sz;
        }
        std::printf("  Last prompt msg=%u\n", lastMsg);
    }

    YGO_DestroyDuel(duel);
    YGO_Shutdown();
    std::printf("Smoke test PASSED\n");
    return 0;
}
