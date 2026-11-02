using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Rev.Core.Data;
using Rev.Core.Models;
using Rev.Linux;
using Rev.Linux.Controls;

[assembly: AvaloniaTestApplication(typeof(Rev.Linux.Tests.TestApp))]
// One app, one database at a time: the screens share App.Database.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Rev.Linux.Tests;

/// <summary>Starts Rev for Linux without a monitor, drawing real pictures (Skia) with Rev's own font.</summary>
public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithRevFont();
}

/// <summary>
/// A fresh Rev for each test: its own throw-away data folder with a few decks, and the main window open.
/// Helpers press keys and type like you would, and save pictures of the screen.
/// </summary>
public abstract class ScreenTest : IDisposable
{
    protected readonly string Folder = Path.Combine(Path.GetTempPath(), "rev-linux-tests-" + Guid.NewGuid().ToString("N"));
    protected readonly RevDatabase Db;
    protected Deck Pharmacology = null!;
    protected Deck Parasitology = null!;
    private MainWindow? window;

    protected ScreenTest()
    {
        Environment.SetEnvironmentVariable("REV_DATA_DIR", Folder);
        Directory.CreateDirectory(Folder);
        Db = RevDatabase.OpenDefault();
        App.Database = Db;
    }

    /// <summary>Two decks with a few cards, a cloze, a type-in card, tags and an image marker.</summary>
    protected void AddSampleDecks()
    {
        Pharmacology = Db.AddDeck("Pharmacology", DateTime.Now.AddDays(-3));
        Parasitology = Db.AddDeck("Parasitology", DateTime.Now.AddDays(-2));
        Db.AddNote(Pharmacology.Id, CardKind.Basic, "Class of **meloxicam**?", "NSAID (COX-2 preferential)", ["nsaid", "paper_1"]);
        Db.AddNote(Pharmacology.Id, CardKind.Reversed, "Atipamezole", "Reverses medetomidine", ["sedation"]);
        Db.AddNote(Pharmacology.Id, CardKind.TypeIn, "Antidote for *paracetamol* toxicity in cats?", "Acetylcysteine", []);
        Db.AddNote(Parasitology.Id, CardKind.Cloze, "{{c1::Fasciola hepatica}} lives in the {{c2::bile ducts}}", "", ["flukes"]);
        Db.AddNote(Parasitology.Id, CardKind.Basic, "Intermediate host of *Fasciola hepatica*?", "• Galba truncatula (a mud snail)\n• found in wet pasture", []);
    }

    protected MainWindow Window
    {
        get
        {
            if (window is null)
            {
                window = new MainWindow();
                window.Show();
                Settle();
            }
            return window;
        }
    }

    protected Control? CurrentScreen => Window.CurrentScreen;

    /// <summary>Lets the app catch up (layout, posted work, focus) after an action.</summary>
    protected static void Settle()
    {
        for (int i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    /// <summary>Lets the app run for a while, so its timers (e.g. the search delay) can fire.</summary>
    protected static void Wait(int milliseconds)
    {
        using var stop = new CancellationTokenSource(milliseconds);
        Dispatcher.UIThread.MainLoop(stop.Token);
        Settle();
    }

    protected void Press(Key key, KeyModifiers modifiers = KeyModifiers.None, TopLevel? where = null)
    {
        var top = where ?? Window;
        var raw = (RawInputModifiers)(int)modifiers;
        top.KeyPress(key, raw, PhysicalKey.None, null);
        top.KeyRelease(key, raw, PhysicalKey.None, null);
        Settle();
    }

    protected void Type(string text, TopLevel? where = null)
    {
        (where ?? Window).KeyTextInput(text);
        Settle();
    }

    protected void Click(Button button)
    {
        if (button.Command is { } command) command.Execute(null);
        else button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Settle();
    }

    /// <summary>A control on screen by what it's called (its accessible name, as screen readers see it, or its text).</summary>
    protected static T Find<T>(Visual root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.IsEffectivelyVisible
            && (Avalonia.Automation.AutomationProperties.GetName(c) == name || c is ContentControl { Content: string text } && text == name))
        ?? throw new InvalidOperationException($"No visible {typeof(T).Name} called “{name}”.");

    protected static IEnumerable<T> All<T>(Visual root) where T : Visual => root.GetVisualDescendants().OfType<T>();

    protected static DialogLayer Dialogs(Visual root) => DialogLayer.For(root)!;

    /// <summary>The dialog card showing now, or null.</summary>
    protected static RevDialog? OpenDialog(Visual root) => All<RevDialog>(Dialogs(root)).FirstOrDefault(d => d.IsEffectivelyVisible);

    protected static string Texts(Visual root) =>
        string.Join(" | ", root.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text)).Select(t => t.Text));

    /// <summary>Saves a picture of a window, for looking at afterwards (screens/ next to the tests, or REV_SCREENSHOTS).</summary>
    protected static void Picture(TopLevel top, string name)
    {
        Settle();
        string folder = Environment.GetEnvironmentVariable("REV_SCREENSHOTS") is { Length: > 0 } custom
            ? custom : Path.Combine(AppContext.BaseDirectory, "screens");
        Directory.CreateDirectory(folder);
        using var frame = top.CaptureRenderedFrame();
        frame?.Save(Path.Combine(folder, name + ".png"));
    }

    /// <summary>The window that opened on top of the main one (the card editor or Paste many), or null.</summary>
    protected T? Opened<T>() where T : Window => Window.OwnedWindows.OfType<T>().LastOrDefault();

    public void Dispose()
    {
        if (window != null)
        {
            foreach (var owned in window.OwnedWindows.ToList()) owned.Close();
            window.Close();
            Settle();
        }
        Db.Dispose();
        try { Directory.Delete(Folder, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }
}
