using System.Media;
using System.Windows;

namespace PromptTester.Wpf.Services;

/// <summary>
/// Plays the standard Windows notification sound when a run completes.
/// </summary>
public static class CompletionAnnouncer
{
    public static void AnnounceRunCompleted()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        _ = dispatcher.BeginInvoke(PlayCompletionSound);
    }

    private static void PlayCompletionSound()
    {
        try
        {
            SystemSounds.Asterisk.Play();
        }
        catch
        {
            // A notification failure must not affect a completed extraction run.
        }
    }
}
