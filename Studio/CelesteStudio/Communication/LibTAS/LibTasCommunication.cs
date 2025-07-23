using CelesteStudio.Communication.LibTAS.TAS;
using MemoryPack;
using StudioCommunication;
using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TAS;
using TAS.EverestInterop;
using TAS.Module;
using TAS.Playback;
using TAS.Utils;

// ReSharper disable UnusedVariable

namespace CelesteStudio.Communication.LibTAS;

public sealed class LibTasCommunication(
    Socket socket,
    NetworkStream stream,
    BinaryReader reader,
    BinaryWriter writer,
    LuaEnv lua
) : IDisposable {
    private const string SocketPath = "/tmp/libTAS.socket";

    private bool configChanged;
    public SharedConfig Config = new();

    public int? Pid;

    public static LibTasCommunication? Instance;

    public void SendPath(string path) {
        Manager.Controller.FilePath = path;
        Manager.EnableRunLater();
    }

    public void SendHotkey(HotkeyID hotkey) {
        Hotkeys.AllHotkeys[hotkey].OverrideCheck = true;
    }

    private void UpdateConfig(Action action) {
        action();
        configChanged = true;
    }


    public static void Start() {
        new CelesteTasSettings();

        AttributeUtils.CollectOwnMethods<LoadAttribute>();
        AttributeUtils.CollectOwnMethods<LoadContentAttribute>();
        AttributeUtils.CollectOwnMethods<UnloadAttribute>();
        AttributeUtils.CollectOwnMethods<InitializeAttribute>();

        AttributeUtils.Invoke<LoadAttribute>();
        AttributeUtils.Invoke<LoadContentAttribute>();
        // AttributeUtils.CollectOwnMethods<UnloadAttribute>();
        AttributeUtils.Invoke<InitializeAttribute>();

        Task.Run(() => {
            while (true) {
                try {
                    Instance?.Dispose();
                    Instance = Connect();
                    Instance.GameLoop();
                    Instance = null;
                } catch (Exception e) {
                    if (e is not SocketException { ErrorCode: 111 }) {
                        Console.WriteLine($"Exception in libTAS thread: {e.GetType().Name}, restarting in 1s");
                        Manager.DisableRun();
                    }

                    // TODO don't need to
                    SavestateManager.Unload();

                    Thread.Sleep(1000);
                }
            }
        });
    }


    private static LibTasCommunication Connect() {
        var s = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        var endpoint = new UnixDomainSocketEndPoint(SocketPath);
        s.Connect(endpoint);

        var stream = new NetworkStream(s);
        var reader = new BinaryReader(stream, Encoding.UTF8);
        var writer = new BinaryWriter(stream);

        var lua = new LuaEnv();
        try {
            lua.Load().Wait();
        } catch (Exception e) {
            Console.WriteLine($"lua error: {e}");
        }

        return new LibTasCommunication(s, stream, reader, writer, lua);
    }


    private void GameLoop() {
        InitProcessMessages();

        while (true) {
            if (GameLoopInner()) break;
        }
    }

    private bool GameLoopInner() {
        bool exit = StartFrameMessages();
        if (exit) {
            LoopExit();
            return true;
        }

        if ( /* game_window */ true) {
            while (true) {
                // waitpid check if game running
                Manager.UpdateMeta();

                // bool endInnerLoop = config.Running; // || frameAdvance || quitting
                if (Manager.CurrState != Manager.NextState) {
                    Console.WriteLine($"{Manager.CurrState} -> {Manager.NextState}");
                }

                bool endInnerLoop = Manager.NextState is Manager.State.Running or Manager.State.FrameAdvance;

                Manager.Update();

                SendMarker($"frame: {Manager.Controller.FilePath} {Manager.Controller.CurrentFrameInTas + 1}");

                if (endInnerLoop) {
                    break;
                }

                SleepSendPreview();
            }
        }

        // ReSharper disable once CompareOfFloatsByEqualityOperator
        bool shouldFastForward = Manager.PlaybackSpeed != 1;
        if (shouldFastForward != Config.Fastforward) {
            Config.Fastforward = shouldFastForward;
            configChanged = true;
        }


        /*bool managerRunning = Manager.CurrState is Manager.State.Running or Manager.State.FrameAdvance;
        Console.WriteLine($"manager running: {managerRunning}");
        if (managerRunning != config.Running) {
            Console.WriteLine($"exporting running: {managerRunning}");
            UpdateConfig(() => config.Running = managerRunning);
        }*/

        if (InputHelper.LastInputFrame is { } inputFrame) {
            uint[] keys = inputFrame.Actions.Sorted()
                .Select(ActionToXcbSym)
                .OfType<uint>()
                .ToArray();

            uint? framerate = InputHelper.Framerate;
            SendInput(keys, InputHelper.Framerate ?? DefaultFramerate);
        }

        EndFrameMessages();
        
        return false;
    }

    private uint? ActionToXcbSym(Actions action) {
        // https://www.cl.cam.ac.uk/~mgk25/ucs/keysymdef.h
        uint? key = action switch {
            Actions.None => null,
            Actions.Left => 0xff51,
            Actions.Right => 0xff53,
            Actions.Up => 0xff52,
            Actions.Down => 0xff54,
            Actions.Confirm => 0xff0d, // Return
            Actions.Escape => 0xff1b,
            Actions.Inventory => 0x0041 + ('I' - 'A'),
            Actions.Jump => 0x0041 + ('Z' - 'A'),
            Actions.Dash => 0x0041 + ('C' - 'A'),
            Actions.DashOnly => 0x0041 + ('X' - 'A'),
            _ => null,
        };
        if (key is null) {
            Console.WriteLine($"Unhandled key: {action}");
        }

        return key;
    }


    private void LoopExit() {
    }

    private void SendInput(uint[] keys, uint framerate, bool preview = false) {
        WriteMessage(preview ? MessageId.MSGN_PREVIEW_INPUTS : MessageId.MSGN_ALL_INPUTS);

        const int maxkeys = 16;

        if (keys.Length > maxkeys) {
            Console.WriteLine("More keys than allowed per frame");
        }

        byte[] byteArray = new byte[maxkeys * 4];
        Buffer.BlockCopy(keys, 0, byteArray, 0, keys.Length * 4);
        writer.Write(byteArray);

        // pointer inputs
        // controller rinputs

        if (framerate != DefaultFramerate) { // TODO
            Console.WriteLine("send updated framerate");
            // uint framerateNum = newFramerate ?? 100; // TODO
            WriteMessage(MessageId.MSGN_MISC_INPUTS);
            const uint flags = 0;
            writer.Write(flags);
            writer.Write(1u);
            writer.Write(framerate);
            // TODO realtime
            writer.Write(0u);
            writer.Write(0u);
        }

        // events


        WriteMessage(MessageId.MSGN_END_INPUTS);
    }


    private void InitProcessMessages() {
        var message = ReceiveMessage();
        while (message != MessageId.MSGB_END_INIT) {
            Console.WriteLine($"Received {message}");

            switch (message) {
                case MessageId.MSGB_PID_ARCH:
                    Pid = reader.ReadInt32();
                    int addrSize = reader.ReadInt32();
                    Console.WriteLine($"Arch: {Pid} {addrSize}");
                    break;
                case MessageId.MSGB_GIT_COMMIT:
                    string commit = ReceiveString();
                    Console.WriteLine($"Commit: {commit}");
                    break;

                default:
                    Console.WriteLine($"Unhandled init message {message}");
                    break;
            }

            message = ReceiveMessage();
        }

        // config.Running = true;
        Config.Osd = true;
        Config.Fastforward = false;
        Config.InitialFramerateNum = DefaultFramerate;
        Config.Running = true;
        Config.FastforwardRender = FastForwardRender.Some;
        Config.VirtualSteam = true;
        // Manager.EnableRun();
        // Manager.CurrState = Manager.NextState = Manager.State.Paused;

        WriteConfig(Config);

        WriteMessage(MessageId.MSGN_END_INIT);
    }

    public uint DefaultFramerate = 100;

    private bool StartFrameMessages() {
        bool drawFrame = true;
        bool skipDrawFrame = false;
        // TODO

        var message = ReceiveMessage();
        while (message != MessageId.MSGB_START_FRAMEBOUNDARY) {
            switch (message) {
                case MessageId.MSGB_WINDOW_ID:
                    uint id = reader.ReadUInt32();
                    Console.WriteLine($"Window: {id}");
                    break;
                case MessageId.MSGB_ALERT_MSG:
                    string alert = reader.ReadString();
                    Console.WriteLine($"Alert: {alert}");
                    break;
                case MessageId.MSGB_FRAMECOUNT_TIME:
                    ReadDataFramecountTime();
                    // Console.WriteLine($"Frame count: {framecount}");
                    break;
                case MessageId.MSGB_GAMEINFO:
                    reader.ReadBytes(36);
                    break;
                case MessageId.MSGB_FPS:
                    float fps = reader.ReadSingle();
                    float lfps = reader.ReadSingle();
                    // Console.WriteLine($"FPS: {fps}, lFPS: {lfps}");
                    break;
                case MessageId.MSGB_SYMBOL_ADDRESS:
                    string symbol = ReceiveString();
                    const ulong addr = 0; // TODO
                    writer.Write(addr);
                    Console.WriteLine($"Symbol: {symbol}");
                    break;
                case MessageId.MSGB_QUIT:
                    return true;

                case MessageId.MSGN_LUA_PIXEL:
                    float x = reader.ReadSingle();
                    float y = reader.ReadSingle();
                    uint color = reader.ReadUInt32();
                    Console.WriteLine($"LuaPixel({x}, {y}, {color})");
                    break;

                case MessageId.MSGB_NONDRAW_FRAME:
                    drawFrame = false;
                    break;
                case MessageId.MSGB_SKIPDRAW_FRAME:
                    skipDrawFrame = true;
                    break;
                case MessageId.MSGN_USERQUIT:
                    break;

                default:
                    Console.WriteLine($"Unhandled frame message: {message}");
                    break;
            }

            message = ReceiveMessage();
        }

        if (drawFrame && !skipDrawFrame) {
            lua.OnPaint();
        }

        WriteMessage(MessageId.MSGN_START_FRAMEBOUNDARY);
        return false;
    }


    private void EndFrameMessages() {
        if (configChanged) {
            WriteConfig(Config);
            configChanged = false;
            Console.WriteLine($"updating config ff={Config.Fastforward}");
        }

        WriteMessage(MessageId.MSGN_END_FRAMEBOUNDARY);
    }

    private void SleepSendPreview() {
        Thread.Sleep(33);

        // TODO preview inputs

        WriteMessage(MessageId.MSGN_EXPOSE);
    }

    public void WriteMessage(MessageId message) {
        writer.Write((int)message);
    }

    private void WriteString(string msg) {
        writer.Write(msg.Length);
        writer.Write(Encoding.UTF8.GetBytes(msg));
    }

    public void SendMarker(string msg) {
        WriteMessage(MessageId.MSGN_MARKER);
        WriteString(msg);
    }

    public void SendOsdMessage(string msg) {
        WriteMessage(MessageId.MSGN_OSD_MSG);
        WriteString(msg);
    }

    public void SendSavestatePath(string path) {
        WriteMessage(MessageId.MSGN_SAVESTATE_PATH);
        WriteString(path);
    }

    public void SendSavestateIndex(int index) {
        WriteMessage(MessageId.MSGN_SAVESTATE_INDEX);
        writer.Write(index);
    }

    public bool SendSavestate() {
        WriteMessage(MessageId.MSGN_SAVESTATE);
        return ReceiveMessage() == MessageId.MSGB_SAVING_SUCCEEDED;
    }

    public bool SendLoadstate() {
        WriteMessage(MessageId.MSGN_LOADSTATE);
        var message = ReceiveMessage();

        if (message != MessageId.MSGB_LOADING_SUCCEEDED) {
            return false;
        }

        WriteConfig(Config);
        message = ReceiveMessage();
        if (message != MessageId.MSGB_FRAMECOUNT_TIME) {
            throw new Exception($"Got {message} instead of framecount after loading state");
        }

        ReadDataFramecountTime();

        return true;
    }

    public void SendExpose() {
        WriteMessage(MessageId.MSGN_EXPOSE);
    }

    public (int, int) LuaResolution() {
        WriteMessage(MessageId.MSGN_LUA_RESOLUTION);
        var message = ReceiveMessage();
        if (message != MessageId.MSGB_LUA_RESOLUTION) {
            Console.WriteLine($"Expected MSGB_LUA_RESOLUTION, got {message}");
            return (-1, -1);
        }

        int w = reader.ReadInt32();
        int h = reader.ReadInt32();
        return (w, h);
    }

    public void LuaText(
        float x,
        float y,
        string text,
        int? color = null,
        float? anchorX = null,
        float? anchorY = null,
        float? fontSize = null,
        bool? monospace = null
    ) {
        WriteMessage(MessageId.MSGN_LUA_TEXT);
        writer.Write(x);
        writer.Write(y);
        WriteString(text);
        writer.Write(color ?? int.MaxValue);
        writer.Write(anchorX ?? 0);
        writer.Write(anchorY ?? 0);
        writer.Write(fontSize ?? 16);
        writer.Write(monospace ?? false);
    }

    public void LuaLine(
        float x0,
        float y0,
        float x1,
        float y1,
        int? color
    ) {
        WriteMessage(MessageId.MSGN_LUA_LINE);
        writer.Write(x0);
        writer.Write(y0);
        writer.Write(x1);
        writer.Write(y1);
        writer.Write(color ?? int.MaxValue);
    }

    public void LuaEllipse(float centerX, float centerY, float radiusX, float radiusY, float? thickness, int? color, int? filled) {
        WriteMessage(MessageId.MSGN_LUA_ELLIPSE);
        writer.Write(centerX);
        writer.Write(centerY);
        writer.Write(radiusX);
        writer.Write(radiusY);
        writer.Write(thickness ?? 1);
        writer.Write(color ?? int.MaxValue);
        writer.Write(filled ?? 0);
    }

    private void ReadDataFramecountTime() {
        ulong framecount = reader.ReadUInt64();
        ulong currentTimeSec = reader.ReadUInt64();
        ulong currentTimeNs = reader.ReadUInt64();
        ulong currentRealtimeSec = reader.ReadUInt64();
        ulong currentRealtimeNs = reader.ReadUInt64();
    }

    public void WriteConfig(SharedConfig config) {
        WriteMessage(MessageId.MSGN_CONFIG);
        byte[] bin = MemoryPackSerializer.Serialize(config);
        writer.Write(bin);
    }

    private string ReceiveString() {
        uint size = reader.ReadUInt32();
        return Encoding.UTF8.GetString(reader.ReadBytes((int)size));
    }

    public MessageId ReceiveMessage() {
        return (MessageId)reader.ReadInt32();
    }

    public void Dispose() {
        reader.Dispose();
        writer.Dispose();
        stream.Dispose();
        socket.Dispose();
    }

}

// ReSharper disable InconsistentNaming
public enum MessageId {
    MSGB_START_FRAMEBOUNDARY,
    MSGN_START_FRAMEBOUNDARY,
    MSGB_FRAMECOUNT_TIME,
    MSGN_ALL_INPUTS,
    MSGN_POINTER_INPUTS,
    MSGN_SCALE_POINTER_INPUTS,
    MSGN_MISC_INPUTS,
    MSGN_EVENT_INPUTS,
    MSGN_CONTROLLER_INPUTS,
    MSGN_END_INPUTS,
    MSGN_PREVIEW_INPUTS,
    MSGN_CONFIG_SIZE,
    MSGN_CONFIG,
    MSGN_INITIAL_FRAMECOUNT_TIME,
    MSGN_END_FRAMEBOUNDARY,
    MSGB_QUIT,
    MSGN_USERQUIT,
    MSGB_PID_ARCH,
    MSGB_END_INIT,
    MSGN_END_INIT,
    MSGN_DUMP_FILE,
    MSGB_WINDOW_ID,
    MSGB_ALERT_MSG,
    MSGN_OSD_MSG,
    MSGN_SAVESTATE,
    MSGN_LOADSTATE,
    MSGB_SAVING_SUCCEEDED,
    MSGB_LOADING_SUCCEEDED,
    MSGN_SAVESTATE_PATH,
    MSGN_BASE_SAVESTATE_PATH,
    MSGN_SAVESTATE_INDEX,
    MSGN_BASE_SAVESTATE_INDEX,
    MSGB_ENCODE_FAILED,
    MSGN_STOP_ENCODE,
    MSGB_GAMEINFO,
    MSGN_EXPOSE,
    MSGB_FPS,
    MSGN_RAMWATCH,
    MSGB_ENCODING_SEGMENT,
    MSGN_ENCODING_SEGMENT,
    MSGN_STEAM_USER_DATA_PATH,
    MSGN_STEAM_REMOTE_STORAGE,
    MSGB_GIT_COMMIT,
    MSGB_GETTIME_BACKTRACE,
    MSGB_NONDRAW_FRAME,
    MSGB_SKIPDRAW_FRAME,
    MSGN_LUA_TEXT,
    MSGN_LUA_WINDOW,
    MSGN_LUA_PIXEL,
    MSGN_LUA_RECT,
    MSGN_LUA_LINE,
    MSGN_LUA_QUAD,
    MSGN_LUA_ELLIPSE,
    MSGN_LUA_RESOLUTION,
    MSGB_LUA_RESOLUTION,
    MSGB_SYMBOL_ADDRESS,
    MSGN_MARKER,
    MSGN_SCREENSHOT,
    MSGN_SDL_DYNAPI_ADDR,
    MSGN_UNITY_WAIT_ADDR,
}
// ReSharper restore InconsistentNaming
