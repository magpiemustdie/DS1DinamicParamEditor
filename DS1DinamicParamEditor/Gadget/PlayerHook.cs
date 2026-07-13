using PropertyHook;
using System;
using System.Diagnostics;

namespace DS1ParamEditor
{
    public sealed class PlayerHook : PHook, IGadgetHook, IDisposable
    {
        private DSROffsets Offsets;

        private PHPointer GroupMaskAddr;
        private PHPointer ChrDbgAddr;
        private PHPointer ChrClassBasePtr;
        private PHPointer ItemGetAddr;
        private PHPointer BonfireWarpAddr;
        private PHPointer ChrClassWarp;
        private PHPointer WorldChrBase;
        private PHPointer ChrData1;
        private PHPointer ChrMapData;
        private PHPointer ChrAnimData;
        private PHPointer ChrPosData;
        private PHPointer ChrData2;
        private PHPointer GraphicsData;
        private PHPointer MenuMan;
        private PHPointer EventFlags;
        private PHPointer ChrFollowCam;

        private Dictionary<string, byte[]>? _mapSnapshot;
        private Dictionary<string, int>? _mapScanResults;

        public PlayerHook(int refreshInterval, int minLifetime, string? exePath = null) :
            base(refreshInterval, minLifetime, p => MatchesExe(p, exePath))
        {
            Offsets = new DSROffsets();
            
            // Register all AOB pointers - same as DSR-Gadget
            GroupMaskAddr = RegisterRelativeAOB(DSROffsets.GroupMaskAOB, 2, 7);
            GraphicsData = RegisterRelativeAOB(DSROffsets.GraphicsDataAOB, 3, 7, DSROffsets.GraphicsDataOffset1, DSROffsets.GraphicsDataOffset2);
            ChrClassWarp = RegisterRelativeAOB(DSROffsets.ChrClassWarpAOB, 3, 7, DSROffsets.ChrClassWarpOffset1);
            WorldChrBase = RegisterRelativeAOB(DSROffsets.WorldChrBaseAOB, 3, 7, DSROffsets.WorldChrBaseOffset1);
            ChrDbgAddr = RegisterRelativeAOB(DSROffsets.ChrDbgAOB, 2, 7);
            MenuMan = RegisterRelativeAOB(DSROffsets.MenuManAOB, 3, 7, DSROffsets.MenuManOffset1);
            ChrClassBasePtr = RegisterRelativeAOB(DSROffsets.ChrClassBaseAOB, 3, 7);
            EventFlags = RegisterRelativeAOB(DSROffsets.EventFlagsAOB, 3, 7, DSROffsets.EventFlagsOffset1, DSROffsets.EventFlagsOffset2);
            ChrFollowCam = RegisterRelativeAOB(DSROffsets.ChrFollowCamAOB, 3, 7, DSROffsets.ChrFollowCamOffset1, DSROffsets.ChrFollowCamOffset2, DSROffsets.ChrFollowCamOffset3);
            ItemGetAddr = RegisterAbsoluteAOB(DSROffsets.ItemGetAOB);
            BonfireWarpAddr = RegisterAbsoluteAOB(DSROffsets.BonfireWarpAOB);

            ChrData1 = CreateChildPointer(WorldChrBase, (int)DSROffsets.WorldChrBase.ChrData1);
            ChrMapData = CreateBasePointer(IntPtr.Zero);
            ChrAnimData = CreateBasePointer(IntPtr.Zero);
            ChrPosData = CreateBasePointer(IntPtr.Zero);
            ChrData2 = CreateChildPointer(ChrClassBasePtr, DSROffsets.ChrData2Offset1, DSROffsets.ChrData2Offset2);

            OnHooked += PlayerHook_OnHooked;
            OnUnhooked += PlayerHook_OnUnhooked;
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

        private void PlayerHook_OnHooked(object sender, PHEventArgs e)
        {
            Offsets = DSROffsets.GetOffsets(Process.MainModule.ModuleMemorySize);
            ChrMapData = CreateChildPointer(ChrData1, (int)DSROffsets.ChrData1.ChrMapData + Offsets.ChrData1Boost1);
            ChrAnimData = CreateChildPointer(ChrMapData, (int)DSROffsets.ChrMapData.ChrAnimData);
            ChrPosData = CreateChildPointer(ChrMapData, (int)DSROffsets.ChrMapData.ChrPosData);
            
            Console.WriteLine($"[PlayerHook] Hooked to DSR version {Version}");
            Console.WriteLine($"[PlayerHook] AOB scan result: {(AOBScanSucceeded ? "SUCCESS" : "FAILED")}");
        }

        private void PlayerHook_OnUnhooked(object sender, PHEventArgs e)
        {
            Console.WriteLine("[PlayerHook] Unhooked from game process");
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

        public int ProcessId => Process?.Id ?? -1;
        public bool IsValid => Hooked && ChrData1.Resolve() != IntPtr.Zero;
        public bool IsResolving => false; // PropertyHook handles this automatically
        public bool Loaded
        {
            get
            {
                try
                {
                    return Hooked && !Process.HasExited && ChrFollowCam.Resolve() != IntPtr.Zero;
                }
                catch
                {
                    return false;
                }
            }
        }
        public bool Focused
        {
            get
            {
                try
                {
                    return Hooked && !Process.HasExited && User32.GetForegroundProcessID() == Process.Id;
                }
                catch
                {
                    return false;
                }
            }
        }

        #region Player Position & Movement
        public bool GetPosition(out float x, out float y, out float z, out float angle)
        {
            x = y = z = angle = 0f;
            if (!IsValid) return false;
            
            try
            {
                if (Process.HasExited) return false;
                
                x = ChrPosData.ReadSingle((int)DSROffsets.ChrPosData.PosX);
                y = ChrPosData.ReadSingle((int)DSROffsets.ChrPosData.PosY);
                z = ChrPosData.ReadSingle((int)DSROffsets.ChrPosData.PosZ);
                angle = ChrPosData.ReadSingle((int)DSROffsets.ChrPosData.PosAngle);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void GetStablePosition(out float x, out float y, out float z, out float angle)
        {
            x = ChrClassWarp.ReadSingle((int)DSROffsets.ChrClassWarp.StableX + Offsets.ChrClassWarpBoost);
            y = ChrClassWarp.ReadSingle((int)DSROffsets.ChrClassWarp.StableY + Offsets.ChrClassWarpBoost);
            z = ChrClassWarp.ReadSingle((int)DSROffsets.ChrClassWarp.StableZ + Offsets.ChrClassWarpBoost);
            angle = ChrClassWarp.ReadSingle((int)DSROffsets.ChrClassWarp.StableAngle + Offsets.ChrClassWarpBoost);
        }

        public bool PosWarp(float x, float y, float z, float angle)
        {
            if (!IsValid) return false;
            try
            {
                ChrMapData.WriteSingle((int)DSROffsets.ChrMapData.WarpX, x);
                ChrMapData.WriteSingle((int)DSROffsets.ChrMapData.WarpY, y);
                ChrMapData.WriteSingle((int)DSROffsets.ChrMapData.WarpZ, z);
                ChrMapData.WriteSingle((int)DSROffsets.ChrMapData.WarpAngle, angle);
                ChrMapData.WriteBoolean((int)DSROffsets.ChrMapData.Warp, true);
                return true;
            }
            catch { return false; }
        }

        public bool NoGravity
        {
            set => ChrData1.WriteFlag32((int)DSROffsets.ChrData1.ChrFlags1 + Offsets.ChrData1Boost1, (uint)DSROffsets.ChrFlags1.NoGravity, value);
        }

        public bool NoCollision
        {
            set => ChrMapData.WriteFlag32((int)DSROffsets.ChrMapData.ChrMapFlags, (uint)DSROffsets.ChrMapFlags.DisableMapHit, value);
        }

        public bool DeathCam
        {
            get => WorldChrBase.ReadBoolean((int)DSROffsets.WorldChrBase.DeathCam);
            set => WorldChrBase.WriteBoolean((int)DSROffsets.WorldChrBase.DeathCam, value);
        }

        public float AnimSpeed
        {
            set => ChrAnimData.WriteSingle((int)DSROffsets.ChrAnimData.AnimSpeed, value);
        }
        #endregion

        #region Player Stats
        public int Health
        {
            get => ChrData1.ReadInt32((int)DSROffsets.ChrData1.Health + Offsets.ChrData1Boost2);
            set => ChrData1.WriteInt32((int)DSROffsets.ChrData1.Health + Offsets.ChrData1Boost2, value);
        }

        public int HealthMax
        {
            get => ChrData1.ReadInt32((int)DSROffsets.ChrData1.MaxHealth + Offsets.ChrData1Boost2);
        }

        public int Stamina
        {
            get => ChrData1.ReadInt32((int)DSROffsets.ChrData1.Stamina + Offsets.ChrData1Boost2);
            set => ChrData1.WriteInt32((int)DSROffsets.ChrData1.Stamina + Offsets.ChrData1Boost2, value);
        }

        public int StaminaMax
        {
            get => ChrData1.ReadInt32((int)DSROffsets.ChrData1.MaxStamina + Offsets.ChrData1Boost2);
        }

        public byte Class
        {
            get => ChrData2.ReadByte((int)DSROffsets.ChrData2.Class);
            set => ChrData2.WriteByte((int)DSROffsets.ChrData2.Class, value);
        }

        public int Humanity
        {
            get => ChrData2.ReadInt32((int)DSROffsets.ChrData2.Humanity);
            set => ChrData2.WriteInt32((int)DSROffsets.ChrData2.Humanity, value);
        }

        public int Souls
        {
            get => ChrData2.ReadInt32((int)DSROffsets.ChrData2.Souls);
            set => ChrData2.WriteInt32((int)DSROffsets.ChrData2.Souls, value);
        }

        public int SoulLevel => ChrData2.ReadInt32((int)DSROffsets.ChrData2.SoulLevel);
        public int Vitality => ChrData2.ReadInt32((int)DSROffsets.ChrData2.Vitality);
        public int Attunement => ChrData2.ReadInt32((int)DSROffsets.ChrData2.Attunement);
        public int Endurance => ChrData2.ReadInt32((int)DSROffsets.ChrData2.Endurance);
        public int Strength => ChrData2.ReadInt32((int)DSROffsets.ChrData2.Strength);
        public int Dexterity => ChrData2.ReadInt32((int)DSROffsets.ChrData2.Dexterity);
        public int Resistance => ChrData2.ReadInt32((int)DSROffsets.ChrData2.Resistance);
        public int Intelligence => ChrData2.ReadInt32((int)DSROffsets.ChrData2.Intelligence);
        public int Faith => ChrData2.ReadInt32((int)DSROffsets.ChrData2.Faith);
        #endregion

        #region Bonfire Warp
        public int GetLastBonfire()
        {
            if (!IsValid) return -1;
            try { return ChrClassWarp.ReadInt32((int)DSROffsets.ChrClassWarp.LastBonfire + Offsets.ChrClassWarpBoost); }
            catch { return -1; }
        }

        public int LastBonfire
        {
            get => GetLastBonfire();
            set
            {
                if (IsValid)
                    ChrClassWarp.WriteInt32((int)DSROffsets.ChrClassWarp.LastBonfire + Offsets.ChrClassWarpBoost, value);
            }
        }

        public void BonfireWarp()
        {
            if (!IsValid) return;

            byte[] asm = new byte[]
            {
                0x48, 0xB9, 0,0,0,0,0,0,0,0,
                0x48, 0x8B, 0x09,
                0xBA, 0x01, 0x00, 0x00, 0x00,
                0x48, 0x83, 0xEC, 0x38,
                0x49, 0xBE, 0,0,0,0,0,0,0,0,
                0x41, 0xFF, 0xD6,
                0x48, 0x83, 0xC4, 0x38,
                0xC3
            };

            IntPtr chrClassBasePtr = ChrClassBasePtr.Resolve();
            IntPtr bonfireWarpFunc = BonfireWarpAddr.Resolve();
            if (chrClassBasePtr == IntPtr.Zero || bonfireWarpFunc == IntPtr.Zero) return;

            Array.Copy(BitConverter.GetBytes(chrClassBasePtr.ToInt64()), 0, asm, 2, 8);
            Array.Copy(BitConverter.GetBytes(bonfireWarpFunc.ToInt64()), 0, asm, 24, 8);
            Execute(asm);
        }
        #endregion

        #region Items
        public void GetItem(int category, int id, int quantity)
        {
            if (!IsValid)
            {
                Console.WriteLine("[GetItem] Not hooked or AOB scan failed");
                return;
            }

            byte[] asm = (byte[])DSRAssembly.GetItem.Clone();

            // Fill in the parameters at correct offsets (matching DSR-Gadget)
            byte[] bytes = BitConverter.GetBytes(category);
            Array.Copy(bytes, 0, asm, 0x1, 4);          // mov edx, category
            
            bytes = BitConverter.GetBytes(quantity);
            Array.Copy(bytes, 0, asm, 0x7, 4);          // mov r9d, quantity
            
            bytes = BitConverter.GetBytes(id);
            Array.Copy(bytes, 0, asm, 0xD, 4);          // mov r8d, id
            
            // The shellcode uses opcode 0x48 0xA1 = "movabs rax, ds:[imm64]"
            // This DEREFERENCES the address - so we pass the ADDRESS where the pointer is stored
            // Resolve() on a RelativeAOB with no offsets returns the address itself (not dereferenced)
            // which is exactly what we need here
            bytes = BitConverter.GetBytes((ulong)ChrClassBasePtr.Resolve());
            Array.Copy(bytes, 0, asm, 0x19, 8);         // movabs rax, ds:[ChrClassBasePtrAddr]
            
            bytes = BitConverter.GetBytes((ulong)ItemGetAddr.Resolve());
            Array.Copy(bytes, 0, asm, 0x46, 8);         // movabs r14, ItemGetFunc

            Console.WriteLine($"[GetItem] Executing: category=0x{category:X} id={id} qty={quantity}");
            Console.WriteLine($"[GetItem] ChrClassBasePtrAddr=0x{ChrClassBasePtr.Resolve().ToInt64():X} ItemGetFunc=0x{ItemGetAddr.Resolve().ToInt64():X}");

            uint exitCode = Execute(asm);
            
            Console.WriteLine($"[GetItem] Thread exit code: 0x{exitCode:X}");

            if (exitCode == 0xC0000005)
            {
                Console.WriteLine("[GetItem] ACCESS_VIOLATION - game state may be invalid or item ID is wrong");
            }
            else if (exitCode == 0)
            {
                Console.WriteLine("[GetItem] ✓ Item given successfully!");
            }
            else
            {
                Console.WriteLine($"[GetItem] ✗ Failed with exit code 0x{exitCode:X}");
            }
        }
        #endregion

        #region Cheats
        public bool PlayerDeadMode
        {
            get => ChrData1.ReadFlag32((int)DSROffsets.ChrData1.ChrFlags1 + Offsets.ChrData1Boost1, (uint)DSROffsets.ChrFlags1.SetDeadMode);
            set => ChrData1.WriteFlag32((int)DSROffsets.ChrData1.ChrFlags1 + Offsets.ChrData1Boost1, (uint)DSROffsets.ChrFlags1.SetDeadMode, value);
        }

        public bool PlayerNoDead
        {
            set => ChrDbgAddr.WriteBoolean((int)DSROffsets.ChrDbg.PlayerNoDead, value);
        }

        public bool PlayerDisableDamage
        {
            set => ChrData1.WriteFlag32((int)DSROffsets.ChrData1.ChrFlags1 + Offsets.ChrData1Boost1, (uint)DSROffsets.ChrFlags1.DisableDamage, value);
        }

        public bool PlayerNoHit
        {
            set => ChrData1.WriteFlag32((int)DSROffsets.ChrData1.ChrFlags2 + Offsets.ChrData1Boost2, (uint)DSROffsets.ChrFlags2.NoHit, value);
        }

        public bool PlayerNoStamina
        {
            set => ChrData1.WriteFlag32((int)DSROffsets.ChrData1.ChrFlags2 + Offsets.ChrData1Boost2, (uint)DSROffsets.ChrFlags2.NoStaminaConsumption, value);
        }

        public bool PlayerSuperArmor
        {
            set => ChrData1.WriteFlag32((int)DSROffsets.ChrData1.ChrFlags1 + Offsets.ChrData1Boost1, (uint)DSROffsets.ChrFlags1.SetSuperArmor, value);
        }

        public bool PlayerHide
        {
            set => ChrDbgAddr.WriteBoolean((int)DSROffsets.ChrDbg.PlayerHide, value);
        }

        public bool PlayerSilence
        {
            set => ChrDbgAddr.WriteBoolean((int)DSROffsets.ChrDbg.PlayerSilence, value);
        }

        public bool PlayerExterminate
        {
            set => ChrDbgAddr.WriteBoolean((int)DSROffsets.ChrDbg.PlayerExterminate, value);
        }

        public bool PlayerNoGoods
        {
            set => ChrData1.WriteFlag32((int)DSROffsets.ChrData1.ChrFlags2 + Offsets.ChrData1Boost2, (uint)DSROffsets.ChrFlags2.NoGoodsConsume, value);
        }

        public bool AllNoArrow
        {
            set => ChrDbgAddr.WriteBoolean((int)DSROffsets.ChrDbg.AllNoArrowConsume, value);
        }

        public bool AllNoMagicQty
        {
            set => ChrDbgAddr.WriteBoolean((int)DSROffsets.ChrDbg.AllNoMagicQtyConsume, value);
        }

        public bool AllNoDead
        {
            set => ChrDbgAddr.WriteBoolean((int)DSROffsets.ChrDbg.AllNoDead, value);
        }

        public bool AllNoDamage
        {
            set => ChrDbgAddr.WriteBoolean((int)DSROffsets.ChrDbg.AllNoDamage, value);
        }

        public bool AllNoHit
        {
            set => ChrDbgAddr.WriteBoolean((int)DSROffsets.ChrDbg.AllNoHit, value);
        }

        public bool AllNoStamina
        {
            set => ChrDbgAddr.WriteBoolean((int)DSROffsets.ChrDbg.AllNoStaminaConsume, value);
        }

        public bool AllNoAttack
        {
            set => ChrDbgAddr.WriteBoolean((int)DSROffsets.ChrDbg.AllNoAttack, value);
        }

        public bool AllNoMove
        {
            set => ChrDbgAddr.WriteBoolean((int)DSROffsets.ChrDbg.AllNoMove, value);
        }

        public bool AllNoUpdateAI
        {
            set => ChrDbgAddr.WriteBoolean((int)DSROffsets.ChrDbg.AllNoUpdateAI, value);
        }
        #endregion

        #region Graphics
        public bool DrawMap
        {
            set => GroupMaskAddr.WriteBoolean((int)DSROffsets.GroupMask.Map, value);
        }

        public bool DrawObjects
        {
            set => GroupMaskAddr.WriteBoolean((int)DSROffsets.GroupMask.Objects, value);
        }

        public bool DrawCharacters
        {
            set => GroupMaskAddr.WriteBoolean((int)DSROffsets.GroupMask.Characters, value);
        }

        public bool DrawSFX
        {
            set => GroupMaskAddr.WriteBoolean((int)DSROffsets.GroupMask.SFX, value);
        }

        public bool DrawCutscenes
        {
            set => GroupMaskAddr.WriteBoolean((int)DSROffsets.GroupMask.Cutscenes, value);
        }

        public bool FilterOverride
        {
            set => GraphicsData.WriteBoolean((int)DSROffsets.GraphicsData.FilterOverride, value);
        }

        public void SetFilterValues(float brightR, float brightG, float brightB, float contR, float contG, float contB, float saturation, float hue)
        {
            GraphicsData.WriteSingle((int)DSROffsets.GraphicsData.FilterBrightnessR, brightR);
            GraphicsData.WriteSingle((int)DSROffsets.GraphicsData.FilterBrightnessG, brightG);
            GraphicsData.WriteSingle((int)DSROffsets.GraphicsData.FilterBrightnessB, brightB);
            GraphicsData.WriteSingle((int)DSROffsets.GraphicsData.FilterContrastR, contR);
            GraphicsData.WriteSingle((int)DSROffsets.GraphicsData.FilterContrastG, contG);
            GraphicsData.WriteSingle((int)DSROffsets.GraphicsData.FilterContrastB, contB);
            GraphicsData.WriteSingle((int)DSROffsets.GraphicsData.FilterSaturation, saturation);
            GraphicsData.WriteSingle((int)DSROffsets.GraphicsData.FilterHue, hue);
        }
        #endregion

        #region Misc
        public void MenuKick()
        {
            MenuMan.WriteInt32((int)DSROffsets.MenuMan.MenuKick, 2);
        }

        public byte[] DumpFollowCam()
        {
            return ChrFollowCam.ReadBytes(0, 512);
        }

        /// <summary>
        /// Reads the current area and block (map ID) from ChrClassWarp.
        /// Returns [area, block] or null if not hooked/loaded.
        /// </summary>
        public byte[]? ReadMapId()
        {
            if (!IsValid) return null;
            try
            {
                int bonfire = ChrClassWarp.ReadInt32((int)DSROffsets.ChrClassWarp.LastBonfire + Offsets.ChrClassWarpBoost);
                if (bonfire <= 0) return null;
                if (BonfireToMap.TryGetValue(bonfire, out var map))
                    return new byte[] { map.Item1, map.Item2 };
                // Fallback: try prefix (bonfire / 10000)
                int prefix = bonfire / 10000;
                if (BonfirePrefixToMap.TryGetValue(prefix, out map))
                    return new byte[] { map.Item1, map.Item2 };
                return null;
            }
            catch { return null; }
        }

        private static readonly System.Collections.Generic.Dictionary<int, (byte, byte)> BonfireToMap = new()
        {
            // Depths
            { 1002900, (0x10, 0x00) }, { 1002950, (0x10, 0x00) }, { 1002960, (0x10, 0x00) },
            // Undead Burg / Parish
            { 1012960, (0x10, 0x01) }, { 1012961, (0x10, 0x01) }, { 1012962, (0x10, 0x01) },
            { 1012964, (0x10, 0x01) }, { 1012965, (0x10, 0x01) }, { 1012966, (0x10, 0x01) },
            // Firelink Shrine
            { 1020980, (0x10, 0x02) }, { 1022960, (0x10, 0x02) },
            // Painted World
            { 1102511, (0x11, 0x00) }, { 1102960, (0x11, 0x00) }, { 1102961, (0x11, 0x00) },
            // Darkroot Garden / Basin
            { 1202961, (0x12, 0x00) }, { 1602961, (0x12, 0x00) },
            // Oolacile / Chasm
            { 1212950, (0x12, 0x01) }, { 1212961, (0x12, 0x01) }, { 1212962, (0x12, 0x01) },
            { 1212963, (0x12, 0x01) }, { 1212964, (0x12, 0x01) },
            // Catacombs
            { 1302960, (0x13, 0x00) }, { 1302961, (0x13, 0x00) }, { 1302962, (0x13, 0x00) },
            // Tomb of Giants
            { 1312950, (0x13, 0x01) }, { 1312960, (0x13, 0x01) }, { 1312961, (0x13, 0x01) }, { 1312962, (0x13, 0x01) },
            // Ash Lake / Great Hollow
            { 1320980, (0x13, 0x02) }, { 1322960, (0x13, 0x02) }, { 1322961, (0x13, 0x02) }, { 1322962, (0x13, 0x02) },
            // Blighttown / Quelaag
            { 1400980, (0x14, 0x00) }, { 1402960, (0x14, 0x00) }, { 1402961, (0x14, 0x00) },
            { 1402962, (0x14, 0x00) }, { 1402963, (0x14, 0x00) },
            // Demon Ruins / Lost Izalith
            { 1412950, (0x14, 0x01) }, { 1412960, (0x14, 0x01) }, { 1412961, (0x14, 0x01) },
            { 1412962, (0x14, 0x01) }, { 1412963, (0x14, 0x01) }, { 1412964, (0x14, 0x01) },
            // Sen's Fortress
            { 1502960, (0x15, 0x00) }, { 1502961, (0x15, 0x00) }, { 1502962, (0x15, 0x00) },
            // Anor Londo
            { 1510980, (0x15, 0x01) }, { 1512950, (0x15, 0x01) }, { 1512960, (0x15, 0x01) },
            { 1512961, (0x15, 0x01) }, { 1512962, (0x15, 0x01) },
            // New Londo / Valley of Drakes
            { 1600980, (0x16, 0x00) }, { 1602951, (0x16, 0x00) }, { 1602960, (0x16, 0x00) },
            // The Abyss
            { 1602950, (0x16, 0x01) },
            // Duke's Archives / Crystal Cave
            { 1702900, (0x17, 0x00) }, { 1702950, (0x17, 0x00) }, { 1702960, (0x17, 0x00) },
            { 1702961, (0x17, 0x00) }, { 1702962, (0x17, 0x00) },
            // Kiln / Firelink Altar
            { 1802130, (0x18, 0x00) }, { 1802960, (0x18, 0x00) }, { 1802961, (0x18, 0x00) },
            // Undead Asylum
            { 1812100, (0x18, 0x01) }, { 1812900, (0x18, 0x01) }, { 1812960, (0x18, 0x01) }, { 1812961, (0x18, 0x01) },
        };

        private static readonly System.Collections.Generic.Dictionary<int, (byte, byte)> BonfirePrefixToMap = new()
        {
            { 100, (0x10, 0x00) }, { 101, (0x10, 0x01) }, { 102, (0x10, 0x02) },
            { 110, (0x11, 0x00) }, { 120, (0x12, 0x00) }, { 121, (0x12, 0x01) },
            { 130, (0x13, 0x00) }, { 131, (0x13, 0x01) }, { 132, (0x13, 0x02) },
            { 140, (0x14, 0x00) }, { 141, (0x14, 0x01) },
            { 150, (0x15, 0x00) }, { 151, (0x15, 0x01) },
            { 160, (0x16, 0x00) }, { 170, (0x17, 0x00) },
            { 180, (0x18, 0x00) }, { 181, (0x18, 0x01) },
        };

        /// <summary>
        /// Dumps 32 bytes from ChrClassWarp starting at the given offset.
        /// Used to locate the correct area/block bytes in memory.
        /// </summary>
        public byte[]? DumpChrClassWarp(int offset, int count = 32)
        {
            if (!IsValid) return null;
            try { return ChrClassWarp.ReadBytes(offset, (uint)count); }
            catch { return null; }
        }

        /// <summary>
        /// Scans multiple pointers for the map ID pattern [area, block].
        /// </summary>
        public void SnapshotForMapId()
        {
            if (!IsValid) { Console.WriteLine("[MapSnap] Not valid"); return; }

            string[] names = { "ChrClassWarp", "ChrMapData", "ChrData1", "ChrClassBasePtr", "ChrData2" };
            PHPointer[] ptrs = { ChrClassWarp, ChrMapData, ChrData1, ChrClassBasePtr, ChrData2 };

            if (_mapSnapshot == null)
            {
                _mapSnapshot = new Dictionary<string, byte[]>();
                for (int p = 0; p < names.Length; p++)
                {
                    if (ptrs[p].Resolve() == IntPtr.Zero) continue;
                    byte[]? data = null;
                    try { data = ptrs[p].ReadBytes(0, 0x3000); } catch { }
                    if (data != null) _mapSnapshot[names[p]] = data;
                }
                Console.WriteLine("[MapSnap] Snapshot taken. Move to another area, then snapshot again.");
            }
            else
            {
                Console.WriteLine("[MapSnap] Comparing snapshots...");
                bool anyChange = false;
                for (int p = 0; p < names.Length; p++)
                {
                    string name = names[p];
                    if (!_mapSnapshot.ContainsKey(name)) continue;
                    if (ptrs[p].Resolve() == IntPtr.Zero) continue;
                    byte[]? data = null;
                    try { data = ptrs[p].ReadBytes(0, 0x3000); } catch { }
                    if (data == null) continue;
                    byte[] old = _mapSnapshot[name];
                    int len = Math.Min(old.Length, data.Length);
                    for (int i = 0; i < len - 1; i++)
                    {
                        if (old[i] != data[i])
                        {
                            byte nv = data[i];
                            bool interesting = (nv >= 0x10 && nv <= 0x19) || nv == 0x45 || nv == 0x0F;
                            if (interesting)
                                Console.WriteLine("[MapSnap] " + name + "+0x" + i.ToString("X3")
                                    + ": " + old[i].ToString("X2") + " -> " + data[i].ToString("X2")
                                    + "  next: " + old[i+1].ToString("X2") + " -> " + data[i+1].ToString("X2"));
                            anyChange = true;
                        }
                    }
                }
                if (!anyChange) Console.WriteLine("[MapSnap] No changes detected.");
                // Also search for exact map ID pairs in new data
                Console.WriteLine("[MapSnap] Searching for 0F 01 and 15 01 pairs in changed pointers...");
                for (int p2 = 0; p2 < names.Length; p2++)
                {
                    if (!_mapSnapshot.ContainsKey(names[p2])) continue;
                    if (ptrs[p2].Resolve() == IntPtr.Zero) continue;
                    byte[]? d2 = null;
                    try { d2 = ptrs[p2].ReadBytes(0, 0x3000); } catch { }
                    if (d2 == null) continue;
                    byte[] o2 = _mapSnapshot[names[p2]];
                    int l2 = Math.Min(o2.Length, d2.Length);
                    for (int i = 0; i < l2 - 1; i++)
                    {
                        bool match0F = d2[i] == 0x0F && d2[i+1] == 0x01 && (o2[i] != 0x0F || o2[i+1] != 0x01);
                        bool match15 = d2[i] == 0x15 && d2[i+1] == 0x01 && (o2[i] != 0x15 || o2[i+1] != 0x01);
                        bool match10 = d2[i] == 0x10 && d2[i+1] == 0x02 && (o2[i] != 0x10 || o2[i+1] != 0x02);
                        if (match0F || match15 || match10)
                            Console.WriteLine("[MapSnap] PAIR " + names[p2] + "+0x" + i.ToString("X3")
                                + ": was " + o2[i].ToString("X2") + " " + o2[i+1].ToString("X2")
                                + " -> now " + d2[i].ToString("X2") + " " + d2[i+1].ToString("X2"));
                    }
                }
                _mapSnapshot = null;
                Console.WriteLine("[MapSnap] Reset.");
            }
        }

        public void ScanAllPointersForMapId(byte expectedArea, byte expectedBlock)
        {
            if (!IsValid) { Console.WriteLine("[MapScan] Not valid"); return; }

            string[] names = { "ChrClassWarp", "ChrMapData", "ChrData1", "WorldChrBase",
                "ChrClassBasePtr", "ChrData2", "ChrAnimData", "ChrPosData",
                "EventFlags", "MenuMan", "GraphicsData", "ChrFollowCam" };
            PHPointer[] ptrs = { ChrClassWarp, ChrMapData, ChrData1, WorldChrBase,
                ChrClassBasePtr, ChrData2, ChrAnimData, ChrPosData,
                EventFlags, MenuMan, GraphicsData, ChrFollowCam };

            var current = new Dictionary<string, int>();
            for (int p = 0; p < names.Length; p++)
            {
                if (ptrs[p].Resolve() == IntPtr.Zero) continue;
                byte[]? data = null;
                try { data = ptrs[p].ReadBytes(0, 0x2000); } catch { }
                if (data == null) continue;
                for (int i = 0; i < data.Length - 1; i++)
                {
                    if (data[i] == expectedArea && data[i + 1] == expectedBlock)
                    {
                        string key = names[p] + "+0x" + i.ToString("X3");
                        current[key] = 1;
                        Console.WriteLine("[MapScan] " + key + " = " + expectedArea.ToString("X2") + " " + expectedBlock.ToString("X2"));
                    }
                }
            }

            if (_mapScanResults == null)
            {
                _mapScanResults = current;
                Console.WriteLine("[MapScan] First scan saved (" + current.Count + " hits). Move to another area and scan again.");
            }
            else
            {
                foreach (var key in _mapScanResults.Keys)
                    if (!current.ContainsKey(key))
                        Console.WriteLine("[MapScan] *** CANDIDATE: " + key);
                foreach (var key in current.Keys)
                    if (!_mapScanResults.ContainsKey(key))
                        Console.WriteLine("[MapScan] NEW: " + key);
                foreach (var key in _mapScanResults.Keys)
                    if (current.ContainsKey(key))
                        Console.WriteLine("[MapScan] STILL PRESENT: " + key);
                _mapScanResults = null;
                Console.WriteLine("[MapScan] Reset.");
            }
        }

        public void UndumpFollowCam(byte[] value)
        {
            ChrFollowCam.WriteBytes(0, value);
        }
        #endregion

        #region Diagnostics
        public string GetDiagnostics()
        {
            if (!Hooked) return "Not hooked";
            if (!AOBScanSucceeded) return "AOB scan failed";
            
            try
            {
                IntPtr cd1 = ChrData1.Resolve();
                if (cd1 == IntPtr.Zero) return "ChrData1 NULL";
                
                IntPtr cmd = ChrMapData.Resolve();
                if (cmd == IntPtr.Zero) return "ChrMapData NULL";
                
                IntPtr cpd = ChrPosData.Resolve();
                if (cpd == IntPtr.Zero) return "ChrPosData NULL";
                
                return $"OK - CD1=0x{cd1.ToInt64():X}";
            }
            catch (Exception ex) { return $"Error: {ex.Message}"; }
        }

        #region Event Flags
        private static readonly System.Collections.Generic.Dictionary<string, int> eventFlagGroups = new()
        {
            {"0", 0x00000}, {"1", 0x00500}, {"5", 0x05F00}, {"6", 0x0B900}, {"7", 0x11300},
        };

        private static readonly System.Collections.Generic.Dictionary<string, int> eventFlagAreas = new()
        {
            {"000", 0}, {"100", 1}, {"101", 2}, {"102", 3}, {"110", 4},
            {"120", 5}, {"121", 6}, {"130", 7}, {"131", 8}, {"132", 9},
            {"140", 10}, {"141", 11}, {"150", 12}, {"151", 13},
            {"160", 14}, {"170", 15}, {"180", 16}, {"181", 17},
        };

        private int GetEventFlagOffset(int ID, out uint mask)
        {
            string idString = ID.ToString("D8");
            if (idString.Length == 8)
            {
                string group = idString.Substring(0, 1);
                string area = idString.Substring(1, 3);
                int section = int.Parse(idString.Substring(4, 1));
                int number = int.Parse(idString.Substring(5, 3));

                if (eventFlagGroups.ContainsKey(group) && eventFlagAreas.ContainsKey(area))
                {
                    int offset = eventFlagGroups[group];
                    offset += eventFlagAreas[area] * 0x500;
                    offset += section * 128;
                    offset += (number - (number % 32)) / 8;
                    mask = 0x80000000 >> (number % 32);
                    return offset;
                }
            }
            throw new ArgumentException("Unknown event flag ID: " + ID);
        }

        public bool ReadEventFlag(int ID)
        {
            int offset = GetEventFlagOffset(ID, out uint mask);
            return EventFlags.ReadFlag32(offset, mask);
        }

        public void WriteEventFlag(int ID, bool state)
        {
            int offset = GetEventFlagOffset(ID, out uint mask);
            EventFlags.WriteFlag32(offset, mask, state);
        }

        public int CurrentAnim
        {
            get
            {
                var anim = CreateChildPointer(ChrData1, 0x68, 0x48);
                return anim.ReadInt32(0x80);
            }
        }
        #endregion

        public string GetBonfireDiagnostics()
        {
            if (!Hooked) return "Not hooked";
            
            IntPtr warp = ChrClassWarp.Resolve();
            IntPtr basePtr = ChrClassBasePtr.Resolve();
            IntPtr func = BonfireWarpAddr.Resolve();
            
            return $"ChrClassWarp=0x{warp.ToInt64():X} ChrClassBase=0x{basePtr.ToInt64():X} BonfireFunc=0x{func.ToInt64():X}";
        }
        #endregion

        public void Dispose()
        {
            Stop();
        }
    }
}
