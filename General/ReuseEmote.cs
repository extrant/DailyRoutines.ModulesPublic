using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;

namespace DailyRoutines.Modules;

public class ReuseEmote : DailyModuleBase
{
    internal  const string Command = "reuseemote";
    private static Dictionary<string, ushort> EmoteMap=new();
    private static CancellationTokenSource _globalCts=null;
    public override ModuleInfo Info => new()
    {
        Title =GetLoc("ReuseEmoteTitle"),
        Description = GetLoc("ReuseEmoteDescription"),
        Category = ModuleCategories.General,
        Author = ["Xww"],
    };

    public override void Init()
    {
        var emotesheet = LuminaCache.Get<Emote>();
        foreach (var e in emotesheet)
        {
            try
            {
                EmoteMap.Add(e.Name.ToString(),(ushort)e.RowId);
                EmoteMap.Add(e.TextCommand.Value.Command.ToString().Replace("/",""), (ushort)e.RowId);
            }
            catch
            {
                // ignored
            }
        }
        CommandManager.AddSubCommand(Command, new(OnCommand) { HelpMessage =GetLoc("ReuseEmote-CommandHelp")} );
    }

    public override void Uninit()
    {
        CommandManager.RemoveCommand(Command);
        if (_globalCts!=null)
        {
            _globalCts?.Cancel();
            _globalCts?.Dispose();
            _globalCts = null;
            return;
        }
        base.Uninit();
    }
    private static void OnCommand(string command, string args)
    {
        if (_globalCts!=null)
        {
            NotificationInfo(GetLoc("ReuseEmote-Notice-Stop"));
            _globalCts?.Cancel();
            _globalCts?.Dispose();
            _globalCts = null;
            return;
        }
        var argsStrings = args.Split(" ");
        if (!EmoteMap.ContainsKey(argsStrings[0]) || argsStrings[0]=="")
        {
            NotificationInfo($"{GetLoc("ReuseEmote-Notice-notexist")}：{argsStrings[0]}");
            return;
        }
        NotificationInfo($"{GetLoc("ReuseEmote-Notice-Start")}：{argsStrings[0]}");
        _globalCts = new CancellationTokenSource();
        var millisecondsTimeout = 2000;
        if(argsStrings.Length>1)
            if(int.TryParse(argsStrings[1],out var timeOut))
                millisecondsTimeout=timeOut;
        Task.Delay(0).ContinueWith((_ => useemote(_globalCts,EmoteMap[argsStrings[0]],millisecondsTimeout)));
    }

    private static unsafe void useemote(CancellationTokenSource cts,ushort id, int time)
    {
        while (true)
        {
            try
            {
                if (cts.Token.IsCancellationRequested) return;
                AgentEmote.Instance()->ExecuteEmote(id,default,false,false);
                Thread.Sleep(time);
            }
            catch
            {
                return;
            }
        }
    }
}
