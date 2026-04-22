using PropertyHook;
using System;
using System.Collections.Generic;
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

        public ParamHook(int refreshInterval, int minLifetime) :
            base(refreshInterval, minLifetime, p => p.MainWindowTitle == "DARK SOULS™: REMASTERED")
        {
            OnHooked += ParamHook_OnHooked;
            OnUnhooked += ParamHook_OnUnhooked;
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
                    _cachedReads++;
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

        /// <summary>
        /// Scans for a param pattern and caches the result.
        /// Returns the base address (pattern address - offset).
        /// </summary>
        public nint ScanAndCache(string paramName, byte[] pattern, long offset, CancellationToken ct = default)
        {
            if (!Hooked || pattern == null || pattern.Length == 0)
                return nint.Zero;

            _totalScans++;

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
                    _successfulScans++;
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

        /// <summary>
        /// Scans for a param pattern within a specific memory range.
        /// Much faster than full scan when approximate location is known.
        /// </summary>
        public nint ScanInRange(string paramName, byte[] pattern, long offset,
            nint rangeStart, nint rangeEnd, CancellationToken ct = default)
        {
            if (!Hooked || pattern == null) return nint.Zero;

            var cached = GetCachedAddress(paramName);
            if (cached.HasValue) return cached.Value;

            const int CHUNK_SIZE = 0x10000;
            int patternLength = pattern.Length;
            string mask = new string('x', patternLength);

            nint current = rangeStart;
            while (current.ToInt64() < rangeEnd.ToInt64())
            {
                if (ct.IsCancellationRequested) return nint.Zero;
                try
                {
                    int chunkSize = (int)Math.Min(CHUNK_SIZE, rangeEnd.ToInt64() - current.ToInt64());
                    byte[] buffer = new byte[chunkSize];
                    if (ReadProcessMemory(Handle, current, buffer, chunkSize, out int bytesRead) && bytesRead >= patternLength)
                    {
                        for (int i = 0; i <= bytesRead - patternLength; i++)
                        {
                            bool found = true;
                            for (int j = 0; j < patternLength; j++)
                            {
                                if (pattern[j] != buffer[i + j]) { found = false; break; }
                            }
                            if (found)
                            {
                                nint baseAddr = (nint)(current.ToInt64() + i) - (nint)offset;
                                CacheAddress(paramName, baseAddr);
                                return baseAddr;
                            }
                        }
                    }
                }
                catch { }
                current = (nint)(current.ToInt64() + CHUNK_SIZE);
            }
            return nint.Zero;
        }
        /// Based on original AOBScanner implementation.
        /// </summary>
        private nint ScanMemory(byte[] pattern, string? mask, CancellationToken ct)
        {
            const int CHUNK_SIZE = 0x10000; // 64KB chunks
            int patternLength = pattern.Length;

            // Create mask if not provided
            if (mask == null)
                mask = new string('x', patternLength);
            else if (mask.Length != patternLength)
                throw new ArgumentException($"Pattern and mask lengths must match ({patternLength} vs {mask.Length})");

            IntPtr currentAddress = IntPtr.Zero;
            Kernel32.MEMORY_BASIC_INFORMATION memInfo;
            int mbiSize = System.Runtime.InteropServices.Marshal.SizeOf<Kernel32.MEMORY_BASIC_INFORMATION>();

            while (Kernel32.VirtualQueryEx(Handle, currentAddress, out memInfo, (IntPtr)mbiSize) != 0)
            {
                if (ct.IsCancellationRequested)
                    return nint.Zero;

                // Skip if at invalid memory or would overflow
                if (currentAddress.ToInt64() >= (long)(nint.MaxValue - (int)memInfo.RegionSize))
                    break;

                // Only scan PAGE_READWRITE committed memory (same as original AOBScanner)
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
                        try
                        {
                            int chunkSize = (int)Math.Min(CHUNK_SIZE, regionEnd.ToInt64() - chunkStart.ToInt64());
                            byte[] buffer = new byte[chunkSize];
                            int bytesRead;

                            // Use custom ReadProcessMemory with out parameter (same as original AOBScanner)
                            if (ReadProcessMemory(Handle, chunkStart, buffer, chunkSize, out bytesRead))
                            {
                                // Only scan if we got enough data (use actual bytes read, not requested)
                                if (bytesRead >= patternLength)
                                {
                                    for (int i = 0; i <= bytesRead - patternLength; i++)
                                    {
                                        // Check cancellation every 64KB worth of iterations
                                        if ((i & 0xFFFF) == 0 && ct.IsCancellationRequested)
                                            return nint.Zero;

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
                                            return (nint)IntPtr.Add(chunkStart, i);
                                    }
                                }
                            }
                        }
                        catch { /* Skip inaccessible chunks */ }

                        chunkStart = IntPtr.Add(chunkStart, CHUNK_SIZE);
                    }
                }

                // Move to next memory region
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
            _directReads++;

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
