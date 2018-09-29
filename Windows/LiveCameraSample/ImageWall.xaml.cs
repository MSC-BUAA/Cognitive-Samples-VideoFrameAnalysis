using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace LiveCameraSample {
    /// <summary>
    /// Interaction logic for ImageWall.xaml
    /// </summary>
    public partial class ImageWall : Window {
        public ImageWall() {
            InitializeComponent();
        }

        public static int num = 4;
        public int rows = 2;
        public int cols = 2;

        public BitmapSource[] faceBitmaps = new BitmapSource[num];
        public List<string> id = new List<string>(num);
        public bool[] colorful = new bool[num];
        Image[] faceImgs = new Image[num];

        public void UpdateCanvas() {
            canvas.Width = this.grid.Width;
            canvas.Height = this.grid.Height;
            canvas.Children.Clear();
            int MaxX = (int)this.Width;
            int MaxY = (int)this.Height;
            int width = MaxX / cols;
            int height = MaxY / rows;

            for (int i = 0; i < faceImgs.Length; i++) {


                int row = i / cols;
                int col = i % cols;
                faceImgs[i] = new Image();
                if (colorful[i]) {
                    faceImgs[i].Source = faceBitmaps[i];
                } else {
                    faceImgs[i].Source = ToGrayScale(faceBitmaps[i]);
                }
                TransformedBitmap myTransformedSource = new TransformedBitmap();
                myTransformedSource.BeginInit();
                myTransformedSource.Source = (BitmapSource)faceImgs[i].Source;
                myTransformedSource.Transform = new ScaleTransform(width / faceImgs[i].Source.Width, height / faceImgs[i].Source.Height);
                myTransformedSource.EndInit();
                faceImgs[i].Source = myTransformedSource;
                faceImgs[i].Width = width;
                faceImgs[i].Height = height;
                Canvas.SetLeft(faceImgs[i], col * width);
                Canvas.SetTop(faceImgs[i], row * height);
                canvas.Children.Add(faceImgs[i]);
            }
        }

        public unsafe static BitmapSource ToGrayScale(BitmapSource source) {
            const int PIXEL_SIZE = 4;
            int width = source.PixelWidth;
            int height = source.PixelHeight;
            var bitmap = new WriteableBitmap(source);
            bitmap.Lock();
            var backBuffer = (byte*)bitmap.BackBuffer.ToPointer();
            for (int y = 0; y < height - 1; y++) {
                var row = backBuffer + (y * bitmap.BackBufferStride);
                for (int x = 0; x < width; x++) {
                    var grayScale = (byte)(((row[x * PIXEL_SIZE + 1]) + (row[x * PIXEL_SIZE + 2]) + (row[x * PIXEL_SIZE + 3])) / 3);
                    for (int i = 0; i < PIXEL_SIZE; i++)
                        row[x * PIXEL_SIZE + i] = grayScale;
                }
            }
            bitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
            bitmap.Unlock();
            return bitmap;
        }


    }

    

}
