using System;
using System.Numerics;
using ImGuiNET;

namespace DS1ParamEditor
{
    public sealed class GadgetView
    {
        private readonly EditorState _state;

        // Player
        private int _health, _stamina;
        private float _storedX, _storedY, _storedZ, _storedAngle;
        private int _storedHealth, _storedStamina;
        private bool _storedDeathCam;
        private byte[]? _storedCam;
        private bool _hasSavedState;
        private bool _playerNoGravity, _playerNoCollision, _playerDeathCam;
        private bool _speedEnabled;
        private float _animSpeed = 1f;
        private int _selectedBonfire;
        // Stats
        private int _souls, _humanity;
        private int _selectedClass;

        // Cheats
        private bool _deadMode, _noDead, _disableDamage, _noHit, _noStaminaCheat;
        private bool _superArmor, _hide, _silence, _exterminate, _noGoods;
        private bool _allNoArrow, _allNoMagicQty, _allNoDead, _allNoDamage;
        private bool _allNoHit, _allNoStamina, _allNoAttack, _allNoMove, _allNoUpdateAI;

        // Graphics
        private bool _drawMap = true, _drawObjects = true, _drawCharacters = true;
        private bool _drawSFX = true, _drawCutscenes = true;
        private bool _filterOverride;
        private bool _brightnessSync, _contrastSync;
        private float _brightR = 1, _brightG = 1, _brightB = 1;
        private float _contR = 1, _contG = 1, _contB = 1;
        private float _saturation = 1, _hue;

        // Items
        private int _selectedCategory;
        private int _selectedItem;
        private int _selectedInfusion;
        private int _upgrade;
        private int _quantity = 1;
        private bool _restrict = true;

        // Misc
        private string _eventFlagInput = "";
        private bool _eventFlagValue;
        private string _statusMsg = "";

        public GadgetView(EditorState state)
        {
            _state = state;
        }

        public void Reset()
        {
            _health = _stamina = 0;
            _storedX = _storedY = _storedZ = _storedAngle = 0;
            _storedHealth = _storedStamina = 0;
            _storedDeathCam = false;
            _storedCam = null;
            _hasSavedState = false;
            _playerNoGravity = _playerNoCollision = _playerDeathCam = false;
            _speedEnabled = false;
            _animSpeed = 1f;
            _selectedBonfire = 0;
            _souls = _humanity = 0;
            _selectedClass = 0;
            _deadMode = _noDead = _disableDamage = _noHit = _noStaminaCheat = false;
            _superArmor = _hide = _silence = _exterminate = _noGoods = false;
            _allNoArrow = _allNoMagicQty = _allNoDead = _allNoDamage = false;
            _allNoHit = _allNoStamina = _allNoAttack = _allNoMove = _allNoUpdateAI = false;
            _drawMap = _drawObjects = _drawCharacters = _drawSFX = _drawCutscenes = true;
            _filterOverride = false;
            _brightnessSync = _contrastSync = false;
            _brightR = _brightG = _brightB = 1;
            _contR = _contG = _contB = 1;
            _saturation = 1;
            _hue = 0;
            _selectedCategory = 0;
            _selectedItem = 0;
            _selectedInfusion = 0;
            _upgrade = 0;
            _quantity = 1;
            _restrict = true;
            _eventFlagInput = "";
            _eventFlagValue = false;
            _statusMsg = "";
        }

        public void Draw()
        {
            if (!_state.IsPlayerAttached || _state.Player == null)
            {
                ImGui.TextDisabled("Gadget not connected.");
                return;
            }

            var player = _state.Player;

            if (!player.Hooked || !player.Loaded)
            {
                ImGui.TextDisabled("Waiting for game to load...");
                return;
            }

            if (ImGui.BeginTabBar("GadgetTabs"))
            {
                if (ImGui.BeginTabItem("Player"))
                {
                    DrawPlayerTab(player);
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Stats"))
                {
                    DrawStatsTab(player);
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Items"))
                {
                    DrawItemsTab(player);
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Cheats"))
                {
                    DrawCheatsTab(player);
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Graphics"))
                {
                    DrawGraphicsTab(player);
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Misc"))
                {
                    DrawMiscTab(player);
                    ImGui.EndTabItem();
                }
                ImGui.EndTabBar();
            }
        }

        private void DrawPlayerTab(IGadgetHook player)
        {
            float w = ImGui.GetContentRegionAvail().X;

            // HP / Stamina
            _health = player.Health;
            _stamina = player.Stamina;
            int hpMax = player.HealthMax;
            int stMax = player.StaminaMax;

            ImGui.Text("HP");
            ImGui.SameLine(60);
            ImGui.SetNextItemWidth(120);
            if (ImGui.InputInt("##hp", ref _health))
            {
                _health = Math.Max(0, Math.Min(_health, hpMax));
                player.Health = _health;
            }
            ImGui.SameLine();
            ImGui.TextDisabled($"/ {hpMax}");

            ImGui.Text("Stamina");
            ImGui.SameLine(60);
            ImGui.SetNextItemWidth(120);
            if (ImGui.InputInt("##sta", ref _stamina))
            {
                _stamina = Math.Max(0, Math.Min(_stamina, stMax));
                player.Stamina = _stamina;
            }
            ImGui.SameLine();
            ImGui.TextDisabled($"/ {stMax}");

            ImGui.Separator();

            // Position
            player.GetPosition(out float px, out float py, out float pz, out float pa);
            player.GetStablePosition(out float sx, out float sy, out float sz, out float sa);
            pa = AngleToDeg(pa);
            sa = AngleToDeg(sa);

            ImGui.Text("Position");
            ImGui.Columns(4, "pos", false);
            ImGui.Text("X"); ImGui.NextColumn();
            ImGui.Text("Y"); ImGui.NextColumn();
            ImGui.Text("Z"); ImGui.NextColumn();
            ImGui.Text("Angle"); ImGui.NextColumn();
            ImGui.SetNextItemWidth(-1); ImGui.InputFloat("##px", ref px, 0, 0, "%.2f", ImGuiInputTextFlags.ReadOnly); ImGui.NextColumn();
            ImGui.SetNextItemWidth(-1); ImGui.InputFloat("##py", ref py, 0, 0, "%.2f", ImGuiInputTextFlags.ReadOnly); ImGui.NextColumn();
            ImGui.SetNextItemWidth(-1); ImGui.InputFloat("##pz", ref pz, 0, 0, "%.2f", ImGuiInputTextFlags.ReadOnly); ImGui.NextColumn();
            ImGui.SetNextItemWidth(-1); ImGui.InputFloat("##pa", ref pa, 0, 0, "%.1f", ImGuiInputTextFlags.ReadOnly); ImGui.NextColumn();
            ImGui.Columns(1);

            ImGui.Text("Stable");
            ImGui.Columns(4, "stable", false);
            ImGui.SetNextItemWidth(-1); ImGui.InputFloat("##sx", ref sx, 0, 0, "%.2f", ImGuiInputTextFlags.ReadOnly); ImGui.NextColumn();
            ImGui.SetNextItemWidth(-1); ImGui.InputFloat("##sy", ref sy, 0, 0, "%.2f", ImGuiInputTextFlags.ReadOnly); ImGui.NextColumn();
            ImGui.SetNextItemWidth(-1); ImGui.InputFloat("##sz", ref sz, 0, 0, "%.2f", ImGuiInputTextFlags.ReadOnly); ImGui.NextColumn();
            ImGui.SetNextItemWidth(-1); ImGui.InputFloat("##sa", ref sa, 0, 0, "%.1f", ImGuiInputTextFlags.ReadOnly); ImGui.NextColumn();
            ImGui.Columns(1);

            // Store / Restore
            bool deathCam = player.DeathCam;
            if (ImGui.Button("Store"))
            {
                _storedX = px; _storedY = py; _storedZ = pz; _storedAngle = pa;
                _storedHealth = _health; _storedStamina = _stamina; _storedDeathCam = deathCam;
                _storedCam = player.DumpFollowCam();
                _hasSavedState = true;
            }
            ImGui.SameLine();
            if (ImGui.Button("Restore") && _hasSavedState)
            {
                player.PosWarp(_storedX, _storedY, _storedZ, DegToAngle(_storedAngle));
                if (_storedCam != null) player.UndumpFollowCam(_storedCam);
                player.Health = _storedHealth;
                player.Stamina = _storedStamina;
                player.DeathCam = _storedDeathCam;
            }
            ImGui.SameLine();
            ImGui.Text($"Stored: {_storedX:F1}, {_storedY:F1}, {_storedZ:F1}");

            ImGui.Separator();

            // Flags
            if (ImGui.Checkbox("No Gravity", ref _playerNoGravity))
                player.NoGravity = _playerNoGravity;
            ImGui.SameLine();
            if (ImGui.Checkbox("No Collision", ref _playerNoCollision))
                player.NoCollision = _playerNoCollision;
            ImGui.SameLine();
            _playerDeathCam = deathCam;
            if (ImGui.Checkbox("Death Cam", ref _playerDeathCam))
                player.DeathCam = _playerDeathCam;

            // Anim speed
            if (ImGui.Checkbox("Anim Speed", ref _speedEnabled) && !_speedEnabled)
                player.AnimSpeed = 1f;
            if (_speedEnabled)
            {
                ImGui.SameLine();
                ImGui.SetNextItemWidth(120);
                if (ImGui.SliderFloat("##speed", ref _animSpeed, 0.1f, 5f))
                    player.AnimSpeed = _animSpeed;
            }

            ImGui.Separator();

            // Bonfire warp
            ImGui.Text("Bonfire");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(w - 100);
            string[] bonfireNames = new string[BonfireData.All.Length];
            for (int i = 0; i < BonfireData.All.Length; i++)
                bonfireNames[i] = BonfireData.All[i].Name;
            if (ImGui.Combo("##bonfire", ref _selectedBonfire, bonfireNames, bonfireNames.Length))
                player.LastBonfire = BonfireData.All[_selectedBonfire].Id;
            ImGui.SameLine();
            if (ImGui.Button("Warp"))
            {
                int bfId = BonfireData.All[_selectedBonfire].Id;
                if (bfId < 0)
                {
                    _statusMsg = "Select a valid bonfire first.";
                }
                else
                {
                    player.LastBonfire = bfId;
                    player.BonfireWarp();
                    if (_state.AutoLoadParamsOnWarp)
                    {
                        var mapBytes = player.ReadMapId();
                        if (mapBytes != null && mapBytes.Length >= 2)
                            _state.LoadParamsByMapName($"m{mapBytes[0]:X2}_{mapBytes[1]:X2}");
                    }
                }
            }
            bool autoLoad = _state.AutoLoadParamsOnWarp;
            if (ImGui.Checkbox("Auto-load params on warp", ref autoLoad))
                _state.AutoLoadParamsOnWarp = autoLoad;
        }

        private void DrawStatsTab(IGadgetHook player)
        {
            _souls = player.Souls;
            _humanity = player.Humanity;
            int sl = player.SoulLevel;
            byte classId = player.Class;

            ImGui.Text($"Soul Level: {sl}");

            ImGui.Text("Humanity");
            ImGui.SameLine(100);
            ImGui.SetNextItemWidth(120);
            if (ImGui.InputInt("##hum", ref _humanity))
                player.Humanity = _humanity;

            ImGui.Text("Souls");
            ImGui.SameLine(100);
            ImGui.SetNextItemWidth(120);
            if (ImGui.InputInt("##souls", ref _souls))
                player.Souls = _souls;

            ImGui.Separator();

            // Class
            ImGui.Text("Class");
            ImGui.SameLine(100);
            ImGui.SetNextItemWidth(160);
            string[] classNames = new string[ClassData.All.Length];
            for (int i = 0; i < ClassData.All.Length; i++)
                classNames[i] = ClassData.All[i].Name;
            _selectedClass = classId < ClassData.All.Length ? classId : 0;
            ImGui.Combo("##class", ref _selectedClass, classNames, classNames.Length);

            ImGui.Separator();

            DrawStat("Vitality",     player.Vitality);
            DrawStat("Attunement",   player.Attunement);
            DrawStat("Endurance",    player.Endurance);
            DrawStat("Strength",     player.Strength);
            DrawStat("Dexterity",    player.Dexterity);
            DrawStat("Resistance",   player.Resistance);
            DrawStat("Intelligence", player.Intelligence);
            DrawStat("Faith",        player.Faith);
        }

        private static void DrawStat(string label, int val)
        {
            ImGui.Text(label);
            ImGui.SameLine(100);
            ImGui.SetNextItemWidth(80);
            ImGui.InputInt($"##{label}", ref val, 0, 0, ImGuiInputTextFlags.ReadOnly);
        }

        private void DrawItemsTab(IGadgetHook player)
        {
            float w = ImGui.GetContentRegionAvail().X;

            // Category
            ImGui.Text("Category");
            ImGui.SameLine(80);
            ImGui.SetNextItemWidth(200);
            if (ImGui.Combo("##cat", ref _selectedCategory,
                ItemData.Categories.ConvertAll(c => c.Name).ToArray(),
                ItemData.Categories.Count))
            {
                _selectedItem = 0;
            }

            var cat = ItemData.Categories[_selectedCategory];

            // Item list
            string[] itemNames = cat.Items.ConvertAll(i => cat.ShowIds ? $"{i.Id}: {i.Name}" : i.Name).ToArray();
            ImGui.SetNextItemWidth(-1);
            if (ImGui.ListBox("##items", ref _selectedItem, itemNames, itemNames.Length, 12))
            {
                UpdateItemOptions(cat);
            }

            ImGui.Separator();

            var item = _selectedItem < cat.Items.Count ? cat.Items[_selectedItem] : null;
            bool infusionEnabled = false, upgradeEnabled = false;
            int maxUpgrade = 0;
            if (item != null)
            {
                switch (item.UpgradeType)
                {
                    case 1: upgradeEnabled = true; maxUpgrade = 5; break;
                    case 2: upgradeEnabled = true; maxUpgrade = 10; break;
                    case 5: upgradeEnabled = true; maxUpgrade = 15; break;
                    case 6: upgradeEnabled = true; maxUpgrade = 5; break;
                    case 3:
                    case 4:
                        upgradeEnabled = true;
                        infusionEnabled = true;
                        maxUpgrade = DSRInfusion.All[_selectedInfusion].MaxUpgrade;
                        break;
                }
            }

            // Infusion
            ImGui.BeginDisabled(!infusionEnabled);
            ImGui.Text("Infusion");
            ImGui.SameLine(80);
            ImGui.SetNextItemWidth(140);
            string[] infusionNames = DSRInfusion.All.ConvertAll(i => i.Name).ToArray();
            if (ImGui.Combo("##inf", ref _selectedInfusion, infusionNames, infusionNames.Length) && infusionEnabled)
            {
                maxUpgrade = DSRInfusion.All[_selectedInfusion].MaxUpgrade;
                _upgrade = Math.Min(_upgrade, maxUpgrade);
            }
            ImGui.EndDisabled();

            // Upgrade
            ImGui.BeginDisabled(!upgradeEnabled);
            ImGui.SameLine();
            ImGui.Text("+");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(60);
            ImGui.InputInt("##upg", ref _upgrade);
            _upgrade = Math.Max(0, Math.Min(_upgrade, maxUpgrade));
            ImGui.EndDisabled();

            ImGui.Separator();

            // Quantity
            ImGui.Text("Quantity");
            ImGui.SameLine(80);
            ImGui.SetNextItemWidth(80);
            ImGui.InputInt("##qty", ref _quantity);
            _quantity = Math.Max(1, _quantity);
            ImGui.SameLine();
            if (ImGui.Checkbox("Restrict", ref _restrict) && item != null)
            {
                if (_restrict) _quantity = Math.Min(_quantity, item.StackLimit);
            }

            ImGui.Separator();

            if (ImGui.Button("Create Item") && _selectedItem < cat.Items.Count)
            {
                var selItem = cat.Items[_selectedItem];
                int id = selItem.Id;
                if (selItem.UpgradeType == 5 || selItem.UpgradeType == 6)
                    id += _upgrade * 100;
                else
                    id += _upgrade;
                if (selItem.UpgradeType == 3 || selItem.UpgradeType == 4)
                    id += DSRInfusion.All[_selectedInfusion].Value;
                player.GetItem(cat.Id, id, _quantity);
            }
        }

        private void UpdateItemOptions(InvCategory cat)
        {
            _selectedInfusion = 0;
            _upgrade = 0;
        }

        private void DrawCheatsTab(IGadgetHook player)
        {
            ImGui.TextDisabled("-- Player --");
            Cheat("Dead Mode",       ref _deadMode,      v => player.PlayerDeadMode = v);
            Cheat("No Death",        ref _noDead,        v => player.PlayerNoDead = v);
            Cheat("Disable Damage",  ref _disableDamage, v => player.PlayerDisableDamage = v);
            Cheat("No Hit",          ref _noHit,         v => player.PlayerNoHit = v);
            Cheat("No Stamina Use",  ref _noStaminaCheat, v => player.PlayerNoStamina = v);
            Cheat("Super Armor",     ref _superArmor,    v => player.PlayerSuperArmor = v);
            Cheat("Hide",            ref _hide,          v => player.PlayerHide = v);
            Cheat("Silence",         ref _silence,       v => player.PlayerSilence = v);
            Cheat("Exterminate",     ref _exterminate,   v => player.PlayerExterminate = v);
            Cheat("No Goods Use",    ref _noGoods,       v => player.PlayerNoGoods = v);

            ImGui.Separator();
            ImGui.TextDisabled("-- Global --");
            Cheat("All No Arrow",    ref _allNoArrow,    v => player.AllNoArrow = v);
            Cheat("All No Magic Qty",ref _allNoMagicQty, v => player.AllNoMagicQty = v);
            Cheat("All No Dead",     ref _allNoDead,     v => player.AllNoDead = v);
            Cheat("All No Damage",   ref _allNoDamage,   v => player.AllNoDamage = v);
            Cheat("All No Hit",      ref _allNoHit,      v => player.AllNoHit = v);
            Cheat("All No Stamina",  ref _allNoStamina,  v => player.AllNoStamina = v);
            Cheat("All No Attack",   ref _allNoAttack,   v => player.AllNoAttack = v);
            Cheat("All No Move",     ref _allNoMove,     v => player.AllNoMove = v);
            Cheat("All No Update AI",ref _allNoUpdateAI, v => player.AllNoUpdateAI = v);
        }

        private static void Cheat(string label, ref bool val, Action<bool> apply)
        {
            if (ImGui.Checkbox(label, ref val))
                apply(val);
        }

        private void DrawGraphicsTab(IGadgetHook player)
        {
            ImGui.TextDisabled("Render");
            if (ImGui.Checkbox("Map",        ref _drawMap))       player.DrawMap = _drawMap;
            ImGui.SameLine();
            if (ImGui.Checkbox("Objects",    ref _drawObjects))   player.DrawObjects = _drawObjects;
            ImGui.SameLine();
            if (ImGui.Checkbox("Characters", ref _drawCharacters)) player.DrawCharacters = _drawCharacters;
            ImGui.SameLine();
            if (ImGui.Checkbox("SFX",        ref _drawSFX))       player.DrawSFX = _drawSFX;
            ImGui.SameLine();
            if (ImGui.Checkbox("Cutscenes",  ref _drawCutscenes)) player.DrawCutscenes = _drawCutscenes;

            ImGui.Separator();
            if (ImGui.Checkbox("Color Filter Override", ref _filterOverride))
            {
                player.FilterOverride = _filterOverride;
                if (_filterOverride) ApplyFilter(player);
            }

            ImGui.BeginDisabled(!_filterOverride);

            ImGui.Checkbox("Sync Brightness", ref _brightnessSync);
            ImGui.Text("Brightness");
            bool changed = false;
            ImGui.SetNextItemWidth(120); changed |= ImGui.SliderFloat("R##br", ref _brightR, 0, 4);
            ImGui.SameLine();
            ImGui.BeginDisabled(_brightnessSync);
            ImGui.SetNextItemWidth(120); changed |= ImGui.SliderFloat("G##bg", ref _brightG, 0, 4);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(120); changed |= ImGui.SliderFloat("B##bb", ref _brightB, 0, 4);
            ImGui.EndDisabled();
            if (_brightnessSync) { _brightG = _brightR; _brightB = _brightR; }

            ImGui.Checkbox("Sync Contrast", ref _contrastSync);
            ImGui.Text("Contrast ");
            ImGui.SetNextItemWidth(120); changed |= ImGui.SliderFloat("R##cr", ref _contR, 0, 4);
            ImGui.SameLine();
            ImGui.BeginDisabled(_contrastSync);
            ImGui.SetNextItemWidth(120); changed |= ImGui.SliderFloat("G##cg", ref _contG, 0, 4);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(120); changed |= ImGui.SliderFloat("B##cb", ref _contB, 0, 4);
            ImGui.EndDisabled();
            if (_contrastSync) { _contG = _contR; _contB = _contR; }

            ImGui.Text("Saturation");
            ImGui.SameLine(90);
            ImGui.SetNextItemWidth(200); changed |= ImGui.SliderFloat("##sat", ref _saturation, 0, 4);
            ImGui.Text("Hue      ");
            ImGui.SameLine(90);
            ImGui.SetNextItemWidth(200); changed |= ImGui.SliderFloat("##hue", ref _hue, -180, 180);

            if (changed && _filterOverride)
                ApplyFilter(player);

            ImGui.EndDisabled();
        }

        private void ApplyFilter(IGadgetHook player) =>
            player.SetFilterValues(_brightR, _brightG, _brightB, _contR, _contG, _contB, _saturation, _hue);

        private void DrawMiscTab(IGadgetHook player)
        {
            ImGui.TextDisabled("Event Flags");
            ImGui.Text("Flag ID");
            ImGui.SameLine(70);
            ImGui.SetNextItemWidth(120);
            ImGui.InputText("##flagid", ref _eventFlagInput, 16);

            ImGui.SameLine();
            ImGui.BeginDisabled(!player.Hooked || !player.IsValid);
            if (ImGui.Button("Read") && int.TryParse(_eventFlagInput, out int readId))
            {
                try
                {
                    _eventFlagValue = player.ReadEventFlag(readId);
                    _statusMsg = $"Flag {readId} = {_eventFlagValue}";
                }
                catch (Exception ex) { _statusMsg = ex.Message; }
            }
            ImGui.SameLine();
            if (ImGui.Button("Write") && int.TryParse(_eventFlagInput, out int writeId))
            {
                try
                {
                    player.WriteEventFlag(writeId, _eventFlagValue);
                    _statusMsg = $"Flag {writeId} set to {_eventFlagValue}";
                }
                catch (Exception ex) { _statusMsg = ex.Message; }
            }
            ImGui.EndDisabled();

            ImGui.SameLine();
            ImGui.Checkbox("##flagval", ref _eventFlagValue);

            if (!string.IsNullOrEmpty(_statusMsg))
            {
                ImGui.Separator();
                ImGui.TextDisabled(_statusMsg);
            }
        }

        private static float AngleToDeg(float a) => (float)((a + Math.PI) / (Math.PI * 2) * 360);
        private static float DegToAngle(float d) => (float)(d / 360.0 * (Math.PI * 2) - Math.PI);
    }
}
