using System;
using System.Runtime.InteropServices;

namespace TS.Sdl.Video
{
    /// <summary>
    /// Just enough drawing to put a frame in the window. The game draws nothing of its own, but a
    /// window that has never been drawn into is not a window every desktop will show: Wayland maps
    /// a toplevel only once the client has committed a buffer to it, so an undrawn window is listed
    /// by the compositor and never appears, and never takes the keyboard. One cleared frame is
    /// enough to make it a real window.
    /// </summary>
    public static class Renderer
    {
        private const string LibraryName = SdlNativeLibrary.Name;

        /// <summary>The renderer SDL picks for the window, or zero when it cannot make one.</summary>
        public static IntPtr Create(IntPtr window)
        {
            if (!Runtime.IsAvailable || window == IntPtr.Zero)
                return IntPtr.Zero;

            return SDL_CreateRenderer(window, null);
        }

        public static void Destroy(IntPtr renderer)
        {
            if (!Runtime.IsAvailable || renderer == IntPtr.Zero)
                return;

            SDL_DestroyRenderer(renderer);
        }

        /// <summary>Clears to black and shows it, which is the whole of what the window ever displays.</summary>
        public static bool Present(IntPtr renderer)
        {
            if (!Runtime.IsAvailable || renderer == IntPtr.Zero)
                return false;

            SDL_SetRenderDrawColor(renderer, 0, 0, 0, 255);
            SDL_RenderClear(renderer);
            return SDL_RenderPresent(renderer);
        }

        [DllImport(LibraryName, EntryPoint = "SDL_CreateRenderer", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr SDL_CreateRenderer(IntPtr window, [MarshalAs(UnmanagedType.LPUTF8Str)] string? name);

        [DllImport(LibraryName, EntryPoint = "SDL_DestroyRenderer", CallingConvention = CallingConvention.Cdecl)]
        private static extern void SDL_DestroyRenderer(IntPtr renderer);

        [DllImport(LibraryName, EntryPoint = "SDL_SetRenderDrawColor", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool SDL_SetRenderDrawColor(IntPtr renderer, byte r, byte g, byte b, byte a);

        [DllImport(LibraryName, EntryPoint = "SDL_RenderClear", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool SDL_RenderClear(IntPtr renderer);

        [DllImport(LibraryName, EntryPoint = "SDL_RenderPresent", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool SDL_RenderPresent(IntPtr renderer);
    }
}
