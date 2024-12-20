using DailyRoutines.Abstracts;

namespace DailyRoutines.Modules;

public class DisableGroundActionAutoFace : DailyModuleBase
{
    public override ModuleInfo Info => new()
    {
        Title = GetLoc("DisableGroundActionAutoFaceTitle"),
        Description = GetLoc("DisableGroundActionAutoFaceDescription"),
        Category = ModuleCategories.Action,
    };

    private static readonly MemoryPatch GroundActionAutoFacePatch =
        new("74 ?? 48 8D 8F ?? ?? ?? ?? E8 ?? ?? ?? ?? 84 C0 75 ?? 41 B8", [0xEB]);

    public override void Init()
    {
        GroundActionAutoFacePatch.Set(true);
    }

    public override void Uninit()
    {
        GroundActionAutoFacePatch.Dispose();
    }
}
