using Lua;
using Lua.Standard;
using System;
using System.Globalization;
using System.Threading.Tasks;

namespace CelesteStudio.Communication.LibTAS.TAS;

public class LuaEnv {
    private LibTasCommunication comm => LibTasCommunication.Instance!;

    private LuaState lua;

    public LuaEnv() {
        lua = LuaState.Create();
        lua.OpenStandardLibraries();
    }

    private T? ReadMemory<T>(IntPtr addr) where T : unmanaged {
        if (comm.Pid is not { } pid) {
            Console.WriteLine("pid not set");
            return null;
        }

        if (MemoryAccess.Read(pid, addr, out T value) == null) {
            return value;
        } else {
            return null;
        }
    }


    private LuaFunction ReadMemoryFn<T>() where T : unmanaged, IConvertible {
        return new LuaFunction((cx, buffer, _) => {
            IntPtr addr = new(cx.GetArgument<long>(0));
            var val = ReadMemory<T>(addr);
            // Console.WriteLine($"read({addr}) = {val}");
            buffer.Span[0] = (double)Convert.ChangeType(val ?? default, typeof(double));
            return new ValueTask<int>(1);
        });
    }

    public async Task Load() {
        const string luaFile =
            "/home/jakob/.local/share/Steam/steamapps/common/Hollow Knight/HollowKnightTasInfo_v2_52.lua";

        lua.Environment["memory"] = new LuaTable {
            ["readu64"] = ReadMemoryFn<ulong>(),
            ["readu32"] = ReadMemoryFn<uint>(),
            ["readu16"] = ReadMemoryFn<ushort>(),
            ["readu8"] = ReadMemoryFn<byte>(),
        };


        lua.Environment["gui"] = new LuaTable {
            ["resolution"] = new LuaFunction((_, buffer, _) => {
                (int w, int h) = comm.LuaResolution();
                buffer.Span[0] = w;
                buffer.Span[1] = h;
                return new ValueTask<int>(2);
            }),
            ["text"] = new LuaFunction((cx, buffer, _) => {
                float x = ToNumber(cx.Arguments[0]);
                float y = ToNumber(cx.Arguments[1]);
                string text = cx.GetArgument<string>(2);
                int? color = cx.ArgumentCount > 3 ? ToInt(cx.Arguments[3]) : null;
                float? anchorX = cx.ArgumentCount > 4 ? cx.GetArgument<float>(4) : null;
                float? anchorY = cx.ArgumentCount > 5 ? cx.GetArgument<float>(5) : null;
                float? fontSize = cx.ArgumentCount > 6 ? cx.GetArgument<float>(6) : null;
                bool? monospace = cx.ArgumentCount > 7 ? cx.GetArgument<int>(7) != 0 : null;
                // TODO color
                comm.LuaText(x, y, text, int.MaxValue, anchorX, anchorY, fontSize, monospace);

                return new ValueTask<int>(0);
            }),
            ["line"] = new LuaFunction((cx, buffer, _) => {
                float x0 = ToNumber(cx.Arguments[0]);
                float y0 = ToNumber(cx.Arguments[1]);
                float x1 = ToNumber(cx.Arguments[2]);
                float y1 = ToNumber(cx.Arguments[3]);
                int? color = cx.ArgumentCount > 4 ? ToInt(cx.Arguments[4]) : null;
                comm.LuaLine(x0, y0, x1, y1, color);

                return new ValueTask<int>(0);
            }),
            ["ellipse"] = new LuaFunction((cx, buffer, _) => {
                float centerX = ToNumber(cx.Arguments[0]);
                float centerY = ToNumber(cx.Arguments[1]);
                float radiusX = ToNumber(cx.Arguments[2]);
                float radiusY = ToNumber(cx.Arguments[3]);
                float? thickness = cx.ArgumentCount > 4 ? ToNumber(cx.Arguments[4]) : null;
                int? color = cx.ArgumentCount > 5 ? ToInt(cx.Arguments[5]) : null;
                int? filled = cx.ArgumentCount > 6 ? ToInt(cx.Arguments[6]) : null;
                comm.LuaEllipse(centerX, centerY, radiusX, radiusY, thickness, color, filled);

                return new ValueTask<int>(0);
            }),
            ["window"] = Todo("gui.window"),
            ["pixel"] = Todo("gui.pixel"),
            ["rectangle"] = Todo("gui.rectangle"),
            ["quad"] = Todo("gui.quad"),
            ["ellipse"] = Todo("gui.ellipse"),
        };
        lua.Environment["print"] = new LuaFunction((cx, _, _) => {
            foreach (var arg in cx.Arguments) {
                Console.WriteLine(arg);
            }

            return new ValueTask<int>(0);
        });

        await lua.DoFileAsync(luaFile);
        return;
    }

    private static float ToNumber(LuaValue value) {
        if (value.TryRead(out string str)) {
            return float.Parse(str);
        }

        return value.Read<float>();
    }

    private static int ToInt(LuaValue value) {
        if (value.TryRead(out string str)) {
            if (str.StartsWith("0x")) {
                return int.Parse(str[2..], NumberStyles.HexNumber);
            }

            return int.Parse(str);
        }

        return value.Read<int>();
    }

    public void OnPaint() {
        try {
            var onPaint = lua.Environment["onPaint"].Read<LuaFunction>();
            onPaint.InvokeAsync(lua, []).AsTask().Wait();
        } catch (Exception e) {
            Console.WriteLine($"Error running onPaint: {e}");
            // Console.WriteLine($"Inner: {e.InnerException}");
        }
    }


    private LuaFunction Todo(string name) {
        return new LuaFunction((cx, _, _) => {
            Console.Write($"TODO: {name}(");
            foreach (var arg in cx.Arguments) {
                Console.Write(arg);
                Console.Write(",");
            }

            Console.WriteLine(")");
            return new ValueTask<int>(0);
        });
    }
}
