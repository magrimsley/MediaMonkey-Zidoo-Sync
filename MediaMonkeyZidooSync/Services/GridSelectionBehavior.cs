using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace MediaMonkeyZidooSync.Services;

/// <summary>Shared read-only grid behavior: blank-area clicks clear selection,
/// while clicks on cells/rows preserve native WPF selection and keyboard navigation.</summary>
public static class GridSelectionBehavior
{
    public static void Attach(DataGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        grid.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
        grid.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
    }

    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid || e.OriginalSource is not DependencyObject source) return;
        for (DependencyObject? node = source; node is not null;)
        {
            if (node is ScrollBar || node is Thumb || node is DataGridRow || node is DataGridCell || node is DataGridColumnHeader) return;
            if (ReferenceEquals(node, grid))
            {
                grid.UnselectAll();
                return;
            }
            node = node is Visual || node is Visual3D
                ? VisualTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }
    }
}
