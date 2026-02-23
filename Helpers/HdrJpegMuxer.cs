using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

namespace TransparentWinUI3.Helpers
{
    public class HdrJpegMuxer
    {
        /// <summary>
        /// Muxes an SDR JPEG and a Gain Map JPEG into a single Ultra HDR (v1) compatible JPEG.
        /// This uses the Multi-Picture Format (MPF) approach.
        /// </summary>
        public async Task<bool> MuxUltraHdrJpegAsync(string sdrPath, string gainMapPath, string outputPath, float headroom)
        {
            try
            {
                byte[] sdrBytes = await File.ReadAllBytesAsync(sdrPath);
                byte[] gmBytes = await File.ReadAllBytesAsync(gainMapPath);

                // 1. Prepare XMP Metadata
                string xmp = CreateUltraHdrXmp(headroom, gmBytes.Length);
                byte[] xmpSegment = CreateApp1Segment(xmp, "http://ns.adobe.com/xap/1.0/\0");

                // 2. Prepare MPF (Multi-Picture Format) segment entry
                int mpfPayloadSize = 92;
                int mpfTotalSegmentSize = mpfPayloadSize + 4; // Marker(2) + Length(2)
                
                // We'll insert our headers after the first segment if it's JFIF (APP0)
                // Otherwise after SOI.
                int insertionPos = 2; // Start after SOI
                if (sdrBytes.Length > 6 && sdrBytes[2] == 0xFF && sdrBytes[3] == 0xE0) // APP0
                {
                    int app0Len = (sdrBytes[4] << 8) | sdrBytes[5];
                    insertionPos = 2 + app0Len + 2; 
                }

                // Offset of TIFF header "II" from file start:
                // insertionPos + APP2 Marker(2) + Length(2) + Identifier "MPF\0"(4)
                int tiffHeaderOffsetInFile = insertionPos + xmpSegment.Length + 4 + 4;
                
                // Total primary image size
                int primarySize = sdrBytes.Length + xmpSegment.Length + mpfTotalSegmentSize;
                
                // Offset of second image relative to TIFF header
                int offsetToSecond = primarySize - tiffHeaderOffsetInFile;
                
                byte[] mpfSegment = CreateMpfSegment(primarySize, gmBytes.Length, offsetToSecond);

                // 3. Construct and write the combined JPEG
                using (var outputStream = File.Create(outputPath))
                {
                    // Write up to insertion point
                    outputStream.Write(sdrBytes, 0, insertionPos);
                    
                    // Insert our new headers
                    outputStream.Write(xmpSegment, 0, xmpSegment.Length);
                    outputStream.Write(mpfSegment, 0, mpfSegment.Length);
                    
                    // Write the rest of the SDR
                    outputStream.Write(sdrBytes, insertionPos, sdrBytes.Length - insertionPos);
                    
                    if (outputStream.Position != primarySize)
                        Debug.WriteLine($"[HdrJpegMuxer] Warning: Size mismatch! {outputStream.Position} != {primarySize}");

                    outputStream.Write(gmBytes, 0, gmBytes.Length); // Append secondary image
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HdrJpegMuxer] Error: {ex.Message}");
                return false;
            }
        }

        private byte[] CreateApp1Segment(string content, string namespaceUri)
        {
            byte[] nsBytes = Encoding.UTF8.GetBytes(namespaceUri);
            byte[] contentBytes = Encoding.UTF8.GetBytes(content);
            
            int totalPayload = nsBytes.Length + contentBytes.Length;
            byte[] segment = new byte[totalPayload + 4];

            segment[0] = 0xFF;
            segment[1] = 0xE1;
            segment[2] = (byte)((totalPayload + 2) >> 8);
            segment[3] = (byte)((totalPayload + 2) & 0xFF);
            
            Array.Copy(nsBytes, 0, segment, 4, nsBytes.Length);
            Array.Copy(contentBytes, 0, segment, 4 + nsBytes.Length, contentBytes.Length);
            
            return segment;
        }

        private byte[] CreateMpfSegment(int primarySize, int secondarySize, int offsetToSecond)
        {
            byte[] segment = new byte[92 + 4];
            
            segment[0] = 0xFF;
            segment[1] = 0xE2; // APP2
            segment[2] = 0x00; segment[3] = 0x5E; // Length 94 (92 payload + 2 length field)

            int p = 4;
            Array.Copy(Encoding.ASCII.GetBytes("MPF\0"), 0, segment, p, 4); p += 4;
            segment[p++] = 0x49; segment[p++] = 0x49; segment[p++] = 0x2A; segment[p++] = 0x00; // II*
            segment[p++] = 0x08; segment[p++] = 0x00; segment[p++] = 0x00; segment[p++] = 0x00; // Offset (8)

            segment[p++] = 0x03; segment[p++] = 0x00; // Entries (3)

            // B000: Version
            segment[p++] = 0x00; segment[p++] = 0xB0; segment[p++] = 0x07; segment[p++] = 0x00;
            segment[p++] = 0x04; segment[p++] = 0x00; segment[p++] = 0x00; segment[p++] = 0x00;
            segment[p++] = 0x30; segment[p++] = 0x31; segment[p++] = 0x30; segment[p++] = 0x30; // 0100

            // B001: Image Count
            segment[p++] = 0x01; segment[p++] = 0xB0; segment[p++] = 0x04; segment[p++] = 0x00;
            segment[p++] = 0x01; segment[p++] = 0x00; segment[p++] = 0x00; segment[p++] = 0x00;
            segment[p++] = 0x02; segment[p++] = 0x00; segment[p++] = 0x00; segment[p++] = 0x00;

            // B002: Image List Offset
            segment[p++] = 0x02; segment[p++] = 0xB0; segment[p++] = 0x07; segment[p++] = 0x00;
            segment[p++] = 0x20; segment[p++] = 0x00; segment[p++] = 0x00; segment[p++] = 0x00;
            segment[p++] = 0x32; segment[p++] = 0x00; segment[p++] = 0x00; segment[p++] = 0x00; // II + 50 bytes

            segment[p++] = 0x00; segment[p++] = 0x00; segment[p++] = 0x00; segment[p++] = 0x00;

            // Image 1 Entry (at II+50)
            segment[p++] = 0x03; segment[p++] = 0x00; segment[p++] = 0x01; segment[p++] = 0x00; // Primary
            segment[p++] = (byte)primarySize; segment[p++] = (byte)(primarySize >> 8); segment[p++] = (byte)(primarySize >> 16); segment[p++] = (byte)(primarySize >> 24);
            segment[p++] = 0x00; segment[p++] = 0x00; segment[p++] = 0x00; segment[p++] = 0x00; // Offset 0
            segment[p++] = 0x00; segment[p++] = 0x00; segment[p++] = 0x00; segment[p++] = 0x00;

            // Image 2 Entry (at II+66)
            segment[p++] = 0x00; segment[p++] = 0x00; segment[p++] = 0x02; segment[p++] = 0x00; // Secondary
            segment[p++] = (byte)secondarySize; segment[p++] = (byte)(secondarySize >> 8); segment[p++] = (byte)(secondarySize >> 16); segment[p++] = (byte)(secondarySize >> 24);
            segment[p++] = (byte)offsetToSecond; segment[p++] = (byte)(offsetToSecond >> 8); segment[p++] = (byte)(offsetToSecond >> 16); segment[p++] = (byte)(offsetToSecond >> 24);
            segment[p++] = 0x00; segment[p++] = 0x00; segment[p++] = 0x00; segment[p++] = 0x00;
            
            return segment;
        }




        private string CreateUltraHdrXmp(float headroom, int secondaryLength)
        {
            // "Super XMP" covering ISO 21496-1, Google v1, and Apple compatibility
            float gainMax = MathF.Log2(headroom);

            return $@"<?xpacket begin=""?"" id=""W5M0MpCehiHzreSzNTczkc9d""?>
<x:xmpmeta xmlns:x=""adobe:ns:meta/"" x:xmptk=""XMP Core 5.5.0"">
 <rdf:RDF xmlns:rdf=""http://www.w3.org/1999/02/22-rdf-syntax-ns#"">
  <rdf:Description rdf:about=""""
    xmlns:hdrgm=""http://ns.adobe.com/hdr-gain-map/1.0/""
    xmlns:apple-hdrgm=""http://ns.apple.com/HDRGainMap/1.0/""
    xmlns:gainmap=""http://ns.google.com/photos/1.0/gainmap/""
    xmlns:Container=""http://ns.google.com/photos/1.0/container/""
    xmlns:Item=""http://ns.google.com/photos/1.0/container/item/""
    hdrgm:Version=""1.0""
    hdrgm:GainMapMax=""{gainMax:F4}""
    hdrgm:GainMapMin=""0.0""
    hdrgm:Gamma=""1.0""
    hdrgm:OffsetSdr=""0.0""
    hdrgm:OffsetHdr=""0.0""
    hdrgm:HDRCapacityMin=""0.0""
    hdrgm:HDRCapacityMax=""{gainMax:F4}""
    hdrgm:BaseRendition=""SDR""
    apple-hdrgm:Version=""1.0""
    gainmap:Version=""1.0""
    gainmap:GainMapMax=""{gainMax:F4}""
    gainmap:GainMapMin=""0.0""
    gainmap:Gamma=""1.0"">
   <Container:Directory>
    <rdf:Seq>
     <rdf:li rdf:parseType=""Resource"">
      <Container:Item Item:Mime=""image/jpeg"" Item:Semantic=""Primary""/>
     </rdf:li>
     <rdf:li rdf:parseType=""Resource"">
      <Container:Item Item:Mime=""image/jpeg"" Item:Semantic=""GainMap"">
       <Container:Item Item:Length=""{secondaryLength}""/>
      </Container:Item>
     </rdf:li>
    </rdf:Seq>
   </Container:Directory>
  </rdf:Description>
 </rdf:RDF>
</x:xmpmeta>
<?xpacket end=""w""?>";
        }
    }
}
