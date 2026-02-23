using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using TransparentWinUI3.Helpers;

namespace TransparentWinUI3
{
    public class FFmpegHelper
    {
        private readonly string _ffmpegPath;
        private readonly string _ffprobePath;
        private readonly string _heifDecPath;

        public FFmpegHelper()
        {
            try
            {
                // Get project root ffmpeg path
                string projectRoot = AppDomain.CurrentDomain.BaseDirectory;
                _ffmpegPath = Path.GetFullPath(Path.Combine(projectRoot, "ffmpeg", "bin", "ffmpeg.exe"));
                _ffprobePath = Path.GetFullPath(Path.Combine(projectRoot, "ffmpeg", "bin", "ffprobe.exe"));
                _heifDecPath = Path.GetFullPath(Path.Combine(projectRoot, "ffmpeg", "bin", "libheif", "heif-dec.exe"));
                
                Debug.WriteLine($"[FFmpeg] FFmpeg Path: {_ffmpegPath}");
                Debug.WriteLine($"[FFmpeg] FFprobe Path: {_ffprobePath}");
                Debug.WriteLine($"[FFmpeg] HeifDec Path: {_heifDecPath}");
            }
            catch (Exception)
            {
                // Fallback or ignore
                _ffmpegPath = "ffmpeg.exe";
                _ffprobePath = "ffprobe.exe";
            }
        }

        public string GetFFmpegPath() => _ffmpegPath;
        public string GetHeifDecPath() => _heifDecPath;
        public string GetExifToolPath() => Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg", "bin", "exiftool.exe"));

        public bool IsFFmpegAvailable()
        {
            return File.Exists(_ffmpegPath) && File.Exists(_ffprobePath);
        }

        public async Task<bool> IsValidImageAsync(string imagePath)
        {
            if (!File.Exists(imagePath)) return false;

            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = _ffprobePath,
                    Arguments = $"-v error -select_streams v:0 -show_entries stream=codec_type -of default=noprint_wrappers=1:nokey=1 \"{imagePath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processInfo);
                if (process == null) return false;

                string output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                return output.Trim().Equals("video", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public async Task<ImageInfo?> GetImageInfoAsync(string imagePath)
        {
            if (!File.Exists(imagePath)) return null;

            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = _ffprobePath,
                    Arguments = $"-v error -select_streams v:0 -show_entries stream=width,height,codec_name -of csv=p=0 \"{imagePath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processInfo);
                if (process == null) return null;

                string output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output)) return null;

                var parts = output.Trim().Split(',');
                if (parts.Length >= 3)
                {
                    return new ImageInfo
                    {
                        Width = int.TryParse(parts[0], out int w) ? w : 0,
                        Height = int.TryParse(parts[1], out int h) ? h : 0,
                        CodecName = parts[2],
                        FilePath = imagePath,
                        FileName = Path.GetFileName(imagePath)
                    };
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        public async Task<string?> ConvertImageToPngAsync(string imagePath, string outputPath)
        {
            if (!File.Exists(imagePath)) return null;

            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = _ffmpegPath,
                    Arguments = $"-i \"{imagePath}\" -vframes 1 -f image2 -compression_level 1 \"{outputPath}\" -y",
                    RedirectStandardOutput = false,
                    RedirectStandardError = false,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processInfo);
                if (process == null) return null;

                await process.WaitForExitAsync();

                return process.ExitCode == 0 && File.Exists(outputPath) ? outputPath : null;
            }
            catch
            {
                return null;
            }
        }

        public async Task<string?> ConvertImageToJpegAsync(string imagePath, string outputPath, int quality = 2)
        {
            if (!File.Exists(imagePath)) return null;

            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = _ffmpegPath,
                    Arguments = $"-i \"{imagePath}\" -q:v {quality} \"{outputPath}\" -y",
                    RedirectStandardOutput = false,
                    RedirectStandardError = false,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processInfo);
                if (process == null) return null;

                await process.WaitForExitAsync();

                return process.ExitCode == 0 && File.Exists(outputPath) ? outputPath : null;
            }
            catch
            {
                return null;
            }
        }

        public async Task<string?> ConvertHdrToLinearScRgbAsync(string imagePath, string outputPath)
        {
            if (!File.Exists(imagePath)) return null;

            try
            {
                // Uses zscale to map HDR (PQ/HLG, BT2020) to absolute Linear Light with BT.709 primaries (scRGB standard).
                // -vf zscale=transfer=linear:matrix=709:primaries=709
                // -pix_fmt rgba64le ensures 16-bit integer per channel output, perfectly preserving the massive float range linearly.
                var processInfo = new ProcessStartInfo
                {
                    FileName = _ffmpegPath,
                    Arguments = $"-i \"{imagePath}\" -vf zscale=transfer=linear:matrix=709:primaries=709 -vframes 1 -f image2 -pix_fmt rgba64le -compression_level 1 \"{outputPath}\" -y",
                    RedirectStandardOutput = false,
                    RedirectStandardError = true, // Capture errors for debugging
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processInfo);
                if (process == null) return null;
                
                string errorLog = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0) 
                {
                    Debug.WriteLine($"[FFmpeg] HDR to Linear scRGB failed: {errorLog}");
                    return null;
                }

                return File.Exists(outputPath) ? outputPath : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FFmpeg] Exception during HDR to Linear scRGB: {ex.Message}");
                return null;
            }
        }

        public async Task<string?> ExtractGainMapAsync(string sourcePath)
        {
            if (!File.Exists(sourcePath)) return null;
            string ext = Path.GetExtension(sourcePath).ToLowerInvariant();
            string tempDir = Path.Combine(Path.GetTempPath(), $"gm_extract_{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDir);

            try
            {
                if (ext == ".heic" || ext == ".heif")
                {
                    // HEIC extraction via heif-dec
                    string baseImg = Path.Combine(tempDir, "base.png");
                    var processInfo = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c \"\"{_heifDecPath}\" --with-aux --no-colons \"{sourcePath}\" \"{baseImg}\"\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WorkingDirectory = Path.GetDirectoryName(_heifDecPath)
                    };
                    
                    using var process = Process.Start(processInfo);
                    if (process != null)
                    {
                        await process.WaitForExitAsync();
                        var files = Directory.GetFiles(tempDir, "*");
                        foreach (var f in files)
                        {
                            string name = Path.GetFileName(f).ToLowerInvariant();
                            if ((name.Contains("aux") || name.Contains("gainmap") || name.Contains("-1")) && name != "base.png")
                                return f;
                        }
                    }
                }
                else if (ext == ".jpg" || ext == ".jpeg")
                {
                    // JPEG extraction via exiftool -MPImage2
                    string exiftoolPath = Path.Combine(Path.GetDirectoryName(_ffmpegPath) ?? "", "exiftool.exe");
                    if (File.Exists(exiftoolPath))
                    {
                        string gainPath = Path.Combine(tempDir, "gain_extracted.jpg");
                        var processInfo = new ProcessStartInfo
                        {
                            FileName = exiftoolPath,
                            Arguments = $"-b -MPImage2 \"{sourcePath}\"",
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var process = Process.Start(processInfo);
                        if (process != null)
                        {
                            using var fs = File.Create(gainPath);
                            await process.StandardOutput.BaseStream.CopyToAsync(fs);
                            await process.WaitForExitAsync();
                            if (fs.Length > 100) return gainPath; // Sanity check
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FFmpeg] Gain Map Extraction failed: {ex.Message}");
            }
            return null;
        }

        public async Task<(string? baseImg, string? gainImg)> ExtractSamsungStreamsAsync(string heicPath)
        {
            if (!File.Exists(heicPath)) return (null, null);

            try
            {
                string tempFolder = Path.GetTempPath();
                string baseImg = Path.Combine(tempFolder, $"samsung_base_{Guid.NewGuid()}.png");
                string gainImg = Path.Combine(tempFolder, $"samsung_gain_{Guid.NewGuid()}.png");

                // Extract stream 0 (Primary SDR) and stream 1 (Auxiliary Gain Map)
                // We use PNG to avoid quality loss during this intermediate step
                var processInfo = new ProcessStartInfo
                {
                    FileName = _ffmpegPath,
                    Arguments = $"-i \"{heicPath}\" -map 0:v:0 \"{baseImg}\" -map 0:v:1 \"{gainImg}\" -y",
                    RedirectStandardOutput = false,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processInfo);
                if (process == null) return (null, null);

                string errorLog = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    Debug.WriteLine($"[FFmpeg] Multi-stream extraction failed: {errorLog}");
                    // Attempt fallback to just base if gain map isn't in stream 1
                    return (File.Exists(baseImg) ? baseImg : null, null);
                }

                return (File.Exists(baseImg) ? baseImg : null, File.Exists(gainImg) ? gainImg : null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FFmpeg] Exception in ExtractSamsungStreamsAsync: {ex.Message}");
                return (null, null);
            }
        }

        public async Task<(string? baseImg, string? gainImg, string? xmpPath)> ExtractHeicStreamsAsync(string heicPath)
        {
            if (!File.Exists(heicPath)) return (null, null, null);

            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), $"heif_extract_{Guid.NewGuid()}");
                Directory.CreateDirectory(tempDir);
                
                string baseImg = Path.Combine(tempDir, "base.png");
                string xmpPath = Path.Combine(tempDir, "base.xmp");

                if (!File.Exists(_heifDecPath))
                {
                    Debug.WriteLine($"[FFmpeg] CRITICAL: heif-dec.exe not found at {_heifDecPath}");
                    return (null, null, null);
                }
                
                string? binDir = Path.GetDirectoryName(_heifDecPath);
                Debug.WriteLine($"[FFmpeg] Binary Dir: {binDir}");
                Debug.WriteLine($"[FFmpeg] Binary Exists: {File.Exists(_heifDecPath)}");

                // Use cmd.exe as a wrapper to ensure environment and paths are handled correctly by the shell
                // Added --no-colons to avoid "Invalid argument" errors on Windows when aux names contain URIs
                var processInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"\"{_heifDecPath}\" --with-aux --with-xmp --no-colons \"{heicPath}\" \"{baseImg}\"\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = binDir
                };

                // Add DLL directory to PATH for heif-dec.exe
                string libHeifDir = Path.GetDirectoryName(_heifDecPath) ?? "";
                if (!string.IsNullOrEmpty(libHeifDir))
                {
                    processInfo.EnvironmentVariables["PATH"] = libHeifDir + ";" + Environment.GetEnvironmentVariable("PATH");
                }

                Debug.WriteLine($"[FFmpeg] Executing: {processInfo.FileName} {processInfo.Arguments}");
                
                using var process = Process.Start(processInfo);
                if (process == null)
                {
                    Debug.WriteLine("[FFmpeg] ERROR: Process.Start returned null.");
                    return (null, null, null);
                }

                string stdout = await process.StandardOutput.ReadToEndAsync();
                string stderr = await process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync();
                
                Debug.WriteLine($"[FFmpeg] Process Exited with code: {process.ExitCode}");
                if (!string.IsNullOrEmpty(stdout)) Debug.WriteLine($"[FFmpeg] StdOut: {stdout}");
                if (!string.IsNullOrEmpty(stderr)) Debug.WriteLine($"[FFmpeg] StdErr: {stderr}");

                // Find auxiliary image (Gain Map)
                string gainImg = "";
                var files = Directory.GetFiles(tempDir, "base*");
                Debug.WriteLine($"[FFmpeg] Search results in {tempDir}: {string.Join(", ", files.Select(Path.GetFileName))}");
                
                foreach (var f in files)
                {
                    string name = Path.GetFileName(f).ToLowerInvariant();
                    // Samsung aux maps often have "hdrgainmap" or "aux" in the name
                    // With --no-colons, "urn:com:samsung..." becomes "urn_com_samsung..."
                    if (name.Contains("aux") || name.Contains("gainmap") || name.Contains("-1"))
                    {
                        if (f.EndsWith(".png") || f.EndsWith(".jpg") || f.EndsWith(".jpeg"))
                        {
                            // Skip the base image itself
                            if (name == "base.png" || name == "base.jpg" || name == "base.jpeg") continue;
                            
                            gainImg = f;
                            break;
                        }
                    }
                }

                Debug.WriteLine($"[FFmpeg] Final selection - Base: {baseImg}, Gain: {gainImg}, XMP: {xmpPath}");

                return (
                    File.Exists(baseImg) ? baseImg : null,
                    !string.IsNullOrEmpty(gainImg) ? gainImg : null,
                    File.Exists(xmpPath) ? xmpPath : null
                );
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FFmpeg] libheif extraction failed: {ex}");
                return (null, null, null);
            }
        }

        public enum HdrTransfer { HLG, PQ }

        public async Task<string?> TranscodeHdrToHeicAsync(string imagePath, string outputPath, HdrTransfer transfer = HdrTransfer.HLG)
        {
            if (!File.Exists(imagePath)) return null;
            try
            {
                string trc = (transfer == HdrTransfer.PQ) ? "smpte2084" : "arib-std-b67";
                
                // Transcode pre-encoded 16-bit PNG to 10-bit HEIC directly.
                // -vf format=yuv420p10le: Convert RGB to 10-bit YUV.
                // -x265-params: crucial for HEVC MP4/HEIF signaling.
                var processInfo = new ProcessStartInfo
                {
                    FileName = _ffmpegPath,
                    Arguments = $"-i \"{imagePath}\" -vf \"format=yuv420p10le\" -c:v libx265 -x265-params \"colorprim=bt2020:transfer={trc}:colormatrix=bt2020nc\" -crf 15 -color_primaries bt2020 -color_trc {trc} -colorspace bt2020nc -still-picture 1 \"{outputPath}\" -y",
                    RedirectStandardOutput = false,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processInfo);
                if (process == null) return null;
                string errorLog = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                if (process.ExitCode != 0) Debug.WriteLine($"[FFmpeg] Transcode to HEIC failed: {errorLog}");
                return File.Exists(outputPath) ? outputPath : null;
            }
            catch (Exception ex) { Debug.WriteLine($"[FFmpeg] Exception: {ex.Message}"); return null; }
        }

        public async Task<string?> PackageUltraHdrHeicAsync(string sdrPath, string gmPath, string outputPath, float headroom, bool isApple)
        {
            try
            {
                // Multi-stream HEIC: Primary SDR + Auxiliary Gain Map
                // We use libx265 for both. SDR is 8-bit, GM is 8-bit (Rec709 encoded).
                // Note: Standard support for 'auxiliary' streams in FFmpeg HEIC muxer is minimal,
                // but we'll use stream mapping which is how Samsung/Apple store them.
                var processInfo = new ProcessStartInfo
                {
                    FileName = _ffmpegPath,
                    Arguments = $"-i \"{sdrPath}\" -i \"{gmPath}\" -map 0:v -map 1:v -c:v:0 libx265 -crf 15 -c:v:1 libx265 -crf 20 -tag:v:1 \"hvc1\" -still-picture 1 \"{outputPath}\" -y",
                    RedirectStandardOutput = false,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processInfo);
                if (process == null) return null;
                string errorLog = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                return File.Exists(outputPath) ? outputPath : null;
            }
            catch (Exception ex) { Debug.WriteLine($"[FFmpeg] Exception: {ex.Message}"); return null; }
        }

        public async Task<string?> PackageUltraHdrOfficialAsync(string sdrJpgPath, string gmJpgPath, string outputPath, float maxContentBoost)
        {
            try
            {
                string projectRoot = AppDomain.CurrentDomain.BaseDirectory;
                string ultraHdrAppPath = Path.GetFullPath(Path.Combine(projectRoot, "ffmpeg", "bin", "ultrahdr_app.exe"));
                
                if (!File.Exists(ultraHdrAppPath))
                {
                    Debug.WriteLine($"[UltraHDR] Official app not found at: {ultraHdrAppPath}");
                    return null;
                }

                string tempCfg = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_metadata.cfg");
                
                // CRITICAL: libultrahdr requires hdrCapacityMax > hdrCapacityMin (1.0)
                float capacityMax = Math.Max(1.01f, maxContentBoost);
                float boost = Math.Max(1.01f, maxContentBoost);

                // Construct metadata.cfg
                // maxContentBoost is linear scale (e.g. 4.0 for 2 stops)
                string cfgContent = 
                    $"--maxContentBoost {boost:F6} {boost:F6} {boost:F6}\n" +
                    $"--minContentBoost 1.000000 1.000000 1.000000\n" +
                    $"--gamma 1.000000 1.000000 1.000000\n" +
                    $"--offsetSdr 0.000000 0.000000 0.000000\n" +
                    $"--offsetHdr 0.000000 0.000000 0.000000\n" +
                    $"--hdrCapacityMin 1.000000\n" +
                    $"--hdrCapacityMax {capacityMax:F6}\n" +
                    $"--useBaseColorSpace 1\n";
                
                await File.WriteAllTextAsync(tempCfg, cfgContent);

                // Scenario 4: ultrahdr_app -m 0 -i sdr.jpg -g gm.jpg -f metadata.cfg -z out.jpg
                var processInfo = new ProcessStartInfo
                {
                    FileName = ultraHdrAppPath,
                    Arguments = $"-m 0 -i \"{sdrJpgPath}\" -g \"{gmJpgPath}\" -f \"{tempCfg}\" -z \"{outputPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(ultraHdrAppPath)
                };

                using var process = Process.Start(processInfo);
                if (process == null) return null;

                string stdout = await process.StandardOutput.ReadToEndAsync();
                string stderr = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    Debug.WriteLine($"[UltraHDR] Official packaging failed: {stderr}");
                }

                // Cleanup
                try { File.Delete(tempCfg); } catch { }

                return process.ExitCode == 0 && File.Exists(outputPath) ? outputPath : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UltraHDR] Exception in Official Packaging: {ex.Message}");
                return null;
            }
        }

        public async Task<string?> PackageUltraHdrJpgAsync(string sdrPath, string gmPath, string outputPath, float headroom, bool isApple)
        {
            try
            {
                // First, ensure both SDR and GM are high quality JPEGs
                string tempSdrJpg = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_sdr.jpg");
                string tempGmJpg = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_gm.jpg");

                // Encode SDR to JPG
                var sdrProcess = new ProcessStartInfo
                {
                    FileName = _ffmpegPath,
                    Arguments = $"-i \"{sdrPath}\" -q:v 2 \"{tempSdrJpg}\" -y",
                    UseShellExecute = false, CreateNoWindow = true
                };
                using (var p1 = Process.Start(sdrProcess)) await p1.WaitForExitAsync();

                // Encode Gain Map to JPG
                var gmProcess = new ProcessStartInfo
                {
                    FileName = _ffmpegPath,
                    Arguments = $"-i \"{gmPath}\" -q:v 2 \"{tempGmJpg}\" -y",
                    UseShellExecute = false, CreateNoWindow = true
                };
                using (var p2 = Process.Start(gmProcess)) await p2.WaitForExitAsync();

                // Swap: Use Official Muxer if possible, fallback to custom if Apple-style is strictly required 
                // (Note: Apple trigger is mostly about MakerNote, which ultrahdr_app might not do exactly,
                // but ultrahdr_app produces ISO standard which is better for web/mobile compatibility)
                
                string? result = await PackageUltraHdrOfficialAsync(tempSdrJpg, tempGmJpg, outputPath, headroom);
                
                // Fallback for Apple-specific trigger if requested (though ISO is preferred now)
                if (result == null && isApple)
                {
                    Debug.WriteLine("[UltraHDR] Official packaging failed or fallback requested. Using Manual Muxer for Apple.");
                    var muxer = new HdrJpegMuxer();
                    bool success = await muxer.MuxUltraHdrJpegAsync(tempSdrJpg, tempGmJpg, outputPath, headroom);
                    result = success ? outputPath : null;
                }

                // Cleanup temps
                try { File.Delete(tempSdrJpg); File.Delete(tempGmJpg); } catch { }

                return result;
            }
            catch (Exception ex) { Debug.WriteLine($"[FFmpeg] Exception: {ex.Message}"); return null; }
        }

        public async Task<string?> TranscodeSdrToHeicAsync(string imagePath, string outputPath)
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = _ffmpegPath,
                    Arguments = $"-i \"{imagePath}\" -c:v libx265 -crf 15 -still-picture 1 \"{outputPath}\" -y",
                    RedirectStandardOutput = false,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processInfo);
                if (process == null) return null;
                await process.WaitForExitAsync();
                return File.Exists(outputPath) ? outputPath : null;
            }
            catch (Exception ex) { Debug.WriteLine($"[FFmpeg] Exception: {ex.Message}"); return null; }
        }

        public async Task<string?> TranscodeHdrToAvifAsync(string imagePath, string outputPath, HdrTransfer transfer = HdrTransfer.HLG, bool inputIsLinear = false)
        {
            if (!File.Exists(imagePath)) return null;

            try
            {
                // Core Parameters
                string trc = (transfer == HdrTransfer.PQ) ? "smpte2084" : "arib-std-b67";
                
                // Transcode pre-baked 16-bit PNG to HDR AVIF (10-bit yuv420p10le)
                var processInfo = new ProcessStartInfo
                {
                    FileName = _ffmpegPath,
                    Arguments = $"-i \"{imagePath}\" -vf \"format=yuv420p10le\" -c:v libaom-av1 -color_primaries bt2020 -color_trc {trc} -colorspace bt2020nc -crf 15 -b:v 0 -still-picture 1 \"{outputPath}\" -y",
                    RedirectStandardOutput = false,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processInfo);
                if (process == null) return null;

                string errorLog = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    Debug.WriteLine($"[FFmpeg] Transcode to AVIF failed: {errorLog}");
                    return null;
                }

                return File.Exists(outputPath) ? outputPath : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FFmpeg] Exception in TranscodeHdrToAvifAsync: {ex.Message}");
                return null;
            }
        }

        public async Task<(byte[]? profile, string? description, bool isHdrPotential, Models.ColorPrimaries primaries, Models.TransferFunction transfer, Models.MatrixCoefficients matrix, Models.ColorRange range, Models.HdrMetadata? hdrMetadata, Models.GainMapParameters? gainMapParams)> GetImageColorProfileAsync(string imagePath)
        {
            if (!File.Exists(imagePath)) return (null, null, false, Models.ColorPrimaries.Unknown, Models.TransferFunction.Unknown, Models.MatrixCoefficients.Unknown, Models.ColorRange.Unknown, null, null);

            var resPrimaries = Models.ColorPrimaries.Unknown;
            var resTransfer = Models.TransferFunction.Unknown;
            var resMatrix = Models.MatrixCoefficients.Unknown;
            var resRange = Models.ColorRange.Unknown;
            Models.HdrMetadata? resMetadata = null;

            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = _ffprobePath,
                    Arguments = $"-v quiet -show_format -show_streams -show_entries format_tags:stream_tags:stream_side_data_list -of json \"{imagePath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processInfo);
                if (process == null) return (null, null, false, resPrimaries, resTransfer, resMatrix, resRange, null, null);

                string output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (string.IsNullOrWhiteSpace(output)) 
                {
                    Debug.WriteLine($"[FFmpeg] ffprobe returned empty output for {imagePath}");
                    return (null, null, false, resPrimaries, resTransfer, resMatrix, resRange, null, null);
                }

                string desc = "";
                
                // Regex to find color properties
                var primariesStr = System.Text.RegularExpressions.Regex.Match(output, "\"color_primaries\":\\s*\"([^\"]+)\"").Groups[1].Value;
                var transferStr = System.Text.RegularExpressions.Regex.Match(output, "\"color_transfer\":\\s*\"([^\"]+)\"").Groups[1].Value;
                var spaceStr = System.Text.RegularExpressions.Regex.Match(output, "\"color_space\":\\s*\"([^\"]+)\"").Groups[1].Value;
                var rangeStr = System.Text.RegularExpressions.Regex.Match(output, "\"color_range\":\\s*\"([^\"]+)\"").Groups[1].Value;

                // Map Primaries
                if (primariesStr == "bt709") resPrimaries = Models.ColorPrimaries.Bt709;
                else if (primariesStr == "bt2020") resPrimaries = Models.ColorPrimaries.Bt2020;
                else if (primariesStr == "smpte432" || primariesStr == "smpte431") resPrimaries = Models.ColorPrimaries.DisplayP3;

                // Map Transfer
                if (transferStr == "smpte2084") resTransfer = Models.TransferFunction.PQ;
                else if (transferStr == "arib-std-b67") resTransfer = Models.TransferFunction.HLG;
                else if (transferStr == "iec61966-2-1" || transferStr == "srgb") resTransfer = Models.TransferFunction.SRgb;
                else if (transferStr == "bt709") resTransfer = Models.TransferFunction.Bt709;

                // Map Matrix
                if (spaceStr == "bt2020nc") resMatrix = Models.MatrixCoefficients.Bt2020nc;
                else if (spaceStr == "bt709") resMatrix = Models.MatrixCoefficients.Bt709;

                // Map Range
                if (rangeStr == "limited") resRange = Models.ColorRange.Limited;
                else if (rangeStr == "full") resRange = Models.ColorRange.Full;

                // Parse Metadata (CLL/Mastering)
                resMetadata = new Models.HdrMetadata();
                bool hasMetadata = false;

                // Content Light Level
                var maxCllMatch = System.Text.RegularExpressions.Regex.Match(output, "\"max_content\":\\s*(\\d+)");
                var maxFallMatch = System.Text.RegularExpressions.Regex.Match(output, "\"max_average\":\\s*(\\d+)");
                
                if (maxCllMatch.Success) 
                { 
                    resMetadata.MaxCLL = float.Parse(maxCllMatch.Groups[1].Value); 
                    hasMetadata = true;
                }
                if (maxFallMatch.Success) 
                {
                    resMetadata.MaxFALL = float.Parse(maxFallMatch.Groups[1].Value);
                    hasMetadata = true;
                }

                // Mastering Display Metadata
                // Example: "min_luminance": "50/10000", "max_luminance": "40000000/10000"
                var minLumMatch = System.Text.RegularExpressions.Regex.Match(output, "\"min_luminance\":\\s*\"(\\d+)/(\\d+)\"");
                var maxLumMatch = System.Text.RegularExpressions.Regex.Match(output, "\"max_luminance\":\\s*\"(\\d+)/(\\d+)\"");

                if (minLumMatch.Success)
                {
                    float num = float.Parse(minLumMatch.Groups[1].Value);
                    float den = float.Parse(minLumMatch.Groups[2].Value);
                    if (den != 0) resMetadata.MinLuminance = num / den;
                    hasMetadata = true;
                }
                if (maxLumMatch.Success)
                {
                    float num = float.Parse(maxLumMatch.Groups[1].Value);
                    float den = float.Parse(maxLumMatch.Groups[2].Value);
                    if (den != 0) resMetadata.MaxLuminance = num / den;
                    hasMetadata = true;
                }
                
                if (!hasMetadata) resMetadata = null;

                bool isHdrPotential = false;
                if (!string.IsNullOrEmpty(primariesStr))
                {
                    if (resPrimaries == Models.ColorPrimaries.Bt709 && resTransfer == Models.TransferFunction.SRgb) desc = "sRGB (FFmpeg)";
                    else if (resPrimaries == Models.ColorPrimaries.Bt2020 && resTransfer == Models.TransferFunction.PQ) { desc = "HDR10 / BT.2020 PQ (FFmpeg)"; isHdrPotential = true; }
                    else if (resPrimaries == Models.ColorPrimaries.Bt2020 && resTransfer == Models.TransferFunction.HLG) { desc = "HLG / BT.2020 (FFmpeg)"; isHdrPotential = true; }
                    else if (resPrimaries == Models.ColorPrimaries.Bt2020) { desc = "BT.2020 (FFmpeg)"; isHdrPotential = true; }
                    else desc = $"{primariesStr}/{transferStr} (FFmpeg)";
                }

                if (resMetadata?.MaxCLL > 200 || resMetadata?.MaxLuminance > 200) 
                {
                    isHdrPotential = true;
                    if (!desc.Contains("HDR")) desc += $" (HDR Potential)";
                }

                Debug.WriteLine($"[FFmpeg] Color info: P={primariesStr}, T={transferStr}, R={rangeStr}, HDR={isHdrPotential}");
                if (resMetadata != null) Debug.WriteLine($"[FFmpeg] Metadata: MaxCLL={resMetadata.MaxCLL}, MaxLum={resMetadata.MaxLuminance}");

                // Check for ICC Profile
                if (output.Contains("\"side_data_type\": \"ICC profile\"") || output.Contains("\"side_data_type\": \"ICC Profile\""))
                {
                    var nameMatch = System.Text.RegularExpressions.Regex.Match(output, "\"name\":\\s*\"([^\"]+)\"");
                    if (nameMatch.Success) desc = nameMatch.Groups[1].Value + " (FFmpeg)";
                    else if (string.IsNullOrEmpty(desc)) desc = "Embedded ICC (FFmpeg)";
                }

                // NEW: Detect Samsung/Apple/ISO HDR Gain Map auxiliary data or XMP Gain Map
                // We use case-insensitive check and include 'apple', 'samsung', 'headroom' as heuristics
                string lowerOutput = output.ToLowerInvariant();
                bool hasGainMapContext = lowerOutput.Contains("hdrgainmap") || 
                                        lowerOutput.Contains("gainmapmax") || 
                                        lowerOutput.Contains("tmap") || 
                                        lowerOutput.Contains("gain_map") ||
                                        lowerOutput.Contains("apple:photo:2020:aux:hdrgainmap") ||
                                        lowerOutput.Contains("hdr headroom");

                bool isHdrHeic = (imagePath.EndsWith(".heic", StringComparison.OrdinalIgnoreCase) || imagePath.EndsWith(".heif", StringComparison.OrdinalIgnoreCase)) && 
                                      (lowerOutput.Contains("samsung") || lowerOutput.Contains("galaxy") || lowerOutput.Contains("apple") || lowerOutput.Contains("iphone"));

                Models.GainMapParameters? resGainMap = null;
                if (hasGainMapContext || isHdrHeic)
                {
                    isHdrPotential = true;
                    if (string.IsNullOrEmpty(desc) || !desc.Contains("HDR"))
                        desc = String.IsNullOrEmpty(desc) ? (lowerOutput.Contains("apple") ? "Apple HDR (Gain Map)" : "Single-Stream/Gain-Map HDR") : (desc + " [HDR Potential]").Trim();
                    
                    Debug.WriteLine($"[FFmpeg] Detected HDR potential. GainMapContext={hasGainMapContext}, IsHdrHeic={isHdrHeic}");

                    // Extract Apple Headroom if possible
                    // Example regex for Apple MakerNote tag or ffprobe side data
                    var headroomMatch = System.Text.RegularExpressions.Regex.Match(output, "(?:HDR Headroom|hdr_headroom)\"?\\s*[:=]\\s*\"?([\\d\\.-]+)");
                    if (headroomMatch.Success)
                    {
                        float headroom = float.Parse(headroomMatch.Groups[1].Value);
                        resGainMap = new Models.GainMapParameters
                        {
                            GainMapMax = MathF.Log2(headroom),
                            GainMapMin = 0.0f,
                            Gamma = 1.0f
                        };
                        Debug.WriteLine($"[FFmpeg] Extracted Apple Headroom: {headroom} -> GainMapMax: {resGainMap.GainMapMax}");
                    }
                }

                return (null, desc, isHdrPotential, resPrimaries, resTransfer, resMatrix, resRange, resMetadata, resGainMap);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FFmpeg] Error extracting profile: {ex.Message}");
            }

            return (null, null, false, resPrimaries, resTransfer, resMatrix, resRange, null, null);
        }
    }

    public class ImageInfo
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public string CodecName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;

        public override string ToString()
        {
            return $"{FileName} ({Width}x{Height}, {CodecName})";
        }
    }
}
