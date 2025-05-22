using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using DailyRoutines.Abstracts;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using FFXIVClientStructs.FFXIV.Client.Game.Control;

namespace DailyRoutines.ModulesPublic;

public class AutoCountPlayers : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoCountPlayersTitle"),
        Description = GetLoc("AutoCountPlayersDescription"),
        Category    = ModuleCategories.General,
    };
    
    private const ImGuiWindowFlags WindowFlags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar |
                                                 ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoFocusOnAppearing |
                                                 ImGuiWindowFlags.NoNavFocus | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize |
                                                 ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoInputs;

    private static readonly uint LineColorBlue = ImGui.ColorConvertFloat4ToU32(LightSkyBlue);
    private static readonly uint LineColorRed  = ImGui.ColorConvertFloat4ToU32(Red);
    private static readonly uint DotColor      = ImGui.ColorConvertFloat4ToU32(RoyalBlue);

    private static Config        ModuleConfig = null!;
    private static IDtrBarEntry? Entry;

    private static List<IPlayerCharacter> TargetingMePlayers = [];

    private static string SearchInput = string.Empty;

    public override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();
        
        Overlay            ??= new(this);
        Overlay.Flags      &=  ~ImGuiWindowFlags.NoTitleBar;
        Overlay.Flags      &=  ~ImGuiWindowFlags.AlwaysAutoResize;
        Overlay.WindowName =   $"{GetLoc("AutoCountPlayers-PlayersAroundInfo")}###AutoCountPlayers-Overlay";

        Entry ??= DService.DtrBar.Get("DailyRoutines-AutoCountPlayers");
        Entry.Shown = true;
        Entry.OnClick += () => Overlay.IsOpen ^= true;

        DService.UiBuilder.Draw += OnDraw;

        PlayersManager.ReceivePlayersAround += OnUpdate;
    }

    public override void ConfigUI()
    {
        ImGui.SetNextItemWidth(120f * GlobalFontScale);
        if (ImGui.InputFloat(GetLoc("Scale"), ref ModuleConfig.ScaleFactor, 0, 0, "%.1f"))
            ModuleConfig.ScaleFactor = Math.Max(0.1f, ModuleConfig.ScaleFactor);
        if (ImGui.IsItemDeactivatedAfterEdit())
            ModuleConfig.Save(this);
        
        ImGui.NewLine();
        
        if (ImGui.Checkbox(GetLoc("AutoCountPlayers-DisplayLineWhenTargetingMe"), ref ModuleConfig.DisplayLineWhenTargetingMe))
            ModuleConfig.Save(this);

        if (ModuleConfig.DisplayLineWhenTargetingMe)
        {
            using (ImRaii.PushIndent())
            {
                if (ImGui.Checkbox(GetLoc("SendChat"), ref ModuleConfig.SendChat))
                    ModuleConfig.Save(this);
                
                if (ImGui.Checkbox(GetLoc("SendNotification"), ref ModuleConfig.SendNotification))
                    ModuleConfig.Save(this);
                
                if (ImGui.Checkbox(GetLoc("SendTTS"), ref ModuleConfig.SendTTS))
                    ModuleConfig.Save(this);
            }
        }
    }

    public override unsafe void OverlayUI()
    {
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputText("###Search", ref SearchInput, 128);

        if (BetweenAreas || DService.ClientState.LocalPlayer is not { } localPlayer) return;

        var source = PlayersManager.PlayersAround.Where(x => string.IsNullOrWhiteSpace(SearchInput) ||
                                               x.ToString().Contains(SearchInput, StringComparison.OrdinalIgnoreCase))
                                   .OrderBy(x => x.Name.TextValue.Length);

        using var child = ImRaii.Child("列表", ImGui.GetContentRegionAvail() - ImGui.GetStyle().ItemSpacing, true);
        if (!child) return;
        
        foreach (var playerAround in source)
        {
            using var id = ImRaii.PushId($"{playerAround.GameObjectId}");
            if (ImGuiOm.ButtonIcon("定位", FontAwesomeIcon.Flag, GetLoc("Locate")))
            {
                var mapPos = WorldToMap(playerAround.Position.ToVector2(), GameState.MapData);
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

            if (DService.Gui.WorldToScreen(playerAround.Position, out var screenPos) &&
                DService.Gui.WorldToScreen(localPlayer.Position,  out var localScreenPos))
            {
                if (!ImGui.IsAnyItemHovered() || ImGui.IsItemHovered())
                    DrawLine(localScreenPos, screenPos, playerAround);
            }
            

            ImGui.SameLine();
            ImGui.Text($"{playerAround.Name} ({playerAround.ClassJob.Value.Name})");
        }
    }
    
    private static unsafe void OnDraw()
    {
        if (!ModuleConfig.DisplayLineWhenTargetingMe || TargetingMePlayers.Count == 0) return;

        var localPlayer = Control.GetLocalPlayer();
        if (localPlayer == null) return;

        foreach (var player in TargetingMePlayers)
        {
            if (DService.Gui.WorldToScreen(player.Position, out var screenPos) &&
                DService.Gui.WorldToScreen(localPlayer->Position,  out var localScreenPos))
                DrawLine(localScreenPos, screenPos, player, LineColorRed);
        }
    }

    private static void OnUpdate(IReadOnlyList<IPlayerCharacter> characters)
    {
        if (Entry == null) return;

        var last = TargetingMePlayers.ToList();
        TargetingMePlayers = characters.Where(x => x.TargetObjectId == GameState.EntityID).OrderBy(x => x.EntityId).ToList();

        if (TargetingMePlayers.Count > 0 && TargetingMePlayers.Any(x => !x.StatusFlags.HasFlag(StatusFlags.PartyMember)) && !last.SequenceEqual(TargetingMePlayers))
        {
            if (ModuleConfig.SendTTS)
                Speak(GetLoc("AutoCountPlayers-Notification-SomeoneTargetingMe"));
            
            if (ModuleConfig.SendNotification)
                NotificationWarning(GetLoc("AutoCountPlayers-Notification-SomeoneTargetingMe"));
            
            if (ModuleConfig.SendChat)
            {
                var builder = new SeStringBuilder();
                
                builder.Append($"{GetLoc("AutoCountPlayers-Notification-SomeoneTargetingMe")}:\n");
                TargetingMePlayers.ForEach(x =>
                {
                    builder.AddIcon(x.ClassJob.Value.ToBitmapFontIcon());
                    builder.Append($" {x.Name}\n");
                });
                
                Chat(builder.ToString().Trim());
            }
        }
        
        Entry.Text = $"{GetLoc("AutoCountPlayers-PlayersAroundCount")}: {PlayersManager.PlayersAroundCount}" +
                     (TargetingMePlayers.Count == 0 ? string.Empty : $" ({TargetingMePlayers.Count})");

        if (characters.Count == 0)
        {
            Entry.Tooltip = string.Empty;
            return;
        }
        
        var tooltip = new StringBuilder();

        if (TargetingMePlayers.Count > 0)
        {
            tooltip.AppendLine($"{GetLoc("AutoCountPlayers-PlayersTargetingMe")}:");
            TargetingMePlayers.ForEach(x => tooltip.AppendLine($"{x.Name} ({x.ClassJob.Value.Name.ExtractText()})"));
            tooltip.AppendLine(string.Empty);
        }
        
        tooltip.AppendLine($"{GetLoc("AutoCountPlayers-PlayersAroundInfo")}:");
        characters.ForEach(x => tooltip.AppendLine($"{x.Name} ({x.ClassJob.Value.Name.ExtractText()})"));
        
        Entry.Tooltip = tooltip.ToString().Trim();
    }

    private static void DrawLine(Vector2 startPos, Vector2 endPos, ICharacter chara, uint lineColor = 0)
    {
        lineColor = lineColor == 0 ? LineColorBlue : lineColor;
        
        var drawList = ImGui.GetForegroundDrawList();

        drawList.AddLine(startPos, endPos, lineColor, 8f);
        drawList.AddCircleFilled(startPos, 12f, DotColor);
        drawList.AddCircleFilled(endPos,   12f, DotColor);
        
        ImGui.SetNextWindowPos(endPos);
        if (ImGui.Begin($"AutoCountPlayers-{chara.EntityId}", WindowFlags))
        {
            using (ImRaii.Group())
            {
                ScaledDummy(12f);

                ImGui.SameLine();
                ImGuiHelpers.SeStringWrapped(new SeStringBuilder().AddIcon(chara.ClassJob.Value.ToBitmapFontIcon()).Encode());
                
                ImGui.SameLine();
                ImGuiOm.TextOutlined(Orange, $"{chara.Name}");
            }

            ImGui.End();
        }
    }

    public override void Uninit()
    {
        DService.UiBuilder.Draw -= OnDraw;
        PlayersManager.ReceivePlayersAround -= OnUpdate;
        
        TargetingMePlayers.Clear();
        
        Entry?.Remove();
        Entry = null;
        
        base.Uninit();
    }
    
    public class Config : ModuleConfiguration
    {
        public float ScaleFactor = 1;

        public bool DisplayLineWhenTargetingMe = true;

        public bool SendNotification = true;
        public bool SendChat         = true;
        public bool SendTTS          = true;
    }
}
