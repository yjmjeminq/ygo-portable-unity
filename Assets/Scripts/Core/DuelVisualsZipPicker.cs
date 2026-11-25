using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;

namespace YGOPortable.Core
{
    internal static class DuelVisualsZipPicker
    {
        internal static bool Supported => Application.platform == RuntimePlatform.IPhonePlayer;

        internal static async Task<bool> Import(string root)
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (_selection != null) throw new InvalidOperationException("A ZIP picker is already open.");
            var request = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _selection = request;
            string archive;
            try
            {
                YgoPickDuelVisualsZip(Completion);
                archive = await request.Task;
            }
            finally { _selection = null; }
            if (archive == null) return false;

            try
            {
                await Task.Run(() => DuelVisualsZipImport.Install(archive, root));
                return true;
            }
            finally
            {
                try { File.Delete(archive); }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
                {
                    Debug.LogWarning($"[DuelVisuals] Could not remove the temporary ZIP: {error.Message}");
                }
            }
#else
            return await Task.FromResult(false);
#endif
        }

#if UNITY_IOS && !UNITY_EDITOR
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ZipPicked(int status, IntPtr path);
        private static readonly ZipPicked Completion = OnPicked;
        private static TaskCompletionSource<string> _selection;

        [AOT.MonoPInvokeCallback(typeof(ZipPicked))]
        private static void OnPicked(int status, IntPtr path)
        {
            if (status == 0) _selection?.TrySetResult(null);
            else if (status == 1 && path != IntPtr.Zero) _selection?.TrySetResult(Marshal.PtrToStringUTF8(path));
            else _selection?.TrySetException(new IOException("파일 앱에서 ZIP을 가져오지 못했습니다."));
        }

        [DllImport("__Internal", CallingConvention = CallingConvention.Cdecl)]
        private static extern void YgoPickDuelVisualsZip(ZipPicked completion);
#endif
    }
}
