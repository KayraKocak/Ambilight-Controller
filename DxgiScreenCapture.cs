using System;
using System.Drawing;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Device = SharpDX.Direct3D11.Device;
using MapFlags = SharpDX.Direct3D11.MapFlags;

namespace AmbilightControllerForm
{
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

        public System.Drawing.Color? TryAcquireNextFrame()
        {
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
                // We sample an 8x8 grid out of the center portion of the screen (excluding 1/15th margins)
                int marginX = _width / 15;
                int marginY = _height / 15;
                int captureWidth = _width - 2 * marginX;
                int captureHeight = _height - 2 * marginY;

                long rSum = 0, gSum = 0, bSum = 0;
                int stepX = captureWidth / 8;
                int stepY = captureHeight / 8;

                unsafe
                {
                    byte* sourcePtr = (byte*)mapSource.DataPointer;
                    int pitch = mapSource.RowPitch;

                    for (int y = 0; y < 8; y++)
                    {
                        int pixelY = marginY + (y * stepY);
                        // Make sure we don't go out of bounds
                        if (pixelY >= _height) pixelY = _height - 1;

                        byte* rowPtr = sourcePtr + (pixelY * pitch);

                        for (int x = 0; x < 8; x++)
                        {
                            int pixelX = marginX + (x * stepX);
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

                return System.Drawing.Color.FromArgb((int)(rSum / 64), (int)(gSum / 64), (int)(bSum / 64));
            }

            return null;
        }

        public System.Drawing.Color[] TryAcquireAddressableFrame(System.Drawing.Rectangle[] regions)
        {
            if (regions == null || regions.Length == 0) return null;

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
                        
                        // Bound the rectangle to screen bounds
                        int startX = Math.Max(0, rect.X);
                        int startY = Math.Max(0, rect.Y);
                        int endX = Math.Min(_width - 1, rect.Right);
                        int endY = Math.Min(_height - 1, rect.Bottom);

                        int captureWidth = endX - startX;
                        int captureHeight = endY - startY;

                        if (captureWidth <= 0 || captureHeight <= 0)
                        {
                            results[i] = System.Drawing.Color.Black;
                            continue;
                        }

                        // We sample an 8x8 grid within this region for performance
                        long rSum = 0, gSum = 0, bSum = 0;
                        int stepX = Math.Max(1, captureWidth / 8);
                        int stepY = Math.Max(1, captureHeight / 8);
                        int sampleCount = 0;

                        for (int y = 0; y < 8; y++)
                        {
                            int pixelY = startY + (y * stepY);
                            if (pixelY > endY) break;

                            byte* rowPtr = sourcePtr + (pixelY * pitch);

                            for (int x = 0; x < 8; x++)
                            {
                                int pixelX = startX + (x * stepX);
                                if (pixelX > endX) break;

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
