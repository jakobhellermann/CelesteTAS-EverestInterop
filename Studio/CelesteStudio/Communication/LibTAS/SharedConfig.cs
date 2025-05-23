using MemoryPack;
using System;

#pragma warning disable CS0169 // Field is never used

namespace CelesteStudio.Communication.LibTAS;

[Flags]
public enum SaveStateFlags {
    Incremental = 0x01, Ram = 0x02, Compressed = 0x08, Present = 0x10, Fork = 0x20,
}

[Flags]
public enum FastForwardMode {
    Sleep = 0x1,
    Mixing = 0x02,
}
[Flags]
public enum FastForwardRender {
    All,
    Some,
    No,
}

// ReSharper disable UnusedVariable
[MemoryPackable]
public partial struct SharedConfig {
    public int SpeedDivisor = 1;
    public FastForwardMode FastForwardMode = FastForwardMode.Sleep | FastForwardMode.Mixing;
    public FastForwardRender FastforwardRender = FastForwardRender.Some;
    public int Recording = 0;
    public ulong MovieFramecount = 0;
    public int LoggingStatus = 1;
    public uint LoggingLevel = 3;
    public uint LoggingIncludeFlags = uint.MaxValue;
    public uint LoggingExcludeFlags = 0;
    public uint InitialFramerateNum = 60;
    public uint InitialFramerateDen = 1;
    public int NbControllers = 0;
    public int AudioBitdepth = 16;
    public int AudioChannels = 2;
    public int AudioFrequency = 44100;
    public float AudioGain = 1.0f;
    public int VideoCodec = 0;
    public int VideoBitrate = 4000;
    public int VideoFramerate = 60;
    public int AudioCodec = 0;
    public int AudioBitrate = 128;
    public int MainGettimesThreshold0 = -1;
    public int MainGettimesThreshold1 = -1;
    public int MainGettimesThreshold2 = -1;
    public int MainGettimesThreshold3 = -1;
    public int MainGettimesThreshold4 = -1;
    public int MainGettimesThreshold5 = -1;
    public int MainGettimesThreshold6 = -1;
    public int MainGettimesThreshold7 = -1;
    public int MainGettimesThreshold8 = -1;
    public int MainGettimesThreshold9 = -1;
    public int SecGettimesThreshold0 = -1;
    public int SecGettimesThreshold1 = -1;
    public int SecGettimesThreshold2 = -1;
    public int SecGettimesThreshold3 = -1;
    public int SecGettimesThreshold4 = -1;
    public int SecGettimesThreshold5 = -1;
    public int SecGettimesThreshold6 = -1;
    public int SecGettimesThreshold7 = -1;
    public int SecGettimesThreshold8 = -1;
    public int SecGettimesThreshold9 = -1;

    public long InitialTimeSec = 1;
    public long InitialTimeNsec = 0;

    public long InitialMonotonicTimeSec = 1;
    public long InitialMonotonicTimeNsec = 0;

    public int ScreenWidth = 0;
    public int ScreenHeight = 0;

    public int DebugState = 0;
    public int Locale = 0;
    public int AsyncEvents = 0;
    public int WaitTimeout = 0;
    public int SleepHandling = 1;
    public int GameSpecificTiming = 0;
    public int GameSpecificSync = 0;


    public SaveStateFlags SavestateSettings = SaveStateFlags.Compressed;
    public ulong BusyLoopHash = 0;
    public bool Running = false;
    public bool Fastforward = false;
    public bool AvDumping = false;
    public bool MouseSupport = true;
    public bool MouseModeRelative = false;
    public bool MousePreventWarp = false;
    public bool Osd = false;
    public bool OsdEncode = false;
    public bool PreventSavefiles = true;
    public bool WriteSavefilesOnExit = false;
    public bool AudioMute = true;
    public bool AudioDisabled = false;
    public bool OpenalSoft = true;
    public bool VirtualSteam = false;
    public bool OpenglSoft = false;
    public bool OpenglPerformance = false;
    public bool BusyloopDetection = false;
    public bool TimeTrace = false;
    public bool SigintUponLaunch = false;
    public bool HasClone3SetTid = false;
    public bool CanSetLastPid = false;

    public SharedConfig() {
    }
}
