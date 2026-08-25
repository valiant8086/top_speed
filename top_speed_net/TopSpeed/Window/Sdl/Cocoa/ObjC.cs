using System;
using System.Runtime.InteropServices;

namespace TopSpeed.Windowing.Sdl.Cocoa
{
    /// <summary>
    /// The slice of the Objective-C runtime needed to put a real Cocoa control inside the SDL
    /// window. This talks to libobjc directly rather than through MonoMac: MonoMac has to be
    /// started with NSApplication.Init, which Eto used to do for us and SDL does not, and the
    /// point of the SDL window host is to stop depending on Eto on macOS.
    ///
    /// Nothing here is macOS-only at compile time - the imports simply are never reached
    /// anywhere else, because the only caller checks the platform first.
    /// </summary>
    internal static class ObjC
    {
        private const string LibObjC = "/usr/lib/libobjc.dylib";

        [DllImport(LibObjC, EntryPoint = "objc_getClass")]
        internal static extern IntPtr GetClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

        [DllImport(LibObjC, EntryPoint = "sel_registerName")]
        internal static extern IntPtr Selector([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

        [DllImport(LibObjC, EntryPoint = "objc_allocateClassPair")]
        internal static extern IntPtr AllocateClassPair(IntPtr superclass, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, IntPtr extraBytes);

        [DllImport(LibObjC, EntryPoint = "objc_registerClassPair")]
        internal static extern void RegisterClassPair(IntPtr cls);

        [DllImport(LibObjC, EntryPoint = "class_addMethod")]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool AddMethod(IntPtr cls, IntPtr selector, IntPtr implementation, [MarshalAs(UnmanagedType.LPUTF8Str)] string types);

        // objc_msgSend takes the argument list of whatever method is being called, so it has to be
        // imported once per shape rather than once. Getting the shape wrong corrupts the call, so
        // each of these is named for exactly what it sends.

        [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
        internal static extern IntPtr Send(IntPtr receiver, IntPtr selector);

        [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
        internal static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr argument);

        [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
        internal static extern IntPtr SendRect(IntPtr receiver, IntPtr selector, CGRect frame);

        [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
        internal static extern void SendVoid(IntPtr receiver, IntPtr selector);

        [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
        internal static extern void SendVoid(IntPtr receiver, IntPtr selector, IntPtr argument);

        [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
        internal static extern void SendVoidRect(IntPtr receiver, IntPtr selector, CGRect frame);

        [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
        internal static extern void SendVoidBool(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool argument);

        [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
        internal static extern void SendVoidUInt(IntPtr receiver, IntPtr selector, ulong argument);

        [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool SendBool(IntPtr receiver, IntPtr selector, IntPtr argument);

        [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool SendBool(IntPtr receiver, IntPtr selector);

        /// <summary>Allocates and initializes an NSString the caller owns and must release.</summary>
        internal static IntPtr NewString(string? value)
        {
            var utf8 = Marshal.StringToCoTaskMemUTF8(value ?? string.Empty);
            try
            {
                var instance = Send(GetClass("NSString"), Selector("alloc"));
                return Send(instance, Selector("initWithUTF8String:"), utf8);
            }
            finally
            {
                Marshal.FreeCoTaskMem(utf8);
            }
        }

        /// <summary>Reads an NSString without taking ownership of it.</summary>
        internal static string ReadString(IntPtr nsString)
        {
            if (nsString == IntPtr.Zero)
                return string.Empty;

            var utf8 = Send(nsString, Selector("UTF8String"));
            return utf8 == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(utf8) ?? string.Empty;
        }

        internal static void Release(IntPtr instance)
        {
            if (instance != IntPtr.Zero)
                SendVoid(instance, Selector("release"));
        }
    }

    /// <summary>Cocoa's rectangle: four CGFloats, which are doubles on every 64 bit Mac.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct CGRect
    {
        public double X;
        public double Y;
        public double Width;
        public double Height;

        public CGRect(double x, double y, double width, double height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }
    }
}
