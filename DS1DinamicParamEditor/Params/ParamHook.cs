using PropertyHook;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using SoulsFormats;
using Kernel32 = PropertyHook.Kernel32;

namespace DS1ParamEditor
{
    /// <summary>
    /// PropertyHook for param scanning and memory access.
    /// Separate from PlayerHook (DSR-Gadget functionality).
    /// Provides caching, batch operations, and diagnostics for param editing.
    /// </summary>
    public sealed class ParamHook : PHook
    {
        // Custom ReadProcessMemory with out parameter for accurate byte count
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            byte[] lpBuffer,
            int dwSize,
            out int lpNumberOfBytesRead);

        // Cached param addresses: paramName -> base address
        private readonly Dictionary<string, nint> _cachedAddresses = new();
        private readonly object _cacheLock = new();

        // Statistics
        private int _totalScans;
        private int _successfulScans;
        private int _cachedReads;
        private int _directReads;

        public ParamHook(int refreshInterval, int minLifetime, string? exePath = null) :
            base(refreshInterval, minLifetime, p => MatchesExe(p, exePath))
        {
            OnHooked += ParamHook_OnHooked;
            OnUnhooked += ParamHook_OnUnhooked;
        }

        private static bool MatchesExe(Process p, string? exePath)
        {
            if (string.IsNullOrEmpty(exePath)) return false;
            try
            {
                return string.Equals(p.MainModule.FileName, exePath, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private void ParamHook_OnHooked(object sender, PHEventArgs e)
        {
            Console.WriteLine($"[ParamHook] Hooked to DSR version {Version}");
            Console.WriteLine($"[ParamHook] Process: {Process.ProcessName} (PID: {Process.Id})");
            Console.WriteLine($"[ParamHook] Module size: 0x{Process.MainModule.ModuleMemorySize:X}");
        }

        private void ParamHook_OnUnhooked(object sender, PHEventArgs e)
        {
            Console.WriteLine($"[ParamHook] Unhooked. Stats: {_successfulScans}/{_totalScans} scans successful");
            ClearCache();
        }

        public string Version
        {
            get
            {
                if (!Hooked)
                    return "N/A";
                
                try
                {
                    if (Process == null || Process.HasExited)
                        return "N/A";
                    
                    int size = Process.MainModule.ModuleMemorySize;
                    return size switch
                    {
                        0x4869400 => "1.01",
                        0x496BE00 => "1.01.1",
                        0x37CB400 => "1.01.2",
                        0x3817800 => "1.03",
                        _ => $"Unknown (0x{size:X})"
                    };
                }
                catch
                {
                    return "N/A";
                }
            }
        }

        // ── Cache Management ──────────────────────────────────────────────────

        /// <summary>Caches a param address for faster subsequent access.</summary>
        public void CacheAddress(string paramName, nint address)
        {
            lock (_cacheLock)
            {
                _cachedAddresses[paramName] = address;
                Console.WriteLine($"[ParamHook] Cached '{paramName}' @ 0x{address:X}");
            }
        }

        /// <summary>Gets a cached param address, or null if not cached.</summary>
        public nint? GetCachedAddress(string paramName)
        {
            lock (_cacheLock)
            {
                if (_cachedAddresses.TryGetValue(paramName, out nint addr))
                {
                    Interlocked.Increment(ref _cachedReads);
                    return addr;
                }
                return null;
            }
        }

        /// <summary>Removes a param from the cache.</summary>
        public void RemoveFromCache(string paramName)
        {
            lock (_cacheLock)
            {
                if (_cachedAddresses.Remove(paramName))
                    Console.WriteLine($"[ParamHook] Removed '{paramName}' from cache");
            }
        }

        /// <summary>Clears all cached addresses.</summary>
        public void ClearCache()
        {
            lock (_cacheLock)
            {
                int count = _cachedAddresses.Count;
                _cachedAddresses.Clear();
                if (count > 0)
                    Console.WriteLine($"[ParamHook] Cleared {count} cached addresses");
            }
        }

        /// <summary>Gets all cached param names.</summary>
        public IReadOnlyCollection<string> GetCachedParams()
        {
            lock (_cacheLock)
                return new List<string>(_cachedAddresses.Keys);
        }

        // ── Scanning ──────────────────────────────────────────────────────────

        /// <summary>Builds Boyer-Moore-Horspool skip table for a pattern/mask pair.</summary>
        private static int[] BuildBmhSkipTable(byte[] pattern, string mask)
        {
            int len = pattern.Length;
            int[] skip = new int[256];

            // Default: slide pattern past current position
            for (int c = 0; c < 256; c++)
                skip[c] = len;

            if (len == 0) return skip;

            // If last byte is wildcard, BMH can't use it — fall back to shift=1
            if (mask[len - 1] == '?')
            {
                for (int c = 0; c < 256; c++)
                    skip[c] = 1;
                return skip;
            }

            // For each byte in pattern[0..len-2], record distance from end
            for (int i = 0; i < len - 1; i++)
            {
                if (mask[i] != '?')
                    skip[pattern[i]] = len - 1 - i;
            }

            return skip;
        }

        /// <summary>
        /// Scans for a param pattern and caches the result.
        /// Returns the base address (pattern address - offset).
        /// </summary>
        public nint ScanAndCache(string paramName, byte[] pattern, long offset, CancellationToken ct = default)
        {
            if (!Hooked || pattern == null || pattern.Length == 0)
                return nint.Zero;

            _totalScans = Interlocked.Increment(ref _totalScans);

            // Check cache first
            var cached = GetCachedAddress(paramName);
            if (cached.HasValue)
            {
                Console.WriteLine($"[ParamHook] Using cached address for '{paramName}': 0x{cached.Value:X}");
                return cached.Value;
            }

            Console.WriteLine($"[ParamHook] Scanning for '{paramName}' ({pattern.Length} bytes)...");

            try
            {
                nint hit = ScanMemory(pattern, null, ct);
                if (hit != nint.Zero)
                {
                    nint baseAddr = hit - (nint)offset;
                    CacheAddress(paramName, baseAddr);
                    Interlocked.Increment(ref _successfulScans);
                    return baseAddr;
                }
                else
                {
                    Console.WriteLine($"[ParamHook] Pattern not found for '{paramName}'");
                    return nint.Zero;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ParamHook] Scan error for '{paramName}': {ex.Message}");
                return nint.Zero;
            }
        }

        /// <summary>Scans for a param pattern within a specific memory range.</summary>
        public nint ScanInRange(string paramName, byte[] pattern, long offset,
            nint rangeStart, nint rangeEnd, CancellationToken ct = default)
        {
            if (!Hooked || pattern == null) return nint.Zero;

            var cached = GetCachedAddress(paramName);
            if (cached.HasValue) return cached.Value;

            const int CHUNK_SIZE = 0x10000;
            int patternLength = pattern.Length;
            string mask = new string('x', patternLength);
            var skip = BuildBmhSkipTable(pattern, mask);
            int lastIdx = patternLength - 1;

            nint current = rangeStart;
            while (current.ToInt64() < rangeEnd.ToInt64())
            {
                if (ct.IsCancellationRequested) return nint.Zero;

                int chunkSize = (int)Math.Min(CHUNK_SIZE, rangeEnd.ToInt64() - current.ToInt64());
                byte[] buffer = ArrayPool<byte>.Shared.Rent(chunkSize);
                try
                {
                    if (ReadProcessMemory(Handle, current, buffer, chunkSize, out int bytesRead) && bytesRead >= patternLength)
                    {
                        int searchLen = bytesRead - patternLength;
                        int i = 0;
                        while (i <= searchLen)
                        {
                            int bufPos = i + lastIdx;
                            byte lastByte = buffer[bufPos];

                            if (mask[lastIdx] == '?' || pattern[lastIdx] == lastByte)
                            {
                                bool found = true;
                                for (int j = 0; j < lastIdx; j++)
                                {
                                    if (mask[j] != '?' && pattern[j] != buffer[i + j])
                                    { found = false; break; }
                                }
                                if (found)
                                {
                                    nint baseAddr = (nint)(current.ToInt64() + i) - (nint)offset;
                                    CacheAddress(paramName, baseAddr);
                                    return baseAddr;
                                }
                            }

                            i += skip[lastByte];
                        }
                    }
                }
                catch { }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
                current = (nint)(current.ToInt64() + CHUNK_SIZE);
            }
            return nint.Zero;
        }
        /// <summary>BMH-accelerated scanner. Based on original AOBScanner.</summary>
        private nint ScanMemory(byte[] pattern, string? mask, CancellationToken ct)
        {
            const int CHUNK_SIZE = 0x10000;
            int patternLength = pattern.Length;

            if (mask == null)
                mask = new string('x', patternLength);
            else if (mask.Length != patternLength)
                throw new ArgumentException($"Pattern and mask lengths must match ({patternLength} vs {mask.Length})");

            var skip = BuildBmhSkipTable(pattern, mask);
            int lastIdx = patternLength - 1;

            IntPtr currentAddress = IntPtr.Zero;
            Kernel32.MEMORY_BASIC_INFORMATION memInfo;
            int mbiSize = Marshal.SizeOf<Kernel32.MEMORY_BASIC_INFORMATION>();

            while (Kernel32.VirtualQueryEx(Handle, currentAddress, out memInfo, (IntPtr)mbiSize) != 0)
            {
                if (ct.IsCancellationRequested)
                    return nint.Zero;

                if (currentAddress.ToInt64() >= (long)(nint.MaxValue - (int)memInfo.RegionSize))
                    break;

                const uint PAGE_READWRITE = 0x04;
                const uint MEM_COMMIT = 0x1000;
                const uint PAGE_NOACCESS = 0x01;

                if ((memInfo.Protect & PAGE_READWRITE) != 0 &&
                    (memInfo.State & MEM_COMMIT) != 0 &&
                    memInfo.Protect != PAGE_NOACCESS)
                {
                    IntPtr regionEnd = IntPtr.Add(memInfo.BaseAddress, (int)memInfo.RegionSize);
                    IntPtr chunkStart = memInfo.BaseAddress;

                    while (chunkStart.ToInt64() < regionEnd.ToInt64())
                    {
                        if (ct.IsCancellationRequested) return nint.Zero;

                        int chunkSize = (int)Math.Min(CHUNK_SIZE, regionEnd.ToInt64() - chunkStart.ToInt64());
                        byte[] buffer = ArrayPool<byte>.Shared.Rent(chunkSize);
                        try
                        {
                            if (ReadProcessMemory(Handle, chunkStart, buffer, chunkSize, out int bytesRead) && bytesRead >= patternLength)
                            {
                                int searchLen = bytesRead - patternLength;
                                int i = 0;
                                while (i <= searchLen)
                                {
                                    if ((i & 0xFFFF) == 0 && ct.IsCancellationRequested)
                                        return nint.Zero;

                                    int bufPos = i + lastIdx;
                                    byte lastByte = buffer[bufPos];

                                    if (mask[lastIdx] == '?' || pattern[lastIdx] == lastByte)
                                    {
                                        bool found = true;
                                        for (int j = 0; j < lastIdx; j++)
                                        {
                                            if (mask[j] != '?' && pattern[j] != buffer[i + j])
                                            { found = false; break; }
                                        }
                                        if (found)
                                            return (nint)IntPtr.Add(chunkStart, i);
                                    }

                                    i += skip[lastByte];
                                }
                            }
                        }
                        catch { }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(buffer);
                        }

                        chunkStart = IntPtr.Add(chunkStart, CHUNK_SIZE);
                    }
                }

                IntPtr nextAddress = IntPtr.Add(memInfo.BaseAddress, (int)memInfo.RegionSize);
                if (nextAddress.ToInt64() <= currentAddress.ToInt64())
                    break;
                currentAddress = nextAddress;
            }

            return nint.Zero;
        }

        // ── Direct Read/Write ─────────────────────────────────────────────────

        /// <summary>Reads a field from a cached param address.</summary>
        public object? ReadParamField(string paramName, long rowOffset, long fieldOffset, PARAMDEF.DefType type)
        {
            var baseAddr = GetCachedAddress(paramName);
            if (!baseAddr.HasValue)
            {
                Console.WriteLine($"[ParamHook] Cannot read '{paramName}': not cached");
                return null;
            }

            nint addr = baseAddr.Value + (nint)rowOffset + (nint)fieldOffset;
            Interlocked.Increment(ref _directReads);

            return type switch
            {
                PARAMDEF.DefType.s8 => Kernel32.ReadSByte(Handle, (IntPtr)addr),
                PARAMDEF.DefType.u8 => Kernel32.ReadByte(Handle, (IntPtr)addr),
                PARAMDEF.DefType.s16 => Kernel32.ReadInt16(Handle, (IntPtr)addr),
                PARAMDEF.DefType.u16 => Kernel32.ReadUInt16(Handle, (IntPtr)addr),
                PARAMDEF.DefType.s32 => Kernel32.ReadInt32(Handle, (IntPtr)addr),
                PARAMDEF.DefType.b32 => Kernel32.ReadInt32(Handle, (IntPtr)addr),
                PARAMDEF.DefType.u32 => Kernel32.ReadUInt32(Handle, (IntPtr)addr),
                PARAMDEF.DefType.f32 => Kernel32.ReadSingle(Handle, (IntPtr)addr),
                PARAMDEF.DefType.angle32 => Kernel32.ReadSingle(Handle, (IntPtr)addr),
                PARAMDEF.DefType.f64 => Kernel32.ReadDouble(Handle, (IntPtr)addr),
                _ => null
            };
        }

        /// <summary>Writes a field to a cached param address.</summary>
        public bool WriteParamField(string paramName, long rowOffset, long fieldOffset, PARAMDEF.DefType type, object value)
        {
            var baseAddr = GetCachedAddress(paramName);
            if (!baseAddr.HasValue)
            {
                Console.WriteLine($"[ParamHook] Cannot write '{paramName}': not cached");
                return false;
            }

            nint addr = baseAddr.Value + (nint)rowOffset + (nint)fieldOffset;

            try
            {
                IntPtr ptr = (IntPtr)addr;
                return type switch
                {
                    PARAMDEF.DefType.s8 => Kernel32.WriteSByte(Handle, ptr, Convert.ToSByte(value)),
                    PARAMDEF.DefType.u8 => Kernel32.WriteByte(Handle, ptr, Convert.ToByte(value)),
                    PARAMDEF.DefType.s16 => Kernel32.WriteInt16(Handle, ptr, Convert.ToInt16(value)),
                    PARAMDEF.DefType.u16 => Kernel32.WriteUInt16(Handle, ptr, Convert.ToUInt16(value)),
                    PARAMDEF.DefType.s32 or PARAMDEF.DefType.b32 => Kernel32.WriteInt32(Handle, ptr, Convert.ToInt32(value)),
                    PARAMDEF.DefType.u32 => Kernel32.WriteUInt32(Handle, ptr, Convert.ToUInt32(value)),
                    PARAMDEF.DefType.f32 or PARAMDEF.DefType.angle32 => Kernel32.WriteSingle(Handle, ptr, Convert.ToSingle(value)),
                    PARAMDEF.DefType.f64 => Kernel32.WriteDouble(Handle, ptr, Convert.ToDouble(value)),
                    _ => false
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ParamHook] Write error: {ex.Message}");
                return false;
            }
        }

        /// <summary>Reads raw bytes from memory.</summary>
        public byte[]? ReadBytes(nint address, int length)
        {
            if (!Hooked || address == nint.Zero || length <= 0)
                return null;

            return Kernel32.ReadBytes(Handle, (IntPtr)address, (uint)length);
        }

        /// <summary>Writes raw bytes to memory.</summary>
        public bool WriteBytes(nint address, byte[] data)
        {
            if (!Hooked || address == nint.Zero || data == null || data.Length == 0)
                return false;

            return Kernel32.WriteBytes(Handle, (IntPtr)address, data);
        }

        // ── Diagnostics ───────────────────────────────────────────────────────

        /// <summary>Gets diagnostic information about the hook state.</summary>
        public string GetDiagnostics()
        {
            if (!Hooked)
                return "Not hooked";

            lock (_cacheLock)
            {
                return $"DSR {Version} | Cached: {_cachedAddresses.Count} params | " +
                       $"Scans: {_successfulScans}/{_totalScans} | " +
                       $"Reads: {_directReads} direct, {_cachedReads} cached";
            }
        }

        /// <summary>Prints detailed cache information to console.</summary>
        public void PrintCacheInfo()
        {
            lock (_cacheLock)
            {
                Console.WriteLine($"[ParamHook] ═══ Cache Info ═══");
                Console.WriteLine($"[ParamHook] Cached params: {_cachedAddresses.Count}");
                foreach (var kvp in _cachedAddresses)
                    Console.WriteLine($"[ParamHook]   {kvp.Key} @ 0x{kvp.Value:X}");
                Console.WriteLine($"[ParamHook] Statistics:");
                Console.WriteLine($"[ParamHook]   Scans: {_successfulScans}/{_totalScans} successful");
                Console.WriteLine($"[ParamHook]   Reads: {_directReads} direct, {_cachedReads} cached");
            }
        }

        /// <summary>Validates that a cached address is still readable.</summary>
        public bool ValidateCachedAddress(string paramName)
        {
            var addr = GetCachedAddress(paramName);
            if (!addr.HasValue)
                return false;

            // Try to read a single byte to validate
            byte[]? test = ReadBytes(addr.Value, 1);
            bool valid = test != null;

            if (!valid)
            {
                Console.WriteLine($"[ParamHook] Cached address for '{paramName}' is no longer valid");
                RemoveFromCache(paramName);
            }

            return valid;
        }
    }
}
