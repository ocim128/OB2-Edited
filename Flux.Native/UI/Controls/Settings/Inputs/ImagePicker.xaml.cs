using Microsoft.Win32;
using Flux.Core.Helpers;
using Flux.Native.Helpers;
using Flux.Native.Utils;
using Flux.Native.ViewModels;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Flux.Native.ViewModels.Base;

namespace Flux.Native.Controls
{
    /// <summary>
    /// Interaction logic for ImagePicker.xaml
    /// </summary>
    public partial class ImagePicker : UserControl
    {
        private ImagePickerViewModel vm;
        public event EventHandler<byte[]> ImageChanged;

        public ImagePicker(byte[] imageBytes)
        {
            InitializeComponent();
            vm = new ImagePickerViewModel
            {
                ImageBytes = imageBytes
            };
            DataContext = vm;
        }

        private void OpenImage(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Filter = "Images | *.ico;*.jpg;*.jpeg;*.png;*.bmp",
                FilterIndex = 1
            };

            ofd.ShowDialog();

            if (!string.IsNullOrEmpty(ofd.FileName))
            {
                try
                {
                    vm.SetImageFromFile(ofd.FileName);
                    ImageChanged?.Invoke(this, vm.ImageBytes);
                }
                catch (Exception ex)
                {
                    Alert.Exception(ex);
                }
            }
        }
    }

    public class ImagePickerViewModel : ViewModelBase
    {
        private byte[] imageBytes;
        public byte[] ImageBytes
        {
            get => imageBytes;
            set
            {
                imageBytes = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Image));
            }
        }

        public BitmapImage Image => ImageBytes is null ? null : Images.BytesToBitmapImage(ImageBytes);

        public void SetImageFromFile(string fileName)
            => ImageBytes = ImageEditor.ToCompatibleFormat(File.ReadAllBytes(fileName));
    }
}
