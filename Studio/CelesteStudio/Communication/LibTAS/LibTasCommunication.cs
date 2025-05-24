using CelesteStudio.Communication.LibTAS.TAS;
using CelesteStudio.Editing.ContextActions;
using CelesteStudio.Tool;
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
using TAS.Utils;

// ReSharper disable UnusedVariable

namespace CelesteStudio.Communication.LibTAS;

public sealed class LibTasCommunication(
    Socket socket,
    NetworkStream stream,
    BinaryReader reader,
    BinaryWriter writer
) : IDisposable {
    private const string SocketPath = "/tmp/libTAS.socket";

    private bool configChanged;
    private SharedConfig config = new();

    public static LibTasCommunication? Instance;

    public void SendHotkey(HotkeyID hotkey) {
        configChanged = true;

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
                    if (e is not SocketException { ErrorCode: 111}) {
                        Console.WriteLine($"eption in libTAS thread: {e.GetType().Name}, restarting in 1s");
                    }

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

        return new LibTasCommunication(s, stream, reader, writer);
    }


    private void GameLoop() {
        InitProcessMessages();

        while (true) {
            bool exit = StartFrameMessages();
            if (exit) {
                LoopExit();
                break;
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

                    WriterMarker($"frame: {Manager.Controller.FilePath} {Manager.Controller.CurrentFrameInTas + 1}");

                    if (endInnerLoop) {
                        break;
                    }

                    SleepSendPreview();
                }
            }


            bool managerRunning = Manager.CurrState is Manager.State.Running or Manager.State.FrameAdvance;
            if (managerRunning != config.Running) {
                Console.WriteLine($"exporting running: {managerRunning}");
                UpdateConfig(() => config.Running = managerRunning);
            }

            var inputFrame = InputHelper.LastInputFrame;

            uint[] keys = inputFrame.Actions.Sorted()
                .Select(ActionToXcbSym)
                .OfType<uint>()
                .ToArray();

            SendInput(keys);

            EndFrameMessages();
        }
    }

    private uint? ActionToXcbSym(Actions action) {
        // https://www.cl.cam.ac.uk/~mgk25/ucs/keysymdef.h
        uint? key = action switch {
            Actions.None => null,
            Actions.Left => 0xff51,
            Actions.Right => 0xff53,
            Actions.Up => 0xff52,
            Actions.Down => 0xff54,
            _ => null,
        };
        if (key is null) {
            Console.WriteLine($"Unhandled key: {action}");
        }
        return key;
    }
    

    private void LoopExit() {
    }

    private void SendInput(uint[] keys, bool preview = false) {
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
        // misc inputs
        // events


        WriteMessage(MessageId.MSGN_END_INPUTS);
    }


    private void InitProcessMessages() {
        var message = ReceiveMessage();
        while (message != MessageId.MSGB_END_INIT) {
            Console.WriteLine($"Received {message}");

            switch (message) {
                case MessageId.MSGB_PID_ARCH:
                    int pid = reader.ReadInt32();
                    int addrSize = reader.ReadInt32();
                    Console.WriteLine($"Arch: {pid} {addrSize}");
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
        config.Osd = true;
        Manager.EnableRun();
        Manager.CurrState = Manager.NextState = Manager.State.Paused;

        WriteConfig(config);

        WriteMessage(MessageId.MSGN_END_INIT);
    }

    private bool StartFrameMessages() {
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
                    ulong framecount = reader.ReadUInt64();
                    ulong currentTimeSec = reader.ReadUInt64();
                    ulong currentTimeNs = reader.ReadUInt64();
                    ulong currentRealtimeSec = reader.ReadUInt64();
                    ulong currentRealtimeNs = reader.ReadUInt64();
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

                default:
                    Console.WriteLine($"Unhandled frame message: {message}");
                    break;
            }

            message = ReceiveMessage();
        }

        WriteMessage(MessageId.MSGN_START_FRAMEBOUNDARY);
        return false;
    }


    private void EndFrameMessages() {
        if (configChanged) {
            WriteConfig(config);
            configChanged = false;
            Console.WriteLine($"updating config {config.Running}");
        }

        WriteMessage(MessageId.MSGN_END_FRAMEBOUNDARY);
    }

    private void SleepSendPreview() {
        Thread.Sleep(33);

        // TODO preview inputs

        WriteMessage(MessageId.MSGN_EXPOSE);
    }

    private void WriteMessage(MessageId message) {
        writer.Write((int)message);
    }

    private void WriteString(string msg) {
        writer.Write(msg.Length);
        writer.Write(Encoding.UTF8.GetBytes(msg));
    }

    private void WriterMarker(string msg) {
        WriteMessage(MessageId.MSGN_MARKER);
        WriteString(msg);
    }

    private void WriteConfig(SharedConfig config) {
        WriteMessage(MessageId.MSGN_CONFIG);
        byte[] bin = MemoryPackSerializer.Serialize(config);
        writer.Write(bin);
    }

    private string ReceiveString() {
        uint size = reader.ReadUInt32();
        return Encoding.UTF8.GetString(reader.ReadBytes((int)size));
    }

    private MessageId ReceiveMessage() {
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
internal enum MessageId {
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
