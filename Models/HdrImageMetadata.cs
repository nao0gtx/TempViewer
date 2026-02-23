using System;

namespace TransparentWinUI3.Models
{
    public enum ColorPrimaries
    {
        Unknown,
        Bt709,
        Bt2020,
        DisplayP3,
        AdobeRgb,
        ProPhoto
    }

    public enum TransferFunction
    {
        Unknown,
        SRgb,
        Bt709,
        Linear,
        PQ, // SMPTE ST 2084
        HLG
    }

    public enum MatrixCoefficients
    {
        Unknown,
        Identity, // RGB
        Bt601,
        Bt709,
        Bt2020nc
    }

    public enum ColorRange
    {
        Unknown,
        Full,
        Limited
    }

    public class HdrMetadata
    {
        public float? MaxCLL { get; set; }
        public float? MaxFALL { get; set; }
        public float? MinLuminance { get; set; }
        public float? MaxLuminance { get; set; }
    }

    public class GainMapParameters
    {
        public float GainMapMin { get; set; } = 1.0f;
        public float GainMapMax { get; set; } = 2.0f;
        public float Gamma { get; set; } = 1.0f;
        public float OffsetSdr { get; set; } = 0.0f;
        public float OffsetHdr { get; set; } = 0.0f;
        public float HDRCapacityMin { get; set; } = 1.0f;
        public float HDRCapacityMax { get; set; } = 2.0f;
    }

    public class HdrImageMetadata
    {
        public ColorPrimaries Primaries { get; set; } = ColorPrimaries.Unknown;
        public TransferFunction Transfer { get; set; } = TransferFunction.Unknown;
        public MatrixCoefficients Matrix { get; set; } = MatrixCoefficients.Unknown;
        public ColorRange Range { get; set; } = ColorRange.Unknown;
        
        public HdrMetadata MasteringMetadata { get; set; } = new HdrMetadata();
        
        // Gain Map Info
        public bool HasGainMap { get; set; } = false;
        public GainMapParameters? GainMapParams { get; set; }
        
        public string Description { get; set; } = "Unknown";
    }
}
