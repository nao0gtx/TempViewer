// GainMapShader.hlsl
// Calculates the ratio map between HDR and SDR images.

#define D2D_INPUT_COUNT 2
#define D2D_INPUT0_SIMPLE_SAMPLING
#define D2D_INPUT1_SIMPLE_SAMPLING

#include "d2d1effecthelpers.hlsli"

// Headroom parameter (passed as a constant buffer or per-pixel)
// For simplicity, we can use a hardcoded value if we can't easily pass it.
// But we'll try to use a standard constant buffer.
cbuffer Constants : register(b0)
{
    float Headroom : packoffset(c0.x);
};

D2D_PS_ENTRY(main)
{
    float4 hdr = D2DGetInput(0);
    float4 sdr = D2DGetInput(1);
    
    // 1. Get luminance (max RGB)
    float hdr_m = max(hdr.r, max(hdr.g, hdr.b));
    float sdr_m = max(sdr.r, max(sdr.g, sdr.b));
    
    // 2. Calculate ratio
    float ratio = (sdr_m > 0.0) ? (hdr_m / sdr_m) : 1.0;
    
    // 3. Normalize to [0..1] range
    // Normalized = (log2(ratio) - log2(min)) / (log2(max) - log2(min))
    // Google/ISO use log space for better distribution.
    // min = 1.0 (0 stops), max = Headroom (stops)
    float log_ratio = log2(max(1.0, ratio));
    float log_max = log2(max(1.1, Headroom));
    
    float norm = saturate(log_ratio / log_max);
    
    // 4. Apply gamma for storage (Rec.709-ish)
    // Most browsers expect the gain map to have a gamma applied.
    float gamma_norm = pow(norm, 1.0 / 2.2);

    return float4(gamma_norm, gamma_norm, gamma_norm, 1.0);
}
