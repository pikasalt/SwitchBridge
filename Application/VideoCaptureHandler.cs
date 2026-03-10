using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using DirectShowLib;

namespace SwitchBridge
{
    /// <summary>
    /// Information about an available video capture device.
    /// </summary>
    public class CaptureDeviceInfo
    {
        public string Name { get; set; } = "";
        public string DevicePath { get; set; } = "";
        public int Index { get; set; }

        public override string ToString() => Name;
    }

    /// <summary>
    /// Handles video capture from DirectShow devices (capture cards, webcams, etc.)
    /// using the SampleGrabber filter to capture frames for display.
    /// </summary>
    public class VideoCaptureHandler : IDisposable, ISampleGrabberCB
    {
        private IFilterGraph2? _graph;
        private IMediaControl? _mediaControl;
        private ISampleGrabber? _sampleGrabber;
        private IBaseFilter? _captureFilter;
        private IBaseFilter? _grabberFilter;
        private IBaseFilter? _nullRenderer;

        private int _width;
        private int _height;
        private int _stride;
        private byte[]? _frameBuffer;
        private readonly object _frameLock = new();
        private bool _newFrame = false;

        public bool IsCapturing { get; private set; }
        public int FrameWidth => _width;
        public int FrameHeight => _height;

        /// <summary>
        /// Enumerate all available video capture devices.
        /// </summary>
        public static List<CaptureDeviceInfo> GetDevices()
        {
            var devices = new List<CaptureDeviceInfo>();

            var devEnum = new CreateDevEnum() as ICreateDevEnum;
            if (devEnum == null) return devices;

            int hr = devEnum.CreateClassEnumerator(
                FilterCategory.VideoInputDevice, out IEnumMoniker enumMoniker, 0);

            if (hr != 0 || enumMoniker == null) return devices;

            var moniker = new IMoniker[1];
            int index = 0;

            while (enumMoniker.Next(1, moniker, IntPtr.Zero) == 0)
            {
                moniker[0].BindToStorage(null, null, typeof(IPropertyBag).GUID, out object bagObj);
                var bag = (IPropertyBag)bagObj;

                string name = "Unknown Device";
                string path = "";

                try
                {
                    bag.Read("FriendlyName", out object nameObj, null);
                    if (nameObj != null) name = nameObj.ToString()!;
                }
                catch { }

                try
                {
                    bag.Read("DevicePath", out object pathObj, null);
                    if (pathObj != null) path = pathObj.ToString()!;
                }
                catch { }

                devices.Add(new CaptureDeviceInfo
                {
                    Name = name,
                    DevicePath = path,
                    Index = index++
                });

                Marshal.ReleaseComObject(moniker[0]);
            }

            Marshal.ReleaseComObject(enumMoniker);
            return devices;
        }

        /// <summary>
        /// Start capturing from the specified device.
        /// </summary>
        public bool StartCapture(CaptureDeviceInfo device)
        {
            StopCapture();

            try
            {
                // Create the filter graph
                _graph = (IFilterGraph2)new FilterGraph();
                _mediaControl = (IMediaControl)_graph;

                // Find and add the capture device
                var devEnum = new CreateDevEnum() as ICreateDevEnum;
                devEnum!.CreateClassEnumerator(
                    FilterCategory.VideoInputDevice, out IEnumMoniker enumMoniker, 0);

                var moniker = new IMoniker[1];
                int idx = 0;
                while (enumMoniker.Next(1, moniker, IntPtr.Zero) == 0)
                {
                    if (idx == device.Index)
                    {
                        moniker[0].BindToObject(null, null, typeof(IBaseFilter).GUID, out object filterObj);
                        _captureFilter = (IBaseFilter)filterObj;
                        _graph.AddFilter(_captureFilter, "Capture");
                        Marshal.ReleaseComObject(moniker[0]);
                        break;
                    }
                    Marshal.ReleaseComObject(moniker[0]);
                    idx++;
                }

                Marshal.ReleaseComObject(enumMoniker);

                if (_captureFilter == null) return false;

                // Create and configure the sample grabber
                _sampleGrabber = (ISampleGrabber)new SampleGrabber();
                _grabberFilter = (IBaseFilter)_sampleGrabber;

                var mediaType = new AMMediaType
                {
                    majorType = MediaType.Video,
                    subType = MediaSubType.RGB24,
                    formatType = FormatType.VideoInfo
                };

                _sampleGrabber.SetMediaType(mediaType);
                DsUtils.FreeAMMediaType(mediaType);

                _graph.AddFilter(_grabberFilter, "Grabber");

                // Add null renderer (we handle display ourselves)
                _nullRenderer = (IBaseFilter)new NullRenderer();
                _graph.AddFilter(_nullRenderer, "Null Renderer");

                // Connect: Capture -> Grabber -> Null Renderer
                var captureOutPin = DsFindPin.ByDirection(_captureFilter, PinDirection.Output, 0);
                var grabberInPin = DsFindPin.ByDirection(_grabberFilter, PinDirection.Input, 0);
                var grabberOutPin = DsFindPin.ByDirection(_grabberFilter, PinDirection.Output, 0);
                var nullInPin = DsFindPin.ByDirection(_nullRenderer, PinDirection.Input, 0);

                _graph.Connect(captureOutPin, grabberInPin);
                _graph.Connect(grabberOutPin, nullInPin);

                // Get the actual video dimensions
                var grabMediaType = new AMMediaType();
                _sampleGrabber.GetConnectedMediaType(grabMediaType);

                if (grabMediaType.formatType == FormatType.VideoInfo)
                {
                    var header = (VideoInfoHeader)Marshal.PtrToStructure(
                        grabMediaType.formatPtr, typeof(VideoInfoHeader))!;
                    _width = header.BmiHeader.Width;
                    _height = header.BmiHeader.Height;
                    _stride = _width * 3; // RGB24
                    _frameBuffer = new byte[_stride * _height];
                }

                DsUtils.FreeAMMediaType(grabMediaType);

                // Configure grabber for callback mode
                _sampleGrabber.SetBufferSamples(false);
                _sampleGrabber.SetOneShot(false);
                _sampleGrabber.SetCallback(this, 1); // BufferCB mode

                // Start the graph
                _mediaControl.Run();
                IsCapturing = true;

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Capture start error: {ex.Message}");
                StopCapture();
                return false;
            }
        }

        /// <summary>
        /// Stop capturing.
        /// </summary>
        public void StopCapture()
        {
            IsCapturing = false;

            try { _mediaControl?.Stop(); } catch { }

            if (_nullRenderer != null) { Marshal.ReleaseComObject(_nullRenderer); _nullRenderer = null; }
            if (_grabberFilter != null) { Marshal.ReleaseComObject(_grabberFilter); _grabberFilter = null; }
            if (_captureFilter != null) { Marshal.ReleaseComObject(_captureFilter); _captureFilter = null; }
            if (_graph != null) { Marshal.ReleaseComObject(_graph); _graph = null; }

            _sampleGrabber = null;
            _mediaControl = null;
        }

        /// <summary>
        /// Get the latest captured frame as a Bitmap. Returns null if no new frame.
        /// </summary>
        public Bitmap? GetFrame()
        {
            lock (_frameLock)
            {
                if (!_newFrame || _frameBuffer == null || _width <= 0 || _height <= 0)
                    return null;

                _newFrame = false;

                // Create bitmap from RGB24 buffer (DirectShow gives BGR bottom-up)
                var bmp = new Bitmap(_width, _height,
                    System.Drawing.Imaging.PixelFormat.Format24bppRgb);

                var bmpData = bmp.LockBits(
                    new Rectangle(0, 0, _width, _height),
                    System.Drawing.Imaging.ImageLockMode.WriteOnly,
                    System.Drawing.Imaging.PixelFormat.Format24bppRgb);

                // DirectShow RGB24 is bottom-up, flip vertically
                for (int y = 0; y < _height; y++)
                {
                    int srcOffset = (_height - 1 - y) * _stride;
                    IntPtr destPtr = bmpData.Scan0 + (y * bmpData.Stride);
                    Marshal.Copy(_frameBuffer, srcOffset, destPtr, _stride);
                }

                bmp.UnlockBits(bmpData);
                return bmp;
            }
        }

        // ISampleGrabberCB.SampleCB - not used
        public int SampleCB(double sampleTime, IMediaSample pSample)
        {
            return 0;
        }

        // ISampleGrabberCB.BufferCB - called for each frame
        public int BufferCB(double sampleTime, IntPtr pBuffer, int bufferLen)
        {
            if (_frameBuffer == null) return 0;

            lock (_frameLock)
            {
                int copyLen = Math.Min(bufferLen, _frameBuffer.Length);
                Marshal.Copy(pBuffer, _frameBuffer, 0, copyLen);
                _newFrame = true;
            }

            return 0;
        }

        public void Dispose()
        {
            StopCapture();
        }
    }

    // COM class instantiation helpers
    [ComImport, Guid("62BE5D10-60EB-11d0-BD3B-00A0C911CE86")]
    internal class CreateDevEnum { }

    [ComImport, Guid("C1F400A0-3F08-11d3-9F0B-006008039E37")]
    internal class SampleGrabber { }

    [ComImport, Guid("C1F400A4-3F08-11d3-9F0B-006008039E37")]
    internal class NullRenderer { }
}
