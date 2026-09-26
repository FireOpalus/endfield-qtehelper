namespace EndfieldQteHelper;

internal static class Program
{
    internal static string Version => typeof(Program).Assembly.GetName().Version!.ToString(3);
    internal static Icon LoadIcon()
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("EndfieldQteHelper.AppIcon")!;
        using var original = new Icon(stream);
        return (Icon)original.Clone();
    }

    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length > 0 && args[0] == "--self-test") return Verification.Run(args.Skip(1).ToArray());
        if (args.Length > 0 && args[0] == "--render-preview") return Verification.Render(args[1]);
        Application.ThreadException += (_, e) => MessageBox.Show(e.Exception.Message, "QTE 助手");
        Application.Run(new MainForm());
        return 0;
    }
}
