// アプリのアイコン（PartsTray.ico）を読み込みます。
// 絵を変えるときは PartsTray.ico を差し替えます（16〜256 ドットの絵が入っています）。
namespace PartsTray;

internal static class AppIcon
{
    // exe に埋め込んだ PartsTray.ico（PartsTray.csproj の EmbeddedResource）。読めなければ Windows の標準のアイコンにする。
    private static Icon Load(Size? size)
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("PartsTray.ico");
        if (stream is null) return (Icon)SystemIcons.Application.Clone();
        return size is { } s ? new Icon(stream, s) : new Icon(stream);
    }

    // 設定画面のタイトルバーとタスクバー用（Windows が中から合う大きさを選ぶ）。アプリが終わるまで使い回す。
    public static Icon Window { get; } = Load(null);

    // 通知領域用（温度のアイコンを出さない設定のとき、本体のアイコンになる）。アプリが終わるまで使い回す。
    public static Icon Small { get; } = Load(SystemInformation.SmallIconSize);
}
