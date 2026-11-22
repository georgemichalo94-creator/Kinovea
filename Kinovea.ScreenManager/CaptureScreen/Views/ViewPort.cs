#region License
/*
Copyright © Joan Charmant 2013.
jcharmant@gmail.com 
 
This file is part of Kinovea.

Kinovea is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License version 2 
as published by the Free Software Foundation.

Kinovea is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with Kinovea. If not, see http://www.gnu.org/licenses/.
*/
#endregion
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// Viewport is the core viewer for the image and drawing tools.
    /// This class should be only concerned with display, the actual intelligence being in ViewportController.
    /// Interactions only concerned with moving the image around, zoom window, etc. are handled here.
    /// </summary>
    public partial class Viewport : Control
    {
        #region Members
        private ViewportController controller;
        private bool dblClickZoom = true;
        private bool showRecordingIndicator = true;
        private Size referenceSize;             // Original image size after optional rotation.
        private Rectangle displayRectangle;     // Position and size of the region of the viewport where we draw the image.
        private ImageManipulator imageManipulator = new ImageManipulator();
        
        private ZoomHelper zoomHelper = new ZoomHelper();
        private List<EmbeddedButton> resizers = new List<EmbeddedButton>();
        private static Bitmap resizerBitmap = Properties.Resources.resizer;
        private static int resizerOffset = resizerBitmap.Width / 2;
        private int resizerIndex = -1;

        private RecordingStatus recordingStatus;
        private float recordingStatusProgress = 1.0f;
        private MessageToaster toaster;

        // (Swim 2.27.0) The wall of cameras: the image fitted in the viewport (the display rectangle of the screen stays for the
        // classic screen), its number, the name and the state of its camera over it, its buttons while the mouse is over it; no
        // resizers, no moving of the image, no drawing with the mouse.
        private bool swimWall;
        private bool swimSolo;
        private bool swimHover;
        private int swimHoverButton = -1;
        private SwimWallCamera swimCamera = new SwimWallCamera();
        private int swimStateTick;
        private bool swimStateRead;

        // (Swim 2.29.0) The line of a camera under the water dragged onto the mark of its distance in the pool, and the two marks of a
        // pole clicked for the scale of its image (in pixels of the image).
        private bool swimLineHover;
        private bool swimLineDrag;
        private float swimLineDragValue = float.NaN;
        private bool swimPole;
        private readonly List<PointF> swimPolePoints = new List<PointF>();
        private Point? swimPoleMouse;
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        #endregion
        
        public Viewport(ViewportController controller, bool dblClickZoom = true, bool showRecordingIndicator = true)
        {
            this.controller = controller;
            this.dblClickZoom = dblClickZoom;
            this.showRecordingIndicator = showRecordingIndicator;

            this.BackColor = Color.FromArgb(255, 44, 44, 44);
            this.DoubleBuffered = true;
            this.MouseWheel += Viewport_MouseWheel;
            
            resizers.Add(new EmbeddedButton(resizerBitmap, 0, 0, Cursors.SizeNWSE));
            resizers.Add(new EmbeddedButton(resizerBitmap, 0, 0, Cursors.SizeNESW));
            resizers.Add(new EmbeddedButton(resizerBitmap, 0, 0, Cursors.SizeNWSE));
            resizers.Add(new EmbeddedButton(resizerBitmap, 0, 0, Cursors.SizeNESW));
            
            toaster = new MessageToaster(this);
        }
        
        public void InitializeDisplayRectangle(Rectangle saved, Size referenceSize)
        {
            double aspect = SwimImageAspect;
            this.referenceSize = referenceSize;
            InitializeDisplayRectangle(saved);
            if (displayRectangle != saved)
                controller.UpdateDisplayRectangle(displayRectangle);
            
            ForceZoomValue();

            // (Swim 2.27.0) The place of the screen on the wall of cameras follows the shape of its image.
            if (Math.Abs(aspect - SwimImageAspect) > 0.001 && SwimAspectChanged != null)
                SwimAspectChanged(this, EventArgs.Empty);
        }
        
        public void IncreaseZoom()
        {
            zoomHelper.Increase();
            AfterZoomChanged(displayRectangle.Center());
        }

        public void DecreaseZoom()
        {
            zoomHelper.Decrease();
            AfterZoomChanged(displayRectangle.Center());
        }

        public void ResetZoom()
        {
            zoomHelper.Increase();
            AfterZoomChanged(displayRectangle.Center());
        }
        
        public void SetContextMenu(ContextMenuStrip menuStrip)
        {
            this.ContextMenuStrip = menuStrip;
        }

        public void ToastMessage(string message, int duration)
        {
            toaster.SetDuration(duration);
            toaster.Show(message);
        }

        public void UpdateRecordingIndicator(RecordingStatus status, float progress)
        {
            this.recordingStatus = status;
            this.recordingStatusProgress = progress;
        }

        #region Swim: wall of cameras (2.27.0)
        /// <summary>A double click on the image asks for it alone on the wall (or for all the cameras back).</summary>
        public event EventHandler SwimSoloAsked;

        /// <summary>A button over the image was clicked (SwimCaptureWall.ButtonSolo, ButtonSnapshot, ButtonConfigure).</summary>
        public event Action<int> SwimButtonClicked;

        /// <summary>The shape of the image changed (the place of the screen on the wall follows it).</summary>
        public event EventHandler SwimAspectChanged;

        /// <summary>Where the state of the camera is read for the wall.</summary>
        public Func<SwimWallCamera> SwimWallSource { get; set; }

        /// <summary>Width over height of the image (0 before the first image).</summary>
        public double SwimImageAspect
        {
            get { return referenceSize.Width > 0 && referenceSize.Height > 0 ? (double)referenceSize.Width / referenceSize.Height : 0; }
        }

        public bool SwimWallOn
        {
            get { return swimWall; }
        }

        /// <summary>The wall of cameras on or off: only the image, fitted, with the state of the camera over it.</summary>
        public void SwimWall(bool on)
        {
            if (on == swimWall)
                return;

            swimWall = on;
            swimHover = false;
            swimHoverButton = -1;
            swimStateRead = false;
            swimLineHover = false;
            swimLineDrag = false;
            swimLineDragValue = float.NaN;
            swimPole = false;
            swimPolePoints.Clear();
            BackColor = on ? SwimTheme.Back : Color.FromArgb(255, 44, 44, 44);
            Cursor = on ? Cursors.Default : controller.GetCursor(zoomHelper.Value);
            Invalidate();
        }

        /// <summary>The image is alone on the wall (its button then puts all the cameras back).</summary>
        public void SwimWallSolo(bool solo)
        {
            if (solo == swimSolo)
                return;

            swimSolo = solo;
            Invalidate();
        }

        private float SwimScale
        {
            get { return Math.Max(1f, DeviceDpi / 96f); }
        }

        /// <summary>The place of the image on the wall: as big as it fits, in the middle (the whole viewport before the first image).</summary>
        private Rectangle SwimImageRectangle()
        {
            if (controller.Bitmap == null || referenceSize.Width <= 0 || referenceSize.Height <= 0 || ClientSize.Width <= 0 || ClientSize.Height <= 0)
                return ClientRectangle;

            return SwimCaptureWall.Fit((double)referenceSize.Width / referenceSize.Height, ClientRectangle);
        }

        /// <summary>The state of the camera, read again a few times a second (the stream draws the image many times a second).</summary>
        private SwimWallCamera SwimState()
        {
            int now = Environment.TickCount;
            int age = unchecked(now - swimStateTick);
            if (!swimStateRead || age >= SwimCaptureWall.StateMilliseconds || age < 0)
            {
                swimStateRead = true;
                swimStateTick = now;
                try
                {
                    Func<SwimWallCamera> source = SwimWallSource;
                    SwimWallCamera state = source != null ? source() : null;
                    if (state != null)
                        swimCamera = state;
                }
                catch (Exception e)
                {
                    log.Error("Error while reading the state of the camera for the wall of cameras.", e);
                }
            }

            return swimCamera;
        }

        private bool SwimRecording(SwimWallCamera state)
        {
            return recordingStatus == RecordingStatus.Recording || (state != null && state.Recording);
        }

        /// <summary>The button under the mouse (-1: none, or a screen without a camera, whose buttons are not shown).</summary>
        private int SwimHit(Point location)
        {
            SwimWallCamera state = SwimState();
            if (!state.Loaded || recordingStatus == RecordingStatus.Disconnected)
                return -1;

            return SwimCaptureWall.HitButton(SwimImageRectangle(), SwimScale, location);
        }

        private void PaintWall(Graphics canvas)
        {
            Rectangle image = SwimImageRectangle();
            bool hasImage = controller.Bitmap != null && referenceSize.Width > 0 && referenceSize.Height > 0;
            if (hasImage)
            {
                ConfigureCanvas(canvas);
                try
                {
                    canvas.DrawImage(controller.Bitmap, image);
                }
                catch (Exception e)
                {
                    log.ErrorFormat(e.Message);
                }

                controller.DrawKVA(canvas, image.Location, (float)image.Width / referenceSize.Width);
            }

            // (2.29.0) The line where the mouse drags it, solid while the mouse is on it; the marks of the pole being clicked.
            SwimWallCamera state = SwimState();
            state.LineActive = swimLineDrag || swimLineHover;
            if (swimLineDrag && !float.IsNaN(swimLineDragValue))
                state.Line = swimLineDragValue;

            SwimCaptureWall.PaintOverlay(canvas, image, state, recordingStatus, recordingStatusProgress, hasImage, swimHover, swimHoverButton, swimSolo, SwimScale);
            if (swimPole)
            {
                List<PointF> marks = new List<PointF>();
                foreach (PointF p in swimPolePoints)
                    marks.Add(SwimToViewport(p, image));
                SwimCaptureWall.PaintPole(canvas, image, marks, swimPoleMouse, SwimScale);
            }

            toaster.Draw(canvas);
        }

        private void SwimMouseDown(MouseEventArgs e)
        {
            Focus();
            if (e.Button == MouseButtons.Left)
            {
                controller.SwimActivate();

                // (2.29.0) The marks of the pole first, then the line taken to be dragged (the buttons stay over it).
                if (swimPole)
                {
                    SwimPoleClick(e.Location);
                    return;
                }

                int button = SwimHit(e.Location);
                if (button < 0 && SwimOnLine(e.Location))
                {
                    swimLineDrag = true;
                    swimLineDragValue = SwimLineAt(e.Location.X);
                    Cursor = Cursors.SizeWE;
                    Invalidate();
                    return;
                }

                if (button == SwimCaptureWall.ButtonConfigure && SwimRecording(SwimState()))
                    return;

                if (button >= 0 && SwimButtonClicked != null)
                    SwimButtonClicked(button);
            }
            else if (e.Button == MouseButtons.Right && (swimPole || swimLineDrag))
            {
                // (2.29.0) A right click stops the measure of the pole or the drag of the line (the line stays where it was).
                if (swimPole)
                    SwimPoleEnd();

                swimLineDrag = false;
                swimLineDragValue = float.NaN;
                Cursor = Cursors.Default;
                Invalidate();
            }
            else if (e.Button == MouseButtons.Right && controller.Bitmap != null && referenceSize.Width > 0)
            {
                // The menu of the right click (the settings of the camera, the drawings) for the place of the image on the wall.
                Rectangle image = SwimImageRectangle();
                controller.OnMouseRightDown(e.Location, image.Location, (float)image.Width / referenceSize.Width);
            }
        }

        private void SwimMouseMove(Point location)
        {
            // (2.29.0) The line follows the mouse while it is dragged; the line of the pole from its first mark to the mouse.
            if (swimLineDrag)
            {
                swimLineDragValue = SwimLineAt(location.X);
                Invalidate();
                return;
            }

            if (swimPole)
            {
                swimPoleMouse = location;
                Cursor = Cursors.Cross;
                if (swimPolePoints.Count > 0)
                    Invalidate();
                return;
            }

            int button = SwimHit(location);
            bool onLine = button < 0 && SwimOnLine(location);
            bool hover = ClientRectangle.Contains(location);
            if (hover == swimHover && button == swimHoverButton && onLine == swimLineHover)
                return;

            swimHover = hover;
            swimHoverButton = button;
            swimLineHover = onLine;
            Cursor = button >= 0 ? Cursors.Hand : (onLine ? Cursors.SizeWE : Cursors.Default);
            Invalidate();
        }

        /// <summary>(2.29.0) The line of the camera was dragged to a new place: a part of the width of the image.</summary>
        public event Action<float> SwimLineMoved;

        /// <summary>(2.29.0) The two marks of the pole were clicked: in pixels of the image, with the width of the image in pixels.</summary>
        public event Action<PointF, PointF, int> SwimPoleMeasured;

        /// <summary>(2.29.0) The two marks of a pole are clicked next on the image, for its scale; a right click stops it.</summary>
        public void SwimMeasurePole()
        {
            if (!swimWall || controller.Bitmap == null || referenceSize.Width <= 0)
                return;

            swimPole = true;
            swimPolePoints.Clear();
            swimPoleMouse = null;
            swimLineDrag = false;
            Cursor = Cursors.Cross;
            Invalidate();
        }

        private void SwimPoleEnd()
        {
            swimPole = false;
            swimPolePoints.Clear();
            swimPoleMouse = null;
            Cursor = Cursors.Default;
            Invalidate();
        }

        /// <summary>A place of the viewport in pixels of the image.</summary>
        private PointF SwimToImage(Point location, Rectangle image)
        {
            return new PointF((location.X - image.Left) * (float)referenceSize.Width / Math.Max(1, image.Width),
                (location.Y - image.Top) * (float)referenceSize.Height / Math.Max(1, image.Height));
        }

        /// <summary>A place in pixels of the image in the viewport.</summary>
        private PointF SwimToViewport(PointF point, Rectangle image)
        {
            return new PointF(image.Left + point.X * image.Width / Math.Max(1f, referenceSize.Width),
                image.Top + point.Y * image.Height / Math.Max(1f, referenceSize.Height));
        }

        private void SwimPoleClick(Point location)
        {
            Rectangle image = SwimImageRectangle();
            if (!image.Contains(location) || referenceSize.Width <= 0 || referenceSize.Height <= 0)
                return;

            swimPolePoints.Add(SwimToImage(location, image));
            if (swimPolePoints.Count < 2)
            {
                Invalidate();
                return;
            }

            PointF a = swimPolePoints[0], b = swimPolePoints[1];
            int width = referenceSize.Width;
            SwimPoleEnd();
            Action<PointF, PointF, int> measured = SwimPoleMeasured;
            if (measured != null)
                measured(a, b, width);
        }

        /// <summary>
        /// (2.29.0) The line of the camera can be taken from this place: on it or its chip, a camera under the water with an image, not
        /// while it records.
        /// </summary>
        private bool SwimOnLine(Point location)
        {
            SwimWallCamera state = SwimState();
            if (!state.Loaded || !SwimWallPlaces.Under(state.Distance) || SwimRecording(state) || controller.Bitmap == null || referenceSize.Width <= 0)
                return false;

            Rectangle image = SwimImageRectangle();
            float s = SwimCaptureWall.OverlayScale(image, SwimScale);
            float x = image.Left + Math.Max(0.02f, Math.Min(0.98f, state.Line)) * image.Width;
            return Math.Abs(location.X - x) <= 9 * s && location.Y >= image.Top + 40 * s && location.Y <= image.Bottom - 20 * s;
        }

        /// <summary>(2.29.0) The place of the line for a place of the mouse: a part of the width of the image.</summary>
        private float SwimLineAt(int x)
        {
            Rectangle image = SwimImageRectangle();
            return Math.Max(0.02f, Math.Min(0.98f, (x - image.Left) / (float)Math.Max(1, image.Width)));
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!swimWall || (!swimHover && swimHoverButton < 0 && !swimLineHover))
                return;

            swimHover = false;
            swimHoverButton = -1;
            swimLineHover = false;
            Invalidate();
        }
        #endregion
        
        #region Drawing
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            // (Swim 2.27.0) The wall of cameras.
            if (swimWall)
            {
                PaintWall(e.Graphics);
                return;
            }

            if (showRecordingIndicator)
                DrawRecordingIndicator(e.Graphics);
            
            if(controller.Bitmap == null)
                return;
                
            ConfigureCanvas(e.Graphics);

            DrawImage(e.Graphics);
            DrawKVA(e.Graphics);
            DrawResizers(e.Graphics);
            toaster.Draw(e.Graphics);
        }
        private void ConfigureCanvas(Graphics canvas)
        {
            canvas.InterpolationMode = InterpolationMode.NearestNeighbor;
            canvas.PixelOffsetMode = PixelOffsetMode.Half;
            canvas.CompositingQuality = CompositingQuality.HighSpeed;
            canvas.SmoothingMode = SmoothingMode.None;
            
            //canvas.PixelOffsetMode = PixelOffsetMode.HighSpeed;
            //canvas.InterpolationMode = InterpolationMode.Bilinear;
        }
        private void DrawImage(Graphics canvas)
        {
            try
            {
                if (displayRectangle.Size == controller.Bitmap.Size)
                    canvas.DrawImageUnscaled(controller.Bitmap, displayRectangle.Location);
                else
                    canvas.DrawImage(controller.Bitmap, displayRectangle);
            }
            catch (Exception e)
            {
                log.ErrorFormat(e.Message);
            }
        }
        private void DrawKVA(Graphics canvas)
        {
            if(referenceSize.Width != 0)
            {
                float zoom = (float)displayRectangle.Size.Width / referenceSize.Width;
                controller.DrawKVA(canvas, displayRectangle.Location, zoom);
            }
        }
        private void DrawRecordingIndicator(Graphics canvas)
        {
            // Draw the recording status banner/reverse progress bar.
            Color bannerColor = GetBannerColor(recordingStatus);
            int bannerHeight = GetBannerHeight(recordingStatus);
            int progressPixels = (int)(recordingStatusProgress * this.Width);
            
            using (SolidBrush brush = new SolidBrush(bannerColor))
            {
                canvas.FillRectangle(brush, new Rectangle(0, 0, progressPixels, bannerHeight));
            }
        }

        private Color GetBannerColor(RecordingStatus status)
        {
            switch (status)
            {
                case RecordingStatus.Disconnected:
                    return Color.DarkOrchid;
                case RecordingStatus.Paused:
                    return Color.Khaki;
                case RecordingStatus.Armed:
                    return Color.LightGreen;
                case RecordingStatus.Recording:
                    return Color.Red;
                case RecordingStatus.Disarmed:
                case RecordingStatus.Quiet:
                default:
                    return Color.SkyBlue;
            }
        }

        private int GetBannerHeight(RecordingStatus status)
        {
            switch (status)
            {
                case RecordingStatus.Recording:
                    return 50;
                case RecordingStatus.Disconnected:
                case RecordingStatus.Armed:
                case RecordingStatus.Quiet:
                    return 25;
                case RecordingStatus.Paused:
                case RecordingStatus.Disarmed:
                default:
                    return 10;
            }
        }
        
        private void DrawResizers(Graphics canvas)
        {
            foreach(EmbeddedButton resizer in resizers)
                resizer.Draw(canvas);
        }
        #endregion
        
        #region Interaction
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            // (Swim 2.27.0) On the wall of cameras: the buttons over the image and the menu of the right click only.
            if (swimWall)
            {
                SwimMouseDown(e);
                return;
            }

            if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Middle)
                OnMouseLeftDown(e);
            else if (e.Button == MouseButtons.Right)
                OnMouseRightDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            // (Swim 2.27.0) On the wall of cameras the mouse shows the buttons of the image (and does not take the keyboard).
            if (swimWall)
            {
                SwimMouseMove(e.Location);
                return;
            }

            this.Focus();

            if (controller.Bitmap == null)
                return;

            if (e.Button == MouseButtons.None && controller.MetadataManipulator.DrawingInitializing)
            {
                controller.OnMouseMove(e, ModifierKeys, displayRectangle.Location, zoomHelper.Value);
            }
            else if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Middle)
            {
                if (imageManipulator.Started)
                {
                    bool sticky = true;
                    imageManipulator.Move(e, sticky, this.Size, referenceSize);
                    displayRectangle = imageManipulator.DisplayRectangle;
                    AfterDisplayRectangleChanged();
                }
                else
                {
                    controller.OnMouseMove(e, ModifierKeys, displayRectangle.Location, zoomHelper.Value);
                }
            }
            else 
            {
                UpdateCursor(e.Location);
            }
        }
        
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            // (Swim 2.29.0) The line dragged on the wall of cameras goes to its new place.
            if (swimWall && swimLineDrag)
            {
                if (e.Button == MouseButtons.Left)
                {
                    swimLineDrag = false;
                    float line = swimLineDragValue;
                    swimLineDragValue = float.NaN;
                    Action<float> moved = SwimLineMoved;
                    if (!float.IsNaN(line) && moved != null)
                        moved(line);
                    Invalidate();
                }

                return;
            }
            
            if(controller.Bitmap == null || swimWall)
                return;

            imageManipulator.End();

            if (e.Button == MouseButtons.Middle)
            {
                UpdateCursor(e.Location);
                return;
            }

            if (e.Button != MouseButtons.Left)
                return;

            controller.OnMouseUp(e, ModifierKeys, displayRectangle.Location, zoomHelper.Value);

            // TODO: do not call this if we are not resizing.
            ForceZoomValue();
            controller.UpdateDisplayRectangle(displayRectangle);
            Cursor = controller.GetCursor(zoomHelper.Value);
        }
        
        protected override void OnDoubleClick(EventArgs e)
        {
            // (Swim 2.27.0) On the wall of cameras: the camera alone on the wall, or all the cameras back (not on its buttons).
            if (swimWall)
            {
                base.OnDoubleClick(e);
                Point at = PointToClient(Control.MousePosition);
                if (!swimPole && SwimHit(at) < 0 && !SwimOnLine(at) && SwimSoloAsked != null)
                    SwimSoloAsked(this, EventArgs.Empty);

                return;
            }

            if(controller.Bitmap == null)
                return;
                
            base.OnDoubleClick(e);

            if (!dblClickZoom)
                return;
            
            Point mouse = this.PointToClient(Control.MousePosition);
            if(displayRectangle.Contains(mouse))
            {
                imageManipulator.Expand(referenceSize, displayRectangle, this.Size);
                displayRectangle = imageManipulator.DisplayRectangle;
                AfterDisplayRectangleChanged();
                ForceZoomValue();
                controller.UpdateDisplayRectangle(displayRectangle);
            }
        }
        
        private void Viewport_MouseWheel(object sender, MouseEventArgs e)
        {
            if(controller.Bitmap == null || swimWall)
                return;
                
            if ((ModifierKeys & Keys.Control) == Keys.Control)
                Zoom(e);
        }
        
        private int HitTestResizers(Point mouse)
        {
            int hit = -1;
            for(int i = 0; i < resizers.Count; i++)
            {
                if(resizers[i].HitTest(mouse))
                {
                    hit = i+1;
                    break;
                }
            }
            return hit;
        }
        
        private int HitTestImage(Point mouse)
        {
            if(displayRectangle.Contains(mouse))
                return 0;
            
            return -1;
        }
        
        private void Zoom(MouseEventArgs e)
        {
            int steps = e.Delta * SystemInformation.MouseWheelScrollLines / 120;
            if(steps > 0)
                zoomHelper.Increase();
            else
                zoomHelper.Decrease();

            AfterZoomChanged(e.Location);
        }

        /// <summary>
        /// Explicit zoom change (mouse scroll or reset).
        /// </summary>
        private void AfterZoomChanged(Point location)
        {
            RecomputeDisplayRectangle(referenceSize, displayRectangle, location);
            ToastZoom();
            controller.InvalidateCursor();
        }
        
        private void ToastZoom()
        {
            string message = string.Format("Zoom:{0}", zoomHelper.GetLabel());
            ToastMessage(message, 750);
        }
        
        /// <summary>
        /// Implicit zoom change (resizers or auto fit).
        /// </summary>
        private void ForceZoomValue()
        {
            // The display rectangle has changed size outside the context of zoom (e.g: dragging corners).
            // Recompute the current zoom value to keep it in sync.
            float oldZoom = zoomHelper.Value;
            float newZoom = (float)displayRectangle.Size.Width / referenceSize.Width;
            if (oldZoom == newZoom)
                return;

            zoomHelper.Value = newZoom;
            
            float maxDifference = 0.1f;
            if (Math.Abs(newZoom - oldZoom) > maxDifference)
                ToastZoom();

            controller.InvalidateCursor();
        }
        
        private void InitializeDisplayRectangle(Rectangle saved)
        {
            if (saved.Size == Size.Empty)
            {
                if (referenceSize.FitsIn(this.Size))
                {
                    int left = (this.Size.Width - referenceSize.Width) / 2;
                    int top = (this.Size.Height - referenceSize.Height) / 2;
                    displayRectangle = new Rectangle(left, top, referenceSize.Width, referenceSize.Height);
                }
                else
                {
                    displayRectangle = UIHelper.RatioStretch(referenceSize, this.Size);
                }
            }
            else
            {
                displayRectangle = saved;
            }

            AfterDisplayRectangleChanged();
        }
        
        private void RecomputeDisplayRectangle(Size imageSize, Rectangle displayRectangle, Point mouse)
        {
            // Resize display rectangle while keeping it centerer on the mouse.
            PointF normalizedMouse = new PointF((mouse.X - displayRectangle.X) / (float)displayRectangle.Width, (mouse.Y - displayRectangle.Y) / (float)displayRectangle.Height);
            Size size = imageSize.Scale(zoomHelper.Value);
            int left = (int)Math.Round((mouse.X - (size.Width * normalizedMouse.X)));
            int top = (int)Math.Round((mouse.Y - (size.Height * normalizedMouse.Y)));
            Point location = new Point(left, top);
            this.displayRectangle = new Rectangle(location, size);
            
            AfterDisplayRectangleChanged();
        }
        
        private void AfterDisplayRectangleChanged()
        {
            // Force aspect ratio to match original size.
            double aspectRatio = (double)referenceSize.Width / referenceSize.Height;
            displayRectangle.Width = (int)Math.Round((double)displayRectangle.Height * aspectRatio);

            resizers[0].Location = new Point(displayRectangle.Left - resizerOffset, displayRectangle.Top - resizerOffset);
            resizers[1].Location = new Point(displayRectangle.Right - resizerOffset, displayRectangle.Top - resizerOffset);
            resizers[2].Location = new Point(displayRectangle.Right - resizerOffset, displayRectangle.Bottom - resizerOffset);
            resizers[3].Location = new Point(displayRectangle.Left - resizerOffset, displayRectangle.Bottom - resizerOffset);
        }
        
        /// <summary>
        /// Update the cursor to the current tool or resizer.
        /// </summary>
        private void UpdateCursor(Point mouse)
        {
            // This is called for every mouse move so it must be as quick as possible and not create unecessary resources.
            int onResizerIndex = -1;
            for(int i = 0; i < resizers.Count; i++)
            {
                if (!resizers[i].HitTest(mouse))
                    continue;
                
                onResizerIndex = i;
                break;
            }

            if (onResizerIndex >= 0)
            {
                if (onResizerIndex != resizerIndex)
                {
                    resizerIndex = onResizerIndex;
                    Cursor = resizers[resizerIndex].CursorMouseOver;
                }
                
                return;
            }

            // Not on a resizer (hand tool or normal tool).
            resizerIndex = -1;
            Cursor = controller.GetCursor(zoomHelper.Value);
        }
        #endregion
        
        private void OnMouseLeftDown(MouseEventArgs e)
        {
            if(controller.Bitmap == null || imageManipulator.Started)
                return;
                
            // Resize image.
            int hit = HitTestResizers(e.Location);
            if(hit > 0)
            {
                // Cursor should be resizer cursor.
                imageManipulator.Start(e.Location, hit, displayRectangle);
                return;
            }
            
            // Create a new drawing,
            // start moving an existing drawing or a handle,
            // continue initializing a multi-step drawing.
            bool handled = controller.OnMouseDown(e, displayRectangle.Location, zoomHelper.Value);
            if(handled)
            {
                Cursor = controller.GetCursor(zoomHelper.Value);
                return;
            }
            
            // Start moving image.
            hit = HitTestImage(e.Location);
            if(hit == 0)
            {
                imageManipulator.Start(e.Location, hit, displayRectangle);
                Cursor = imageManipulator.GetCursorClosedHand();
            }
        }
        
        private void OnMouseRightDown(MouseEventArgs e)
        {
            if(controller.Bitmap == null)
                return;
                
            controller.OnMouseRightDown(e.Location, displayRectangle.Location, zoomHelper.Value);
        }
        
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
        }
        
    }
}
