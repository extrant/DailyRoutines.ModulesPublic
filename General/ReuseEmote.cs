using System.Collections.Generic;
using System.Threading;
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
        for (ushort i = 1; i < 300; i++)
        {
            try
            {
                var emote=DService.Data.GetExcelSheet<Emote>().GetRow(i);
                EmoteMap.Add(emote.Name.ToString(), i);
                EmoteMap.Add(emote.TextCommand.Value.Command.ToString().Replace("/",""), i);  //英文指令取出/
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
        Task.Delay(0).ContinueWith((_ => useemote(_globalCts, argsStrings)));
    }

    private static unsafe void useemote(CancellationTokenSource cts,string[] args)
    {
        AgentEmote.Instance()->ExecuteEmote(EmoteMap[args[0]],default,false,false);
        var timeout = 2000;
        if (args.Length == 2)
        {
            if (int.TryParse(args[1], out int i))
                timeout = i;
        }
        while (true)
        {
            try
            {
                Thread.Sleep(timeout);
                if (cts.Token.IsCancellationRequested) return;
                AgentEmote.Instance()->ExecuteEmote(EmoteMap[args[0]],default,false,false);
            }
            catch
            {
                return;
            }
        }
    }
}
