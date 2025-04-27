using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace DailyRoutines.ModulesPublic;

public class AutoCountPlayers : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = GetLoc("AutoCountPlayersTitle"),
        Description = GetLoc("AutoCountPlayersDescription"),
        Category = ModuleCategories.General,
    };

    private static readonly uint LineColor;
    private static readonly uint DotColor;
    private static readonly uint TextColor;

    private static Config ModuleConfig = null!;

    private static IDtrBarEntry? Entry;

    private static string SearchInput = string.Empty;

    static AutoCountPlayers()
    {
        LineColor = ImGui.ColorConvertFloat4ToU32(LightSkyBlue);
        DotColor  = ImGui.ColorConvertFloat4ToU32(RoyalBlue);
        TextColor = ImGui.ColorConvertFloat4ToU32(Orange);
    }

    public override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();
        
        Overlay ??= new(this);
        Overlay.Flags &= ~ImGuiWindowFlags.NoTitleBar;
        Overlay.Flags &= ~ImGuiWindowFlags.AlwaysAutoResize;
        Overlay.WindowName = $"{GetLoc("AutoCountPlayers-PlayersAroundInfo")}###AutoCountPlayers-Overlay";

        Entry ??= DService.DtrBar.Get("DailyRoutines-AutoCountPlayers");
        Entry.Shown = true;
        Entry.OnClick += () => Overlay.IsOpen ^= true;

        PlayersManager.ReceivePlayersAround += OnUpdate;
    }

    public override void ConfigUI()
    {
        ImGui.SetNextItemWidth(120f * GlobalFontScale);
        if (ImGui.InputFloat(GetLoc("Scale"), ref ModuleConfig.ScaleFactor, 0, 0, "%.1f"))
            ModuleConfig.ScaleFactor = Math.Max(0.1f, ModuleConfig.ScaleFactor);
        if (ImGui.IsItemDeactivatedAfterEdit())
            ModuleConfig.Save(this);
    }

    public override unsafe void OverlayUI()
    {
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputText("###Search", ref SearchInput, 128);

        if (BetweenAreas || DService.ObjectTable.LocalPlayer is not { } localPlayer) return;

        var source = PlayersManager.PlayersAround.Where(x => string.IsNullOrWhiteSpace(SearchInput) ||
                                               x.ToString().Contains(SearchInput, StringComparison.OrdinalIgnoreCase))
                                   .OrderBy(x => x.Name.TextValue.Length);

        using var child = ImRaii.Child("列表", ImGui.GetContentRegionAvail() - ImGui.GetStyle().ItemSpacing, true);
        if (!child) return;
        
        foreach (var playerAround in source)
        {
            using var id = ImRaii.PushId($"{playerAround.GameObjectId}");
            if (ImGuiOm.ButtonIcon("定位", FontAwesomeIcon.Flag, GetLoc("AutoCountPlayers-Locate")))
            {
                if (LuminaGetter.TryGetRow<Map>(DService.ClientState.MapId, out var map))
                {
                    var mapPos = WorldToMap(playerAround.Position.ToVector2(), map);
                    var message = new SeStringBuilder()
                                  .Add(new PlayerPayload(playerAround.Name.TextValue,
                                                         playerAround.ToStruct()->HomeWorld))
                                  .Append(" (")
                                  .AddIcon(playerAround.ClassJob.Value.ToBitmapFontIcon())
                                  .Append($" {playerAround.ClassJob.Value.Name})")
                                  .Add(new NewLinePayload())
                                  .Append("     ")
                                  .Append(SeString.CreateMapLink(DService.ClientState.TerritoryType,
                                                                 DService.ClientState.MapId, mapPos.X, mapPos.Y))
                                  .Build();
                    Chat(message);
                }
            }

            if (ImGui.IsItemHovered() &&
                DService.Gui.WorldToScreen(playerAround.Position, out var screenPos) &&
                DService.Gui.WorldToScreen(localPlayer.Position, out var localScreenPos))
            {
                var drawList = ImGui.GetForegroundDrawList();

                drawList.AddLine(localScreenPos, screenPos, LineColor, 8f);
                drawList.AddCircleFilled(localScreenPos, 12f, DotColor);
                drawList.AddCircleFilled(screenPos, 12f, DotColor);
                drawList.AddText(screenPos + ScaledVector2(16f), TextColor, $"{playerAround.Name} ({playerAround.ClassJob.Value.Name})");
            }

            ImGui.SameLine();
            ImGui.Text($"{playerAround.Name} ({playerAround.ClassJob.Value.Name})");
        }
    }

    private static void OnUpdate(IReadOnlyList<IPlayerCharacter> characters)
    {
        if (Entry == null) return;
        
        Entry.Text = $"{GetLoc("AutoCountPlayers-PlayersAroundCount")}: {PlayersManager.PlayersAroundCount}";
        
        var tooltip = new StringBuilder();
        tooltip.AppendLine($"{GetLoc("AutoCountPlayers-PlayersAroundInfo")}:");
        characters.ForEach(x => tooltip.AppendLine($"{x.Name} ({x.ClassJob.Value.Name.ExtractText()})"));
        Entry.Tooltip = tooltip.ToString().Trim();
    }

    public override void Uninit()
    {
        PlayersManager.ReceivePlayersAround -= OnUpdate;
        
        Entry?.Remove();
        Entry = null;
        
        base.Uninit();
    }
    
    public class Config : ModuleConfiguration
    {
        public float ScaleFactor = 1;
    }
}
