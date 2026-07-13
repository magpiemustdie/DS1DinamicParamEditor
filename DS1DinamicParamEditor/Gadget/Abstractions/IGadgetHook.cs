using System;
using PropertyHook;

namespace DS1ParamEditor
{
    public interface IGadgetHook : IDisposable
    {
        bool Hooked { get; }
        bool AOBScanSucceeded { get; }
        bool Loaded { get; }
        bool Focused { get; }
        string Version { get; }
        bool IsValid { get; }
        int ProcessId { get; }

        event EventHandler<PHEventArgs>? OnHooked;
        event EventHandler<PHEventArgs>? OnUnhooked;

        void Start();
        void Stop();

        #region Player Stats
        int Health { get; set; }
        int HealthMax { get; }
        int Stamina { get; set; }
        int StaminaMax { get; }
        int Humanity { get; set; }
        int Souls { get; set; }
        int SoulLevel { get; }
        byte Class { get; set; }
        int Vitality { get; }
        int Attunement { get; }
        int Endurance { get; }
        int Strength { get; }
        int Dexterity { get; }
        int Resistance { get; }
        int Intelligence { get; }
        int Faith { get; }
        #endregion

        #region Position & Movement
        bool GetPosition(out float x, out float y, out float z, out float angle);
        void GetStablePosition(out float x, out float y, out float z, out float angle);
        bool PosWarp(float x, float y, float z, float angle);
        bool NoGravity { set; }
        bool NoCollision { set; }
        float AnimSpeed { set; }
        bool DeathCam { get; set; }
        byte[] DumpFollowCam();
        void UndumpFollowCam(byte[] value);
        #endregion

        #region Bonfire
        int GetLastBonfire();
        int LastBonfire { get; set; }
        void BonfireWarp();
        #endregion

        #region Items
        void GetItem(int category, int id, int quantity);
        #endregion

        #region Cheats
        bool PlayerDeadMode { get; set; }
        bool PlayerNoDead { set; }
        bool PlayerDisableDamage { set; }
        bool PlayerNoHit { set; }
        bool PlayerNoStamina { set; }
        bool PlayerSuperArmor { set; }
        bool PlayerHide { set; }
        bool PlayerSilence { set; }
        bool PlayerExterminate { set; }
        bool PlayerNoGoods { set; }
        bool AllNoArrow { set; }
        bool AllNoMagicQty { set; }
        bool AllNoDead { set; }
        bool AllNoDamage { set; }
        bool AllNoHit { set; }
        bool AllNoStamina { set; }
        bool AllNoAttack { set; }
        bool AllNoMove { set; }
        bool AllNoUpdateAI { set; }
        #endregion

        #region Graphics
        bool DrawMap { set; }
        bool DrawObjects { set; }
        bool DrawCharacters { set; }
        bool DrawSFX { set; }
        bool DrawCutscenes { set; }
        bool FilterOverride { set; }
        void SetFilterValues(float brightR, float brightG, float brightB, float contR, float contG, float contB, float saturation, float hue);
        #endregion

        #region Misc
        void MenuKick();
        byte[]? ReadMapId();
        bool ReadEventFlag(int id);
        void WriteEventFlag(int id, bool state);
        int CurrentAnim { get; }
        #endregion

        #region Diagnostics
        string GetDiagnostics();
        string GetBonfireDiagnostics();
        #endregion
    }
}
