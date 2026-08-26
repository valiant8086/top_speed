using System;
using System.Runtime.InteropServices;

namespace TS.Sdl.Input
{
    public static class Keyboard
    {
        private const string LibraryName = SdlNativeLibrary.Name;
        private const string PropType = "SDL.textinput.type";
        private const string PropCapitalization = "SDL.textinput.capitalization";
        private const string PropAutoCorrect = "SDL.textinput.autocorrect";
        private const string PropMultiLine = "SDL.textinput.multiline";
        private const string PropAndroidInputType = "SDL.textinput.android.inputtype";

        public static KeyboardState GetState()
        {
            if (!Runtime.IsAvailable)
                return default;

            var state = SDL_GetKeyboardState(out var count);
            return new KeyboardState(state, count);
        }

        /// <summary>
        /// The window the keyboard is currently talking to, or zero when it is talking to something
        /// else entirely. Zero is the honest answer for another application having the keyboard, and
        /// is what tells a caller that anything the hardware reports as held is not meant for us.
        /// </summary>
        public static IntPtr GetFocusedWindow()
        {
            if (!Runtime.IsAvailable)
                return IntPtr.Zero;

            return SDL_GetKeyboardFocus();
        }

        public static void Reset()
        {
            if (!Runtime.IsAvailable)
                return;

            SDL_ResetKeyboard();
        }

        public static bool StartTextInput(IntPtr window)
        {
            if (!Runtime.IsAvailable)
                return false;

            return SDL_StartTextInput(window);
        }

        public static bool StartTextInput(IntPtr window, TextInputOptions? options)
        {
            if (!Runtime.IsAvailable)
                return false;

            if (options == null)
                return StartTextInput(window);

            var properties = SDL_CreateProperties();
            if (properties == 0)
                return false;

            try
            {
                if (!SDL_SetNumberProperty(properties, PropType, (long)options.Type))
                    return false;

                if (!SDL_SetNumberProperty(properties, PropCapitalization, (long)options.Capitalization))
                    return false;

                if (!SDL_SetBooleanProperty(properties, PropAutoCorrect, options.AutoCorrect))
                    return false;

                if (!SDL_SetBooleanProperty(properties, PropMultiLine, options.MultiLine))
                    return false;

                if (options.AndroidInputType.HasValue &&
                    !SDL_SetNumberProperty(properties, PropAndroidInputType, options.AndroidInputType.Value))
                {
                    return false;
                }

                return SDL_StartTextInputWithProperties(window, properties);
            }
            finally
            {
                SDL_DestroyProperties(properties);
            }
        }

        public static bool StartTextInputWithProperties(IntPtr window, uint properties)
        {
            if (!Runtime.IsAvailable)
                return false;

            return SDL_StartTextInputWithProperties(window, properties);
        }

        public static bool StopTextInput(IntPtr window)
        {
            if (!Runtime.IsAvailable)
                return false;

            return SDL_StopTextInput(window);
        }

        public static bool IsTextInputActive(IntPtr window)
        {
            if (!Runtime.IsAvailable)
                return false;

            return SDL_TextInputActive(window);
        }

        public static bool ClearComposition(IntPtr window)
        {
            if (!Runtime.IsAvailable)
                return false;

            return SDL_ClearComposition(window);
        }

        public static bool SetTextInputArea(IntPtr window, TextInputArea? area, int cursor)
        {
            if (!Runtime.IsAvailable)
                return false;

            unsafe
            {
                if (!area.HasValue)
                    return SDL_SetTextInputArea(window, null, cursor);

                var native = ToNative(area.Value);
                return SDL_SetTextInputArea(window, &native, cursor);
            }
        }

        public static bool GetTextInputArea(IntPtr window, out TextInputArea area, out int cursor)
        {
            area = default;
            cursor = 0;
            if (!Runtime.IsAvailable)
                return false;

            if (!SDL_GetTextInputArea(window, out var native, out cursor))
                return false;

            area = FromNative(native);
            return true;
        }

        public static bool HasScreenKeyboardSupport()
        {
            if (!Runtime.IsAvailable)
                return false;

            return SDL_HasScreenKeyboardSupport();
        }

        public static bool IsScreenKeyboardShown(IntPtr window)
        {
            if (!Runtime.IsAvailable)
                return false;

            return SDL_ScreenKeyboardShown(window);
        }

        [DllImport(LibraryName, EntryPoint = "SDL_GetKeyboardState", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr SDL_GetKeyboardState(out int numkeys);

        [DllImport(LibraryName, EntryPoint = "SDL_GetKeyboardFocus", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr SDL_GetKeyboardFocus();

        [DllImport(LibraryName, EntryPoint = "SDL_ResetKeyboard", CallingConvention = CallingConvention.Cdecl)]
        private static extern void SDL_ResetKeyboard();

        [DllImport(LibraryName, EntryPoint = "SDL_StartTextInput", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool SDL_StartTextInput(IntPtr window);

        [DllImport(LibraryName, EntryPoint = "SDL_StartTextInputWithProperties", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool SDL_StartTextInputWithProperties(IntPtr window, uint properties);

        [DllImport(LibraryName, EntryPoint = "SDL_StopTextInput", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool SDL_StopTextInput(IntPtr window);

        [DllImport(LibraryName, EntryPoint = "SDL_TextInputActive", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool SDL_TextInputActive(IntPtr window);

        [DllImport(LibraryName, EntryPoint = "SDL_ClearComposition", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool SDL_ClearComposition(IntPtr window);

        [DllImport(LibraryName, EntryPoint = "SDL_SetTextInputArea", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern unsafe bool SDL_SetTextInputArea(IntPtr window, SdlRect* rect, int cursor);

        [DllImport(LibraryName, EntryPoint = "SDL_GetTextInputArea", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool SDL_GetTextInputArea(IntPtr window, out SdlRect rect, out int cursor);

        [DllImport(LibraryName, EntryPoint = "SDL_HasScreenKeyboardSupport", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool SDL_HasScreenKeyboardSupport();

        [DllImport(LibraryName, EntryPoint = "SDL_ScreenKeyboardShown", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool SDL_ScreenKeyboardShown(IntPtr window);

        [DllImport(LibraryName, EntryPoint = "SDL_CreateProperties", CallingConvention = CallingConvention.Cdecl)]
        private static extern uint SDL_CreateProperties();

        [DllImport(LibraryName, EntryPoint = "SDL_DestroyProperties", CallingConvention = CallingConvention.Cdecl)]
        private static extern void SDL_DestroyProperties(uint props);

        [DllImport(LibraryName, EntryPoint = "SDL_SetNumberProperty", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool SDL_SetNumberProperty(uint props, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, long value);

        [DllImport(LibraryName, EntryPoint = "SDL_SetBooleanProperty", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool SDL_SetBooleanProperty(uint props, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.I1)] bool value);

        private static SdlRect ToNative(TextInputArea value)
        {
            return new SdlRect
            {
                X = value.X,
                Y = value.Y,
                W = value.Width,
                H = value.Height
            };
        }

        private static TextInputArea FromNative(SdlRect value)
        {
            return new TextInputArea(value.X, value.Y, value.W, value.H);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SdlRect
        {
            public int X;
            public int Y;
            public int W;
            public int H;
        }
    }
}
