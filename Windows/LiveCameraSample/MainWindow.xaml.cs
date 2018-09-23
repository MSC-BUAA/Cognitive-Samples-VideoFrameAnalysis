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
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using Newtonsoft.Json;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using VideoFrameAnalyzer;
using Common = Microsoft.ProjectOxford.Common;
using FaceAPI = Microsoft.ProjectOxford.Face;
using VisionAPI = Microsoft.ProjectOxford.Vision;
using Microsoft.ProjectOxford.Face;
using Microsoft.ProjectOxford.Face.Controls;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Media;
using ClientContract = Microsoft.ProjectOxford.Face.Contract;
using LiveCameraSample;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace LiveCameraSample
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : System.Windows.Window
    {
        private FaceAPI.FaceServiceClient _faceClient = null;
        private VisionAPI.VisionServiceClient _visionClient = null;
        private readonly FrameGrabber<LiveCameraResult> _grabber = null;
        private static readonly ImageEncodingParam[] s_jpegParams = {
            new ImageEncodingParam(ImwriteFlags.JpegQuality, 60)
        };
        private readonly CascadeClassifier _localFaceDetector = new CascadeClassifier();
        private bool _fuseClientRemoteResults;
        private LiveCameraResult _latestResultsToDisplay = null;
        private AppMode _mode;
        private DateTime _startTime;

        public enum AppMode
        {
            Faces,
            Emotions,
            EmotionsWithClientFaceDetect,
            Tags,
            Celebrities
        }

        RealTimeOut outWin = new RealTimeOut();


        public MainWindow()
        {
            InitializeComponent();

            outWin.Show();

            

            // Create grabber. 
            _grabber = new FrameGrabber<LiveCameraResult>();

            // Set up a listener for when the client receives a new frame.
            _grabber.NewFrameProvided += (s, e) =>
            {
                if (_mode == AppMode.EmotionsWithClientFaceDetect)
                {
                    // Local face detection. 
                    var rects = _localFaceDetector.DetectMultiScale(e.Frame.Image);
                    // Attach faces to frame. 
                    e.Frame.UserData = rects;
                }

                // The callback may occur on a different thread, so we must use the
                // MainWindow.Dispatcher when manipulating the UI. 
                this.Dispatcher.BeginInvoke((Action)(() =>
                {
                    // Display the image in the left pane.
                    LeftImage.Source = e.Frame.Image.ToBitmapSource();

                    // If we're fusing client-side face detection with remote analysis, show the
                    // new frame now with the most recent analysis available. 
                    if (_fuseClientRemoteResults)
                    {
                        outWin.RightImage.Source = VisualizeResult(e.Frame);
                    }
                }));

                // See if auto-stop should be triggered. 
                if (Properties.Settings.Default.AutoStopEnabled && (DateTime.Now - _startTime) > Properties.Settings.Default.AutoStopTime)
                {
                    _grabber.StopProcessingAsync();
                }
            };

            // Set up a listener for when the client receives a new result from an API call. 
            _grabber.NewResultAvailable += (s, e) =>
            {
                this.Dispatcher.BeginInvoke((Action)(() =>
                {
                    if (e.TimedOut)
                    {
                        MessageArea.Text = "API call timed out.";
                    }
                    else if (e.Exception != null)
                    {
                        string apiName = "";
                        string message = e.Exception.Message;
                        var faceEx = e.Exception as FaceAPI.FaceAPIException;
                        var emotionEx = e.Exception as Common.ClientException;
                        var visionEx = e.Exception as VisionAPI.ClientException;
                        if (faceEx != null)
                        {
                            apiName = "Face";
                            message = faceEx.ErrorMessage;
                        }
                        else if (emotionEx != null)
                        {
                            apiName = "Emotion";
                            message = emotionEx.Error.Message;
                        }
                        else if (visionEx != null)
                        {
                            apiName = "Computer Vision";
                            message = visionEx.Error.Message;
                        }
                        MessageArea.Text = string.Format("{0} API call failed on frame {1}. Exception: {2}", apiName, e.Frame.Metadata.Index, message);

                        _latestResultsToDisplay = e.Analysis;

                        // Display the image and visualization in the right pane. 
                        if (!_fuseClientRemoteResults) {
                            outWin.RightImage.Source = VisualizeResult(e.Frame);
                        }

                    }
                    else
                    {
                        _latestResultsToDisplay = e.Analysis;

                        // Display the image and visualization in the right pane. 
                        if (!_fuseClientRemoteResults)
                        {
                            outWin.RightImage.Source = VisualizeResult(e.Frame);
                        }
                    }
                }));
            };

            // Create local face detector. 
            _localFaceDetector.Load("Data/haarcascade_frontalface_alt2.xml");
        }

        private static string sampleGroupId = Guid.NewGuid().ToString();
        public string GroupId
        {
            get
            {
                return sampleGroupId;
            }

            set
            {
                sampleGroupId = value;
            }
        }

        public int MaxImageSize
        {
            get
            {
                return 300;
            }
        }

        public class Person : INotifyPropertyChanged
        {
            #region Fields

            /// <summary>
            /// Person's faces from database
            /// </summary>
            private ObservableCollection<Face> _faces = new ObservableCollection<Face>();

            /// <summary>
            /// Person's id
            /// </summary>
            private string _personId;

            /// <summary>
            /// Person's name
            /// </summary>
            private string _personName;

            #endregion Fields

            #region Events

            /// <summary>
            /// Implement INotifyPropertyChanged interface
            /// </summary>
            public event PropertyChangedEventHandler PropertyChanged;

            #endregion Events

            #region Properties

            /// <summary>
            /// Gets or sets person's faces from database
            /// </summary>
            public ObservableCollection<Face> Faces
            {
                get
                {
                    return _faces;
                }

                set
                {
                    _faces = value;
                    if (PropertyChanged != null)
                    {
                        PropertyChanged(this, new PropertyChangedEventArgs("Faces"));
                    }
                }
            }

            /// <summary>
            /// Gets or sets person's id
            /// </summary>
            public string PersonId
            {
                get
                {
                    return _personId;
                }

                set
                {
                    _personId = value;
                    if (PropertyChanged != null)
                    {
                        PropertyChanged(this, new PropertyChangedEventArgs("PersonId"));
                    }
                }
            }

            /// <summary>
            /// Gets or sets person's name
            /// </summary>
            public string PersonName
            {
                get
                {
                    return _personName;
                }

                set
                {
                    _personName = value;
                    if (PropertyChanged != null)
                    {
                        PropertyChanged(this, new PropertyChangedEventArgs("PersonName"));
                    }
                }
            }

            #endregion Properties
        }
        /// <summary>
        /// Faces to identify
        /// </summary>
        private ObservableCollection<Face> _faces = new ObservableCollection<Face>();

        /// <summary>
        /// Person database
        /// </summary>
        private ObservableCollection<Person> _persons = new ObservableCollection<Person>();

        public ObservableCollection<Face> TargetFaces
        {
            get
            {
                return _faces;
            }
        }

        public ObservableCollection<Person> Persons
        {
            get
            {
                return _persons;
            }
        }

        /// <summary> Function which submits a frame to the Face API. </summary>
        /// <param name="frame"> The video frame to submit. </param>
        /// <returns> A <see cref="Task{LiveCameraResult}"/> representing the asynchronous API call,
        ///     and containing the faces returned by the API. </returns>
        private async Task<LiveCameraResult> FacesAnalysisFunction(VideoFrame frame)
        {
            // Encode image. 
            var jpg = frame.Image.ToMemoryStream(".jpg", s_jpegParams);
            // Submit image to API. 
            var attrs = new List<FaceAPI.FaceAttributeType> {
                FaceAPI.FaceAttributeType.Age,
                FaceAPI.FaceAttributeType.Gender,
                FaceAPI.FaceAttributeType.HeadPose
            };
            Properties.Settings.Default.FaceAPICallCount++;
            var faces = await _faceClient.DetectAsync(jpg, returnFaceAttributes: attrs);
            var imageInfo = new Tuple<int, int>(frame.Image.Width, frame.Image.Height);
            foreach (var face in UIHelper.CalculateFaceRectangleForRendering(faces, MaxImageSize, imageInfo))
            {
                TargetFaces.Add(face);
            }
            //this.GroupId = "caeeeec7-10eb-485b-be2b-dd61513b73c5";
            var identifyResult = await _faceClient.IdentifyAsync(faces.Select(ff => ff.FaceId).ToArray(), largePersonGroupId: this.GroupId);
            for (int idx = 0; idx < faces.Length; idx++)
            {
                // Update identification result for rendering
                var face = TargetFaces[idx];
                var res = identifyResult[idx];
                if (res.Candidates.Length > 0 && Persons.Any(p => p.PersonId == res.Candidates[0].PersonId.ToString()))
                {
                    face.PersonName = Persons.Where(p => p.PersonId == res.Candidates[0].PersonId.ToString()).First().PersonName;
                }
                else
                {
                    face.PersonName = "Unknown";
                }
            }
            // Count the API call. 
            Properties.Settings.Default.FaceAPICallCount++;
            // Output. 
            return new LiveCameraResult { Faces = faces, TargetFaces = TargetFaces };
        }

        /// <summary> Function which submits a frame to the Emotion API. </summary>
        /// <param name="frame"> The video frame to submit. </param>
        /// <returns> A <see cref="Task{LiveCameraResult}"/> representing the asynchronous API call,
        ///     and containing the emotions returned by the API. </returns>
        private async Task<LiveCameraResult> EmotionAnalysisFunction(VideoFrame frame)
        {
            // Encode image. 
            var jpg = frame.Image.ToMemoryStream(".jpg", s_jpegParams);
            // Submit image to API. 
            FaceAPI.Contract.Face[] faces = null;

            // See if we have local face detections for this image.
            var localFaces = (OpenCvSharp.Rect[])frame.UserData;
            if (localFaces == null || localFaces.Count() > 0)
            {
                // If localFaces is null, we're not performing local face detection.
                // Use Cognigitve Services to do the face detection.
                Properties.Settings.Default.FaceAPICallCount++;
                faces = await _faceClient.DetectAsync(
                    jpg,
                    /* returnFaceId= */ false,
                    /* returnFaceLandmarks= */ false,
                    new FaceAPI.FaceAttributeType[1] { FaceAPI.FaceAttributeType.Emotion });
            }
            else
            {
                // Local face detection found no faces; don't call Cognitive Services.
                faces = new FaceAPI.Contract.Face[0];
            }

            // Output. 
            return new LiveCameraResult
            {
                Faces = faces.Select(e => CreateFace(e.FaceRectangle)).ToArray(),
                // Extract emotion scores from results. 
                EmotionScores = faces.Select(e => e.FaceAttributes.Emotion).ToArray()
            };
        }

        /// <summary> Function which submits a frame to the Computer Vision API for tagging. </summary>
        /// <param name="frame"> The video frame to submit. </param>
        /// <returns> A <see cref="Task{LiveCameraResult}"/> representing the asynchronous API call,
        ///     and containing the tags returned by the API. </returns>
        private async Task<LiveCameraResult> TaggingAnalysisFunction(VideoFrame frame)
        {
            // Encode image. 
            var jpg = frame.Image.ToMemoryStream(".jpg", s_jpegParams);
            // Submit image to API. 
            var analysis = await _visionClient.GetTagsAsync(jpg);
            // Count the API call. 
            Properties.Settings.Default.VisionAPICallCount++;
            // Output. 
            return new LiveCameraResult { Tags = analysis.Tags };
        }

        /// <summary> Function which submits a frame to the Computer Vision API for celebrity
        ///     detection. </summary>
        /// <param name="frame"> The video frame to submit. </param>
        /// <returns> A <see cref="Task{LiveCameraResult}"/> representing the asynchronous API call,
        ///     and containing the celebrities returned by the API. </returns>
        private async Task<LiveCameraResult> CelebrityAnalysisFunction(VideoFrame frame)
        {
            // Encode image. 
            var jpg = frame.Image.ToMemoryStream(".jpg", s_jpegParams);
            // Submit image to API. 
            var result = await _visionClient.AnalyzeImageInDomainAsync(jpg, "celebrities");
            // Count the API call. 
            Properties.Settings.Default.VisionAPICallCount++;
            // Output. 
            var celebs = JsonConvert.DeserializeObject<CelebritiesResult>(result.Result.ToString()).Celebrities;
            return new LiveCameraResult
            {
                // Extract face rectangles from results. 
                Faces = celebs.Select(c => CreateFace(c.FaceRectangle)).ToArray(),
                // Extract celebrity names from results. 
                CelebrityNames = celebs.Select(c => c.Name).ToArray()
            };
        }

        private BitmapSource VisualizeResult(VideoFrame frame)
        {
            // Draw any results on top of the image. 
            BitmapSource visImage = frame.Image.ToBitmapSource();

            var result = _latestResultsToDisplay;

            if (result != null)
            {
                // See if we have local face detections for this image.
                var clientFaces = (OpenCvSharp.Rect[])frame.UserData;
                if (clientFaces != null && result.Faces != null)
                {
                    // If so, then the analysis results might be from an older frame. We need to match
                    // the client-side face detections (computed on this frame) with the analysis
                    // results (computed on the older frame) that we want to display. 
                    MatchAndReplaceFaceRectangles(result.Faces, clientFaces);
                }

                visImage = Visualization.DrawFaces(visImage, result.Faces, result.TargetFaces, result.EmotionScores, result.CelebrityNames);
                visImage = Visualization.DrawTags(visImage, result.Tags);
            }

            return visImage;
        }

        /// <summary> Populate CameraList in the UI, once it is loaded. </summary>
        /// <param name="sender"> Source of the event. </param>
        /// <param name="e">      Routed event information. </param>
        private void CameraList_Loaded(object sender, RoutedEventArgs e)
        {
            int numCameras = _grabber.GetNumCameras();

            if (numCameras == 0)
            {
                MessageArea.Text = "No cameras found!";
            }

            var comboBox = sender as ComboBox;
            comboBox.ItemsSource = Enumerable.Range(0, numCameras).Select(i => string.Format("Camera {0}", i + 1));
            comboBox.SelectedIndex = 0;
        }

        /// <summary> Populate ModeList in the UI, once it is loaded. </summary>
        /// <param name="sender"> Source of the event. </param>
        /// <param name="e">      Routed event information. </param>
        private void ModeList_Loaded(object sender, RoutedEventArgs e)
        {
            var modes = (AppMode[])Enum.GetValues(typeof(AppMode));

            var comboBox = sender as ComboBox;
            comboBox.ItemsSource = modes.Select(m => m.ToString());
            comboBox.SelectedIndex = 0;
        }

        private void ModeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Disable "most-recent" results display. 
            _fuseClientRemoteResults = false;

            var comboBox = sender as ComboBox;
            var modes = (AppMode[])Enum.GetValues(typeof(AppMode));
            _mode = modes[comboBox.SelectedIndex];
            switch (_mode)
            {
                case AppMode.Faces:
                    _grabber.AnalysisFunction = FacesAnalysisFunction;
                    break;
                case AppMode.Emotions:
                    _grabber.AnalysisFunction = EmotionAnalysisFunction;
                    break;
                case AppMode.EmotionsWithClientFaceDetect:
                    // Same as Emotions, except we will display the most recent faces combined with
                    // the most recent API results. 
                    _grabber.AnalysisFunction = EmotionAnalysisFunction;
                    _fuseClientRemoteResults = true;
                    break;
                case AppMode.Tags:
                    _grabber.AnalysisFunction = TaggingAnalysisFunction;
                    break;
                case AppMode.Celebrities:
                    _grabber.AnalysisFunction = CelebrityAnalysisFunction;
                    break;
                default:
                    _grabber.AnalysisFunction = null;
                    break;
            }
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (!CameraList.HasItems)
            {
                MessageArea.Text = "No cameras found; cannot start processing";
                return;
            }

            // Clean leading/trailing spaces in API keys. 
            Properties.Settings.Default.FaceAPIKey = Properties.Settings.Default.FaceAPIKey.Trim();
            Properties.Settings.Default.VisionAPIKey = Properties.Settings.Default.VisionAPIKey.Trim();

            // Create API clients. 
            _visionClient = new VisionAPI.VisionServiceClient(Properties.Settings.Default.VisionAPIKey, Properties.Settings.Default.VisionAPIHost);

            // How often to analyze. 
            _grabber.TriggerAnalysisOnInterval(Properties.Settings.Default.AnalysisInterval);

            // Reset message. 
            MessageArea.Text = "";

            // Record start time, for auto-stop
            _startTime = DateTime.Now;

            await _grabber.StartProcessingCameraAsync(CameraList.SelectedIndex);
        }

        private async void StopButton_Click(object sender, RoutedEventArgs e)
        {
            await _grabber.StopProcessingAsync();
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            SettingsPanel.Visibility = 1 - SettingsPanel.Visibility;
        }

        private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            //SettingsPanel.Visibility = Visibility.Hidden;
            Properties.Settings.Default.Save();
            Log("Settings Saved.");
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri));
            e.Handled = true;
        }

        private FaceAPI.Contract.Face CreateFace(FaceAPI.Contract.FaceRectangle rect)
        {
            return new FaceAPI.Contract.Face
            {
                FaceRectangle = new FaceAPI.Contract.FaceRectangle
                {
                    Left = rect.Left,
                    Top = rect.Top,
                    Width = rect.Width,
                    Height = rect.Height
                }
            };
        }

        private FaceAPI.Contract.Face CreateFace(VisionAPI.Contract.FaceRectangle rect)
        {
            return new FaceAPI.Contract.Face
            {
                FaceRectangle = new FaceAPI.Contract.FaceRectangle
                {
                    Left = rect.Left,
                    Top = rect.Top,
                    Width = rect.Width,
                    Height = rect.Height
                }
            };
        }

        private FaceAPI.Contract.Face CreateFace(Common.Rectangle rect)
        {
            return new FaceAPI.Contract.Face
            {
                FaceRectangle = new FaceAPI.Contract.FaceRectangle
                {
                    Left = rect.Left,
                    Top = rect.Top,
                    Width = rect.Width,
                    Height = rect.Height
                }
            };
        }

        private void MatchAndReplaceFaceRectangles(FaceAPI.Contract.Face[] faces, OpenCvSharp.Rect[] clientRects)
        {
            // Use a simple heuristic for matching the client-side faces to the faces in the
            // results. Just sort both lists left-to-right, and assume a 1:1 correspondence. 

            // Sort the faces left-to-right. 
            var sortedResultFaces = faces
                .OrderBy(f => f.FaceRectangle.Left + 0.5 * f.FaceRectangle.Width)
                .ToArray();

            // Sort the clientRects left-to-right.
            var sortedClientRects = clientRects
                .OrderBy(r => r.Left + 0.5 * r.Width)
                .ToArray();

            // Assume that the sorted lists now corrrespond directly. We can simply update the
            // FaceRectangles in sortedResultFaces, because they refer to the same underlying
            // objects as the input "faces" array. 
            for (int i = 0; i < Math.Min(faces.Length, clientRects.Length); i++)
            {
                // convert from OpenCvSharp rectangles
                OpenCvSharp.Rect r = sortedClientRects[i];
                sortedResultFaces[i].FaceRectangle = new FaceAPI.Contract.FaceRectangle { Left = r.Left, Top = r.Top, Width = r.Width, Height = r.Height };
            }
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }
        private ImageSource _selectedFile;
        public event PropertyChangedEventHandler PropertyChanged;
        public ImageSource SelectedFile {
            get {
                return _selectedFile;
            }

            set {
                _selectedFile = value;
                if (PropertyChanged != null) {
                    PropertyChanged(this, new PropertyChangedEventArgs("SelectedFile"));
                }
            }
        }

        private int _maxConcurrentProcesses = 4;

        private async void FolderPicker_Click(object sender, RoutedEventArgs e) {
            _faceClient = new FaceAPI.FaceServiceClient(Properties.Settings.Default.FaceAPIKey, Properties.Settings.Default.FaceAPIHost);
            bool groupExists = false;

            MainWindow mainWindow = System.Windows.Window.GetWindow(this) as MainWindow;
            var faceServiceClient = _faceClient;

            // Test whether the group already exists
            try {
                Log("Request: Group {0} will be used to build a person database. Checking whether the group exists.", this.GroupId);

                await faceServiceClient.GetLargePersonGroupAsync(this.GroupId);
                groupExists = true;
                Log("Response: Group {0} exists.", this.GroupId);
            } catch (FaceAPIException ex) {
                if (ex.ErrorCode != "LargePersonGroupNotFound") {
                    Log("Response: {0}. {1}", ex.ErrorCode, ex.ErrorMessage);
                    return;
                } else {
                    Log("Response: Group {0} did not exist previously.", this.GroupId);
                }
            }

            if (groupExists) {
                var cleanGroup = System.Windows.MessageBox.Show(string.Format("Requires a clean up for group \"{0}\" before setting up a new person database. Click OK to proceed, group \"{0}\" will be cleared.", this.GroupId), "Warning", MessageBoxButton.OKCancel);
                if (cleanGroup == MessageBoxResult.OK) {
                    await faceServiceClient.DeleteLargePersonGroupAsync(this.GroupId);
                    this.GroupId = Guid.NewGuid().ToString();
                } else {
                    return;
                }
            }

            // Show folder picker
            //System.Windows.Forms.FolderBrowserDialog dlg = new System.Windows.Forms.FolderBrowserDialog();
            //var result = dlg.ShowDialog();

            // Set the suggestion count is intent to minimum the data preparation step only,
            // it's not corresponding to service side constraint
            const int SuggestionCount = 15;

            if (personPath.Text != "") {
                // User picked a root person database folder
                // Clear person database
                Persons.Clear();
                TargetFaces.Clear();
                SelectedFile = null;
                //IdentifyButton.IsEnabled = false;

                // Call create large person group REST API
                // Create large person group API call will failed if group with the same name already exists
                Log("Request: Creating group \"{0}\"", this.GroupId);
                try {
                    await faceServiceClient.CreateLargePersonGroupAsync(this.GroupId, this.GroupId);
                    Log("Response: Success. Group \"{0}\" created.", this.GroupId);
                } catch (FaceAPIException ex) {
                    Log("Response: {0}. {1}", ex.ErrorCode, ex.ErrorMessage);
                    return;
                }

                int processCount = 0;
                bool forceContinue = false;

                Log("Request: Preparing faces for identification, detecting faces in chosen folder.");

                // Enumerate top level directories, each directory contains one person's images
                int invalidImageCount = 0;
                foreach (var dir in System.IO.Directory.EnumerateDirectories(personPath.Text)) {
                    var tasks = new List<Task>();
                    var tag = System.IO.Path.GetFileName(dir);
                    Person p = new Person();
                    p.PersonName = tag;

                    var faces = new ObservableCollection<Face>();
                    p.Faces = faces;

                    // Call create person REST API, the new create person id will be returned
                    Log("Request: Creating person \"{0}\"", p.PersonName);
                    p.PersonId = (await faceServiceClient.CreatePersonInLargePersonGroupAsync(this.GroupId, p.PersonName)).PersonId.ToString();
                    Log("Response: Success. Person \"{0}\" (PersonID:{1}) created. Please wait for training.", p.PersonName, p.PersonId);

                    string img;
                    // Enumerate images under the person folder, call detection
                    var imageList =
                    new ConcurrentBag<string>(
                        Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
                            .Where(s => s.ToLower().EndsWith(".jpg") || s.ToLower().EndsWith(".png") || s.ToLower().EndsWith(".bmp") || s.ToLower().EndsWith(".gif")));

                    while (imageList.TryTake(out img)) {
                        tasks.Add(Task.Factory.StartNew(
                            async (obj) =>
                            {
                                var imgPath = obj as string;

                                using (var fStream = File.OpenRead(imgPath)) {
                                    try {
                                        // Update person faces on server side
                                        var persistFace = await faceServiceClient.AddPersonFaceInLargePersonGroupAsync(this.GroupId, Guid.Parse(p.PersonId), fStream, imgPath);
                                        return new Tuple<string, ClientContract.AddPersistedFaceResult>(imgPath, persistFace);
                                    } catch (FaceAPIException ex) {
                                        // if operation conflict, retry.
                                        if (ex.ErrorCode.Equals("ConcurrentOperationConflict")) {
                                            imageList.Add(imgPath);
                                            return null;
                                        }
                                        // if operation cause rate limit exceed, retry.
                                        else if (ex.ErrorCode.Equals("RateLimitExceeded")) {
                                            imageList.Add(imgPath);
                                            return null;
                                        } else if (ex.ErrorMessage.Contains("more than 1 face in the image.")) {
                                            Interlocked.Increment(ref invalidImageCount);
                                        }
                                        // Here we simply ignore all detection failure in this sample
                                        // You may handle these exceptions by check the Error.Error.Code and Error.Message property for ClientException object
                                        return new Tuple<string, ClientContract.AddPersistedFaceResult>(imgPath, null);
                                    }
                                }
                            },
                            img).Unwrap().ContinueWith((detectTask) =>
                            {
                                // Update detected faces for rendering
                                var detectionResult = detectTask?.Result;
                                if (detectionResult == null || detectionResult.Item2 == null) {
                                    return;
                                }

                                this.Dispatcher.Invoke(
                                    new Action<ObservableCollection<Face>, string, ClientContract.AddPersistedFaceResult>(UIHelper.UpdateFace),
                                    faces,
                                    detectionResult.Item1,
                                    detectionResult.Item2);
                            }));
                        if (processCount >= SuggestionCount && !forceContinue) {
                            var continueProcess = System.Windows.Forms.MessageBox.Show("The images loaded have reached the recommended count, may take long time if proceed. Would you like to continue to load images?", "Warning", System.Windows.Forms.MessageBoxButtons.YesNo);
                            if (continueProcess == System.Windows.Forms.DialogResult.Yes) {
                                forceContinue = true;
                            } else {
                                break;
                            }
                        }

                        if (tasks.Count >= _maxConcurrentProcesses || imageList.IsEmpty) {
                            await Task.WhenAll(tasks);
                            tasks.Clear();
                        }
                    }

                    Persons.Add(p);
                }
                if (invalidImageCount > 0) {
                    Log("Warning: more or less than one face is detected in {0} images, can not add to face list.", invalidImageCount);
                }
                Log("Response: Success. Total {0} faces are detected.", Persons.Sum(p => p.Faces.Count));

                try {
                    // Start train large person group
                    Log("Request: Training group \"{0}\"", this.GroupId);
                    await faceServiceClient.TrainLargePersonGroupAsync(this.GroupId);

                    // Wait until train completed
                    while (true) {
                        await Task.Delay(1000);
                        var status = await faceServiceClient.GetLargePersonGroupTrainingStatusAsync(this.GroupId);
                        Log("Response: {0}. Group \"{1}\" training process is {2}", "Success", this.GroupId, status.Status);
                        if (status.Status != ClientContract.Status.Running) {
                            break;
                        }
                    }
                    //IdentifyButton.IsEnabled = true;
                } catch (FaceAPIException ex) {
                    Log("Response: {0}. {1}", ex.ErrorCode, ex.ErrorMessage);
                }
            }
            GC.Collect();
        }

        public void Log(string format, params object[] args) {
            MessageArea.Text = string.Format(format, args);
        }

        private void FullScreen(object sender, RoutedEventArgs e) {
            ExpendMethod.GoFullscreen(outWin);
        }

        private void UnFullScreen(object sender, RoutedEventArgs e) {
            ExpendMethod.ExitFullscreen(outWin);
        }

        private void Button_Click(object sender, RoutedEventArgs e) {
            System.Windows.Forms.FolderBrowserDialog dlg = new System.Windows.Forms.FolderBrowserDialog();
            var result = dlg.ShowDialog();

            if (result == System.Windows.Forms.DialogResult.OK) {

                personPath.Text = dlg.SelectedPath;
            }
        }

        private void Button_Click_1(object sender, RoutedEventArgs e) {
            outWin = new RealTimeOut();
            outWin.Show();
        }

        public static System.Drawing.Bitmap BitmapSourceToBitmap2(BitmapSource srs) {
            int width = srs.PixelWidth;
            int height = srs.PixelHeight;
            int stride = width * ((srs.Format.BitsPerPixel + 7) / 8);
            IntPtr ptr = IntPtr.Zero;
            try {
                ptr = Marshal.AllocHGlobal(height * stride);
                srs.CopyPixels(new Int32Rect(0, 0, width, height), ptr, height * stride, stride);
                using (var btm = new System.Drawing.Bitmap(width, height, stride, System.Drawing.Imaging.PixelFormat.Format1bppIndexed, ptr)) {
                    // Clone the bitmap so that we can dispose it and
                    // release the unmanaged memory at ptr
                    return new System.Drawing.Bitmap(btm);
                }
            } finally {
                if (ptr != IntPtr.Zero)
                    Marshal.FreeHGlobal(ptr);
            }
        }

        private System.Drawing.Image ImageWpfToGDI(System.Windows.Media.ImageSource image) {
            MemoryStream ms = new MemoryStream();
            var encoder = new System.Windows.Media.Imaging.BmpBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image as System.Windows.Media.Imaging.BitmapSource));
            encoder.Save(ms);
            ms.Flush();
            return System.Drawing.Image.FromStream(ms);
        }

        private void QRCode(object sender, RoutedEventArgs e) {
            try {
                System.Drawing.Image img = new Bitmap("template.png");
                Graphics g = Graphics.FromImage(img);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                System.Drawing.Point[] destPoints1 = {
                         new System.Drawing.Point(25, 200), //图片左上点
                         new System.Drawing.Point(725, 200), //图片右上点
                         new System.Drawing.Point(25, 750),    //图片左下点
                };

                System.Drawing.Image outImg = ImageWpfToGDI(outWin.RightImage.Source);
                g.DrawImage(outImg, destPoints1);

                img.Save(QRCodePath.Text);
                Log("Saved as {0}", QRCodePath.Text);
                img.Dispose();
                g.Dispose();
            } catch (Exception) {

                Log("Failed. Start Camera first.");
            }
            
        }
    }
}




