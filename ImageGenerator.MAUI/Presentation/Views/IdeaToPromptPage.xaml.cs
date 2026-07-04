using ImageGenerator.MAUI.Presentation.ViewModels;

namespace ImageGenerator.MAUI.Presentation.Views;

public partial class IdeaToPromptPage
{
    private WorkspaceLayoutMode _workspaceLayoutMode = WorkspaceLayoutMode.Wide;

    private enum WorkspaceLayoutMode { Wide, Narrow }

    public IdeaToPromptPage(IdeaToPromptViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is IdeaToPromptViewModel viewModel)
            viewModel.PrepareForNavigation();
    }

    private async void OnBackClicked(object sender, EventArgs e)
    {
        try
        {
            await Shell.Current.GoToAsync("..");
        }
        catch
        {
            // Back is best-effort; a failed pop just leaves the user on the page.
        }
    }

    private void OnWorkspaceSizeChanged(object? sender, EventArgs e)
    {
        if (WorkspaceGrid.Width <= 0) return;

        var mode = WorkspaceGrid.Width >= 760 ? WorkspaceLayoutMode.Wide : WorkspaceLayoutMode.Narrow;
        if (mode == _workspaceLayoutMode) return;

        _workspaceLayoutMode = mode;
        if (mode == WorkspaceLayoutMode.Wide)
        {
            SetColumns(new GridLength(5, GridUnitType.Star), new GridLength(4, GridUnitType.Star));
            SetRows(new GridLength(1, GridUnitType.Star));
            WorkspaceGrid.ColumnSpacing = 16;
            WorkspaceGrid.RowSpacing = 16;
            PlacePane(SourcePane, row: 0, column: 0);
            PlacePane(ResultPane, row: 0, column: 1);
        }
        else
        {
            SetColumns(new GridLength(1, GridUnitType.Star));
            SetRows(new GridLength(1, GridUnitType.Star), new GridLength(1, GridUnitType.Star));
            WorkspaceGrid.ColumnSpacing = 0;
            WorkspaceGrid.RowSpacing = 16;
            PlacePane(SourcePane, row: 0, column: 0);
            PlacePane(ResultPane, row: 1, column: 0);
        }
    }

    private void SetColumns(params GridLength[] widths)
    {
        WorkspaceGrid.ColumnDefinitions.Clear();
        foreach (var width in widths)
            WorkspaceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
    }

    private void SetRows(params GridLength[] heights)
    {
        WorkspaceGrid.RowDefinitions.Clear();
        foreach (var height in heights)
            WorkspaceGrid.RowDefinitions.Add(new RowDefinition { Height = height });
    }

    private static void PlacePane(BindableObject pane, int row, int column)
    {
        Grid.SetRow(pane, row);
        Grid.SetColumn(pane, column);
        Grid.SetColumnSpan(pane, 1);
    }
}
