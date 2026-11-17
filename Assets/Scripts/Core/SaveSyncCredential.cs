using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace YGOPortable.Core
{
    internal static class SaveSyncCredential
    {
        internal static bool Supported
        {
            get
            {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN || (UNITY_IOS && !UNITY_EDITOR)
                return true;
#else
                return false;
#endif
            }
        }

        internal static string Read(string directory)
        {
#if UNITY_IOS && !UNITY_EDITOR
            IntPtr token = YgoCloudCopyToken();
            if (token == IntPtr.Zero) return null;
            try { return Marshal.PtrToStringAnsi(token); }
            finally { YgoCloudFreeToken(token); }
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            string encrypted = SaveSyncFiles.ReadOptional(TokenPath(directory));
            return encrypted == null ? null :
                Encoding.UTF8.GetString(Protect(Convert.FromBase64String(encrypted), decrypt: true));
#else
            return null;
#endif
        }

        internal static void Write(string directory, string token)
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (YgoCloudSetToken(token) != 0)
                throw new IOException("Could not update the iOS Keychain.");
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (token == null) File.Delete(TokenPath(directory));
            else SaveSyncFiles.WriteAtomic(TokenPath(directory),
                Convert.ToBase64String(Protect(Encoding.UTF8.GetBytes(token), decrypt: false)));
#else
            throw new PlatformNotSupportedException();
#endif
        }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern IntPtr YgoCloudCopyToken();
        [DllImport("__Internal")] private static extern int YgoCloudSetToken(string token);
        [DllImport("__Internal")] private static extern void YgoCloudFreeToken(IntPtr token);
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private static string TokenPath(string directory) => Path.Combine(directory, "cloud-save-token.dpapi");

        [StructLayout(LayoutKind.Sequential)]
        private struct Blob
        {
            internal int length;
            internal IntPtr data;
        }

        [DllImport("crypt32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptProtectData(ref Blob input, IntPtr description, IntPtr entropy,
            IntPtr reserved, IntPtr prompt, int flags, out Blob output);

        [DllImport("crypt32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy,
            IntPtr reserved, IntPtr prompt, int flags, out Blob output);

        [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);

        private static byte[] Protect(byte[] bytes, bool decrypt)
        {
            var input = new Blob { length = bytes.Length, data = Marshal.AllocHGlobal(bytes.Length) };
            Blob output = default;
            try
            {
                Marshal.Copy(bytes, 0, input.data, input.length);
                const int blockUi = 1;
                bool success = decrypt
                    ? CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, blockUi, out output)
                    : CryptProtectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, blockUi, out output);
                if (!success) throw new IOException("Windows could not unlock the GitHub credential.");
                var result = new byte[output.length];
                Marshal.Copy(output.data, result, 0, output.length);
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(input.data);
                if (output.data != IntPtr.Zero) LocalFree(output.data);
            }
        }
#endif
    }
}
