using System;
using System.Numerics;
using ImGuiNET;

namespace DS1ParamEditor
{
    public sealed class GadgetView
    {
        private readonly EditorState _state;

        // Player stats
        private int _health, _stamina, _souls, _humanity;
        
        // Cheats state
        private bool _playerNoDead, _playerNoHit, _playerNoStamina, _playerNoGoods;
        private bool _playerSuperArmor, _playerHide, _playerSilence;
        private bool _allNoDead, _allNoHit, _allNoStamina, _allNoAttack, _allNoMove;
        
        // Graphics state
        private bool _drawMap = true, _drawObjects = true, _drawCharacters = true;
        private bool _drawSFX = true, _drawCutscenes = true;
        private bool _filterOverride;
        private float _brightR = 1f, _brightG = 1f, _brightB = 1f;
        private float _contR = 1f, _contG = 1f, _contB = 1f;
        private float _saturation = 1f, _hue = 0f;
        
        // Movement
        private bool _noGravity, _noCollision;
        private float _animSpeed = 1f;
        
        // Items
        private int _itemCategory;
        private int _selectedItemIndex = -1;
        private string _itemSearchFilter = string.Empty;
        private int _itemQuantity = 1;
        
        // Item category IDs from DSR-Gadget
        private static readonly int[] ItemCategoryIds = { ItemData.WEAPON, ItemData.ARMOR, ItemData.RING, ItemData.GOODS };

        public GadgetView(EditorState state)
        {
            _state = state;
        }

        public void Draw()
        {
            if (!_state.IsPlayerAttached || _state.Player == null)
            {
                ImGui.TextDisabled("DSR-Gadget not connected.");
                ImGui.TextDisabled("Use 'Connect to game' button above.");
                return;
            }

            var player = _state.Player;

            if (!player.Hooked || !player.AOBScanSucceeded || !player.IsValid)
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

                if (ImGui.BeginTabItem("Items"))
                {
                    DrawItemsTab(player);
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }
        }

        private void DrawPlayerTab(PlayerHook player)
        {
            ImGui.BeginChild("PlayerTabScroll", Vector2.Zero, ImGuiChildFlags.None);

            ImGui.SeparatorText("Stats");

            float w = ImGui.GetContentRegionAvail().X;

            // Health
            _health = player.Health;
            int maxHealth = player.HealthMax;
            ImGui.Text("Health");
            ImGui.SetNextItemWidth(w - 55);
            if (ImGui.SliderInt("##hp", ref _health, 0, maxHealth))
                player.Health = _health;
            ImGui.SameLine();
            if (ImGui.Button("Max##hp", new Vector2(50, 0)))
            {
                _health = maxHealth;
                player.Health = _health;
            }

            // Stamina
            _stamina = player.Stamina;
            int maxStamina = player.StaminaMax;
            ImGui.Text("Stamina");
            ImGui.SetNextItemWidth(w - 55);
            if (ImGui.SliderInt("##st", ref _stamina, 0, maxStamina))
                player.Stamina = _stamina;
            ImGui.SameLine();
            if (ImGui.Button("Max##st", new Vector2(50, 0)))
            {
                _stamina = maxStamina;
                player.Stamina = _stamina;
            }

            // Souls
            _souls = player.Souls;
            ImGui.Text("Souls");
            ImGui.SetNextItemWidth(w);
            if (ImGui.InputInt("##souls", ref _souls))
                player.Souls = Math.Max(0, _souls);

            // Humanity
            _humanity = player.Humanity;
            ImGui.Text("Humanity");
            ImGui.SetNextItemWidth(w);
            if (ImGui.InputInt("##humanity", ref _humanity))
                player.Humanity = Math.Max(0, _humanity);

            ImGui.Spacing();
            ImGui.SeparatorText("Attributes");

            ImGui.Text($"Soul Level: {player.SoulLevel}");
            ImGui.Text($"Vitality: {player.Vitality}");
            ImGui.Text($"Attunement: {player.Attunement}");
            ImGui.Text($"Endurance: {player.Endurance}");
            ImGui.Text($"Strength: {player.Strength}");
            ImGui.Text($"Dexterity: {player.Dexterity}");
            ImGui.Text($"Resistance: {player.Resistance}");
            ImGui.Text($"Intelligence: {player.Intelligence}");
            ImGui.Text($"Faith: {player.Faith}");

            ImGui.Spacing();
            ImGui.SeparatorText("Movement");

            if (ImGui.Checkbox("No Gravity", ref _noGravity))
                player.NoGravity = _noGravity;
            
            if (ImGui.Checkbox("No Collision", ref _noCollision))
                player.NoCollision = _noCollision;

            ImGui.Text("Anim Speed");
            ImGui.SetNextItemWidth(w - 55);
            if (ImGui.SliderFloat("##animspeed", ref _animSpeed, 0.1f, 5f, "%.2f"))
                player.AnimSpeed = _animSpeed;
            ImGui.SameLine();
            if (ImGui.Button("1.0##as", new Vector2(50, 0)))
            {
                _animSpeed = 1f;
                player.AnimSpeed = 1f;
            }

            ImGui.EndChild();
        }

        private void DrawCheatsTab(PlayerHook player)
        {
            ImGui.BeginChild("CheatsTabScroll", Vector2.Zero, ImGuiChildFlags.None);

            ImGui.SeparatorText("Player Cheats");

            if (ImGui.Checkbox("No Dead", ref _playerNoDead))
                player.PlayerNoDead = _playerNoDead;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Cannot die");

            if (ImGui.Checkbox("No Hit", ref _playerNoHit))
                player.PlayerNoHit = _playerNoHit;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Cannot be hit");

            if (ImGui.Checkbox("No Stamina", ref _playerNoStamina))
                player.PlayerNoStamina = _playerNoStamina;

            if (ImGui.Checkbox("No Goods Use", ref _playerNoGoods))
                player.PlayerNoGoods = _playerNoGoods;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Items don't get consumed");

            if (ImGui.Checkbox("Super Armor", ref _playerSuperArmor))
                player.PlayerSuperArmor = _playerSuperArmor;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Cannot be staggered");

            if (ImGui.Checkbox("Hide", ref _playerHide))
                player.PlayerHide = _playerHide;

            if (ImGui.Checkbox("Silence", ref _playerSilence))
                player.PlayerSilence = _playerSilence;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("No footstep sounds");

            ImGui.Spacing();
            ImGui.SeparatorText("All Characters");

            if (ImGui.Checkbox("All No Dead", ref _allNoDead))
                player.AllNoDead = _allNoDead;

            if (ImGui.Checkbox("All No Hit", ref _allNoHit))
                player.AllNoHit = _allNoHit;

            if (ImGui.Checkbox("All No Stamina", ref _allNoStamina))
                player.AllNoStamina = _allNoStamina;

            if (ImGui.Checkbox("All No Attack", ref _allNoAttack))
                player.AllNoAttack = _allNoAttack;

            if (ImGui.Checkbox("All No Move", ref _allNoMove))
                player.AllNoMove = _allNoMove;

            ImGui.Spacing();
            if (ImGui.Button("Reset All", new Vector2(-1, 0)))
            {
                _playerNoDead = _playerNoHit = _playerNoStamina = _playerNoGoods = false;
                _playerSuperArmor = _playerHide = _playerSilence = false;
                _allNoDead = _allNoHit = _allNoStamina = _allNoAttack = _allNoMove = false;
                
                player.PlayerNoDead = false;
                player.PlayerNoHit = false;
                player.PlayerNoStamina = false;
                player.PlayerNoGoods = false;
                player.PlayerSuperArmor = false;
                player.PlayerHide = false;
                player.PlayerSilence = false;
                player.AllNoDead = false;
                player.AllNoHit = false;
                player.AllNoStamina = false;
                player.AllNoAttack = false;
                player.AllNoMove = false;
            }

            ImGui.EndChild();
        }

        private void DrawGraphicsTab(PlayerHook player)
        {
            ImGui.BeginChild("GraphicsTabScroll", Vector2.Zero, ImGuiChildFlags.None);

            ImGui.SeparatorText("Draw Groups");

            if (ImGui.Checkbox("Map", ref _drawMap))
                player.DrawMap = _drawMap;

            if (ImGui.Checkbox("Objects", ref _drawObjects))
                player.DrawObjects = _drawObjects;

            if (ImGui.Checkbox("Characters", ref _drawCharacters))
                player.DrawCharacters = _drawCharacters;

            if (ImGui.Checkbox("SFX", ref _drawSFX))
                player.DrawSFX = _drawSFX;

            if (ImGui.Checkbox("Cutscenes", ref _drawCutscenes))
                player.DrawCutscenes = _drawCutscenes;

            ImGui.Spacing();
            ImGui.SeparatorText("Color Filter");

            if (ImGui.Checkbox("Enable", ref _filterOverride))
                player.FilterOverride = _filterOverride;

            if (_filterOverride)
            {
                bool changed = false;
                float w = ImGui.GetContentRegionAvail().X;

                ImGui.Text("Brightness (0 black, 1 normal, 2 bright)");
                ImGui.SetNextItemWidth(w);
                changed |= ImGui.SliderFloat("R##br", ref _brightR, 0f, 2f, "%.2f");
                ImGui.SetNextItemWidth(w);
                changed |= ImGui.SliderFloat("G##bg", ref _brightG, 0f, 2f, "%.2f");
                ImGui.SetNextItemWidth(w);
                changed |= ImGui.SliderFloat("B##bb", ref _brightB, 0f, 2f, "%.2f");

                ImGui.Spacing();
                ImGui.Text("Contrast (0 none, 1 normal, 2 high)");
                ImGui.SetNextItemWidth(w);
                changed |= ImGui.SliderFloat("R##cr", ref _contR, 0f, 2f, "%.2f");
                ImGui.SetNextItemWidth(w);
                changed |= ImGui.SliderFloat("G##cg", ref _contG, 0f, 2f, "%.2f");
                ImGui.SetNextItemWidth(w);
                changed |= ImGui.SliderFloat("B##cb", ref _contB, 0f, 2f, "%.2f");

                ImGui.Spacing();
                ImGui.SetNextItemWidth(w);
                changed |= ImGui.SliderFloat("Saturation (0 b/w, 1 normal, 2 vivid)", ref _saturation, 0f, 2f, "%.2f");
                
                ImGui.SetNextItemWidth(w);
                changed |= ImGui.SliderFloat("Hue (0-360 degrees)", ref _hue, 0f, 360f, "%.0f");

                if (changed)
                {
                    player.SetFilterValues(_brightR, _brightG, _brightB,
                        _contR, _contG, _contB, _saturation, _hue);
                }

                if (ImGui.Button("Reset", new Vector2(-1, 0)))
                {
                    _brightR = _brightG = _brightB = 1f;
                    _contR = _contG = _contB = 1f;
                    _saturation = 1f;
                    _hue = 0f;
                    player.SetFilterValues(1, 1, 1, 1, 1, 1, 1, 0);
                }
            }

            ImGui.EndChild();
        }

        private void DrawItemsTab(PlayerHook player)
        {
            ImGui.BeginChild("ItemsTabScroll", Vector2.Zero, ImGuiChildFlags.None);

            float w = ImGui.GetContentRegionAvail().X;

            if (ImGui.CollapsingHeader("Get Item", ImGuiTreeNodeFlags.DefaultOpen))
            {
                // Category selector
                ImGui.SetNextItemWidth(w);
                if (ImGui.Combo("##category", ref _itemCategory, "Weapon\0Armor\0Ring\0Goods\0\0"))
                {
                    _selectedItemIndex = -1;
                    _itemSearchFilter = string.Empty;
                }

                // Search filter
                ImGui.SetNextItemWidth(w);
                ImGui.InputTextWithHint("##search", "Search...", ref _itemSearchFilter, 64);

                // Item list
                int categoryId = ItemCategoryIds[_itemCategory];
                var items = ItemData.GetItemsByCategory(categoryId);

                var filteredItems = new System.Collections.Generic.List<(int originalIndex, ItemData.Item item)>();
                for (int i = 0; i < items.Length; i++)
                {
                    var item = items[i];
                    if (!string.IsNullOrEmpty(_itemSearchFilter) &&
                        !item.Name.Contains(_itemSearchFilter, StringComparison.OrdinalIgnoreCase) &&
                        !item.Id.ToString().Contains(_itemSearchFilter))
                        continue;
                    filteredItems.Add((i, item));
                }

                ImGui.BeginChild("ItemList", new Vector2(-1, 150), ImGuiChildFlags.Borders);
                for (int fi = 0; fi < filteredItems.Count; fi++)
                {
                    var (origIdx, item) = filteredItems[fi];
                    bool selected = _selectedItemIndex == origIdx;
                    if (ImGui.Selectable($"{item.Name}##item{origIdx}", selected))
                        _selectedItemIndex = origIdx;
                    if (selected)
                        ImGui.SetItemDefaultFocus();
                }
                ImGui.EndChild();

                // Quantity + button on same line
                ImGui.SetNextItemWidth(80);
                ImGui.InputInt("##quantity", ref _itemQuantity, 0);
                _itemQuantity = Math.Max(1, Math.Min(_itemQuantity, 999));
                ImGui.SameLine();

                bool canGetItem = _selectedItemIndex >= 0 && _selectedItemIndex < items.Length;
                if (ImGui.Button("Get Item##btn", new Vector2(-1, 0)) && canGetItem)
                {
                    var item = items[_selectedItemIndex];
                    player.GetItem(categoryId, item.Id, _itemQuantity);
                    Console.WriteLine($"[GadgetView] GetItem: {item.Name} (category=0x{categoryId:X}, id={item.Id}, qty={_itemQuantity})");
                }
            }

            ImGui.EndChild();
        }
    }
}
