using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ComponentModel.DataAnnotations;
using Vulkan;
using SharpGen.Runtime;
using System.Collections.Concurrent;
using System.Security.Cryptography.Xml;

namespace DS1ParamEditor
{
    public class AOBScanner
    {
        [DllImport("kernel32.dll")]
        public static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, out int lpNumberOfBytesRead);

        [DllImport("kernel32.dll")]
        public static extern int VirtualQueryEx(IntPtr hProcess, IntPtr lpAddress, out MEMORY_BASIC_INFORMATION lpBuffer, int dwLength);

        [DllImport("kernel32.dll")]
        public static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int nSize, out int lpNumberOfBytesWritten);

        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORY_BASIC_INFORMATION
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect;
            public IntPtr RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }

        private readonly Process process;
        private readonly IntPtr processHandle;

        public AOBScanner(Process targetProcess)
        {
            process = targetProcess;
            processHandle = process.Handle;
        }

        public IntPtr FindPattern(byte[] pattern, string mask, bool scanFromZero = true)
        {
            const int CHUNK_SIZE = 0x10000; // 64KB chunks (adjustable)
            int patternLength = pattern.Length;

            if (mask.Length != patternLength)
                throw new ArgumentException("Pattern and mask lengths must be equal");

            IntPtr currentAddress = scanFromZero ? IntPtr.Zero : process.MainModule.BaseAddress;
            MEMORY_BASIC_INFORMATION memInfo = new MEMORY_BASIC_INFORMATION();

            while (true)
            {
                if (VirtualQueryEx(processHandle, currentAddress, out memInfo, Marshal.SizeOf(memInfo)) == 0)
                    break;

                // Skip if we're at invalid memory or would overflow
                if (currentAddress.ToInt64() >= (long)(nint.MaxValue - (int)memInfo.RegionSize))
                    break;

                // Only scan readable & committed memory
                if ((memInfo.Protect & 0x04) != 0 && (memInfo.State & 0x1000) != 0 && memInfo.Protect != 0x01)
                {
                    IntPtr regionEnd = IntPtr.Add(memInfo.BaseAddress, (int)memInfo.RegionSize);
                    IntPtr chunkStart = memInfo.BaseAddress;

                    while (chunkStart.ToInt64() < regionEnd.ToInt64())
                    {
                        try
                        {
                            // Calculate chunk size (don't read past region end)
                            int chunkSize = (int)Math.Min(CHUNK_SIZE, regionEnd.ToInt64() - chunkStart.ToInt64());
                            byte[] buffer = new byte[chunkSize];
                            int bytesRead;

                            if (ReadProcessMemory(processHandle, chunkStart, buffer, chunkSize, out bytesRead))
                            {
                                // Only scan if we got enough data
                                if (bytesRead >= patternLength)
                                {
                                    for (int i = 0; i <= bytesRead - patternLength; i++)
                                    {
                                        bool found = true;
                                        for (int j = 0; j < patternLength; j++)
                                        {
                                            if (mask[j] != '?' && pattern[j] != buffer[i + j])
                                            {
                                                found = false;
                                                break;
                                            }
                                        }
                                        if (found)
                                            return IntPtr.Add(chunkStart, i);
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // Skip inaccessible chunks
                        }

                        // Move to next chunk (align to CHUNK_SIZE boundary)
                        chunkStart = IntPtr.Add(chunkStart, CHUNK_SIZE);
                    }
                }

                // Move to next memory region
                IntPtr nextAddress = IntPtr.Add(memInfo.BaseAddress, (int)memInfo.RegionSize);
                if (nextAddress.ToInt64() <= currentAddress.ToInt64())
                    break;
                currentAddress = nextAddress;
                Console.WriteLine(currentAddress);
            }

            return IntPtr.Zero;
        }

        public byte[] ReadBytes(IntPtr address, int length)
        {
            try
            {
                byte[] buffer = new byte[length];
                int bytesRead;

                if (ReadProcessMemory(processHandle, address, buffer, length, out bytesRead) && bytesRead == length)
                {
                    return buffer;
                }
            }
            catch
            {
                // ignored
            }
            return null;
        }

        public T Read<T>(IntPtr address) where T : struct
        {
            int size = Marshal.SizeOf(typeof(T));

            byte[] bytes = ReadBytes(address, size);

            if (bytes != null)
            {
                GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                try
                {
                    return Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject());
                }
                finally
                {
                    handle.Free();
                }
            }

            return default(T);
        }

        // Convenience methods for common types
        public sbyte ReadSByte(IntPtr address) => Read<sbyte>(address);
        public int ReadInt32(IntPtr address) => Read<int>(address);
        public uint ReadUInt32(IntPtr address) => Read<uint>(address);
        public long ReadInt64(IntPtr address) => Read<long>(address);
        public ulong ReadUInt64(IntPtr address) => Read<ulong>(address);
        //public float ReadSingle(IntPtr address) => Read<float>(address);
        public float ReadFloat(IntPtr address) => Read<float>(address);
        public double ReadDouble(IntPtr address) => Read<double>(address);
        public short ReadInt16(IntPtr address) => Read<short>(address);
        public ushort ReadUInt16(IntPtr address) => Read<ushort>(address);
        public byte ReadByte(IntPtr address) => ReadBytes(address, 1)?[0] ?? 0;
        public bool ReadBool(IntPtr address) => ReadByte(address) != 0;

        /*
        public string ReadString(IntPtr address, int maxLength = 256, System.Text.Encoding encoding = null)
        {
            if (encoding == null)
                encoding = System.Text.Encoding.UTF8;

            List<byte> bytes = new List<byte>();
            int bytesRead;
            byte[] buffer = new byte[1];

            for (int i = 0; i < maxLength; i++)
            {
                if (!ReadProcessMemory(processHandle, IntPtr.Add(address, i), buffer, 1, out bytesRead) || bytesRead != 1)
                    return null;

                if (buffer[0] == 0)
                    break;

                bytes.Add(buffer[0]);
            }

            return encoding.GetString(bytes.ToArray());
        }

        public string ReadString(IntPtr address, int length, System.Text.Encoding encoding)
        {
            if (encoding == null)
                encoding = System.Text.Encoding.UTF8;

            byte[] bytes = ReadBytes(address, length);
            return bytes != null ? encoding.GetString(bytes) : null;
        }
        */

        public bool WriteBytes(IntPtr address, byte[] data)
        {
            try
            {
                int bytesWritten;
                return WriteProcessMemory(processHandle, address, data, data.Length, out bytesWritten) &&
                       bytesWritten == data.Length;
            }
            catch
            {
                return false;
            }
        }

        public bool WriteSBytes(IntPtr address, sbyte[] data)
        {
            try
            {
                byte[] unsignedData = new byte[data.Length];
                Buffer.BlockCopy(data, 0, unsignedData, 0, data.Length);
                int bytesWritten;
                return WriteProcessMemory(processHandle, address, unsignedData, unsignedData.Length, out bytesWritten) &&
                       bytesWritten == unsignedData.Length;
            }
            catch
            {
                return false;
            }
        }

        public bool Write<T>(IntPtr address, T value) where T : struct
        {
            int size = Marshal.SizeOf(typeof(T));
            byte[] bytes = new byte[size];

            GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                Marshal.StructureToPtr(value, handle.AddrOfPinnedObject(), false);
                return WriteBytes(address, bytes);
            }
            finally
            {
                handle.Free();
            }
        }

        // Convenience methods for common types
        public bool WriteInt32(IntPtr address, int value) => Write(address, value);
        public bool WriteUInt32(IntPtr address, uint value) => Write(address, value);
        public bool WriteInt64(IntPtr address, long value) => Write(address, value);
        public bool WriteUInt64(IntPtr address, ulong value) => Write(address, value);
        public bool WriteSingle(IntPtr address, float value) => Write(address, value);
        public bool WriteFloat(IntPtr address, float value) => Write(address, value);
        public bool WriteDouble(IntPtr address, double value) => Write(address, value);
        public bool WriteInt16(IntPtr address, short value) => Write(address, value);
        public bool WriteUInt16(IntPtr address, ushort value) => Write(address, value);
        public bool WriteByte(IntPtr address, byte value) => WriteBytes(address, [value]);
        public bool WriteSByte(IntPtr address, sbyte value) => WriteSBytes(address, [value]);
        
        /*
        public bool WriteString(IntPtr address, string value, Encoding encoding = null)
        {
            encoding = encoding ?? Encoding.UTF8;
            byte[] bytes = encoding.GetBytes(value + '\0');
            return WriteBytes(address, bytes);
        }

        public bool WriteString(IntPtr address, string value, int length, Encoding encoding = null)
        {
            encoding = encoding ?? Encoding.UTF8;
            byte[] bytes = new byte[length];
            byte[] stringBytes = encoding.GetBytes(value);

            Array.Copy(stringBytes, bytes, Math.Min(stringBytes.Length, length));
            return WriteBytes(address, bytes);
        }
        */
    }
}
