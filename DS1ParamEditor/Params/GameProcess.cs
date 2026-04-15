using System;
using System.Diagnostics;
using System.Threading;
using SoulsFormats;
using Kernel32 = PropertyHook.Kernel32;

namespace DS1ParamEditor
{
    /// <summary>
    /// Represents an active connection to the game process for param operations.
    /// Provides typed param read/write operations using ParamHook.
    /// </summary>
    public sealed class GameProcess : IDisposable
    {
        private readonly Process _process;
        private readonly ParamHook _paramHook;

        public string ProcessName { get; }
        public bool IsAlive => !_process.HasExited;
        public ParamHook Hook => _paramHook;

        private GameProcess(Process process, ParamHook paramHook)
        {
            _process = process;
            _paramHook = paramHook;
            ProcessName = process.ProcessName;
        }

        /// <summary>Tries to attach to a running process using ParamHook. Returns null if not found.</summary>
        public static GameProcess? TryAttach(ParamHook paramHook)
        {
            if (!paramHook.Hooked) return null;
            return new GameProcess(paramHook.Process, paramHook);
        }

        // ── Scan ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Scans memory for the given byte pattern.
        /// Uses ParamHook's internal scanner.
        /// </summary>
        public nint Scan(byte[] pattern, string? mask = null, CancellationToken ct = default)
        {
            return _paramHook.ScanAndCache("_temp_scan", pattern, 0, ct);
        }

        // ── Read / Write ──────────────────────────────────────────────────────

        /// <summary>Reads a raw byte block from game memory.</summary>
        public byte[]? ReadBytes(nint address, int length)
        {
            return _paramHook.ReadBytes(address, length);
        }

        public object? ReadField(nint address, PARAMDEF.DefType type) => type switch
        {
            PARAMDEF.DefType.s8      => Kernel32.ReadSByte(_paramHook.Handle, (IntPtr)address),
            PARAMDEF.DefType.u8      => Kernel32.ReadByte(_paramHook.Handle, (IntPtr)address),
            PARAMDEF.DefType.s16     => Kernel32.ReadInt16(_paramHook.Handle, (IntPtr)address),
            PARAMDEF.DefType.u16     => Kernel32.ReadUInt16(_paramHook.Handle, (IntPtr)address),
            PARAMDEF.DefType.s32     => Kernel32.ReadInt32(_paramHook.Handle, (IntPtr)address),
            PARAMDEF.DefType.b32     => Kernel32.ReadInt32(_paramHook.Handle, (IntPtr)address),
            PARAMDEF.DefType.u32     => Kernel32.ReadUInt32(_paramHook.Handle, (IntPtr)address),
            PARAMDEF.DefType.f32     => Kernel32.ReadSingle(_paramHook.Handle, (IntPtr)address),
            PARAMDEF.DefType.angle32 => Kernel32.ReadSingle(_paramHook.Handle, (IntPtr)address),
            PARAMDEF.DefType.f64     => Kernel32.ReadDouble(_paramHook.Handle, (IntPtr)address),
            _ => null
        };

        public bool WriteField(nint address, PARAMDEF.DefType type, object value)
        {
            try
            {
                IntPtr handle = _paramHook.Handle;
                IntPtr addr = (IntPtr)address;
                
                switch (type)
                {
                    case PARAMDEF.DefType.s8:      return Kernel32.WriteSByte(handle, addr, Convert.ToSByte(value));
                    case PARAMDEF.DefType.u8:      return Kernel32.WriteByte(handle, addr, Convert.ToByte(value));
                    case PARAMDEF.DefType.s16:     return Kernel32.WriteInt16(handle, addr, Convert.ToInt16(value));
                    case PARAMDEF.DefType.u16:     return Kernel32.WriteUInt16(handle, addr, Convert.ToUInt16(value));
                    case PARAMDEF.DefType.s32:
                    case PARAMDEF.DefType.b32:     return Kernel32.WriteInt32(handle, addr, Convert.ToInt32(value));
                    case PARAMDEF.DefType.u32:     return Kernel32.WriteUInt32(handle, addr, Convert.ToUInt32(value));
                    case PARAMDEF.DefType.f32:
                    case PARAMDEF.DefType.angle32: return Kernel32.WriteSingle(handle, addr, Convert.ToSingle(value));
                    case PARAMDEF.DefType.f64:     return Kernel32.WriteDouble(handle, addr, Convert.ToDouble(value));
                    default: return false;
                }
            }
            catch { return false; }
        }

        public void Dispose()
        {
            // ParamHook manages the process lifecycle
        }
    }
}
