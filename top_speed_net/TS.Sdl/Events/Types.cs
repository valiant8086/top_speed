using System;
using System.Runtime.InteropServices;
using TS.Sdl.Interop;

namespace TS.Sdl.Events
{
    public enum EventType : uint
    {
        First = 0,
        Quit = 0x100,
        // The compositor asking for the window's contents again. Wayland shows a window only while
        // it holds a buffer, so the frame has to be drawn again whenever this arrives.
        WindowExposed = 0x204,
        KeyDown = 0x300,
        KeyUp,
        TextEditing,
        TextInput,
        KeymapChanged,
        KeyboardAdded,
        KeyboardRemoved,
        TextEditingCandidates,
        ScreenKeyboardShown,
        ScreenKeyboardHidden,
        MouseMotion = 0x400,
        MouseButtonDown,
        MouseButtonUp,
        MouseWheel,
        MouseAdded,
        MouseRemoved,
        JoystickAxisMotion = 0x600,
        JoystickBallMotion,
        JoystickHatMotion,
        JoystickButtonDown,
        JoystickButtonUp,
        JoystickAdded,
        JoystickRemoved,
        JoystickBatteryUpdated,
        JoystickUpdateComplete,
        GamepadAxisMotion = 0x650,
        GamepadButtonDown,
        GamepadButtonUp,
        GamepadAdded,
        GamepadRemoved,
        GamepadRemapped,
        GamepadTouchpadDown,
        GamepadTouchpadMotion,
        GamepadTouchpadUp,
        GamepadSensorUpdate,
        GamepadUpdateComplete,
        GamepadSteamHandleUpdated,
        FingerDown = 0x700,
        FingerUp,
        FingerMotion,
        FingerCanceled,
        PinchBegin = 0x710,
        PinchUpdate,
        PinchEnd,
        SensorUpdate = 0x1200,
        PollSentinel = 0x7F00,
        User = 0x8000,
        Last = 0xFFFF
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CommonEvent
    {
        public uint Type;
        private uint _reserved;
        public ulong Timestamp;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KeyboardEvent
    {
        public EventType Type;
        private uint _reserved;
        public ulong Timestamp;
        public uint WindowId;
        public uint Which;
        public Input.Scancode Scancode;
        public int Key;
        public Input.Keymod Mod;
        public ushort Raw;
        [MarshalAs(UnmanagedType.I1)] public bool Down;
        [MarshalAs(UnmanagedType.I1)] public bool Repeat;
        private byte _padding1;
        private byte _padding2;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TextEditingEvent
    {
        public EventType Type;
        private uint _reserved;
        public ulong Timestamp;
        public uint WindowId;
        public IntPtr TextPointer;
        public int Start;
        public int Length;

        public string Text => Utf8.FromNative(TextPointer) ?? string.Empty;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TextEditingCandidatesEvent
    {
        public EventType Type;
        private uint _reserved;
        public ulong Timestamp;
        public uint WindowId;
        public IntPtr CandidatesPointer;
        public int CandidateCount;
        public int SelectedCandidate;
        [MarshalAs(UnmanagedType.I1)] public bool Horizontal;
        private byte _padding1;
        private byte _padding2;
        private byte _padding3;

        public string[] GetCandidates()
        {
            if (CandidatesPointer == IntPtr.Zero || CandidateCount <= 0)
                return Array.Empty<string>();

            var values = new string[CandidateCount];
            for (var i = 0; i < CandidateCount; i++)
            {
                var pointer = Marshal.ReadIntPtr(CandidatesPointer, i * IntPtr.Size);
                values[i] = Utf8.FromNative(pointer) ?? string.Empty;
            }

            return values;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TextInputEvent
    {
        public EventType Type;
        private uint _reserved;
        public ulong Timestamp;
        public uint WindowId;
        public IntPtr TextPointer;

        public string Text => Utf8.FromNative(TextPointer) ?? string.Empty;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct JoyDeviceEvent
    {
        public EventType Type;
        private uint _reserved;
        public ulong Timestamp;
        public uint Which;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct JoyBatteryEvent
    {
        public EventType Type;
        private uint _reserved;
        public ulong Timestamp;
        public uint Which;
        public Input.PowerState State;
        public int Percent;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct JoyAxisEvent
    {
        public EventType Type;
        private uint _reserved;
        public ulong Timestamp;
        public uint Which;
        public byte Axis;
        private byte _padding1;
        private byte _padding2;
        private byte _padding3;
        public short Value;
        private byte _padding4;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct JoyHatEvent
    {
        public EventType Type;
        private uint _reserved;
        public ulong Timestamp;
        public uint Which;
        public byte Hat;
        public byte Value;
        private byte _padding1;
        private byte _padding2;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct JoyButtonEvent
    {
        public EventType Type;
        private uint _reserved;
        public ulong Timestamp;
        public uint Which;
        public byte Button;
        [MarshalAs(UnmanagedType.I1)] public bool Down;
        private byte _padding1;
        private byte _padding2;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GamepadDeviceEvent
    {
        public EventType Type;
        private uint _reserved;
        public ulong Timestamp;
        public uint Which;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GamepadAxisEvent
    {
        public EventType Type;
        private uint _reserved;
        public ulong Timestamp;
        public uint Which;
        public byte Axis;
        private byte _padding1;
        private byte _padding2;
        private byte _padding3;
        public short Value;
        private ushort _padding4;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GamepadButtonEvent
    {
        public EventType Type;
        private uint _reserved;
        public ulong Timestamp;
        public uint Which;
        public byte Button;
        [MarshalAs(UnmanagedType.I1)] public bool Down;
        private byte _padding1;
        private byte _padding2;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TouchFingerEvent
    {
        public EventType Type;
        private uint _reserved;
        public ulong Timestamp;
        public ulong TouchId;
        public ulong FingerId;
        public float X;
        public float Y;
        public float DX;
        public float DY;
        public float Pressure;
        public uint WindowId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PinchFingerEvent
    {
        public EventType Type;
        private uint _reserved;
        public ulong Timestamp;
        public float Scale;
        public uint WindowId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct SensorEvent
    {
        public EventType Type;
        private uint _reserved;
        public ulong Timestamp;
        public uint Which;
        public fixed float Data[6];
        public ulong SensorTimestamp;
    }

    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct Event
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(0)] public CommonEvent Common;
        [FieldOffset(0)] public KeyboardEvent Keyboard;
        [FieldOffset(0)] public TextEditingEvent TextEditing;
        [FieldOffset(0)] public TextEditingCandidatesEvent TextEditingCandidates;
        [FieldOffset(0)] public TextInputEvent TextInput;
        [FieldOffset(0)] public JoyDeviceEvent JoyDevice;
        [FieldOffset(0)] public JoyBatteryEvent JoyBattery;
        [FieldOffset(0)] public JoyAxisEvent JoyAxis;
        [FieldOffset(0)] public JoyHatEvent JoyHat;
        [FieldOffset(0)] public JoyButtonEvent JoyButton;
        [FieldOffset(0)] public GamepadDeviceEvent GamepadDevice;
        [FieldOffset(0)] public GamepadAxisEvent GamepadAxis;
        [FieldOffset(0)] public GamepadButtonEvent GamepadButton;
        [FieldOffset(0)] public TouchFingerEvent TouchFinger;
        [FieldOffset(0)] public PinchFingerEvent Pinch;
        [FieldOffset(0)] public SensorEvent Sensor;
        [FieldOffset(0)] private fixed byte _padding[128];
    }
}
