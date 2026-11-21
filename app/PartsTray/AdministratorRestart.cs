// 設定に応じた runas 再起動と、UAC キャンセル時の継続を扱います。
// 管理者起動の引数・待機・通知を変えるときに開きます。
using System.ComponentModel;
using System.Diagnostics;
using PartsTray.Core;

namespace PartsTray;

internal static class AdministratorRestart
{
    public const string RestartArgument = "--elevated-restart";

    public static bool IsRestart(string[] args) => args.Contains(RestartArgument, StringComparer.Ordinal);
    public static void WaitForParent(string[] args)
    {
        string? value = args.FirstOrDefault(a => a.StartsWith("--wait-for-process=", StringComparison.Ordinal));
        if (!IsRestart(args) || value == null || !int.TryParse(value[19..], out int id) || id == Environment.ProcessId) return;
        try
        {
            using var process = Process.GetProcessById(id);
            if (!process.WaitForExit(20000)) throw new TimeoutException("再起動前の PartsTray が終了しませんでした");
        }
        catch (ArgumentException) { } // すでに終了済み。
        catch (InvalidOperationException) { }
    }

    // true は新しいプロセスへ引き渡したか、別の既存プロセスに引き継いだ場合。
    public static bool TryRestart(SingleInstance instance, ILog log, Action<string> notify)
    {
        instance.Release();
        try
        {
            string executable = Environment.ProcessPath ?? throw new InvalidOperationException("実行ファイルのパスがありません");
            using var process = Process.Start(new ProcessStartInfo(executable)
            {
                UseShellExecute = true, Verb = "runas",
                Arguments = $"{RestartArgument} --wait-for-process={Environment.ProcessId}",
                WorkingDirectory = AppContext.BaseDirectory
            });
            if (process == null) throw new InvalidOperationException("管理者プロセスを起動できませんでした");
            log.Write("管理者として起動し直します");
            return true;
        }
        catch (Exception ex)
        {
            string message = ex is Win32Exception { NativeErrorCode: 1223 }
                ? "管理者起動の確認がキャンセルされました。ふつうの権限で続けます。"
                : "管理者として起動できませんでした。ふつうの権限で続けます。 " + ex.Message;
            log.Write(message); notify(message);
            if (instance.Acquire(3000)) return false;
            instance.Show.Set();
            return true;
        }
    }
}
