using System;
using System.Windows;

namespace SqlDiagnostics.UI.Dialogs;

/// <summary>
/// Helper methods to display the connection quality dialog from host applications.
/// </summary>
public static class ConnectionQualityDialogLauncher
{
    /// <summary>
    /// Shows the connection quality dialog as a non-modal window.
    /// </summary>
    /// <param name="options">Dialog configuration.</param>
    /// <param name="owner">Optional owning window.</param>
    /// <returns>The dialog instance so callers may close it programmatically.</returns>
    public static ConnectionQualityDialog Show(ConnectionQualityDialogOptions options, Window? owner = null)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var dialog = new ConnectionQualityDialog(options);
        if (owner is not null)
        {
            dialog.Owner = owner;
        }

        dialog.Show();
        return dialog;
    }

    /// <summary>
    /// Shows the connection quality dialog modally and blocks the calling thread until it closes.
    /// </summary>
    /// <param name="options">Dialog configuration.</param>
    /// <param name="owner">Optional owning window.</param>
    /// <returns>The dialog result.</returns>
    public static bool? ShowModal(ConnectionQualityDialogOptions options, Window? owner = null)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var dialog = new ConnectionQualityDialog(options);
        if (owner is not null)
        {
            dialog.Owner = owner;
        }

        return dialog.ShowDialog();
    }
}
