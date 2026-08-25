using System;
using System.Runtime.InteropServices;
using TS.Sdl.Events;
using TS.Sdl.Interop;

namespace TS.Sdl
{
    public static class Runtime
    {
        private const string LibraryName = SdlNativeLibrary.Name;

        public static bool IsAvailable => Library.EnsureLoaded();

        public static bool Init(InitFlags flags)
        {
            if (!IsAvailable)
                return false;

            return SDL_Init(flags);
        }

        /// <summary>
        /// Sets one of SDL's own settings. Hints that decide how a subsystem starts are only read
        /// while it starts, so those have to be set before the subsystem is initialized. A hint the
        /// platform knows nothing about is ignored.
        /// </summary>
        public static bool SetHint(string name, string value)
        {
            if (!IsAvailable || string.IsNullOrEmpty(name))
                return false;

            return SDL_SetHint(name, value ?? string.Empty);
        }

        public static void SetMainReady()
        {
            if (!IsAvailable)
                return;

            SDL_SetMainReady();
        }

        public static bool InitSubSystem(InitFlags flags)
        {
            if (!IsAvailable)
                return false;

            return SDL_InitSubSystem(flags);
        }

        public static void QuitSubSystem(InitFlags flags)
        {
            if (!IsAvailable)
                return;

            SDL_QuitSubSystem(flags);
        }

        public static InitFlags WasInit(InitFlags flags)
        {
            if (!IsAvailable)
                return 0;

            return SDL_WasInit(flags);
        }

        public static void Quit()
        {
            if (!IsAvailable)
                return;

            SDL_Quit();
        }

        public static string GetError()
        {
            if (!IsAvailable)
                return Library.LastError;

            return Utf8.FromNative(SDL_GetError()) ?? string.Empty;
        }

        public static void ClearError()
        {
            if (!IsAvailable)
                return;

            SDL_ClearError();
        }

        public static void PumpEvents()
        {
            if (!IsAvailable)
                return;

            SDL_PumpEvents();
        }

        public static bool PollEvent(out Event value)
        {
            value = default;
            if (!IsAvailable)
                return false;

            return SDL_PollEvent(out value);
        }

        public static bool IsMainThread()
        {
            if (!IsAvailable)
                return false;

            return SDL_IsMainThread();
        }

        public static ulong GetTicksNs()
        {
            if (!IsAvailable)
                return 0;

            return SDL_GetTicksNS();
        }

        [DllImport(LibraryName, EntryPoint = "SDL_SetHint", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool SDL_SetHint([MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

        [DllImport(LibraryName, EntryPoint = "SDL_Init", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool SDL_Init(InitFlags flags);

        [DllImport(LibraryName, EntryPoint = "SDL_SetMainReady", CallingConvention = CallingConvention.Cdecl)]
        private static extern void SDL_SetMainReady();

        [DllImport(LibraryName, EntryPoint = "SDL_InitSubSystem", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool SDL_InitSubSystem(InitFlags flags);

        [DllImport(LibraryName, EntryPoint = "SDL_QuitSubSystem", CallingConvention = CallingConvention.Cdecl)]
        private static extern void SDL_QuitSubSystem(InitFlags flags);

        [DllImport(LibraryName, EntryPoint = "SDL_WasInit", CallingConvention = CallingConvention.Cdecl)]
        private static extern InitFlags SDL_WasInit(InitFlags flags);

        [DllImport(LibraryName, EntryPoint = "SDL_Quit", CallingConvention = CallingConvention.Cdecl)]
        private static extern void SDL_Quit();

        [DllImport(LibraryName, EntryPoint = "SDL_GetError", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr SDL_GetError();

        [DllImport(LibraryName, EntryPoint = "SDL_ClearError", CallingConvention = CallingConvention.Cdecl)]
        private static extern void SDL_ClearError();

        [DllImport(LibraryName, EntryPoint = "SDL_PumpEvents", CallingConvention = CallingConvention.Cdecl)]
        private static extern void SDL_PumpEvents();

        [DllImport(LibraryName, EntryPoint = "SDL_PollEvent", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool SDL_PollEvent(out Event value);

        [DllImport(LibraryName, EntryPoint = "SDL_IsMainThread", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool SDL_IsMainThread();

        [DllImport(LibraryName, EntryPoint = "SDL_GetTicksNS", CallingConvention = CallingConvention.Cdecl)]
        private static extern ulong SDL_GetTicksNS();
    }
}
