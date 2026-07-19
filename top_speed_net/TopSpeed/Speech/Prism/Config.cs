using System;
using System.Runtime.InteropServices;

namespace TopSpeed.Speech.Prism
{
    // Mirrors PrismConfig (PRISM_CONFIG_VERSION 3) from prism.h. The struct is
    // returned by value from prism_config_init and passed by ref to prism_init,
    // so its layout must match the native ABI exactly or initialization fails.
    [StructLayout(LayoutKind.Sequential)]
    internal struct Config
    {
        public byte Version;
        public IntPtr Registry;
        public IntPtr AvailabilityCallback;
        public IntPtr AvailabilityUserdata;
        public uint AvailabilityPollIntervalMs;
        public uint AvailabilityDebounceSamples;
        public uint AvailabilityBackoffMaxMs;
        [MarshalAs(UnmanagedType.I1)]
        public bool AvailabilityAutoPowerManage;
    }
}
