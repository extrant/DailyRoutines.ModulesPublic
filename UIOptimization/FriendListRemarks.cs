using System;
using System.Collections.Generic;
using DailyRoutines.Abstracts;
using DailyRoutines.Infos;
using DailyRoutines.Modules;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace DailyRoutines.ModulesPublic;

public unsafe class FriendListRemarks : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("FriendListRemarksTitle"),
        Description = GetLoc("FriendListRemarksDescription"),
        Category    = ModuleCategories.UIOptimization
    };

    private static readonly ModifyInfoMenuItem ModifyInfoItem = new();
    
    private static Config ModuleConfig = null!;
    
    private static readonly List<nint> Utf8Strings = [];

    private static bool   IsNeedToOpen;
    private static ulong  ContentIDToModify;
    private static string NameToModify;

    private static string NicknameInput = string.Empty;
    private static string RemarkInput   = string.Empty;

    public override void Init()
    {
        Overlay        ??= new(this);
        Overlay.IsOpen =   true;
        Overlay.Flags |= ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDecoration |
                         ImGuiWindowFlags.NoDocking    | ImGuiWindowFlags.NoFocusOnAppearing    | ImGuiWindowFlags.NoNav      | ImGuiWindowFlags.NoResize     |
                         ImGuiWindowFlags.NoInputs;
        
        ModuleConfig =   LoadConfig<Config>() ?? new();
        
        DService.AddonLifecycle.RegisterListener(AddonEvent.PostSetup,           "FriendList", OnAddon);
        DService.AddonLifecycle.RegisterListener(AddonEvent.PostRequestedUpdate, "FriendList", OnAddon);
        DService.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize,         "FriendList", OnAddon);
        if (IsAddonAndNodesReady(FriendList)) OnAddon(AddonEvent.PostSetup, null);

        DService.ContextMenu.OnMenuOpened += OnContextMenu;
    }

    public override void OverlayUI()
    {
        if (IsNeedToOpen)
        {
            IsNeedToOpen = false;

            var isExisted = ModuleConfig.PlayerInfos.TryGetValue(ContentIDToModify, out var info);
            
            NicknameInput = isExisted ? info.Nickname : string.Empty;
            RemarkInput   = isExisted ? info.Remark : string.Empty;
            
            ImGui.OpenPopup("ModifyPopup");
        }

        using var popup = ImRaii.Popup("ModifyPopup");
        if (!popup) return;
        
        ImGui.Text($"{LuminaWrapper.GetAddonText(9818)}: {NameToModify}");
        ImGuiOm.TooltipHover($"Content ID: {ContentIDToModify}");

        ImGui.Text($"{LuminaWrapper.GetAddonText(15207)}");
        ImGui.InputText("###NicknameInput", ref NicknameInput, 128);
        
        ImGui.Text($"{LuminaWrapper.GetAddonText(13294).TrimEnd(':')}");
        ImGui.InputText("###RemarkInput", ref RemarkInput, 512);
        ImGui.TextWrapped(RemarkInput);
        
        if (ImGui.Button($"{GetLoc("Confirm")}"))
        {
            ModuleConfig.PlayerInfos[ContentIDToModify] = new()
            {
                ContentID = ContentIDToModify,
                Name      = NameToModify,
                Nickname  = NicknameInput,
                Remark    = RemarkInput,
            };
            ModuleConfig.Save(this);
            
            ImGui.CloseCurrentPopup();
            Modify();
        }

        using (ImRaii.Disabled(!ModuleConfig.PlayerInfos.ContainsKey(ContentIDToModify)))
        {
            ImGui.SameLine();
            if (ImGui.Button($"{GetLoc("Delete")}"))
            {
                ModuleConfig.PlayerInfos.Remove(ContentIDToModify);
                ModuleConfig.Save(this);
                
                ImGui.CloseCurrentPopup();
                InfoProxyFriendList.Instance()->RequestData();
            }
        }
    }

    private static void OnContextMenu(IMenuOpenedArgs args)
    {
        if (ModifyInfoItem.IsDisplay(args))
            args.AddMenuItem(ModifyInfoItem.Get());
    }

    private static void OnAddon(AddonEvent type, AddonArgs? args)
    {
        switch (type)
        {
            case AddonEvent.PostSetup:
                Throttler.Throttle("FriendListRemarks-Update", reThrottle: true);
                break;
            case AddonEvent.PostRequestedUpdate:
                if (!Throttler.Throttle("FriendListRemarks-Update")) return;
                Modify();
                break;
            case AddonEvent.PreFinalize:
                Utf8Strings.ForEach(x => ((Utf8String*)x)->Dtor(true));
                Utf8Strings.Clear();
                break;
        }
    }

    private static void Modify()
    {
        var addon = FriendList;
        if (!IsAddonAndNodesReady(addon)) return;
        Throttler.Throttle("FriendListRemarks-Update", reThrottle: true);

        var info = InfoProxyFriendList.Instance();

        for (var i = 0; i < info->EntryCount; i++)
        {
            var data = info->CharDataSpan[i];
            if (!ModuleConfig.PlayerInfos.TryGetValue(data.ContentId, out var configInfo)) continue;

            if (!string.IsNullOrWhiteSpace(configInfo.Nickname))
            {
                var nicknameBuilder = new SeStringBuilder();
                nicknameBuilder.AddUiForeground($"{configInfo.Nickname}", 37);
                
                var nicknameString = Utf8String.FromSequence(nicknameBuilder.Build().Encode());
                Utf8Strings.Add((nint)nicknameString);
                
                // 名字
                AtkStage.Instance()->GetStringArrayData(StringArrayType.FriendList)->StringArray[0 + (5 * i)] = nicknameString->StringPtr;
            }

            if (!string.IsNullOrWhiteSpace(configInfo.Remark))
            {
                var remarkString = Utf8String.FromString($"{LuminaWrapper.GetAddonText(13294).TrimEnd(':')}: {configInfo.Remark}" +
                                                         (string.IsNullOrWhiteSpace(configInfo.Nickname)
                                                              ? string.Empty
                                                              : $"\n{LuminaWrapper.GetAddonText(9818)}: {data.NameString}"));
                Utf8Strings.Add((nint)remarkString);
                
                // 在线状态
                AtkStage.Instance()->GetStringArrayData(StringArrayType.FriendList)->StringArray[3 + (5 * i)] = remarkString->StringPtr;
            }
        }
        
        FriendList->OnRequestedUpdate(AtkStage.Instance()->GetNumberArrayData(), AtkStage.Instance()->GetStringArrayData());
        DService.Framework.RunOnTick(
            () =>
            {
                if (!IsAddonAndNodesReady(FriendList)) return;
                FriendList->OnRequestedUpdate(AtkStage.Instance()->GetNumberArrayData(), AtkStage.Instance()->GetStringArrayData());
            }, TimeSpan.FromMilliseconds(100));
    }

    public override void Uninit()
    {
        DService.ContextMenu.OnMenuOpened -= OnContextMenu;
        DService.AddonLifecycle.UnregisterListener(OnAddon);

        OnAddon(AddonEvent.PreFinalize, null);
        base.Uninit();

        var info = InfoProxyFriendList.Instance();
        if (info != null)
            info->RequestData();

        IsNeedToOpen = false;
    }

    private class Config : ModuleConfiguration
    {
        public Dictionary<ulong, PlayerInfo> PlayerInfos = [];
    }
    
    private class ModifyInfoMenuItem : MenuItemBase
    {
        public override string Name { get; protected set; } = GetLoc("FriendListRemarks-ContextMenuItemName");

        public override bool IsDisplay(IMenuOpenedArgs args)
        {
            if (args.AddonName         != "FriendList" || args.Target is not MenuTargetDefault target ||
                target.TargetContentId == 0            || string.IsNullOrWhiteSpace(target.TargetName))
                return false;
            
            return true;
        }

        protected override void OnClicked(IMenuItemClickedArgs args)
        {
            if (args.Target is not MenuTargetDefault target) return;
            
            ContentIDToModify = target.TargetContentId;
            NameToModify      = target.TargetName;
            IsNeedToOpen      = true;
        }
    }
    
    public class PlayerInfo
    {
        public ulong  ContentID { get; set; }
        public string Name      { get; set; } = string.Empty;
        public string Nickname  { get; set; } = string.Empty;
        public string Remark    { get; set; } = string.Empty;
    }
}
