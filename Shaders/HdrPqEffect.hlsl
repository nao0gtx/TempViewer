#define D2D_INPUT_COUNT 1
#define D2D_INPUT0_SIMPLE
#include "d2d1effecthelpers.hlsli"

// Float parameter for max nits (e.g., 10000.0)
float maxNits : register(c0);
// Float parameter for paper white (e.g., 80.0)
float paperWhite : register(c1);

// BT.2020 to Rec.709 conversion matrix
static const float3x3 bt2020To709 = float3x3(
    1.6605f, -0.5876f, -0.0728f,
   -0.1246f,  1.1329f, -0.0083f,
   -0.0182f, -0.1006f,  1.1187f
);

// ST 2084 PQ Constants
static const float m1 = 0.1593017578125f; 
static const float m2 = 78.84375f;        
static const float c1 = 0.8359375f;       
static const float c2 = 18.8515625f;      
static const float c3 = 18.6875f;         

float PqEotf(float v)
{
    float v_pow = pow(max(v, 0.0f), 1.0f / m2);
    float num = max(v_pow - c1, 0.0f);
    float den = max(c2 - c3 * v_pow, 0.000001f);
    return pow(num / den, 1.0f / m1);
}

D2D_PS_ENTRY(main)
{
    float4 color = D2DGetInput(0);
    
    // 1. Decode PQ to Linear
    float3 linearColor = float3(
        PqEotf(color.r),
        PqEotf(color.g),
        PqEotf(color.b)
    );
    
    // 2. Gamut Mapping (BT.2020 -> Rec.709)
    linearColor = mul(bt2020To709, linearColor);
    
    // 3. Scale to scRGB
    float boost = maxNits / paperWhite;
    linearColor *= boost;
    
    return float4(linearColor, color.a);
}
