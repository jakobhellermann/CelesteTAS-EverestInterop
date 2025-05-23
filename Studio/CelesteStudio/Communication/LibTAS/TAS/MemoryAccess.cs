using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// ReSharper disable InconsistentNaming

namespace CelesteStudio.Communication.LibTAS.TAS;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct iovec {
    public void* iov_base;
    public int iov_len;
}

public static class MemoryAccess {
    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int process_vm_readv(int pid,
        iovec* local_iov,
        ulong liovcnt,
        iovec* remote_iov,
        ulong riovcnt,
        ulong flags);

    [DllImport("libc")]
    private static extern unsafe int process_vm_writev(int pid,
        iovec* local_iov,
        ulong liovcnt,
        iovec* remote_iov,
        ulong riovcnt,
        ulong flags);

    public static unsafe int? Read<T>(int pid, IntPtr address, out T value) where T : unmanaged {
        int size = Unsafe.SizeOf<T>();
        byte* ptr = stackalloc byte[size];
        var localIo = new iovec {
            iov_base = ptr,
            iov_len = size,
        };
        var remoteIo = new iovec {
            iov_base = address.ToPointer(),
            iov_len = size,
        };


        int res = process_vm_readv(pid, &localIo, 1, &remoteIo, 1, 0);
        value = *(T*)ptr;

        if (res == -1) {
            return Marshal.GetLastWin32Error();
        }

        return null;
    }

    public static unsafe int? Write<T>(int pid, T value, IntPtr address) where T : unmanaged {
        var ptr = &value;
        int size = Unsafe.SizeOf<T>();
        var localIo = new iovec {
            iov_base = ptr,
            iov_len = size,
        };
        var remoteIo = new iovec {
            iov_base = address.ToPointer(),
            iov_len = size,
        };
        int res = process_vm_writev(pid, &localIo, 1, &remoteIo, 1, 0);
        if (res == -1) {
            return Marshal.GetLastWin32Error();
        }

        return null;
    }
}
