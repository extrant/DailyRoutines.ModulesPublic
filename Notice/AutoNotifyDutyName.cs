using DailyRoutines.Abstracts;
using DailyRoutines.Infos;

namespace DailyRoutines.Modules;

public class AutoNotifyDutyName : DailyModuleBase
{
    public override ModuleInfo Info => new()
    {
        Title = GetLoc("AutoNotifyDutyNameTitle"),
        Description = GetLoc("AutoNotifyDutyNameDescription"),
        Category = ModuleCategories.Notice,
    };

    public override void Init()
    {
        DService.ClientState.TerritoryChanged += OnZoneChange;
    }

    private static void OnZoneChange(ushort territory)
    {
        if (!PresetData.Contents.TryGetValue(territory, out var content)) return;

        var message = Lang.Get("AutoNotifyDutyName-NoticeMessage", content.ClassJobLevelSync, content.Name.ExtractText());

        Chat(message);
        NotificationInfo(message);
        Speak(message);
    }

    public override void Uninit()
    {
        DService.ClientState.TerritoryChanged -= OnZoneChange;
    }
}
