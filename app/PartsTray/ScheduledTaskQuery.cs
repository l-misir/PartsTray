// タスクの存在を読み取り、不在とアクセス拒否を区別します。
// 自動起動タスクの検出と、削除保留の判定を変えるときに開きます。
using System.Runtime.InteropServices;

namespace PartsTray;

internal static class ScheduledTaskQuery
{
    // タスクを XML の文字から登録する（同じ名前があれば置き換える）。管理者で動いているときだけ呼ぶ。
    // RegisterTask(path, xml, TASK_CREATE_OR_UPDATE = 6, userId, password, TASK_LOGON_INTERACTIVE_TOKEN = 3, sddl)
    public static void Register(string name, string xml)
    {
        object? service = null, folder = null, task = null;
        try
        {
            var type = Type.GetTypeFromProgID("Schedule.Service")
                ?? throw new InvalidOperationException("タスク スケジューラーが見つかりません");
            service = Activator.CreateInstance(type)!;
            ((dynamic)service).Connect();
            folder = ((dynamic)service).GetFolder(@"\");
            task = ((dynamic)folder).RegisterTask(name, xml, 6, null, null, 3, null);
        }
        finally
        {
            if (task != null) Marshal.FinalReleaseComObject(task);
            if (folder != null) Marshal.FinalReleaseComObject(folder);
            if (service != null) Marshal.FinalReleaseComObject(service);
        }
    }

    public static bool Exists()
    {
        object? service = null, folder = null, task = null;
        try
        {
            var type = Type.GetTypeFromProgID("Schedule.Service")
                ?? throw new InvalidOperationException("タスク スケジューラーが見つかりません");
            service = Activator.CreateInstance(type)!;
            ((dynamic)service).Connect();
            folder = ((dynamic)service).GetFolder(@"\");
            task = ((dynamic)folder).GetTask("PartsTray");
            return true;
        }
        // 11/21 実機：タスクがないときは COMException ではなく FileNotFoundException（同じ 0x80070002）で来た。
        catch (Exception ex) when (ex.HResult == unchecked((int)0x80070002)) { return false; }
        finally
        {
            if (task != null) Marshal.FinalReleaseComObject(task);
            if (folder != null) Marshal.FinalReleaseComObject(folder);
            if (service != null) Marshal.FinalReleaseComObject(service);
        }
    }
}
