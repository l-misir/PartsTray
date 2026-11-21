// 管理者でログオン時に起動するタスクの XML を、副作用なしで作ります。
// タスクの実行条件・実行ユーザー・実行ファイルの指定を変えるときに開きます。
using System.Xml.Linq;

namespace PartsTray.Core;

public static class AutoStartTask
{
    public static string CreateXml(string userSid, string executable, string workingDirectory)
    {
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        XElement E(string name, params object[] content) => new(ns + name, content);
        return new XDocument(new XDeclaration("1.0", "utf-16", null),
            E("Task", new XAttribute("version", "1.2"),
                E("Triggers", E("LogonTrigger", E("Enabled", true), E("UserId", userSid))),
                E("Principals", E("Principal", new XAttribute("id", "Author"),
                    E("UserId", userSid), E("LogonType", "InteractiveToken"), E("RunLevel", "HighestAvailable"))),
                E("Settings", E("MultipleInstancesPolicy", "IgnoreNew"),
                    E("DisallowStartIfOnBatteries", false), E("StopIfGoingOnBatteries", false),
                    E("ExecutionTimeLimit", "PT0S")),
                E("Actions", new XAttribute("Context", "Author"), E("Exec",
                    E("Command", executable), E("WorkingDirectory", workingDirectory)))))
            .ToString();
    }
}
