using System;
using System.Diagnostics;
using System.Threading.Tasks;
using ComputeSharp;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.IO;

namespace TransparentWinUI3
{
    public class GpuImageProcessor
    {
        /// <summary>
        /// 使用 GPU (Compute Shader) 进行反马赛克处理
        /// </summary>
        /// <summary>
        /// 使用 GPU (Compute Shader) 进行反马赛克处理
        /// </summary>
        public async Task<WriteableBitmap?> RenderBayerToBitmapAsync(byte[] bayerData, int width, int height, LibRawNative.BayerPattern pattern, float exposure = 1.0f)
        {
            try
            {
                // ... same as before until shader execution ...
                // 1. 准备数据: byte[] -> ushort[]
                // LibRaw 返回的是 byte 数组，实际上是 ushort 数据
                ushort[] pixelData = new ushort[bayerData.Length / 2];
                // Handle potential size mismatch if width/height changed manually
                int copyLen = Math.Min(bayerData.Length, pixelData.Length * 2);
                Buffer.BlockCopy(bayerData, 0, pixelData, 0, copyLen);

                // 2. 获取 GPU 设备
                GraphicsDevice device = GraphicsDevice.GetDefault();
                
                // 3. 创建纹理
                float[] floatData = new float[width * height];
                // Parallel conversion for speed
                // Check bounds: floatData might be smaller/larger than pixelData if user changed W/H
                int len = Math.Min(floatData.Length, pixelData.Length);
                
                Parallel.For(0, len, i =>
                {
                    floatData[i] = pixelData[i] / 65535.0f;
                });

                using ReadOnlyTexture2D<float> inputTexture = device.AllocateReadOnlyTexture2D<float>(floatData, width, height);
                using ReadWriteTexture2D<uint> outputTexture = device.AllocateReadWriteTexture2D<uint>(width, height);
 
                // 4. 计算 Pattern 索引
                int patternIdx = 0;
                if (pattern == LibRawNative.BayerPattern.BGGR) patternIdx = 1;
                else if (pattern == LibRawNative.BayerPattern.GRBG) patternIdx = 2;
                else if (pattern == LibRawNative.BayerPattern.GBRG) patternIdx = 3;
 
                // 5. 运行 Shader
                device.For(width, height, new BilinearDebayerShader(inputTexture, outputTexture, patternIdx, exposure));

                // 6. 读取结果 (ReadBack)
                uint[] resultPixels = new uint[width * height];
                outputTexture.CopyTo(resultPixels); 

                // 7. 创建 WriteableBitmap
                var bitmap = new WriteableBitmap(width, height);
                using (var stream = bitmap.PixelBuffer.AsStream())
                {
                    byte[] rawBytes = MemoryMarshal.Cast<uint, byte>(resultPixels).ToArray();
                    await stream.WriteAsync(rawBytes, 0, rawBytes.Length);
                }
                
                return bitmap;
                /*
                return null;
                */
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GPU] Error: {ex.Message}");
                return null;
            }
        }
    }

    [AutoConstructor]
    [EmbeddedBytecode(DispatchAxis.XY)]
    public readonly partial struct BilinearDebayerShader : IComputeShader
    {
        public readonly ReadOnlyTexture2D<float> Input;
        public readonly ReadWriteTexture2D<uint> Output;
        public readonly int Pattern;
        public readonly float Exposure;

        public void Execute()
        {
            int col = ThreadIds.X;
            int row = ThreadIds.Y;
            int w = Input.Width;
            int h = Input.Height;
            if (col >= w || row >= h) return;

            // Pattern: 0=RGGB, 1=BGGR, 2=GRBG, 3=GBRG
            int colorType = 1; 
            bool isRowEven = (row & 1) == 0;
            bool isColEven = (col & 1) == 0;

            if (Pattern == 0) // RGGB
            {
                if (isRowEven) colorType = isColEven ? 0 : 1;
                else           colorType = isColEven ? 1 : 2;
            }
            else if (Pattern == 1) // BGGR
            {
                if (isRowEven) colorType = isColEven ? 2 : 1;
                else           colorType = isColEven ? 1 : 0;
            }
            else if (Pattern == 2) // GRBG
            {
                if (isRowEven) colorType = isColEven ? 1 : 0;
                else           colorType = isColEven ? 2 : 1;
            }
            else // GBRG
            {
                if (isRowEven) colorType = isColEven ? 1 : 2;
                else           colorType = isColEven ? 0 : 1;
            }
            
            float kR = 0, kG = 0, kB = 0;
            float wR = 0, wG = 0, wB = 0; 

            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = col + dx;
                    int ny = row + dy;
                    
                    if (nx >= 0 && nx < w && ny >= 0 && ny < h)
                    {
                        float val = Input[new int2(nx, ny)];
                        int nColor = 1; 
                        bool nrEven = (ny & 1) == 0;
                        bool ncEven = (nx & 1) == 0;
                        
                        if (Pattern == 0)      nColor = nrEven ? (ncEven ? 0 : 1) : (ncEven ? 1 : 2);
                        else if (Pattern == 1) nColor = nrEven ? (ncEven ? 2 : 1) : (ncEven ? 1 : 0);
                        else if (Pattern == 2) nColor = nrEven ? (ncEven ? 1 : 0) : (ncEven ? 2 : 1);
                        else                   nColor = nrEven ? (ncEven ? 1 : 2) : (ncEven ? 0 : 1);

                        if (nColor == 0) { kR += val; wR += 1.0f; }
                        else if (nColor == 1) { kG += val; wG += 1.0f; }
                        else { kB += val; wB += 1.0f; }
                    }
                }
            }
            
            float valCenter = Input[new int2(col, row)];
            if (colorType == 0) { kR = valCenter; wR = 1.0f; }
            if (colorType == 1) { kG = valCenter; wG = 1.0f; }
            if (colorType == 2) { kB = valCenter; wB = 1.0f; }

            float r = wR > 0 ? kR / wR : 0;
            float g = wG > 0 ? kG / wG : 0;
            float b = wB > 0 ? kB / wB : 0;
            
            // float exposure = 4.0f; // OLD
            r = Hlsl.Pow(r * Exposure, 1.0f/2.2f);
            g = Hlsl.Pow(g * Exposure, 1.0f/2.2f);
            b = Hlsl.Pow(b * Exposure, 1.0f/2.2f);

            // Clamp 0-1
            r = Hlsl.Clamp(r, 0, 1);
            g = Hlsl.Clamp(g, 0, 1);
            b = Hlsl.Clamp(b, 0, 1);

            // Pack unorm8 BGRA into uint
            // Layout: [BB GG RR AA] (Little Endian uint)
            // B is lowest byte
            uint uB = (uint)(b * 255.0f);
            uint uG = (uint)(g * 255.0f);
            uint uR = (uint)(r * 255.0f);
            uint uA = 255;
            
            Output[new int2(col, row)] = uB | (uG << 8) | (uR << 16) | (uA << 24);
        }
    }
}
