﻿using System;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Wacom.Ink;
using Wacom.Ink.Smoothing;
using Windows.Foundation;
using Windows.Graphics.Display;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Input;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=234238

namespace Tutorial_01
{
	/// <summary>
	/// An empty page that can be used on its own or navigated to within a Frame.
	/// </summary>
	public sealed partial class Part5 : Page
	{
		#region Fields

		private Color _backgroundColor = Colors.Honeydew;
		private uint? _pointerId;
		private int _updateFromIndex;
		private bool _pathFinished;

		private Graphics _graphics;
		private RenderingContext _renderingContext;
		private StrokeRenderer _strokeRenderer;
		private Layer _backbufferLayer;
		private Layer _strokeLayer;
		private IPathBuilder _pathBuilder;
		private ParticleBrush _brush;
		private MultiChannelSmoothener _smoothener;

		#endregion

		public Part5()
		{
			this.InitializeComponent();

			// Attach to events
			this.Loaded += Page_Loaded;
			this.Unloaded += Page_Unloaded;
			Application.Current.Suspending += App_Suspending;
			DisplayInformation.DisplayContentsInvalidated += DisplayInformation_DisplayContentsInvalidated;
			Windows.UI.Core.SystemNavigationManager.GetForCurrentView().BackRequested += Part5_BackRequested;

			CreateWacomInkObjects();
		}

		private void Part5_BackRequested(object sender, Windows.UI.Core.BackRequestedEventArgs e)
		{
			Frame rootFrame = Window.Current.Content as Frame;
			if (rootFrame == null)
				return;

			// Navigate back if possible, and if the event has not 
			// already been handled .
			if (rootFrame.CanGoBack && e.Handled == false)
			{
				e.Handled = true;
				rootFrame.GoBack();
			}
		}

		protected override void OnNavigatedTo(NavigationEventArgs e)
		{
			Frame rootFrame = Window.Current.Content as Frame;

			if (rootFrame.CanGoBack)
			{
				// Show UI in title bar if opted-in and in-app backstack is not empty.
				SystemNavigationManager.GetForCurrentView().AppViewBackButtonVisibility =
					AppViewBackButtonVisibility.Visible;
			}
		}

		protected override void OnNavigatedFrom(NavigationEventArgs e)
		{
			Windows.UI.Core.SystemNavigationManager.GetForCurrentView().BackRequested -= Part5_BackRequested;
		}

		async void Page_Loaded(object sender, RoutedEventArgs e)
		{
			await InitInkRendering();

			// Attach to swap chain panel events
			this.DxPanel.SizeChanged += DxPanel_SizeChanged;
			this.DxPanel.PointerPressed += DxPanel_PointerPressed;
			this.DxPanel.PointerMoved += DxPanel_PointerMoved;
			this.DxPanel.PointerReleased += DxPanel_PointerReleased;
			this.DxPanel.PointerCaptureLost += DxPanel_PointerCaptureLost;
			this.DxPanel.PointerCanceled += DxPanel_PointerCanceled;
		}

		void Page_Unloaded(object sender, RoutedEventArgs e)
		{
			DisposeWacomInkObjects();

			// Detach from swap chain panel events
			this.DxPanel.SizeChanged -= DxPanel_SizeChanged;
			this.DxPanel.PointerPressed -= DxPanel_PointerPressed;
			this.DxPanel.PointerMoved -= DxPanel_PointerMoved;
			this.DxPanel.PointerReleased -= DxPanel_PointerReleased;
			this.DxPanel.PointerCaptureLost -= DxPanel_PointerCaptureLost;
			this.DxPanel.PointerCanceled -= DxPanel_PointerCanceled;
		}

		void App_Suspending(object sender, Windows.ApplicationModel.SuspendingEventArgs e)
		{
			if (_graphics != null)
			{
				_graphics.Trim();
			}
		}

		void DisplayInformation_DisplayContentsInvalidated(DisplayInformation sender, object args)
		{
			if (_graphics != null)
			{
				_graphics.ValidateDevice();
			}
		}

		#region DxPanel Event Handlers

		void DxPanel_PointerPressed(object sender, PointerRoutedEventArgs e)
		{
			OnPointerInputBegin(e);
		}

		void DxPanel_PointerMoved(object sender, PointerRoutedEventArgs e)
		{
			OnPointerInputMove(e);
		}

		void DxPanel_PointerReleased(object sender, PointerRoutedEventArgs e)
		{
			OnPointerInputEnd(e);
		}

		void DxPanel_PointerCanceled(object sender, PointerRoutedEventArgs e)
		{
			OnPointerInputEnd(e);
		}

		void DxPanel_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
		{
			OnPointerInputEnd(e);
		}

		void DxPanel_SizeChanged(object sender, SizeChangedEventArgs e)
		{
			if (_renderingContext == null)
				return;

			// release existing layers
			_renderingContext.SetTarget(null);
			_strokeRenderer.Deinit();
			_strokeLayer.Dispose();
			_backbufferLayer.Dispose();

			// set the new size
			_graphics.SetLogicalSize(e.NewSize);

			// recreate the layers
			InitSizeDependentResources();
		}

		#endregion

		void CreateWacomInkObjects()
		{
			// Create a graphics object
			_graphics = new Graphics();

			// Create a path builder
			_pathBuilder = new PressurePathBuilder();
			_pathBuilder.SetMovementThreshold(0.1f);
			_pathBuilder.SetNormalizationConfig(0.0f, 1.0f);
			_pathBuilder.SetPropertyConfig(PropertyName.Width, 10.0f, 40.0f, null, null, PropertyFunction.Sigmoid, 0.6191646f, false);
			_pathBuilder.SetPropertyConfig(PropertyName.Alpha, 0.08f, 0.9f, null, null, PropertyFunction.Sigmoid, 0.6191646f, false);

			// Create an object that smooths input data
			_smoothener = new MultiChannelSmoothener(_pathBuilder.PathStride);

			// Create a particle brush
			_brush = new ParticleBrush();
			_brush.BlendMode = BlendMode.Normal;
			_brush.Spacing = 0.15f;
			_brush.Scattering = 0.05f;
			_brush.RotationMode = ParticleRotationMode.RotateRandom;
			_brush.FillTileSize = new Size(128.0f, 128.0f);

			// Create a stroke renderer
			_strokeRenderer = new StrokeRenderer();
			_strokeRenderer.Brush = _brush;
			_strokeRenderer.StrokeWidth = null;
			_strokeRenderer.Color = Colors.DarkOrange;
			_strokeRenderer.UseVariableAlpha = true;
			_strokeRenderer.Ts = 0.0f;
			_strokeRenderer.Tf = 1.0f;
		}

		async Task InitInkRendering()
		{
			_graphics.Initialize(this.DxPanel);
			_renderingContext = _graphics.GetRenderingContext();

			await LoadBrushTextures();

			InitSizeDependentResources();
		}

		void InitSizeDependentResources()
		{
			Size canvasSize = _graphics.Size;
			float scale = _graphics.Scale;

			_backbufferLayer = _graphics.CreateBackbufferLayer();
			_strokeLayer = _graphics.CreateLayer(canvasSize, scale);
			_strokeRenderer.Init(_graphics, canvasSize, scale);

			_renderingContext.SetTarget(_backbufferLayer);
			_renderingContext.ClearColor(_backgroundColor);
			_graphics.Present();
		}

		void DisposeWacomInkObjects()
		{
			SafeDispose(_pathBuilder);
			SafeDispose(_brush);
			SafeDispose(_smoothener);
			SafeDispose(_strokeLayer);
			SafeDispose(_backbufferLayer);
			SafeDispose(_renderingContext);
			SafeDispose(_graphics);

			_pathBuilder = null;
			_brush = null;
			_smoothener = null;
			_strokeLayer = null;
			_backbufferLayer = null;
			_renderingContext = null;
			_graphics = null;
		}

		void SafeDispose(object obj)
		{
			IDisposable disposable = obj as IDisposable;

			if (disposable != null)
			{
				disposable.Dispose();
			}
		}

		void OnPointerInputBegin(PointerRoutedEventArgs e)
		{
			// If currently there is an unfinished stroke - do not interrupt it
			if (_pointerId.HasValue)
				return;

			// Capture the pointer and store its Id
			this.DxPanel.CapturePointer(e.Pointer);
			_pointerId = e.Pointer.PointerId;

			// Reset the state related to path building
			_updateFromIndex = -1;
			_pathFinished = false;

			// Reset the smoothener
			_smoothener.Reset();

			// Clear the scene layer
			_strokeRenderer.ResetAndClear();
			_renderingContext.SetTarget(_strokeLayer);
			_renderingContext.ClearColor(_backgroundColor);

			// Add the pointer point to the path builder
			AddCurrentPointToPathBuilder(InputPhase.Begin, e);

			// Draw the scene
			Render(true);
		}

		void OnPointerInputMove(PointerRoutedEventArgs e)
		{
			// Ignore events from other pointers
			if (!_pointerId.HasValue || (e.Pointer.PointerId != _pointerId.Value))
				return;

			// Add the pointer point to the path builder
			AddCurrentPointToPathBuilder(InputPhase.Move, e);

			// Draw the scene
			Render(true);
		}

		void OnPointerInputEnd(PointerRoutedEventArgs e)
		{
			// Ignore events from other pointers
			if (!_pointerId.HasValue || (e.Pointer.PointerId != _pointerId.Value))
				return;

			// Reset the stored id and release the pointer capture
			_pointerId = null;
			this.DxPanel.ReleasePointerCapture(e.Pointer);

			// Add the pointer point to the path builder
			AddCurrentPointToPathBuilder(InputPhase.End, e);

			_pathFinished = true;

			// Draw the scene
			Render(true);
		}

		void AddCurrentPointToPathBuilder(InputPhase phase, PointerRoutedEventArgs e)
		{
			PointerPoint pointerPoint = e.GetCurrentPoint(this.DxPanel);

			Path pathPart = _pathBuilder.AddPoint(phase, pointerPoint);

			if (pathPart.PointsCount > 0)
			{
				_smoothener.Smooth(pathPart, phase == InputPhase.End);

				int indexOfFirstAffectedPoint;
				_pathBuilder.AddPathPart(pathPart, out indexOfFirstAffectedPoint);

				if (_updateFromIndex == -1)
				{
					_updateFromIndex = indexOfFirstAffectedPoint;
				}
			}
		}

		void Render(bool drawPreliminaryPath)
		{
			if (_updateFromIndex < 0)
				return;

			Path currentPath = _pathBuilder.CurrentPath;

			int numberOfPointsToDraw = currentPath.PointsCount - _updateFromIndex;
			if (numberOfPointsToDraw <= 0)
				return;

			_strokeRenderer.DrawStroke(currentPath, _updateFromIndex, numberOfPointsToDraw, _pathFinished);

			// reset the starting index
			_updateFromIndex = -1;

			// draw preliminary path
			if (drawPreliminaryPath && !_pathFinished)
			{
				Path prelimPathPart = _pathBuilder.CreatePreliminaryPath();

				if (prelimPathPart.PointsCount > 0)
				{
					_smoothener.Smooth(prelimPathPart, true);

					Path preliminaryPath = _pathBuilder.FinishPreliminaryPath(prelimPathPart);

					_strokeRenderer.DrawPreliminaryStroke(preliminaryPath, 0, preliminaryPath.PointsCount);
				}
			}

			// recompose the scene within the updated area
			_renderingContext.SetTarget(_strokeLayer, _strokeRenderer.UpdatedRect);
			_renderingContext.ClearColor(_backgroundColor);

			// draw
			_strokeRenderer.BlendStrokeUpdatedAreaInLayer(_strokeLayer, BlendMode.Normal);

			_renderingContext.SetTarget(_backbufferLayer);
			_renderingContext.DrawLayer(_strokeLayer, null, BlendMode.None);

			// present
			_graphics.Present();
		}

		async Task LoadBrushTextures()
		{
			PixelData fillPixelData = await GetPixelData(new Uri(@"ms-appx:///Assets/fill.png"));
			_brush.FillTexture = _graphics.CreateTexture(fillPixelData);

			PixelData shapePixelData = await GetPixelData(new Uri("ms-appx:///Assets/shape.png"));
			_brush.ShapeTexture = _graphics.CreateTexture(shapePixelData);
		}

		static async Task<PixelData> GetPixelData(Uri uri)
		{
			StorageFile file = await StorageFile.GetFileFromApplicationUriAsync(uri);

			using (IRandomAccessStream fileStream = await file.OpenAsync(FileAccessMode.Read))
			{
				BitmapDecoder decoder = await BitmapDecoder.CreateAsync(fileStream);

				PixelDataProvider provider = await decoder.GetPixelDataAsync(
					BitmapPixelFormat.Bgra8,
					BitmapAlphaMode.Premultiplied,
					new BitmapTransform(),
					ExifOrientationMode.IgnoreExifOrientation,
					ColorManagementMode.DoNotColorManage);

				var buffer = provider.DetachPixelData().AsBuffer();

				return new PixelData(buffer, decoder.PixelWidth, decoder.PixelHeight);
			}
		}
	}
}
