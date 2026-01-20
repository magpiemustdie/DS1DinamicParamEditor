using System;
using System.Diagnostics;
using SoulsFormats;

namespace DS1DrawParamEditor
{
    public class AOBReader
    {
        private readonly AOBScanner _scanner;
        private readonly Process _targetProcess;
        public bool IsProcessValid => _targetProcess != null && !_targetProcess.HasExited;

        public AOBReader(string processName)
        {
            try
            {
                var processes = Process.GetProcessesByName(processName);
                if (processes.Length == 0)
                {
                    Console.WriteLine($"Error: Process '{processName}' not found. Please run the game first.");
                    return;
                }

                _targetProcess = processes[0];
                _scanner = new AOBScanner(_targetProcess);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to initialize AOBReader: {ex.Message}");
            }
        }

        public nint AOBScan(byte[] main_pattern, bool autoPattern, int size)
        {
            if (autoPattern)
            {
                try
                {
                    Console.WriteLine("Starting AOB scan...");
                    string mask = new string('x', main_pattern.Length);
                    nint result = _scanner.FindPattern(main_pattern, mask, true);
                    Console.WriteLine($"Found at: 0x{result:X}");
                    return result;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Scan failed: {ex.Message}");
                    return -1;
                }
            }
            else
            {
                try
                {
                    byte[] pattern = new byte[size];
                    Array.Copy(main_pattern, 0, pattern, 0, size);
                    Console.WriteLine("Starting AOB scan...");
                    string mask = new string('x', pattern.Length);
                    nint result = _scanner.FindPattern(pattern, mask, true);
                    Console.WriteLine($"Found at: 0x{result:X}");
                    //_scanner.ReadBytesToFile(result, main_pattern.Length);
                    return result;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Scan failed: {ex.Message}");
                    return -1;
                }
            }
        }

        public object ReadMemory(nint baseAddress, long offset, PARAMDEF.DefType type)
        {
            object value = null;

            try
            {
                nint targetAddress = baseAddress + (int)offset;
                switch (type)
                {
                    case PARAMDEF.DefType.s8: value = _scanner.ReadSByte(targetAddress); break;
                    case PARAMDEF.DefType.u8: value = _scanner.ReadByte(targetAddress); break;
                    case PARAMDEF.DefType.s16: value = _scanner.ReadInt16(targetAddress); break;
                    case PARAMDEF.DefType.u16: value = _scanner.ReadUInt16(targetAddress); break;
                    case PARAMDEF.DefType.s32: value = _scanner.ReadInt32(targetAddress); break;
                    case PARAMDEF.DefType.u32: value = _scanner.ReadUInt32(targetAddress); break;
                    case PARAMDEF.DefType.b32: value = _scanner.ReadInt32(targetAddress); break;
                    case PARAMDEF.DefType.f32: value = _scanner.ReadFloat(targetAddress); break;
                    case PARAMDEF.DefType.angle32: value = _scanner.ReadFloat(targetAddress); break;
                    case PARAMDEF.DefType.f64: value = _scanner.ReadDouble(targetAddress); break;
                }
                return value;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Read failed: {ex.Message}");
                return null;
            }
        }

        public bool WriteMemory(nint baseAddress, long offset, object value, PARAMDEF.DefType type)
        {
            try
            {
                nint targetAddress = baseAddress + (int)offset;
                switch (type)
                {
                    case PARAMDEF.DefType.s8:
                        _scanner.WriteSByte(targetAddress, Convert.ToSByte(value));
                        break;
                    case PARAMDEF.DefType.u8:
                        _scanner.WriteByte(targetAddress, Convert.ToByte(value));
                        break;
                    case PARAMDEF.DefType.s16:
                        _scanner.WriteInt16(targetAddress, Convert.ToInt16(value));
                        break;
                    case PARAMDEF.DefType.u16:
                        _scanner.WriteUInt16(targetAddress, Convert.ToUInt16(value));
                        break;
                    case PARAMDEF.DefType.s32:
                    case PARAMDEF.DefType.b32:
                        _scanner.WriteInt32(targetAddress, Convert.ToInt32(value));
                        break;
                    case PARAMDEF.DefType.u32:
                        _scanner.WriteUInt32(targetAddress, Convert.ToUInt32(value));
                        break;
                    case PARAMDEF.DefType.f32:
                    case PARAMDEF.DefType.angle32:
                        _scanner.WriteSingle(targetAddress, Convert.ToSingle(value));
                        break;
                    case PARAMDEF.DefType.f64:
                        _scanner.WriteDouble(targetAddress, Convert.ToDouble(value));
                        break;
                    default:
                        throw new NotSupportedException($"Unsupported type: {type}");
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Write failed: {ex.Message}");
                return false;
            }
        }
    }
}