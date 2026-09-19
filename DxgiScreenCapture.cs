using System;
using System.Drawing;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Device = SharpDX.Direct3D11.Device;
using MapFlags = SharpDX.Direct3D11.MapFlags;

namespace AmbilightControllerForm
{
    public struct FloatColor
    {
        public float R;
        public float G;
        public float B;

        public FloatColor(float r, float g, float b)
        {
            R = r;
            G = g;
            B = b;
        }

        public static FloatColor FromColor(Color c)
        {
            return new FloatColor(c.R, c.G, c.B);
        }

        public Color ToColor()
        {
            return Color.FromArgb(
                Math.Min(255, Math.Max(0, (int)Math.Round(R))),
                Math.Min(255, Math.Max(0, (int)Math.Round(G))),
                Math.Min(255, Math.Max(0, (int)Math.Round(B)))
            );
        }
    }

    public class DxgiScreenCapture : IDisposable
    {
        private Device _device;
        private OutputDuplication _duplicatedOutput;
        private Texture2D _screenTexture;
        private int _width;
        private int _height;

        public DxgiScreenCapture()
        {
            Initialize();
        }

        private void Initialize()
        {
            using (var factory = new Factory1())
            {
                bool found = false;

                foreach (var adapter in factory.Adapters1)
                {
                    try
                    {
                        var device = new Device(adapter, DeviceCreationFlags.None);
                        
                        foreach (var output in adapter.Outputs)
                        {
                            try
                            {
                                using (var output1 = output.QueryInterface<Output1>())
                                {
                                    _duplicatedOutput = output1.DuplicateOutput(device);
                                    
                                    _device = device;
                                    _width = output.Description.DesktopBounds.Right - output.Description.DesktopBounds.Left;
                                    _height = output.Description.DesktopBounds.Bottom - output.Description.DesktopBounds.Top;
                                    
                                    found = true;
                                    break;
                                }
                            }
                            catch (SharpDXException)
                            {
                                // Unsupported on this output (e.g., discrete GPU not handling desktop)
                            }
                            finally
                            {
                                output.Dispose();
                            }
                        }

                        if (found)
                        {
                            adapter.Dispose();
                            break;
                        }
                        
                        device.Dispose();
                    }
                    finally
                    {
                        if (!found)
                        {
                            adapter.Dispose();
                        }
                    }
                }

                if (!found)
                {
                    throw new Exception("Could not find any DXGI adapter/output that supports Desktop Duplication. Make sure your screen is active.");
                }
            }

            var textureDesc = new Texture2DDescription
            {
                CpuAccessFlags = CpuAccessFlags.Read,
                BindFlags = BindFlags.None,
                Format = Format.B8G8R8A8_UNorm,
                Width = _width,
                Height = _height,
                OptionFlags = ResourceOptionFlags.None,
                MipLevels = 1,
                ArraySize = 1,
                SampleDescription = { Count = 1, Quality = 0 },
                Usage = ResourceUsage.Staging
            };

            _screenTexture = new Texture2D(_device, textureDesc);
        }

        public System.Drawing.Color? TryAcquireNextFrame(int gridWidth = 8, int gridHeight = 8, int marginX = -1, int marginY = -1)
        {
            FloatColor? fc = TryAcquireNextFrameFloat(gridWidth, gridHeight, marginX, marginY);
            return fc?.ToColor();
        }

        public FloatColor? TryAcquireNextFrameFloat(int gridWidth = 8, int gridHeight = 8, int marginX = -1, int marginY = -1)
        {
            if (gridWidth < 1) gridWidth = 1;
            if (gridHeight < 1) gridHeight = 1;
            if (marginX < 0) marginX = _width / 15;
            if (marginY < 0) marginY = _height / 15;

            SharpDX.DXGI.Resource screenResource = null;
            OutputDuplicateFrameInformation duplicateFrameInformation;

            SharpDX.Result result = _duplicatedOutput.TryAcquireNextFrame(10, out duplicateFrameInformation, out screenResource);
            if (result.Failure)
            {
                if (result.Code == SharpDX.DXGI.ResultCode.WaitTimeout.Result.Code)
                {
                    return null; // Expected timeout when screen hasn't updated
                }
                throw new SharpDXException(result); // Rethrow actual access lost or device removed errors
            }

            if (screenResource == null || duplicateFrameInformation.AccumulatedFrames == 0)
            {
                if (screenResource != null)
                {
                    _duplicatedOutput.ReleaseFrame();
                    screenResource.Dispose();
                }
                return null;
            }

            using (var screenTexture2D = screenResource.QueryInterface<Texture2D>())
            {
                _device.ImmediateContext.CopyResource(screenTexture2D, _screenTexture);
            }

            _duplicatedOutput.ReleaseFrame();
            screenResource.Dispose();

            // Calculate average color via unsafe pointer block
            DataBox mapSource = _device.ImmediateContext.MapSubresource(_screenTexture, 0, MapMode.Read, MapFlags.None);

            if (mapSource.DataPointer != IntPtr.Zero)
            {
                int captureWidth = Math.Max(1, _width - 2 * marginX);
                int captureHeight = Math.Max(1, _height - 2 * marginY);

                long rSum = 0, gSum = 0, bSum = 0;
                float stepX = gridWidth > 1 ? (float)captureWidth / (gridWidth - 1) : 0f;
                float stepY = gridHeight > 1 ? (float)captureHeight / (gridHeight - 1) : 0f;

                unsafe
                {
                    byte* sourcePtr = (byte*)mapSource.DataPointer;
                    int pitch = mapSource.RowPitch;

                    for (int y = 0; y < gridHeight; y++)
                    {
                        int pixelY = gridHeight > 1 ? marginY + (int)Math.Round(y * stepY) : marginY + captureHeight / 2;
                        if (pixelY >= _height) pixelY = _height - 1;

                        byte* rowPtr = sourcePtr + (pixelY * pitch);

                        for (int x = 0; x < gridWidth; x++)
                        {
                            int pixelX = gridWidth > 1 ? marginX + (int)Math.Round(x * stepX) : marginX + captureWidth / 2;
                            if (pixelX >= _width) pixelX = _width - 1;

                            int pixelOffset = pixelX * 4;

                            // BGRA format
                            byte b = rowPtr[pixelOffset];
                            byte g = rowPtr[pixelOffset + 1];
                            byte r = rowPtr[pixelOffset + 2];

                            bSum += b;
                            gSum += g;
                            rSum += r;
                        }
                    }
                }

                _device.ImmediateContext.UnmapSubresource(_screenTexture, 0);

                int totalSamples = gridWidth * gridHeight;
                return new FloatColor((float)rSum / totalSamples, (float)gSum / totalSamples, (float)bSum / totalSamples);
            }

            return null;
        }

        public System.Drawing.Color[] TryAcquireAddressableFrame(System.Drawing.Rectangle[] regions, int gridWidth = 8, int gridHeight = 8, int innerMargin = 0)
        {
            if (regions == null || regions.Length == 0) return null;
            if (gridWidth < 1) gridWidth = 1;
            if (gridHeight < 1) gridHeight = 1;
            if (innerMargin < 0) innerMargin = 0;

            SharpDX.DXGI.Resource screenResource = null;
            OutputDuplicateFrameInformation duplicateFrameInformation;

            SharpDX.Result result = _duplicatedOutput.TryAcquireNextFrame(10, out duplicateFrameInformation, out screenResource);
            if (result.Failure)
            {
                if (result.Code == SharpDX.DXGI.ResultCode.WaitTimeout.Result.Code) return null;
                throw new SharpDXException(result);
            }

            if (screenResource == null || duplicateFrameInformation.AccumulatedFrames == 0)
            {
                if (screenResource != null)
                {
                    _duplicatedOutput.ReleaseFrame();
                    screenResource.Dispose();
                }
                return null;
            }

            using (var screenTexture2D = screenResource.QueryInterface<Texture2D>())
            {
                _device.ImmediateContext.CopyResource(screenTexture2D, _screenTexture);
            }

            _duplicatedOutput.ReleaseFrame();
            screenResource.Dispose();

            System.Drawing.Color[] results = new System.Drawing.Color[regions.Length];
            DataBox mapSource = _device.ImmediateContext.MapSubresource(_screenTexture, 0, MapMode.Read, MapFlags.None);

            if (mapSource.DataPointer != IntPtr.Zero)
            {
                unsafe
                {
                    byte* sourcePtr = (byte*)mapSource.DataPointer;
                    int pitch = mapSource.RowPitch;

                    for (int i = 0; i < regions.Length; i++)
                    {
                        System.Drawing.Rectangle rect = regions[i];
                        
                        // Bound the rectangle to screen bounds with inner margin inset
                        int startX = Math.Max(0, rect.X + innerMargin);
                        int startY = Math.Max(0, rect.Y + innerMargin);
                        int endX = Math.Min(_width - 1, rect.Right - innerMargin);
                        int endY = Math.Min(_height - 1, rect.Bottom - innerMargin);

                        int captureWidth = endX - startX;
                        int captureHeight = endY - startY;

                        if (captureWidth < 0 || captureHeight < 0)
                        {
                            startX = Math.Max(0, rect.X);
                            startY = Math.Max(0, rect.Y);
                            endX = Math.Min(_width - 1, rect.Right);
                            endY = Math.Min(_height - 1, rect.Bottom);
                            captureWidth = endX - startX;
                            captureHeight = endY - startY;
                        }

                        if (captureWidth <= 0 || captureHeight <= 0)
                        {
                            results[i] = System.Drawing.Color.Black;
                            continue;
                        }

                        long rSum = 0, gSum = 0, bSum = 0;
                        float stepX = gridWidth > 1 ? (float)captureWidth / (gridWidth - 1) : 0f;
                        float stepY = gridHeight > 1 ? (float)captureHeight / (gridHeight - 1) : 0f;
                        int sampleCount = 0;

                        for (int y = 0; y < gridHeight; y++)
                        {
                            int pixelY = gridHeight > 1 ? startY + (int)Math.Round(y * stepY) : startY + captureHeight / 2;
                            if (pixelY > endY) break;
                            if (pixelY >= _height) pixelY = _height - 1;

                            byte* rowPtr = sourcePtr + (pixelY * pitch);

                            for (int x = 0; x < gridWidth; x++)
                            {
                                int pixelX = gridWidth > 1 ? startX + (int)Math.Round(x * stepX) : startX + captureWidth / 2;
                                if (pixelX > endX) break;
                                if (pixelX >= _width) pixelX = _width - 1;

                                int pixelOffset = pixelX * 4;

                                byte b = rowPtr[pixelOffset];
                                byte g = rowPtr[pixelOffset + 1];
                                byte r = rowPtr[pixelOffset + 2];

                                bSum += b;
                                gSum += g;
                                rSum += r;
                                sampleCount++;
                            }
                        }

                        if (sampleCount > 0)
                        {
                            results[i] = System.Drawing.Color.FromArgb((int)(rSum / sampleCount), (int)(gSum / sampleCount), (int)(bSum / sampleCount));
                        }
                        else
                        {
                            results[i] = System.Drawing.Color.Black;
                        }
                    }
                }

                _device.ImmediateContext.UnmapSubresource(_screenTexture, 0);
                return results;
            }

            return null;
        }

        public void Dispose()
        {
            _screenTexture?.Dispose();
            _duplicatedOutput?.Dispose();
            _device?.Dispose();
        }
    }
}
