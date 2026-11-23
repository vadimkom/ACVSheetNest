namespace DeepNestSharp.Ui.Views
{
  using System;
  using System.Collections.Generic;
  using System.Collections.ObjectModel;
  using System.ComponentModel;
  using System.IO;
  using System.Linq;
  using System.Windows;
  using System.Windows.Controls;
  using System.Windows.Input;
  using System.Windows.Media;
  using System.Windows.Media.Imaging;
  using DeepNestLib.NestProject;
  using DeepNestSharp.Reports;
  using Localization = DeepNestSharp.Ui.Localization;

  /// <summary>
  /// The job's pictures for the report (the user, 2026-11-23): added from files (the button or dropped on the
  /// window) or pasted from the clipboard (Ctrl+V), each with a caption, in the order they print.
  /// </summary>
  public partial class PicturesWindow : Window
  {
    public const string FileFilter = "*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff";

    private static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" };

    private readonly ObservableCollection<Item> items = new ObservableCollection<Item>();

    public PicturesWindow(IEnumerable<ProjectPicture> pictures)
    {
      InitializeComponent();
      foreach (var p in pictures ?? Enumerable.Empty<ProjectPicture>())
      {
        var item = Item.From(p);
        if (item != null)
        {
          this.items.Add(item);
        }
      }

      this.pictureList.ItemsSource = this.items;
      this.items.CollectionChanged += (_, __) => this.ShowEmpty();
      this.ShowEmpty();
      this.PreviewKeyDown += this.OnPreviewKeyDown;
    }

    /// <summary>The pictures as the operator left them; null when there are none.</summary>
    public List<ProjectPicture> Pictures
      => this.items.Count == 0 ? null : this.items.Select(i => new ProjectPicture { Caption = string.IsNullOrWhiteSpace(i.Caption) ? null : i.Caption.Trim(), Data = i.Data }).ToList();

    public static bool IsPictureFile(string path)
      => Extensions.Contains(Path.GetExtension(path ?? string.Empty).ToLowerInvariant());

    /// <summary>What the clipboard holds as pictures: copied picture files, else a picture (PNG first, the
    /// screenshot tools' own; then the bitmap, its alpha dropped: Print Screen leaves it all zero).</summary>
    public static List<ProjectPicture> FromClipboard(List<string> failed)
    {
      var result = new List<ProjectPicture>();
      if (Clipboard.ContainsFileDropList())
      {
        foreach (string file in Clipboard.GetFileDropList().Cast<string>().Where(IsPictureFile))
        {
          Add(result, failed, file, () => ReportPicture.Import(file));
        }

        return result;
      }

      if (Clipboard.GetDataObject() is IDataObject data && data.GetDataPresent("PNG") && data.GetData("PNG") is MemoryStream png)
      {
        Add(result, failed, "PNG", () => ReportPicture.Import(png.ToArray(), null));
        if (result.Count > 0)
        {
          return result;
        }
      }

      if (Clipboard.ContainsImage())
      {
        Add(result, failed, "bitmap", () =>
        {
          BitmapSource image = Clipboard.GetImage();
          return ReportPicture.Import(new FormatConvertedBitmap(image, PixelFormats.Bgr24, null, 0), null);
        });
      }

      return result;
    }

    private static void Add(List<ProjectPicture> result, List<string> failed, string what, Func<ProjectPicture> import)
    {
      try
      {
        result.Add(import());
      }
      catch (Exception ex) when (ex is IOException || ex is NotSupportedException || ex is ArgumentException || ex is UnauthorizedAccessException
        || ex is InvalidOperationException || ex is FileFormatException || ex is System.Runtime.InteropServices.COMException || ex is OverflowException)
      {
        failed.Add($"{what}: {ex.Message}");
      }
    }

    private void ShowEmpty() => this.emptyText.Visibility = this.items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void AddPictures(IEnumerable<ProjectPicture> pictures, List<string> failed)
    {
      Item last = null;
      foreach (var p in pictures)
      {
        var item = Item.From(p);
        if (item != null)
        {
          this.items.Add(item);
          last = item;
        }
      }

      if (last != null)
      {
        this.pictureList.SelectedItem = last;
        this.pictureList.ScrollIntoView(last);
      }

      if (failed.Count > 0)
      {
        MessageBox.Show(this, Localization.Get("Pic_NotRead") + "\n\n" + string.Join("\n", failed.Take(10)), this.Title, MessageBoxButton.OK, MessageBoxImage.Warning);
      }
    }

    private void AddFiles(IEnumerable<string> files)
    {
      var failed = new List<string>();
      var added = new List<ProjectPicture>();
      foreach (string file in files)
      {
        Add(added, failed, Path.GetFileName(file), () => ReportPicture.Import(file));
      }

      this.AddPictures(added, failed);
    }

    private void OnAddFiles(object sender, RoutedEventArgs e)
    {
      var dialog = new Microsoft.Win32.OpenFileDialog
      {
        Filter = $"{Localization.Get("Pic_FileFilter")} ({FileFilter})|{FileFilter}|{Localization.Get("Pic_AllFiles")} (*.*)|*.*",
        Multiselect = true,
        Title = Localization.Get("Pic_AddFiles"),
      };
      if (DeepNestSharp.Ui.Services.FileIoService.Show(dialog))
      {
        this.AddFiles(dialog.FileNames);
      }
    }

    private void OnPaste(object sender, RoutedEventArgs e) => this.Paste(explicitly: true);

    private bool Paste(bool explicitly)
    {
      var failed = new List<string>();
      List<ProjectPicture> pasted;
      try
      {
        pasted = FromClipboard(failed);
      }
      catch (System.Runtime.InteropServices.COMException ex)
      {
        pasted = new List<ProjectPicture>();
        failed.Add(ex.Message); // the clipboard is held by another program
      }

      if (pasted.Count == 0 && failed.Count == 0)
      {
        if (explicitly)
        {
          MessageBox.Show(this, Localization.Get("Pic_ClipboardEmpty"), this.Title, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        return false;
      }

      this.AddPictures(pasted, failed);
      return true;
    }

    /// <summary>Ctrl+V pastes a picture wherever the focus is; text in a caption field still pastes as text.</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
      if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
      {
        bool hasPicture = Clipboard.ContainsImage() || Clipboard.ContainsFileDropList() || Clipboard.ContainsData("PNG");
        if (hasPicture && this.Paste(explicitly: false))
        {
          e.Handled = true;
        }
      }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
      e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
      e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
      if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
      {
        this.AddFiles(files.Where(IsPictureFile));
      }
    }

    private void OnUp(object sender, RoutedEventArgs e) => this.Move(sender, -1);

    private void OnDown(object sender, RoutedEventArgs e) => this.Move(sender, +1);

    private void Move(object sender, int by)
    {
      if ((sender as FrameworkElement)?.DataContext is Item item)
      {
        int i = this.items.IndexOf(item);
        int j = i + by;
        if (i >= 0 && j >= 0 && j < this.items.Count)
        {
          this.items.Move(i, j);
          this.pictureList.SelectedItem = item;
        }
      }
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
      if ((sender as FrameworkElement)?.DataContext is Item item)
      {
        this.items.Remove(item);
      }
    }

    private void OnOk(object sender, RoutedEventArgs e) => this.DialogResult = true;

    /// <summary>One row: the picture's data as stored, its caption and a small copy to show.</summary>
    public sealed class Item : INotifyPropertyChanged
    {
      private string caption;

      public event PropertyChangedEventHandler PropertyChanged;

      public string Data { get; private set; }

      public BitmapSource Thumbnail { get; private set; }

      public string SizeText { get; private set; }

      public string Caption
      {
        get => this.caption;
        set
        {
          if (this.caption != value)
          {
            this.caption = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.Caption)));
          }
        }
      }

      public static Item From(ProjectPicture picture)
      {
        var shown = ReportPicture.FromProject(picture);
        if (shown == null)
        {
          return null;
        }

        BitmapSource thumb = shown.Bitmap;
        double k = Math.Min(1.0, 280.0 / Math.Max(thumb.PixelWidth, thumb.PixelHeight));
        if (k < 1.0)
        {
          thumb = new TransformedBitmap(thumb, new ScaleTransform(k, k));
          thumb.Freeze();
        }

        long bytes = (picture.Data.Length / 4L) * 3L;
        return new Item
        {
          Data = picture.Data,
          caption = picture.Caption,
          Thumbnail = thumb,
          SizeText = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "{0} × {1} px, {2:0.#} KB",
            shown.PixelWidth,
            shown.PixelHeight,
            bytes / 1024.0),
        };
      }
    }
  }
}
