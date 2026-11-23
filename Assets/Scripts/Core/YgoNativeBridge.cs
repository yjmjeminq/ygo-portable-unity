using System;
using System.IO;
using System.Runtime.InteropServices;

namespace YGOPortable.Core
{
    public static class YgoNativeBridge
    {
#if UNITY_IOS && !UNITY_EDITOR
        private const string DllName = "__Internal";
#else
        private const string DllName = "ygoengine";
#endif

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void NativeLogDelegate(int logType, [MarshalAs(UnmanagedType.LPUTF8Str)] string message);

        private static readonly NativeLogDelegate PinnedLogCallback = DispatchNativeLog;
        private static Action<int, string> _onLog;

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int YGO_Init(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string cardPropPath,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string scriptPackPath);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void YGO_Shutdown();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int YGO_GetCardCount();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int YGO_GetScriptCount();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void YGO_SetLogCallback(NativeLogDelegate cb);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int YGO_HasCard(uint code);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int YGO_HasScript([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr YGO_CreateDuel(
            [MarshalAs(UnmanagedType.LPArray, SizeConst = 4)] ulong[] seed,
            ulong flags,
            int team1LP, int team1Draw, int team1DrawPerTurn,
            int team2LP, int team2Draw, int team2DrawPerTurn);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void YGO_DestroyDuel(IntPtr duel);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int YGO_LoadBaseScripts(IntPtr duel);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void YGO_AddCard(
            IntPtr duel, byte team, byte duelist, uint code,
            byte con, uint loc, uint seq, uint pos);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void YGO_StartDuel(IntPtr duel);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int YGO_Process(IntPtr duel);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr YGO_GetMessage(IntPtr duel, out uint outLen);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong YGO_GetSingleTriggerDescription(IntPtr duel, byte player, uint code, ulong hint);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int YGO_GetChainTriggerFlags(IntPtr duel, byte player, [Out] byte[] flags, uint count);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void YGO_SetResponse(IntPtr duel, byte[] buf, uint len);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint YGO_QueryCount(IntPtr duel, byte team, uint loc);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint YGO_QueryCardIdentity(IntPtr duel, byte controller, uint location, uint sequence);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int YGO_MoveDeckCardToTop(IntPtr duel, byte controller, uint sequence);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr YGO_QueryCard(
            IntPtr duel, uint flags, byte con, uint loc, uint seq, uint overlaySeq, out uint outLen);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr YGO_QueryLocation(
            IntPtr duel, uint flags, byte con, uint loc, out uint outLen);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr YGO_QueryField(IntPtr duel, out uint outLen);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int YGO_FilterDeclarableCards(ulong[] opcodes, int opCount, uint[] outCodes, int maxOut);

        public static bool IsInitialized { get; private set; }

        public static int Initialize(string streamingAssetsPath, Action<int, string> onLog = null)
        {
            if (IsInitialized) return 0;
            _onLog = onLog;
            YGO_SetLogCallback(onLog == null ? null : PinnedLogCallback);
            string propPath = Path.Combine(streamingAssetsPath, "cards_prop.bin");
            string scriptPath = Path.Combine(streamingAssetsPath, "cardscripts.pack");
            int rc = YGO_Init(propPath, scriptPath);
            if (rc == 0) IsInitialized = true;
            return rc;
        }

        public static void Shutdown()
        {
            IsInitialized = false;
            YGO_Shutdown();
            _onLog = null;
        }

#if UNITY_IOS && !UNITY_EDITOR
        [AOT.MonoPInvokeCallback(typeof(NativeLogDelegate))]
#endif
        private static void DispatchNativeLog(int logType, string message) => _onLog?.Invoke(logType, message);

        public static IntPtr CreateDuel(ulong[] seed, ulong flags = YgoConstants.DUEL_MODE_MR5,
            int lp1 = 8000, int draw1 = 5, int drawPerTurn1 = 1,
            int lp2 = 8000, int draw2 = 5, int drawPerTurn2 = 1)
        {
            if (seed == null || seed.Length < 4)
            {
                var rng = new Random();
                byte[] bytes = new byte[32];
                rng.NextBytes(bytes);
                seed = new ulong[4];
                Buffer.BlockCopy(bytes, 0, seed, 0, 32);
            }
            return YGO_CreateDuel(seed, flags, lp1, draw1, drawPerTurn1, lp2, draw2, drawPerTurn2);
        }

        public static byte[] GetMessageBuffer(IntPtr duel)
        {
            IntPtr ptr = YGO_GetMessage(duel, out uint len);
            if (ptr == IntPtr.Zero || len == 0) return Array.Empty<byte>();
            byte[] buf = new byte[len];
            Marshal.Copy(ptr, buf, 0, (int)len);
            return buf;
        }

        public static void SetResponse(IntPtr duel, byte[] resp)
        {
            if (resp == null || resp.Length == 0) return;
            YGO_SetResponse(duel, resp, (uint)resp.Length);
        }

        public static void SetResponseInt(IntPtr duel, int value)
        {
            SetResponse(duel, BitConverter.GetBytes(value));
        }

        public static byte[] QueryCardBytes(IntPtr duel, uint flags, byte con, uint loc, uint seq, uint overlaySeq = 0)
        {
            IntPtr ptr = YGO_QueryCard(duel, flags, con, loc, seq, overlaySeq, out uint len);
            if (ptr == IntPtr.Zero || len == 0) return Array.Empty<byte>();
            byte[] buf = new byte[len];
            Marshal.Copy(ptr, buf, 0, (int)len);
            return buf;
        }

        public static byte[] QueryLocationBytes(IntPtr duel, uint flags, byte con, uint loc)
        {
            IntPtr ptr = YGO_QueryLocation(duel, flags, con, loc, out uint len);
            if (ptr == IntPtr.Zero || len == 0) return Array.Empty<byte>();
            byte[] buf = new byte[len];
            Marshal.Copy(ptr, buf, 0, (int)len);
            return buf;
        }

        public static byte[] QueryFieldBytes(IntPtr duel)
        {
            IntPtr ptr = YGO_QueryField(duel, out uint len);
            if (ptr == IntPtr.Zero || len == 0) return Array.Empty<byte>();
            byte[] buf = new byte[len];
            Marshal.Copy(ptr, buf, 0, (int)len);
            return buf;
        }

        public static uint[] FilterDeclarableCards(ulong[] opcodes, int maxOut = 20000)
        {
            if (maxOut <= 0) return Array.Empty<uint>();
            ulong[] effective = (opcodes != null && opcodes.Length > 0) ? opcodes : new ulong[] { 1UL };
            uint[] buf = new uint[maxOut];
            int cnt = YGO_FilterDeclarableCards(effective, effective.Length, buf, maxOut);
            if (cnt <= 0) return Array.Empty<uint>();
            if (cnt == buf.Length) return buf;
            uint[] trimmed = new uint[cnt];
            Array.Copy(buf, trimmed, cnt);
            return trimmed;
        }
    }
}
