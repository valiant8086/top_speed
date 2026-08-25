using System;
using System.Runtime.InteropServices;

namespace TS.Sdl.Video
{
    public static class Window
    {
        private const string LibraryName = SdlNativeLibrary.Name;
        private const string CocoaWindowProperty = "SDL.window.cocoa.window";

        public static IntPtr Create(string title, int width, int height, WindowFlags flags = WindowFlags.None)
        {
            if (!Runtime.IsAvailable)
                return IntPtr.Zero;

            return SDL_CreateWindow(title ?? string.Empty, width, height, flags);
        }

        public static void Destroy(IntPtr window)
        {
            if (!Runtime.IsAvailable || window == IntPtr.Zero)
                return;

            SDL_DestroyWindow(window);
        }

        public static uint GetId(IntPtr window)
        {
            if (!Runtime.IsAvailable || window == IntPtr.Zero)
                return 0;

            return SDL_GetWindowID(window);
        }

        public static bool SetTitle(IntPtr window, string title)
        {
            if (!Runtime.IsAvailable || window == IntPtr.Zero)
                return false;

            return SDL_SetWindowTitle(window, title ?? string.Empty);
        }

        public static bool Show(IntPtr window)
        {
            if (!Runtime.IsAvailable || window == IntPtr.Zero)
                return false;

            return SDL_ShowWindow(window);
        }

        public static uint GetDisplayForWindow(IntPtr window)
        {
            if (!Runtime.IsAvailable || window == IntPtr.Zero)
                return 0;

            return SDL_GetDisplayForWindow(window);
        }

        public static uint GetPrimaryDisplay()
        {
            if (!Runtime.IsAvailable)
                return 0;

            return SDL_GetPrimaryDisplay();
        }

        public static DisplayOrientation GetCurrentDisplayOrientation(uint displayId)
        {
            if (!Runtime.IsAvailable || displayId == 0)
                return DisplayOrientation.Unknown;

            return SDL_GetCurrentDisplayOrientation(displayId);
        }

        /// <summary>The window's size in screen coordinates, or zero by zero if it cannot be read.</summary>
        public static void GetSize(IntPtr window, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (!Runtime.IsAvailable || window == IntPtr.Zero)
                return;

            SDL_GetWindowSize(window, out width, out height);
        }

        /// <summary>
        /// The NSWindow this window is drawn in, or zero anywhere but macOS. Callers that want to
        /// put a real Cocoa control in the window need it; SDL draws its own surface and offers no
        /// controls of its own.
        /// </summary>
        public static IntPtr GetCocoaWindow(IntPtr window)
        {
            if (!Runtime.IsAvailable || window == IntPtr.Zero)
                return IntPtr.Zero;

            var properties = SDL_GetWindowProperties(window);
            if (properties == 0)
                return IntPtr.Zero;

            return SDL_GetPointerProperty(properties, CocoaWindowProperty, IntPtr.Zero);
        }

        [DllImport(LibraryName, EntryPoint = "SDL_CreateWindow", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr SDL_CreateWindow([MarshalAs(UnmanagedType.LPUTF8Str)] string title, int w, int h, WindowFlags flags);

        [DllImport(LibraryName, EntryPoint = "SDL_DestroyWindow", CallingConvention = CallingConvention.Cdecl)]
        private static extern void SDL_DestroyWindow(IntPtr window);

        [DllImport(LibraryName, EntryPoint = "SDL_GetWindowID", CallingConvention = CallingConvention.Cdecl)]
        private static extern uint SDL_GetWindowID(IntPtr window);

        [DllImport(LibraryName, EntryPoint = "SDL_SetWindowTitle", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool SDL_SetWindowTitle(IntPtr window, [MarshalAs(UnmanagedType.LPUTF8Str)] string title);

        [DllImport(LibraryName, EntryPoint = "SDL_ShowWindow", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool SDL_ShowWindow(IntPtr window);

        [DllImport(LibraryName, EntryPoint = "SDL_GetDisplayForWindow", CallingConvention = CallingConvention.Cdecl)]
        private static extern uint SDL_GetDisplayForWindow(IntPtr window);

        [DllImport(LibraryName, EntryPoint = "SDL_GetPrimaryDisplay", CallingConvention = CallingConvention.Cdecl)]
        private static extern uint SDL_GetPrimaryDisplay();

        [DllImport(LibraryName, EntryPoint = "SDL_GetCurrentDisplayOrientation", CallingConvention = CallingConvention.Cdecl)]
        private static extern DisplayOrientation SDL_GetCurrentDisplayOrientation(uint displayId);

        [DllImport(LibraryName, EntryPoint = "SDL_GetWindowSize", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool SDL_GetWindowSize(IntPtr window, out int w, out int h);

        [DllImport(LibraryName, EntryPoint = "SDL_GetWindowProperties", CallingConvention = CallingConvention.Cdecl)]
        private static extern uint SDL_GetWindowProperties(IntPtr window);

        [DllImport(LibraryName, EntryPoint = "SDL_GetPointerProperty", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr SDL_GetPointerProperty(uint props, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, IntPtr defaultValue);
    }
}
