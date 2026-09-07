using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

// Taken from https://github.com/microsoft/WinUI-Gallery/blob/main/WinUIGallery/Controls/TileGallery.xaml.cs
namespace SynToolkit.Controls
{
    public sealed partial class TileGallery : UserControl
    {
        private bool _isShowingUpdateNotes;

        public TileGallery()
        {
            this.InitializeComponent();
            SetText();
            Unloaded += TileGallery_Unloaded;
        }

        private void SetText()
        {
            UpdateNotesTile.Title = "Update notes";
            UpdateNotesTile.Description = "What's new in SynToolkit 1.7.0";
            DocumentationTile.Title = App.GetValueFromItemList("Tile_DocumentationTitle");
            DocumentationTile.Description = App.GetValueFromItemList("Tile_DocumentationDescription");
            GithubTile.Title = App.GetValueFromItemList("Tile_GithubTitle");
            GithubTile.Description = App.GetValueFromItemList("Tile_GithubDescription");
            DiscordTile.Title = App.GetValueFromItemList("Tile_DiscordTitle");
            DiscordTile.Description = App.GetValueFromItemList("Tile_DiscordDescription");
        }

        private async void UpdateNotesTile_Click(object sender, RoutedEventArgs e)
        {
            if (_isShowingUpdateNotes || XamlRoot is null)
            {
                return;
            }

            try
            {
                _isShowingUpdateNotes = true;
                UpdateNotesDialog.XamlRoot = XamlRoot;
                await UpdateNotesDialog.ShowAsync();
            }
            catch (System.Exception exception)
            {
                App.logger.Warn(exception, "The local update-notes dialog could not be displayed.");
            }
            finally
            {
                _isShowingUpdateNotes = false;
            }
        }

        private void TileGallery_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_isShowingUpdateNotes)
            {
                UpdateNotesDialog.Hide();
            }
        }

        private void scroller_ViewChanging(object sender, ScrollViewerViewChangingEventArgs e)
        {
            if (e.FinalView.HorizontalOffset < 1)
            {
                ScrollBackBtn.Visibility = Visibility.Collapsed;
            }
            else if (e.FinalView.HorizontalOffset > 1)
            {
                ScrollBackBtn.Visibility = Visibility.Visible;
            }

            if (e.FinalView.HorizontalOffset > scroller.ScrollableWidth - 1)
            {
                ScrollForwardBtn.Visibility = Visibility.Collapsed;
            }
            else if (e.FinalView.HorizontalOffset < scroller.ScrollableWidth - 1)
            {
                ScrollForwardBtn.Visibility = Visibility.Visible;
            }
        }

        private void ScrollBackBtn_Click(object sender, RoutedEventArgs e)
        {
            scroller.ChangeView(scroller.HorizontalOffset - scroller.ViewportWidth, null, null);
            // Manually focus to ScrollForwardBtn since this button disappears after scrolling to the end.          
            ScrollForwardBtn.Focus(FocusState.Programmatic);
        }

        private void ScrollForwardBtn_Click(object sender, RoutedEventArgs e)
        {
            scroller.ChangeView(scroller.HorizontalOffset + scroller.ViewportWidth, null, null);

            // Manually focus to ScrollBackBtn since this button disappears after scrolling to the end.    
            ScrollBackBtn.Focus(FocusState.Programmatic);
        }

        private void scroller_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateScrollButtonsVisibility();
        }

        private void UpdateScrollButtonsVisibility()
        {
            if (scroller.ScrollableWidth > 0)
            {
                ScrollForwardBtn.Visibility = Visibility.Visible;
            }
            else
            {
                ScrollForwardBtn.Visibility = Visibility.Collapsed;
            }
        }
    }
}
