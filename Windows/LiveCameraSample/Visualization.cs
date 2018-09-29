// 
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license.
// 
// Microsoft Cognitive Services: http://www.microsoft.com/cognitive
// 
// Microsoft Cognitive Services Github:
// https://github.com/Microsoft/Cognitive
// 
// Copyright (c) Microsoft Corporation
// All rights reserved.
// 
// MIT License:
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED ""AS IS"", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
// 

using System;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.ProjectOxford.Common.Contract;
using FaceAPI = Microsoft.ProjectOxford.Face.Contract;
using Microsoft.ProjectOxford.Vision.Contract;
using System.Collections.ObjectModel;

namespace LiveCameraSample
{
    public class Visualization
    {
        public unsafe static BitmapSource ToGrayScale(BitmapSource source) {
            const int PIXEL_SIZE = 4;
            int width = source.PixelWidth;
            int height = source.PixelHeight;
            var bitmap = new WriteableBitmap(source);
            bitmap.Lock();
            var backBuffer = (byte*)bitmap.BackBuffer.ToPointer();
            for (int y = 0; y < height-1; y++) {
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


        private static SolidColorBrush s_lineBrush = new SolidColorBrush(new System.Windows.Media.Color { R = 255, G = 185, B = 0, A = 255 });
        private static Typeface s_typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

        private static BitmapSource DrawOverlay(BitmapSource baseImage, Action<DrawingContext, BitmapSource, double> drawAction)
        {
            BitmapSource oriImage = baseImage;
            //baseImage = ToGrayScale(baseImage);
            double annotationScale = baseImage.PixelHeight / 320;

            DrawingVisual visual = new DrawingVisual();
            DrawingContext drawingContext = visual.RenderOpen();

            drawingContext.DrawImage(baseImage, new Rect(0, 0, baseImage.Width, baseImage.Height));

            drawAction(drawingContext, oriImage, annotationScale);

            drawingContext.Close();

            RenderTargetBitmap outputBitmap = new RenderTargetBitmap(
                baseImage.PixelWidth, baseImage.PixelHeight,
                baseImage.DpiX, baseImage.DpiY, PixelFormats.Pbgra32);

            outputBitmap.Render(visual);

            return outputBitmap;
        }

        public static BitmapSource DrawTags(BitmapSource baseImage, Tag[] tags)
        {
            if (tags == null)
            {
                return baseImage;
            }

            Action<DrawingContext, BitmapSource, double> drawAction = (drawingContext, oriImage, annotationScale) =>
            {
                double y = 0;
                foreach (var tag in tags)
                {
                    // Create formatted text--in a particular font at a particular size
                    FormattedText ft = new FormattedText(tag.Name,
                        CultureInfo.CurrentCulture, FlowDirection.LeftToRight, s_typeface,
                        21 * annotationScale, Brushes.Black);
                    // Instead of calling DrawText (which can only draw the text in a solid colour), we
                    // convert to geometry and use DrawGeometry, which allows us to add an outline. 
                    var geom = ft.BuildGeometry(new System.Windows.Point(10 * annotationScale, y));
                    drawingContext.DrawGeometry(s_lineBrush, new Pen(Brushes.Black, 2 * annotationScale), geom);
                    // Move line down
                    y += 42 * annotationScale;
                }
            };

            return DrawOverlay(baseImage, drawAction);
        }

        public static BitmapSource DrawFaces(BitmapSource baseImage, FaceAPI.Face[] faces, ObservableCollection<Microsoft.ProjectOxford.Face.Controls.Face> targetFaces, EmotionScores[] emotionScores, string[] celebName, List<PersonData> personData, DataTable dataTable, ImageWall imageWall)
        {
            if (faces == null)
            {
                return baseImage;
            }

            Action<DrawingContext, BitmapSource, double> drawAction = (drawingContext, oriImage, annotationScale) =>
            {
                for (int i = 0; i < faces.Length; i++)
                {
                    if (targetFaces[i].PersonName == "Unknown") {
                        continue;
                    }
                    var face = faces[i];
                    imageWall.colorful[imageWall.id.IndexOf(targetFaces[i].FaceId)] = true;
                    if (face.FaceRectangle == null) { continue; }

                    PersonData pD = new PersonData();
                    try {
                        pD = personData.Find(x => x.ID == targetFaces[i].FaceId);
                        pD.Times++;
                    } catch (Exception) {
                        personData.Find(x => x.ID == targetFaces[i].FaceId);
                    }


                    Rect faceRect = new Rect(
                        face.FaceRectangle.Left, face.FaceRectangle.Top,
                        face.FaceRectangle.Width, face.FaceRectangle.Height);
                    Int32Rect faceRectInt32 = new Int32Rect(
                        face.FaceRectangle.Left, face.FaceRectangle.Top,
                        face.FaceRectangle.Width, face.FaceRectangle.Height);
                    string text = "";

                    drawingContext.DrawImage(new CroppedBitmap(oriImage, faceRectInt32), faceRect);

                    if (face.FaceAttributes != null)
                    {
                        text += Aggregation.SummarizeFaceAttributes(face.FaceAttributes, targetFaces[i].PersonName, pD);
                    }

                    if (emotionScores?[i] != null)
                    {
                        text += Aggregation.SummarizeEmotion(emotionScores[i]);
                    }

                    if (celebName?[i] != null)
                    {
                        text += celebName[i];
                    }

                    faceRect.Inflate(6 * annotationScale, 6 * annotationScale);

                    double lineThickness = 4 * annotationScale;

                    drawingContext.DrawRectangle(
                        Brushes.Transparent,
                        new Pen(s_lineBrush, lineThickness),
                        faceRect);

                    if (text != "")
                    {
                        FormattedText ft = new FormattedText(text,
                            CultureInfo.CurrentCulture, FlowDirection.LeftToRight, s_typeface,
                            16 * annotationScale, Brushes.Black);

                        var pad = 3 * annotationScale;

                        var ypad = pad;
                        var xpad = pad + 4 * annotationScale;
                        var origin = new System.Windows.Point(
                            faceRect.Left + xpad - lineThickness / 2,
                            faceRect.Top - ft.Height - ypad + lineThickness / 2);
                        var rect = ft.BuildHighlightGeometry(origin).GetRenderBounds(null);
                        rect.Inflate(xpad, ypad);

                        drawingContext.DrawRectangle(s_lineBrush, null, rect);
                        drawingContext.DrawText(ft, origin);
                    }
                }
                dataTable.dataGrid.ItemsSource = personData;
                imageWall.UpdateCanvas();
            };

            return DrawOverlay(baseImage, drawAction);
        }
    }
}
