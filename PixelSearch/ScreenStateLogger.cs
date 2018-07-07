using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using PixelSearch;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Device = SharpDX.Direct3D11.Device;
using MapFlags = SharpDX.Direct3D11.MapFlags;
using Resource = SharpDX.DXGI.Resource;
using ResultCode = SharpDX.DXGI.ResultCode;

public class PixelLibrary
{
	private bool _run, _init;

	public int Size { get; private set; }

	public int x;
	public int y;
	public int widthCustom;
	public int heightCustom;
	public Color colorCustom;
	public int step;
	public int maxvariance;
	public int maxThreads;

	public bool isPixelSearcherActivated;
	public bool printTimeTaken;

	public void TogglePixelSearcher(bool toggle, bool printDebugInfo = false)
	{
		isPixelSearcherActivated = toggle;
		printTimeTaken = printDebugInfo;
	}

	public PixelLibrary(int x, int y, Color color, int width = 2560, int height = 1440, int step = 1, int maxvariance = 0, int threadCount = 1)
	{
		this.x = x;
		this.y = y;
		widthCustom = width;
		heightCustom = height;
		colorCustom = color;
		this.step = step;
		this.maxvariance = maxvariance;
		maxThreads = threadCount;
	}

	public void Start()
	{
		_run = true;
		var factory = new Factory1();
		//Get first adapter
		var adapter = factory.GetAdapter1(0);
		//Get device from adapter
		var device = new Device(adapter);
		//Get front buffer of the adapter
		var output = adapter.GetOutput(0);
		var output1 = output.QueryInterface<Output1>();

		// Width/Height of desktop to capture
		var width = 0;
		var height = 0;

		if (widthCustom > output.Description.DesktopBounds.Right || widthCustom < 1)
		{
			width = output.Description.DesktopBounds.Right;
			Debug.WriteLine("Invalid Width Specified - Set to Default");
		}
		else
		{
			width = widthCustom;
		}


		if (heightCustom > output.Description.DesktopBounds.Bottom || heightCustom < 1)
		{
			height = output.Description.DesktopBounds.Bottom;
			Debug.WriteLine("Invalid Height Specified - Set to Default");
		}
		else
		{
			height = heightCustom;
		}

		// Create Staging texture CPU-accessible
		var textureDesc = new Texture2DDescription
		{
			CpuAccessFlags = CpuAccessFlags.Read,
			BindFlags = BindFlags.None,
			Format = Format.B8G8R8A8_UNorm,
			Width = output.Description.DesktopBounds.Right,
			Height = output.Description.DesktopBounds.Bottom,
			OptionFlags = ResourceOptionFlags.None,
			MipLevels = 1,
			ArraySize = 1,
			SampleDescription = {Count = 1, Quality = 0},
			Usage = ResourceUsage.Staging
		};
		var screenTexture = new Texture2D(device, textureDesc);

		Task.Factory.StartNew(() =>
		{
			// Duplicate the output
			using (var duplicatedOutput = output1.DuplicateOutput(device))
			{
				while (_run)
					try
					{
						// Try to get duplicated frame within given time is ms
						duplicatedOutput.AcquireNextFrame(5, out OutputDuplicateFrameInformation duplicateFrameInformation, out Resource screenResource);

						// copy resource into memory that can be accessed by the CPU
						using (var screenTexture2D = screenResource.QueryInterface<Texture2D>())
						{
							device.ImmediateContext.CopyResource(screenTexture2D, screenTexture);
						}

						// Get the desktop capture texture
						var mapSource = device.ImmediateContext.MapSubresource(screenTexture, 0, MapMode.Read, MapFlags.None);

						// Create Drawing.Bitmap
						using (var bitmap = new Bitmap(widthCustom, heightCustom, PixelFormat.Format32bppArgb))
						{
							var boundsRect = new Rectangle(0, 0, widthCustom, heightCustom);

							// Copy pixels from screen capture Texture to GDI bitmap
							var mapDest = bitmap.LockBits(boundsRect, ImageLockMode.ReadWrite, bitmap.PixelFormat);
							var sourcePtr = mapSource.DataPointer;
							var destPtr = mapDest.Scan0;
							for (var y = 0; y < heightCustom; y++)
							{
								// Copy a single line 
								Utilities.CopyMemory(destPtr, sourcePtr, widthCustom * 4);

								// Advance pointers
								sourcePtr = IntPtr.Add(sourcePtr, mapSource.RowPitch);
								destPtr = IntPtr.Add(destPtr, mapDest.Stride);
							}

							if (isPixelSearcherActivated)
							{
								var p = PixelSearchThreaded(mapDest, step, maxvariance, printTimeTaken, maxThreads);
								if (p.X != -1 && p.Y != -1)
									PixelFound?.Invoke(this, p);
							}

							// Release source and dest locks
							bitmap.UnlockBits(mapDest);
							device.ImmediateContext.UnmapSubresource(screenTexture, 0);

							using (var ms = new MemoryStream())
							{
								bitmap.Save(ms, ImageFormat.Bmp);
								ScreenRefreshed?.Invoke(this, ms.ToArray());
								_init = true;
							}
						}
						screenResource.Dispose();
						duplicatedOutput.ReleaseFrame();
					}
					catch (SharpDXException e)
					{
						if (e.ResultCode.Code != ResultCode.WaitTimeout.Result.Code)
						{
							Trace.TraceError(e.Message);
							Trace.TraceError(e.StackTrace);
						}
					}
			}
		});
		while (!_init) ;
	}

	#region Imaging Tools
	bool ColorsAreClose(Color a, Color z, int threshold = 50)
	{
		int r = a.R - z.R,
			g = a.G - z.G,
			b = a.B - z.B;
		return (r * r + g * g + b * b) <= threshold * threshold;
	}

	private bool IsWithinShadeRange(Color colorCurrent, Color colorComparing, int maxShadeBothDirections)
	{
		// colorCurrent is the color we use to calculate the possible allowed shades from
		// colorComparing is the color we check if it is in the range of the shades calculated from colorCurrent

		if (maxShadeBothDirections > 255) maxShadeBothDirections = 255;

		var upperR = colorCurrent.R + maxShadeBothDirections;
		var upperG = colorCurrent.G + maxShadeBothDirections;
		var upperB = colorCurrent.B + maxShadeBothDirections;

		if (upperR > 255) upperR = 255;
		if (upperG > 255) upperG = 255;
		if (upperB > 255) upperB = 255;

		var lowerR = colorCurrent.R - maxShadeBothDirections;
		var lowerG = colorCurrent.G - maxShadeBothDirections;
		var lowerB = colorCurrent.B - maxShadeBothDirections;

		if (lowerR < 0) lowerR = 0;
		if (lowerG < 0) lowerG = 0;
		if (lowerB < 0) lowerB = 0;

		if (colorComparing.R >= lowerR && colorComparing.R <= upperR && colorComparing.G >= lowerG &&
		    colorComparing.G <= upperG && colorComparing.B >= lowerB && colorComparing.B <= upperB) return true;

		return false;
	}
	#endregion

	#region PixelSearch Related Functions

	public Point PixelSearch(BitmapData bmpData, int step = 1, int variance = 0, bool debugInfo = false)
	{
		if (bmpData != null)
		{
			Debug.WriteLine("[WARN] 'bmpData' is null");
			return new Point(-1, -1);
		}

		if (step < 1) step = 1;

		unsafe
		{
			var bytesPerPixel = Image.GetPixelFormatSize(bmpData.PixelFormat) / 8;
			var heightInPixels = bmpData.Height;
			var widthInBytes = bmpData.Width * bytesPerPixel;
			var ptrFirstPixel = (byte*) bmpData.Scan0;

			for (var y = 0; y < heightInPixels; y++)
			{
				var currentLine = ptrFirstPixel + y * bmpData.Stride;
				for (var x = 0; x < widthInBytes; x = x + bytesPerPixel * step)
				{
					int currentBlue = currentLine[x];
					int currentGreen = currentLine[x + 1];
					int currentRed = currentLine[x + 2];
					int currentAlpha = currentLine[x + 3];

					if (colorCustom.R == currentRed && colorCustom.G == currentGreen && colorCustom.B == currentBlue
					) // Not checking if matching alpha
					{
						var translatedX = x / bytesPerPixel;
						var translatedY = y;

						return new Point(translatedX, translatedY);
					}

					if (variance > 0)
						if (ColorsAreClose(Color.FromArgb(currentRed, currentGreen, currentBlue), colorCustom, variance))
						{
							var translatedX = x / bytesPerPixel;
							var translatedY = y;

							return new Point(translatedX, translatedY);
						}
				}
			}
		}


		// nothing found
		return new Point(-1, -1);
	}

	public Point PixelSearchThreaded(BitmapData bmpData, int step = 1, int variance = 0, bool debugInfo = false,
		int threadCount = 8)
	{
		if (threadCount > Environment.ProcessorCount) threadCount = Environment.ProcessorCount;
		if (step < 1) step = 1;

		// if a thread count thats either 0 or 1 is set, just set it to use the single threaded version
		if (threadCount < 1 || threadCount == 1) return PixelSearch(bmpData);

		if (bmpData == null)
		{
			Debug.WriteLine("[WARN] 'bmpData' is null");
			return new Point(-1, -1);
		}

		unsafe
		{
			var bytesPerPixel = Image.GetPixelFormatSize(bmpData.PixelFormat) / 8;
			var heightInPixels = bmpData.Height;
			var widthInBytes = bmpData.Width * bytesPerPixel;
			var ptrFirstPixel = (byte*) bmpData.Scan0;

			var options = new ParallelOptions();
			options.MaxDegreeOfParallelism = threadCount;

			var results = new ConcurrentBag<Point>();

			Parallel.For(0, heightInPixels, options, (y, state) =>
			{
				if (results.Count > 0) state.Stop();

				var currentLine = ptrFirstPixel + y * bmpData.Stride;

				for (var x = 0; x < widthInBytes; x += bytesPerPixel * step)
				{
					if (results.Count > 0) state.Stop();

					int currentBlue = currentLine[x];
					int currentGreen = currentLine[x + 1];
					int currentRed = currentLine[x + 2];
					int currentAlpha = currentLine[x + 3];

					if (colorCustom.R == currentRed && colorCustom.G == currentGreen && colorCustom.B == currentBlue) // Not checking if matching alpha
					{
						var translatedX = x / bytesPerPixel;
						var translatedY = y;

						results.Add(new Point(translatedX, translatedY));
						state.Stop();
					}

					if (variance > 0)
						if (IsWithinShadeRange(Color.FromArgb(currentRed, currentGreen, currentBlue), colorCustom, maxvariance))
						{
							var translatedX = x / bytesPerPixel;
							var translatedY = y;

							results.Add(new Point(translatedX, translatedY));
							state.Stop();
						}
				}
			});

			if (results.Count > 0)
			{
				if (results.TryPeek(out Point result))
					return result;
			}
		}

		// nothing found
		return new Point(-1, -1);
	}

	public List<T[,]> Split2DArrayIntoChunks<T>(T[,] bigArray, int chunkCount = 2)
	{
		// for use on colum major 2d arrays

		if (bigArray.Length < 1) return new List<T[,]>();

		var chunkContainer = new List<T[,]>();
		var atCurrentHeight = 0;
		var heightPerChunk = bigArray.GetLength(0) / chunkCount;

		for (var threadBlock = 0; threadBlock < chunkCount; threadBlock++)
			if (threadBlock == chunkCount - 1) // we're at the last chunk
			{
				var remainingLines = bigArray.GetLength(0) - atCurrentHeight;
				Debug.WriteLine("At last chunk, we have " + remainingLines + " lines left to assign the last block!");
				var newThreadBlock = new T[remainingLines, bigArray.GetLength(1)];

				for (var i = 0; i < remainingLines; i++)
				for (var j = 0; j < bigArray.GetLength(1); j++)
					newThreadBlock[i, j] = bigArray[atCurrentHeight + i, j];

				atCurrentHeight += remainingLines;
				chunkContainer.Add(newThreadBlock);
			}
			else
			{
				var newThreadBlock = new T[heightPerChunk, bigArray.GetLength(1)];

				for (var i = 0; i < heightPerChunk; i++)
				for (var j = 0; j < bigArray.GetLength(1); j++)
					newThreadBlock[i, j] = bigArray[atCurrentHeight + i, j];

				atCurrentHeight += heightPerChunk;
				chunkContainer.Add(newThreadBlock);
			}

		Debug.WriteLine("Blocks Available: " + chunkContainer.Count);
		Debug.WriteLine("Height Itterated Through: " + atCurrentHeight);
		Debug.WriteLine("Height Remaining: " +
		                (bigArray.GetLength(0) - atCurrentHeight)); // if bigger than 0 something went wrong

		return chunkContainer;
	}

	public class Chunk
	{
		public byte[,] Chunk2D;
		public int RefIndex;
	}

	public List<Chunk> Chunkify2DArray(byte[,] bigArray, int chunkCount = 2)
	{
		if (bigArray.Length < 1) return new List<Chunk>();

		var chunkContainer = new List<Chunk>();
		var atCurrentHeight = 0;
		var heightPerChunk = bigArray.GetLength(0) / chunkCount;

		for (var threadBlock = 0; threadBlock < chunkCount; threadBlock++)
			if (threadBlock == chunkCount - 1) // we're at the last chunk
			{
				var newChunk = new Chunk();
				var remainingLines = bigArray.GetLength(0) - atCurrentHeight;
				Debug.WriteLine("At last chunk, we have " + remainingLines + " lines left to assign the last block!");
				var newThreadBlock = new byte[remainingLines, bigArray.GetLength(1)];

				for (var i = 0; i < remainingLines; i++)
				for (var j = 0; j < bigArray.GetLength(1); j++)
					newThreadBlock[i, j] = bigArray[atCurrentHeight + i, j];

				newChunk.Chunk2D = newThreadBlock;
				newChunk.RefIndex = threadBlock;

				atCurrentHeight += remainingLines;
				chunkContainer.Add(newChunk);
			}
			else
			{
				var newThreadBlock = new byte[heightPerChunk, bigArray.GetLength(1)];
				var newChunk = new Chunk();

				for (var i = 0; i < heightPerChunk; i++)
				for (var j = 0; j < bigArray.GetLength(1); j++)
					newThreadBlock[i, j] = bigArray[atCurrentHeight + i, j];

				newChunk.Chunk2D = newThreadBlock;
				newChunk.RefIndex = threadBlock;
				atCurrentHeight += heightPerChunk;
				chunkContainer.Add(newChunk);
			}
		return chunkContainer;
	}

	private static byte[,] ConvertBufferTo2DArray(byte[] input, int height, int width)
	{
		byte[,] output = new byte[height, width];
		for (int i = 0; i < height; i++)
		{
			for (int j = 0; j < width; j++)
			{
				output[i, j] = input[i * width + j];
			}
		}
		return output;
	}
	#endregion

	#region Image Search Related Functions
	public List<Point> FindImageInImage(Bitmap sourceBitmap, Bitmap searchingBitmap)
	{
		#region Arguments check

		if (sourceBitmap == null || searchingBitmap == null)
		{
			MessageBox.Show(
				"Passed bitmaps to function is null/empty" + Environment.NewLine + Environment.NewLine + "Is SourceBitmap Null: " +
				(sourceBitmap == null).ToString() + Environment.NewLine + "Is SearchingBitmap Null: " +
				(searchingBitmap == null).ToString(), "Error Encountered!", MessageBoxButtons.OK, MessageBoxIcon.Error);

			return null;
		}

		if (sourceBitmap.PixelFormat != searchingBitmap.PixelFormat)
		{
			MessageBox.Show("The pixelformat for both images does not match!", "Error Encountered!", MessageBoxButtons.OK,
				MessageBoxIcon.Error);
			return null;
		}


		if (sourceBitmap.Width < searchingBitmap.Width || sourceBitmap.Height < searchingBitmap.Height)
		{
			MessageBox.Show("Assigned 'searchingBitmap' is bigger in size than the 'sourceBitmap!", "Error Encountered!",
				MessageBoxButtons.OK, MessageBoxIcon.Error);

			return null;
		}
			

		#endregion

		var pixelFormatSize = Image.GetPixelFormatSize(sourceBitmap.PixelFormat) / 8;

		// Copy sourceBitmap to byte array
		var sourceBitmapData = sourceBitmap.LockBits(new Rectangle(0, 0, sourceBitmap.Width, sourceBitmap.Height),
			ImageLockMode.ReadOnly, sourceBitmap.PixelFormat);
		var sourceBitmapBytesLength = sourceBitmapData.Stride * sourceBitmap.Height;
		var sourceBytes = new byte[sourceBitmapBytesLength];
		Marshal.Copy(sourceBitmapData.Scan0, sourceBytes, 0, sourceBitmapBytesLength);
		sourceBitmap.UnlockBits(sourceBitmapData);

		// Copy serchingBitmap to byte array
		var serchingBitmapData =
			searchingBitmap.LockBits(new Rectangle(0, 0, searchingBitmap.Width, searchingBitmap.Height),
				ImageLockMode.ReadOnly, searchingBitmap.PixelFormat);
		var serchingBitmapBytesLength = serchingBitmapData.Stride * searchingBitmap.Height;
		var serchingBytes = new byte[serchingBitmapBytesLength];
		Marshal.Copy(serchingBitmapData.Scan0, serchingBytes, 0, serchingBitmapBytesLength);
		searchingBitmap.UnlockBits(serchingBitmapData);

		var pointsList = new List<Point>();

		// Serching entries
		// minimazing serching zone
		// sourceBitmap.Height - serchingBitmap.Height + 1
		for (var mainY = 0; mainY < sourceBitmap.Height - searchingBitmap.Height + 1; mainY++)
		{
			var sourceY = mainY * sourceBitmapData.Stride;

			for (var mainX = 0; mainX < sourceBitmap.Width - searchingBitmap.Width + 1; mainX++)
			{// mainY & mainX - pixel coordinates of sourceBitmap
			 // sourceY + sourceX = pointer in array sourceBitmap bytes
				var sourceX = mainX * pixelFormatSize;

				var isEqual = true;
				for (var c = 0; c < pixelFormatSize; c++)
				{// through the bytes in pixel
					if (sourceBytes[sourceX + sourceY + c] == serchingBytes[c])
						continue;
					isEqual = false;
					break;
				}

				if (!isEqual) continue;

				var isStop = false;

				// find fist equalation and now we go deeper) 
				for (var secY = 0; secY < searchingBitmap.Height; secY++)
				{
					var serchY = secY * serchingBitmapData.Stride;

					var sourceSecY = (mainY + secY) * sourceBitmapData.Stride;

					for (var secX = 0; secX < searchingBitmap.Width; secX++)
					{// secX & secY - coordinates of serchingBitmap
					 // serchX + serchY = pointer in array serchingBitmap bytes

						var serchX = secX * pixelFormatSize;

						var sourceSecX = (mainX + secX) * pixelFormatSize;

						for (var c = 0; c < pixelFormatSize; c++)
						{// through the bytes in pixel
							if (sourceBytes[sourceSecX + sourceSecY + c] == serchingBytes[serchX + serchY + c]) continue;

							// not equal - abort iteration
							isStop = true;
							break;
						}

						if (isStop) break;
					}

					if (isStop) break;
				}

				if (!isStop)
				{// serching bitmap is founded!!
					pointsList.Add(new Point(mainX, mainY));
				}
			}
		}

		return pointsList;
	}
	public List<Point> FindImageInImage(int parrentLeft, int parrentTop, int parrentRight, int parrentBottom, Bitmap searchingBitmap)
	{
		Bitmap bmpScreenshot = new Bitmap(Screen.PrimaryScreen.Bounds.Width, Screen.PrimaryScreen.Bounds.Height);
		Graphics g = Graphics.FromImage(bmpScreenshot);
		g.CopyFromScreen(parrentLeft, parrentTop, parrentRight, parrentBottom, Screen.PrimaryScreen.Bounds.Size);

		var pixelFormatSize = Image.GetPixelFormatSize(bmpScreenshot.PixelFormat) / 8;

		// Copy sourceBitmap to byte array
		var sourceBitmapData = bmpScreenshot.LockBits(new Rectangle(0, 0, bmpScreenshot.Width, bmpScreenshot.Height),
			ImageLockMode.ReadOnly, bmpScreenshot.PixelFormat);
		var sourceBitmapBytesLength = sourceBitmapData.Stride * bmpScreenshot.Height;
		var sourceBytes = new byte[sourceBitmapBytesLength];
		Marshal.Copy(sourceBitmapData.Scan0, sourceBytes, 0, sourceBitmapBytesLength);
		bmpScreenshot.UnlockBits(sourceBitmapData);

		// Copy serchingBitmap to byte array
		var serchingBitmapData =
			searchingBitmap.LockBits(new Rectangle(0, 0, searchingBitmap.Width, searchingBitmap.Height),
				ImageLockMode.ReadOnly, searchingBitmap.PixelFormat);
		var serchingBitmapBytesLength = serchingBitmapData.Stride * searchingBitmap.Height;
		var serchingBytes = new byte[serchingBitmapBytesLength];
		Marshal.Copy(serchingBitmapData.Scan0, serchingBytes, 0, serchingBitmapBytesLength);
		searchingBitmap.UnlockBits(serchingBitmapData);

		var pointsList = new List<Point>();

		// Serching entries
		// minimazing serching zone
		// sourceBitmap.Height - serchingBitmap.Height + 1
		for (var mainY = 0; mainY < bmpScreenshot.Height - searchingBitmap.Height + 1; mainY++)
		{
			var sourceY = mainY * sourceBitmapData.Stride;

			for (var mainX = 0; mainX < bmpScreenshot.Width - searchingBitmap.Width + 1; mainX++)
			{// mainY & mainX - pixel coordinates of sourceBitmap
			 // sourceY + sourceX = pointer in array sourceBitmap bytes
				var sourceX = mainX * pixelFormatSize;

				var isEqual = true;
				for (var c = 0; c < pixelFormatSize; c++)
				{// through the bytes in pixel
					if (sourceBytes[sourceX + sourceY + c] == serchingBytes[c])
						continue;
					isEqual = false;
					break;
				}

				if (!isEqual) continue;

				var isStop = false;

				// find fist equalation and now we go deeper) 
				for (var secY = 0; secY < searchingBitmap.Height; secY++)
				{
					var serchY = secY * serchingBitmapData.Stride;

					var sourceSecY = (mainY + secY) * sourceBitmapData.Stride;

					for (var secX = 0; secX < searchingBitmap.Width; secX++)
					{// secX & secY - coordinates of serchingBitmap
					 // serchX + serchY = pointer in array serchingBitmap bytes

						var serchX = secX * pixelFormatSize;

						var sourceSecX = (mainX + secX) * pixelFormatSize;

						for (var c = 0; c < pixelFormatSize; c++)
						{// through the bytes in pixel
							if (sourceBytes[sourceSecX + sourceSecY + c] == serchingBytes[serchX + serchY + c]) continue;

							// not equal - abort iteration
							isStop = true;
							break;
						}

						if (isStop) break;
					}

					if (isStop) break;
				}

				if (!isStop)
				{// serching bitmap is founded!!
					pointsList.Add(new Point(mainX, mainY));
				}
			}
		}

		bmpScreenshot.Dispose();
		g.Dispose();
		return pointsList;
	}
	#endregion

	public void Stop()
	{
		_run = false;
	}

	public void MouseManipulate(int x, int y, bool clickAlso = false)
	{
		MouseManipulator.MouseMove(x, y);
		if (clickAlso)
		{
			MouseManipulator.MouseClickLeft();
		}
	}

	public EventHandler<byte[]> ScreenRefreshed;
	public EventHandler<Point> PixelFound;
}
 