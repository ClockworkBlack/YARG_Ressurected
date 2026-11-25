#if UNITY_ANDROID
using System;
using System.Runtime.InteropServices;
using ManagedBass;

namespace YARG.Audio.BASS
{
    /// <summary>
    /// Direct calls into BASS for the functions that take a callback.
    ///
    /// ManagedBass's own wrappers for these (Bass.CreateStream with file procedures, ChannelSetSync,
    /// ChannelSetDSP, RecordStart, BassMix.ChannelSetSync) also register a hidden clean-up callback of
    /// their own, ManagedBass.ChannelReferences.Callback. That method has no [MonoPInvokeCallback]
    /// attribute, so on IL2CPP (this 64-bit build) the call throws "To marshal a managed method,
    /// please add an attribute named 'MonoPInvokeCallback'" and the song, sound effect or mic never
    /// starts. Calling BASS directly skips that registration. It only existed to keep delegates alive,
    /// which isn't needed here: every callback passed in is a static method marked
    /// [MonoPInvokeCallback], and those have fixed native entry points on IL2CPP.
    /// </summary>
    internal static class BassDirect
    {
        private const string BASS = "bass";
        private const string BASSMIX = "bassmix";

        [DllImport(BASS)]
        private static extern int BASS_StreamCreateFileUser(StreamSystem system, BassFlags flags,
            [In] FileProcedures procs, IntPtr user);

        [DllImport(BASS)]
        private static extern int BASS_ChannelSetSync(int handle, SyncFlags type, long param,
            SyncProcedure proc, IntPtr user);

        [DllImport(BASS)]
        private static extern int BASS_ChannelSetDSP(int handle, DSPProcedure proc, IntPtr user, int priority);

        [DllImport(BASS)]
        private static extern int BASS_RecordStart(int freq, int chans, BassFlags flags,
            RecordProcedure proc, IntPtr user);

        [DllImport(BASSMIX)]
        private static extern int BASS_Mixer_ChannelSetSync(int handle, SyncFlags type, long param,
            SyncProcedure proc, IntPtr user);

        public static int CreateStream(StreamSystem system, BassFlags flags, FileProcedures procs, IntPtr user)
        {
            return BASS_StreamCreateFileUser(system, flags, procs, user);
        }

        public static int ChannelSetSync(int handle, SyncFlags type, long param, SyncProcedure proc, IntPtr user)
        {
            return BASS_ChannelSetSync(handle, type, param, proc, user);
        }

        public static int ChannelSetDSP(int handle, DSPProcedure proc, IntPtr user = default, int priority = 0)
        {
            return BASS_ChannelSetDSP(handle, proc, user, priority);
        }

        /// <summary>Same as ManagedBass: the update period (ms) goes in the high word of the flags.</summary>
        public static int RecordStart(int freq, int chans, BassFlags flags, int period,
            RecordProcedure proc, IntPtr user)
        {
            var packed = (BassFlags) (((int) flags & 0xFFFF) | ((period & 0xFFFF) << 16));
            return BASS_RecordStart(freq, chans, packed, proc, user);
        }

        public static int MixerChannelSetSync(int handle, SyncFlags type, long param, SyncProcedure proc, IntPtr user)
        {
            return BASS_Mixer_ChannelSetSync(handle, type, param, proc, user);
        }
    }
}
#endif
