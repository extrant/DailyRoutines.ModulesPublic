using DailyRoutines.Common.Module.Abstractions;
using DailyRoutines.Common.Module.Enums;
using DailyRoutines.Common.Module.Models;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;

namespace DailyRoutines.ModulesPublic;

public class AutoBlockSystemNotice : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = Lang.Get("AutoBlockSystemNoticeTitle"),
        Description = Lang.Get("AutoBlockSystemNoticeDescription"),
        Category    = ModuleCategory.System
    };

    protected override void Init() =>
        IChatGui.Instance().ChatMessage += OnChat;

    protected override void Uninit() =>
        IChatGui.Instance().ChatMessage -= OnChat;

    private static void OnChat
    (
        IHandleableChatMessage message
    )
    {
        if (message.LogKind != XivChatType.Notice) return;
        message.PreventOriginal();
    }
}
