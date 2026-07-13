using PropertyHook;
using System;
using System.Diagnostics;
using System.Text;

namespace DS1ParamEditor
{
    public sealed class PtdHook : PHook, IGadgetHook
    {
        // Version check
        private PHPointer CheckVersion;
        private const uint VERSION_RELEASE = 0xFC293654;
        private const uint VERSION_DEBUG = 0xCE9634B4;
        private const uint VERSION_BETA = 0xE91B11E2;

        // Cheat addresses (absolute AOB, boolean at offset)
        private PHPointer AllNoMagicQtyConsume;
        private PHPointer _playerNoDeadPtr;
        private PHPointer _playerExterminatePtr;
        private PHPointer AllNoStaminaConsume;

        // Graphics / map
        private PHPointer NodeGraph;
        private PHPointer Compasses;
        private PHPointer CompassSmall;
        private PHPointer Altimeter;
        private PHPointer CompassLarge;
        private PHPointer DrawMapPtr;

        // Core data pointers
        private PHPointer CharData1;
        private PHPointer CharMapData;
        private PHPointer AnimData;
        private PHPointer CharPosData;
        private PHPointer CharData2;
        private PHPointer GraphicsData;
        private PHPointer WorldState;
        private PHPointer MenuManager;
        private PHPointer ChrFollowCam;
        private PHPointer EventFlags;
        private PHPointer Unknown1;
        private PHPointer Unknown2;
        private PHPointer Unknown3;

        // Function pointers for shellcode
        private PHPointer FuncItemGet;
        private PHPointer FuncLevelUp;
        private PHPointer FuncBonfireWarp;
        private PHPointer FuncBonfireWarpUnknown1;
        private PHPointer FuncItemDrop;
        private PHPointer FuncItemDropUnknown1;
        private PHPointer FuncItemDropUnknown2;

        public int ProcessId => Process?.Id ?? -1;
        public bool IsValid => Hooked && CharData1.Resolve() != IntPtr.Zero;
        public bool Loaded => Hooked && ChrFollowCam.Resolve() != IntPtr.Zero;
        public bool Focused
        {
            get
            {
                try { return Hooked && !Process.HasExited && User32.GetForegroundProcessID() == Process.Id; }
                catch { return false; }
            }
        }

        public string Version { get; private set; } = "None";

        public PtdHook(int refreshInterval, int minLifetime, string? exePath) :
            base(refreshInterval, minLifetime, p => MatchesExe(p, exePath))
        {
            CheckVersion = CreateBasePointer((IntPtr)PtdOffsets.CheckVersion);

            RegisterAOBs();
            RegisterFunctions();

            OnHooked += PtdHook_OnHooked;
            OnUnhooked += PtdHook_OnUnhooked;
        }

        private static bool MatchesExe(Process p, string? exePath)
        {
            if (string.IsNullOrEmpty(exePath)) return false;
            try { return string.Equals(p.MainModule.FileName, exePath, StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        private void RegisterAOBs()
        {
            AllNoMagicQtyConsume = RegisterAbsoluteAOB(PtdOffsets.AllNoMagicQtyConsumeAOB, PtdOffsets.AllNoMagicQtyConsumeAOBOffset);
            _playerNoDeadPtr = RegisterAbsoluteAOB(PtdOffsets.PlayerNoDeadAOB, PtdOffsets.PlayerNoDeadAOBOffset);
            _playerExterminatePtr = RegisterAbsoluteAOB(PtdOffsets.PlayerExterminateAOB, PtdOffsets.PlayerExterminateAOBOffset);
            AllNoStaminaConsume = RegisterAbsoluteAOB(PtdOffsets.AllNoStaminaConsumeAOB, PtdOffsets.AllNoStaminaConsumeAOBOffset);
            NodeGraph = RegisterAbsoluteAOB(PtdOffsets.NodeGraphAOB, PtdOffsets.NodeGraphAOBOffset);
            Compasses = RegisterAbsoluteAOB(PtdOffsets.CompassAOB);
            CompassSmall = CreateChildPointer(Compasses, PtdOffsets.CompassSmallAOBOffset);
            Altimeter = CreateChildPointer(Compasses, PtdOffsets.AltimeterAOBOffset);
            CompassLarge = CreateChildPointer(Compasses, PtdOffsets.CompassLargeAOBOffset);
            DrawMapPtr = RegisterAbsoluteAOB(PtdOffsets.DrawMapAOB, PtdOffsets.DrawMapAOBOffset);

            CharData1 = RegisterAbsoluteAOB(PtdOffsets.CharData1AOB, PtdOffsets.CharData1AOBOffset,
                PtdOffsets.CharData1Offset1, PtdOffsets.CharData1Offset2, PtdOffsets.CharData1Offset3);
            CharData2 = RegisterAbsoluteAOB(PtdOffsets.CharData2AOB, PtdOffsets.CharData2AOBOffset,
                PtdOffsets.CharData2Offset1, PtdOffsets.CharData2Offset2);
            GraphicsData = RegisterAbsoluteAOB(PtdOffsets.GraphicsDataAOB, PtdOffsets.GraphicsDataAOBOffset,
                PtdOffsets.GraphicsDataOffset1, PtdOffsets.GraphicsDataOffset2);
            WorldState = RegisterAbsoluteAOB(PtdOffsets.WorldStateAOB, PtdOffsets.WorldStateAOBOffset,
                PtdOffsets.WorldStateOffset1);
            MenuManager = RegisterAbsoluteAOB(PtdOffsets.MenuManagerAOB, PtdOffsets.MenuManagerAOBOffset);
            ChrFollowCam = RegisterAbsoluteAOB(PtdOffsets.ChrFollowCamAOB, PtdOffsets.ChrFollowCamAOBOffset,
                PtdOffsets.ChrFollowCamOffset1, PtdOffsets.ChrFollowCamOffset2, PtdOffsets.ChrFollowCamOffset3);
            EventFlags = RegisterAbsoluteAOB(PtdOffsets.EventFlagsAOB, PtdOffsets.EventFlagsAOBOffset,
                PtdOffsets.EventFlagsOffset1, PtdOffsets.EventFlagsOffset2);
            Unknown1 = RegisterAbsoluteAOB(PtdOffsets.Unknown1AOB, PtdOffsets.Unknown1AOBOffset,
                PtdOffsets.Unknown1Offset1);
            Unknown2 = RegisterAbsoluteAOB(PtdOffsets.Unknown2AOB, PtdOffsets.Unknown2AOBOffset);
            Unknown3 = RegisterAbsoluteAOB(PtdOffsets.Unknown3AOB, PtdOffsets.Unknown3AOBOffset);

            CharMapData = CreateChildPointer(CharData1, (int)PtdOffsets.CharData1.CharMapDataPtr);
            AnimData = CreateChildPointer(CharMapData, (int)PtdOffsets.CharMapData.AnimDataPtr);
            CharPosData = CreateChildPointer(CharMapData, (int)PtdOffsets.CharMapData.CharPosDataPtr);
        }

        private void RegisterFunctions()
        {
            FuncItemGet = RegisterAbsoluteAOB(PtdOffsets.FuncItemGetAOB);
            FuncLevelUp = RegisterAbsoluteAOB(PtdOffsets.FuncLevelUpAOB);
            FuncBonfireWarp = RegisterAbsoluteAOB(PtdOffsets.FuncBonfireWarpAOB);
            FuncBonfireWarpUnknown1 = RegisterAbsoluteAOB(PtdOffsets.FuncBonfireWarpUnknown1AOB,
                PtdOffsets.FuncBonfireWarpUnknown1AOBOffset);
            FuncItemDrop = RegisterAbsoluteAOB(PtdOffsets.FuncItemDropAOB);
            FuncItemDropUnknown1 = RegisterAbsoluteAOB(PtdOffsets.FuncItemDropUnknown1AOB,
                PtdOffsets.FuncItemDropUnknown1AOBOffset);
            FuncItemDropUnknown2 = RegisterAbsoluteAOB(PtdOffsets.FuncItemDropUnknown2AOB,
                PtdOffsets.FuncItemDropUnknown2AOBOffset);
        }

        private void PtdHook_OnHooked(object sender, PHEventArgs e)
        {
            uint version = CheckVersion.ReadUInt32(0);
            Version = version switch
            {
                VERSION_RELEASE => "Steam",
                VERSION_DEBUG => "Debug",
                VERSION_BETA => "Steamworks Beta",
                _ => $"Unknown 0x{version:X8}"
            };
            Console.WriteLine($"[PtdHook] Hooked to PTDE {Version}");
        }

        private void PtdHook_OnUnhooked(object sender, PHEventArgs e)
        {
            Version = "None";
            Console.WriteLine("[PtdHook] Unhooked");
        }

        #region Helpers
        private void PtdExecute(byte[] template, params Action<byte[], int>[] fillers)
        {
            byte[] asm = (byte[])template.Clone();
            for (int i = 0; i < fillers.Length; i++)
                fillers[i](asm, i);
            uint exitCode = Execute(asm);
            if (exitCode == 0xC0000005)
                Console.WriteLine("[PtdHook] ACCESS_VIOLATION in shellcode execution");
        }
        #endregion

        #region IGadgetHook: Player Stats
        public int Health
        {
            get => CharData1.ReadInt32((int)PtdOffsets.CharData1.HP);
            set => CharData1.WriteInt32((int)PtdOffsets.CharData1.HP, value);
        }
        public int HealthMax => CharData2.ReadInt32((int)PtdOffsets.CharData2.HPMax);
        public int Stamina
        {
            get => CharData1.ReadInt32((int)PtdOffsets.CharData1.Stamina);
            set => CharData1.WriteInt32((int)PtdOffsets.CharData1.Stamina, value);
        }
        public int StaminaMax => CharData2.ReadInt32((int)PtdOffsets.CharData2.StaminaMax);
        public int Humanity
        {
            get => CharData2.ReadInt32((int)PtdOffsets.CharData2.Humanity);
            set => CharData2.WriteInt32((int)PtdOffsets.CharData2.Humanity, value);
        }
        public int Souls
        {
            get => CharData2.ReadInt32((int)PtdOffsets.CharData2.Souls);
            set => CharData2.WriteInt32((int)PtdOffsets.CharData2.Souls, value);
        }
        public int SoulLevel => CharData2.ReadInt32((int)PtdOffsets.CharData2.SoulLevel);
        public byte Class
        {
            get => CharData2.ReadByte((int)PtdOffsets.CharData2.Class);
            set => CharData2.WriteByte((int)PtdOffsets.CharData2.Class, value);
        }
        public int Vitality => CharData2.ReadInt32((int)PtdOffsets.CharData2.Vitality);
        public int Attunement => CharData2.ReadInt32((int)PtdOffsets.CharData2.Attunement);
        public int Endurance => CharData2.ReadInt32((int)PtdOffsets.CharData2.Endurance);
        public int Strength => CharData2.ReadInt32((int)PtdOffsets.CharData2.Strength);
        public int Dexterity => CharData2.ReadInt32((int)PtdOffsets.CharData2.Dexterity);
        public int Resistance => CharData2.ReadInt32((int)PtdOffsets.CharData2.Resistance);
        public int Intelligence => CharData2.ReadInt32((int)PtdOffsets.CharData2.Intelligence);
        public int Faith => CharData2.ReadInt32((int)PtdOffsets.CharData2.Faith);
        #endregion

        #region IGadgetHook: Position & Movement
        public bool GetPosition(out float x, out float y, out float z, out float angle)
        {
            x = y = z = angle = 0f;
            if (!IsValid) return false;
            try
            {
                x = CharPosData.ReadSingle((int)PtdOffsets.CharPosData.PosX);
                y = CharPosData.ReadSingle((int)PtdOffsets.CharPosData.PosY);
                z = CharPosData.ReadSingle((int)PtdOffsets.CharPosData.PosZ);
                angle = CharPosData.ReadSingle((int)PtdOffsets.CharPosData.PosAngle);
                return true;
            }
            catch { return false; }
        }

        public void GetStablePosition(out float x, out float y, out float z, out float angle)
        {
            x = WorldState.ReadSingle((int)PtdOffsets.WorldState.PosStableX);
            y = WorldState.ReadSingle((int)PtdOffsets.WorldState.PosStableY);
            z = WorldState.ReadSingle((int)PtdOffsets.WorldState.PosStableZ);
            angle = WorldState.ReadSingle((int)PtdOffsets.WorldState.PosStableAngle);
        }

        public bool PosWarp(float x, float y, float z, float angle)
        {
            if (!IsValid) return false;
            try
            {
                CharMapData.WriteSingle((int)PtdOffsets.CharMapData.WarpX, x);
                CharMapData.WriteSingle((int)PtdOffsets.CharMapData.WarpY, y);
                CharMapData.WriteSingle((int)PtdOffsets.CharMapData.WarpZ, z);
                CharMapData.WriteSingle((int)PtdOffsets.CharMapData.WarpAngle, angle);
                CharMapData.WriteBoolean((int)PtdOffsets.CharMapData.Warp, true);
                return true;
            }
            catch { return false; }
        }

        public bool NoGravity
        {
            set => CharData1.WriteFlag32((int)PtdOffsets.CharData1.CharFlags1,
                (uint)PtdOffsets.CharFlags1.SetDisableGravity, value);
        }

        public bool NoCollision
        {
            set => CharMapData.WriteFlag32((int)PtdOffsets.CharMapData.CharMapFlags,
                (uint)PtdOffsets.CharMapFlags.DisableMapHit, value);
        }

        public bool DeathCam
        {
            get => Unknown2.ReadBoolean((int)PtdOffsets.Unknown2.DeathCam);
            set => Unknown2.WriteBoolean((int)PtdOffsets.Unknown2.DeathCam, value);
        }

        public float AnimSpeed
        {
            set => AnimData.WriteSingle((int)PtdOffsets.AnimData.PlaySpeed, value);
        }
        #endregion

        #region IGadgetHook: Bonfire
        public int GetLastBonfire()
        {
            if (!IsValid) return -1;
            try { return WorldState.ReadInt32((int)PtdOffsets.WorldState.LastBonfire); }
            catch { return -1; }
        }

        public int LastBonfire
        {
            get => GetLastBonfire();
            set { if (IsValid) WorldState.WriteInt32((int)PtdOffsets.WorldState.LastBonfire, value); }
        }

        public void BonfireWarp()
        {
            if (!IsValid) return;

            IntPtr funcUnknown1 = FuncBonfireWarpUnknown1.Resolve();
            IntPtr funcWarp = FuncBonfireWarp.Resolve();
            if (funcUnknown1 == IntPtr.Zero || funcWarp == IntPtr.Zero) return;

            byte[] asm = (byte[])PtdAssembly.BonfireWarp.Clone();
            Buffer.BlockCopy(BitConverter.GetBytes((int)funcUnknown1), 0, asm, 1, 4);
            Buffer.BlockCopy(BitConverter.GetBytes((int)funcWarp), 0, asm, 0xE, 4);
            Execute(asm);
        }
        #endregion

        #region IGadgetHook: Items
        public void GetItem(int category, int id, int quantity)
        {
            if (!IsValid) return;

            IntPtr itemGetFunc = FuncItemGet.Resolve();
            if (itemGetFunc == IntPtr.Zero) return;

            byte[] asm = (byte[])PtdAssembly.ItemGet.Clone();
            Buffer.BlockCopy(BitConverter.GetBytes(category), 0, asm, 1, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(quantity), 0, asm, 6, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(id), 0, asm, 0xB, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(0), 0, asm, 0x10, 4);
            Buffer.BlockCopy(BitConverter.GetBytes((int)itemGetFunc), 0, asm, 0x22, 4);

            uint exitCode = Execute(asm);
            if (exitCode == 0xC0000005)
                Console.WriteLine("[PtdHook:GetItem] ACCESS_VIOLATION");
        }

        public void DropItem(int category, int id, int quantity)
        {
            if (!IsValid) return;

            IntPtr funcDrop = FuncItemDrop.Resolve();
            IntPtr ptr1 = FuncItemDropUnknown1.Resolve();
            IntPtr ptr2 = FuncItemDropUnknown2.Resolve();
            if (funcDrop == IntPtr.Zero || ptr1 == IntPtr.Zero || ptr2 == IntPtr.Zero) return;

            byte[] asm = (byte[])PtdAssembly.ItemDrop.Clone();
            Buffer.BlockCopy(BitConverter.GetBytes(category), 0, asm, 1, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(id), 0, asm, 6, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(quantity), 0, asm, 0x10, 4);
            Buffer.BlockCopy(BitConverter.GetBytes((int)ptr1), 0, asm, 0x15, 4);
            Buffer.BlockCopy(BitConverter.GetBytes((int)ptr2), 0, asm, 0x32, 4);
            Buffer.BlockCopy(BitConverter.GetBytes((int)funcDrop), 0, asm, 0x38, 4);

            uint exitCode = Execute(asm);
            if (exitCode == 0xC0000005)
                Console.WriteLine("[PtdHook:DropItem] ACCESS_VIOLATION");
        }
        #endregion

        #region IGadgetHook: Cheats
        public bool PlayerDeadMode
        {
            get => CharData1.ReadFlag32((int)PtdOffsets.CharData1.CharFlags1,
                (uint)PtdOffsets.CharFlags1.SetDeadMode);
            set => CharData1.WriteFlag32((int)PtdOffsets.CharData1.CharFlags1,
                (uint)PtdOffsets.CharFlags1.SetDeadMode, value);
        }

        public bool PlayerNoDead { set => _playerNoDeadPtr.WriteBoolean(0, value); }
        public bool PlayerDisableDamage
        {
            set => CharData1.WriteFlag32((int)PtdOffsets.CharData1.CharFlags1,
                (uint)PtdOffsets.CharFlags1.DisableDamage, value);
        }
        public bool PlayerNoHit
        {
            set => CharData1.WriteFlag32((int)PtdOffsets.CharData1.CharFlags2,
                (uint)PtdOffsets.CharFlags2.NoHit, value);
        }
        public bool PlayerNoStamina
        {
            set => CharData1.WriteFlag32((int)PtdOffsets.CharData1.CharFlags2,
                (uint)PtdOffsets.CharFlags2.NoStamConsume, value);
        }
        public bool PlayerSuperArmor
        {
            set => CharData1.WriteFlag32((int)PtdOffsets.CharData1.CharFlags1,
                (uint)PtdOffsets.CharFlags1.SetSuperArmor, value);
        }
        public bool PlayerHide { set => AllNoStaminaConsume.WriteBoolean((int)PtdOffsets.ChrDbg.PlayerHide, value); }
        public bool PlayerSilence { set => AllNoStaminaConsume.WriteBoolean((int)PtdOffsets.ChrDbg.PlayerSilence, value); }
        public bool PlayerExterminate { set => _playerExterminatePtr.WriteBoolean(0, value); }
        public bool PlayerNoGoods
        {
            set => CharData1.WriteFlag32((int)PtdOffsets.CharData1.CharFlags2,
                (uint)PtdOffsets.CharFlags2.NoGoodsConsume, value);
        }
        public bool AllNoArrow { set => AllNoStaminaConsume.WriteBoolean((int)PtdOffsets.ChrDbg.AllNoArrowConsume, value); }
        public bool AllNoMagicQty { set => AllNoMagicQtyConsume.WriteBoolean(0, value); }
        public bool AllNoDead { set => AllNoStaminaConsume.WriteBoolean((int)PtdOffsets.ChrDbg.AllNoDead, value); }
        public bool AllNoDamage { set => AllNoStaminaConsume.WriteBoolean((int)PtdOffsets.ChrDbg.AllNoDamage, value); }
        public bool AllNoHit { set => AllNoStaminaConsume.WriteBoolean((int)PtdOffsets.ChrDbg.AllNoHit, value); }
        public bool AllNoStamina { set => AllNoStaminaConsume.WriteBoolean((int)PtdOffsets.ChrDbg.AllNoStaminaConsume, value); }
        public bool AllNoAttack { set => AllNoStaminaConsume.WriteBoolean((int)PtdOffsets.ChrDbg.AllNoAttack, value); }
        public bool AllNoMove { set => AllNoStaminaConsume.WriteBoolean((int)PtdOffsets.ChrDbg.AllNoMove, value); }
        public bool AllNoUpdateAI { set => AllNoStaminaConsume.WriteBoolean((int)PtdOffsets.ChrDbg.AllNoUpdateAI, value); }
        #endregion

        #region IGadgetHook: Graphics
        public bool DrawMap { set => DrawMapPtr.WriteBoolean((int)PtdOffsets.DrawMap.DrawMap, value); }
        public bool DrawObjects { set => DrawMapPtr.WriteBoolean((int)PtdOffsets.DrawMap.DrawObjects, value); }
        public bool DrawCharacters { set => DrawMapPtr.WriteBoolean((int)PtdOffsets.DrawMap.DrawCreatures, value); }
        public bool DrawSFX { set => DrawMapPtr.WriteBoolean((int)PtdOffsets.DrawMap.DrawSFX, value); }
        public bool DrawCutscenes
        {
            set => NodeGraph.WriteBoolean(PtdOffsets.NodeGraphAOBOffset, value);
        }
        public bool FilterOverride
        {
            set => GraphicsData.WriteBoolean((int)PtdOffsets.GraphicsData.EnableFilter, value);
        }

        public void SetFilterValues(float brightR, float brightG, float brightB,
            float contR, float contG, float contB, float saturation, float hue)
        {
            GraphicsData.WriteSingle((int)PtdOffsets.GraphicsData.BrightnessR, brightR);
            GraphicsData.WriteSingle((int)PtdOffsets.GraphicsData.BrightnessG, brightG);
            GraphicsData.WriteSingle((int)PtdOffsets.GraphicsData.BrightnessB, brightB);
            GraphicsData.WriteSingle((int)PtdOffsets.GraphicsData.ContrastR, contR);
            GraphicsData.WriteSingle((int)PtdOffsets.GraphicsData.ContrastG, contG);
            GraphicsData.WriteSingle((int)PtdOffsets.GraphicsData.ContrastB, contB);
            GraphicsData.WriteSingle((int)PtdOffsets.GraphicsData.Saturation, saturation);
            GraphicsData.WriteSingle((int)PtdOffsets.GraphicsData.Hue, hue);
        }

        // PTDE-specific graphics extras (not in IGadgetHook but useful)
        public void DrawBoundingBoxes(bool enable) => GraphicsData.WriteBoolean((int)PtdOffsets.GraphicsData.DrawBoundingBoxes, enable);
        public void DrawTextures(bool enable) => GraphicsData.WriteBoolean((int)PtdOffsets.GraphicsData.DrawTextures, enable);
        public void DrawTrans(bool enable) => GraphicsData.WriteBoolean((int)PtdOffsets.GraphicsData.NormalDraw_Trans, enable);
        public void DrawShadows(bool enable) => GraphicsData.WriteBoolean((int)PtdOffsets.GraphicsData.DrawShadows, enable);
        public void DrawSpriteShadows(bool enable) => GraphicsData.WriteBoolean((int)PtdOffsets.GraphicsData.DrawSpriteShadows, enable);
        public void DrawCompassLarge(bool enable) => CompassLarge.WriteBoolean(0, enable);
        public void DrawCompassSmall(bool enable) => CompassSmall.WriteBoolean(0, enable);
        public void DrawAltimeter(bool enable) => Altimeter.WriteBoolean(0, enable);
        #endregion

        #region IGadgetHook: Misc
        public void MenuKick()
        {
            Unknown3.WriteInt32((int)PtdOffsets.Unknown3.MenuKick, 2);
        }

        public byte[] DumpFollowCam()
        {
            return ChrFollowCam.ReadBytes(0, 512);
        }

        public void UndumpFollowCam(byte[] value)
        {
            ChrFollowCam.WriteBytes(0, value);
        }

        public byte[]? ReadMapId()
        {
            if (!IsValid) return null;
            try
            {
                return new byte[] { Unknown1.ReadByte((int)PtdOffsets.Unknown1.Area), Unknown1.ReadByte((int)PtdOffsets.Unknown1.World) };
            }
            catch { return null; }
        }

        // PTDE-specific extras
        public byte World => Unknown1.ReadByte((int)PtdOffsets.Unknown1.World);
        public byte Area => Unknown1.ReadByte((int)PtdOffsets.Unknown1.Area);
        public int ChrType
        {
            get => CharData1.ReadInt32((int)PtdOffsets.CharData1.ChrType);
            set => CharData1.WriteInt32((int)PtdOffsets.CharData1.ChrType, value);
        }
        public int TeamType
        {
            get => CharData1.ReadInt32((int)PtdOffsets.CharData1.TeamType);
            set => CharData1.WriteInt32((int)PtdOffsets.CharData1.TeamType, value);
        }
        public int PlayRegion
        {
            get => CharData1.ReadInt32((int)PtdOffsets.CharData1.PlayRegion);
            set => CharData1.WriteInt32((int)PtdOffsets.CharData1.PlayRegion, value);
        }

        public void LevelUp(int vitality, int attunement, int endurance, int strength,
            int dexterity, int resistance, int intelligence, int faith, int level)
        {
            if (!IsValid) return;
            IntPtr func = FuncLevelUp.Resolve();
            if (func == IntPtr.Zero) return;

            // Allocate memory for the stats struct in the game process
            int structSize = 0x200;
            IntPtr statsBlock = Allocate((uint)structSize);
            if (statsBlock == IntPtr.Zero) return;
            try
            {
                // Write stat values to known offsets in the struct
                Kernel32.WriteInt32(Handle, statsBlock + (int)PtdOffsets.FuncLevelUp.Vitality, vitality);
                Kernel32.WriteInt32(Handle, statsBlock + (int)PtdOffsets.FuncLevelUp.Attunement, attunement);
                Kernel32.WriteInt32(Handle, statsBlock + (int)PtdOffsets.FuncLevelUp.Endurance, endurance);
                Kernel32.WriteInt32(Handle, statsBlock + (int)PtdOffsets.FuncLevelUp.Strength, strength);
                Kernel32.WriteInt32(Handle, statsBlock + (int)PtdOffsets.FuncLevelUp.Dexterity, dexterity);
                Kernel32.WriteInt32(Handle, statsBlock + (int)PtdOffsets.FuncLevelUp.Resistance, resistance);
                Kernel32.WriteInt32(Handle, statsBlock + (int)PtdOffsets.FuncLevelUp.Intelligence, intelligence);
                Kernel32.WriteInt32(Handle, statsBlock + (int)PtdOffsets.FuncLevelUp.Faith, faith);
                Kernel32.WriteInt32(Handle, statsBlock + (int)PtdOffsets.FuncLevelUp.SoulLevel, level);
                Kernel32.WriteInt32(Handle, statsBlock + (int)PtdOffsets.FuncLevelUp.Souls, Souls);

                byte[] asm = (byte[])PtdAssembly.LevelUp.Clone();
                int statsAddr = (int)statsBlock;
                int funcAddr = (int)func;
                Buffer.BlockCopy(BitConverter.GetBytes(statsAddr), 0, asm, 1, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(statsAddr), 0, asm, 6, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(funcAddr), 0, asm, 0xB, 4);

                Execute(asm);
            }
            finally
            {
                Free(statsBlock);
            }
        }

        public bool ReadEventFlag(int id)
        {
            if (!IsValid) return false;
            IntPtr flags = EventFlags.Resolve();
            if (flags == IntPtr.Zero) return false;
            try
            {
                int byteOffset = id / 8;
                int bit = id % 8;
                byte val = Kernel32.ReadByte(Handle, flags + byteOffset);
                return (val & (1 << bit)) != 0;
            }
            catch { return false; }
        }

        public void WriteEventFlag(int id, bool state)
        {
            if (!IsValid) return;
            IntPtr flags = EventFlags.Resolve();
            if (flags == IntPtr.Zero) return;
            try
            {
                int byteOffset = id / 8;
                int bit = id % 8;
                byte val = Kernel32.ReadByte(Handle, flags + byteOffset);
                if (state) val |= (byte)(1 << bit);
                else val &= (byte)~(1 << bit);
                Kernel32.WriteByte(Handle, flags + byteOffset, val);
            }
            catch { }
        }

        public int CurrentAnim => 0;

        public void UnlockAllGestures()
        {
            if (!IsValid) return;
            IntPtr gestures = CharData2.Resolve();
            if (gestures == IntPtr.Zero) return;
            try
            {
                // Gesture flags start at GesturesUnlockedPtr offset, each gesture is a byte
                for (int g = 0; g < 15; g++)
                {
                    int offset = (int)PtdOffsets.CharData2.GesturesUnlockedPtr + g * 4;
                    Kernel32.WriteByte(Handle, gestures + offset, 1);
                }
            }
            catch { }
        }
        #endregion

        #region IGadgetHook: Diagnostics
        public string GetDiagnostics()
        {
            if (!Hooked) return "Not hooked";
            if (!AOBScanSucceeded) return "AOB scan failed";
            try
            {
                IntPtr cd1 = CharData1.Resolve();
                if (cd1 == IntPtr.Zero) return "CharData1 NULL";
                return $"OK - CD1=0x{cd1.ToInt32():X}";
            }
            catch (Exception ex) { return $"Error: {ex.Message}"; }
        }

        public string GetBonfireDiagnostics()
        {
            if (!Hooked) return "Not hooked";
            IntPtr bw = FuncBonfireWarp.Resolve();
            IntPtr bwu = FuncBonfireWarpUnknown1.Resolve();
            return $"BonfireWarp=0x{bw.ToInt32():X} Unknown1=0x{bwu.ToInt32():X}";
        }
        #endregion

        public void Dispose()
        {
            Stop();
        }
    }
}
