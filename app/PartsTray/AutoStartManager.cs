// Run と管理者用タスクを切り替え、権限不足で残る作業を次の起動に引き継ぎます。
// 自動起動の登録・旧名からの移行・後片付けの再試行を変えるときに開きます。
using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using PartsTray.Core;

namespace PartsTray;

internal sealed class AutoStartManager(ILog log)
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "PartsTray", OldRunName = "LogiGap";
    private readonly string statePath = Path.Combine(AppContext.BaseDirectory, "autostart-state.json");
    private sealed record State(bool Enabled, bool TaskMayExist);
    private State? state;

    public bool Read() => Load().Enabled;

    private State Load()
    {
        if (state != null) return state;
        if (File.Exists(statePath))
            return state = JsonSerializer.Deserialize<State>(File.ReadAllText(statePath))
                ?? throw new InvalidDataException("自動起動の管理情報が空です");
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        bool task;
        try { task = ScheduledTaskQuery.Exists(); }
        catch (Exception ex) when (!ProcessElevation.IsAdministrator)
        {
            // 読めないタスクを「ない」として忘れず、管理者での次回起動へ後片付けを残す。
            log.Write($"自動起動タスクの確認を保留: {ex.Message}");
            return state = new(key?.GetValue(RunName) is string || key?.GetValue(OldRunName) is string, true);
        }
        return state = new(key?.GetValue(RunName) is string || key?.GetValue(OldRunName) is string || task, task);
    }
    private void Save(State value)
    {
        string temp = statePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value));
        File.Move(temp, statePath, true);
        state = value;
    }
    public void Write(bool enabled, bool administrator)
    {
        Save(Load() with { Enabled = enabled });
        Apply(administrator);
    }
    public void Reconcile(bool administrator)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            // 管理情報を作った後でも、旧名の登録があれば引き継ぐ。
            if (key?.GetValue(OldRunName) is string) Save(Load() with { Enabled = true });
            Apply(administrator);
        }
        catch (Exception ex) { log.Write($"自動起動の反映を保留: {ex.Message}"); }
    }
    private void Apply(bool administrator)
    {
        var desired = Load();
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        bool migrate = key.GetValue(OldRunName) is string;
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("実行ファイルのパスがありません");
        if (desired.Enabled && !administrator) key.SetValue(RunName, $"\"{executable}\"");
        else key.DeleteValue(RunName, false);
        // 旧名を消す前に意図を保存し、タスク登録の延期でも自動起動の希望を失わない。
        Save(desired);
        key.DeleteValue(OldRunName, false);
        if (migrate) log.Write($"自動起動の登録を {OldRunName} から {RunName} に引き継ぎました");
        if (desired.Enabled && administrator)
        {
            if (!ProcessElevation.IsAdministrator)
            { log.Write("管理者の自動起動タスク登録を保留。次に管理者で起動したときに登録します"); return; }
            Save(desired with { TaskMayExist = true });
            using var identity = WindowsIdentity.GetCurrent();
            string sid = identity.User?.Value ?? throw new InvalidOperationException("ユーザー SID がありません");
            // 11/21 Claude：一時ファイル（ユーザーの %TEMP%）を経由して schtasks に渡すと、管理者のプロセスが読む前に
            // ふつうの権限のプログラムにすり替えられる隙がある。タスク スケジューラーの COM に XML の文字をそのまま渡す。
            ScheduledTaskQuery.Register(RunName, AutoStartTask.CreateXml(sid, executable, Path.GetDirectoryName(executable)!));
        }
        else if (desired.TaskMayExist)
        {
            if (!ProcessElevation.IsAdministrator)
            {
                // タスクがもともとなければ、消す必要もない（ふつうの権限でも、あるかどうかは調べられる）。
                if (!ScheduledTaskQuery.Exists()) { Save(desired with { TaskMayExist = false }); return; }
                log.Write("自動起動タスクの削除を保留。次に管理者で起動したときに削除します"); return;
            }
            // すでに削除済みなら成功扱い。削除失敗時は印を残して再試行する。
            if (ScheduledTaskQuery.Exists())
                RunTask(["/Delete", "/TN", RunName, "/F"]);
            Save(desired with { TaskMayExist = false });
        }
    }
    private static void RunTask(string[] arguments)
    {
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("タスク操作を開始できません");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(10000)) { process.Kill(); throw new TimeoutException("タスク操作がタイムアウトしました"); }
        string detail = error.GetAwaiter().GetResult() + output.GetAwaiter().GetResult();
        if (process.ExitCode == 0) return;
        throw new InvalidOperationException($"タスク操作失敗 ({process.ExitCode}): {detail.Trim()}");
    }
}
