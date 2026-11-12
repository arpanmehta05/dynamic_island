// Liquid-glass lens for the backdrop image. Compiled to LiquidGlass.ps by tools\compile-shader.ps1.
sampler2D img : register(s0);
float4 bounds : register(c0);   // glass rect in image space: x, y, width, height (y may be negative: top corners are off-screen)
float2 imgSize : register(c1);  // backdrop image size
float radius  : register(c2);   // corner radius of the glass

static const float LENS = 14;       // how deep the bending reaches in from the edge
static const float STRENGTH = 7;    // how far the edge bends the picture, in pixels
static const float BLUR_NEAR = 4;   // frost: radius of the inner / outer tap rings, in pixels
static const float BLUR_FAR = 11;
static const float RIM = 0.05;      // brightness of the light catching the edge

float sdf(float2 p)
{
    float2 hs = bounds.zw * 0.5;
    float2 q = abs(p - (bounds.xy + hs)) - hs + radius;
    return length(max(q, 0)) + min(max(q.x, q.y), 0) - radius;   // negative inside
}

float4 main(float2 uv : TEXCOORD0) : COLOR
{
    float2 p = uv * imgSize;
    float d = sdf(p);

    float2 g = float2(sdf(p + float2(1, 0)) - sdf(p - float2(1, 0)),
                      sdf(p + float2(0, 1)) - sdf(p - float2(0, 1)));
    float2 n = g / max(length(g), 1e-4);                         // outward edge normal

    float t = saturate(1 + d / LENS);                            // 1 at the rim, 0 deep inside
    float2 off = n * (t * t) * STRENGTH / imgSize;               // pull outside content in: the edge "bends" the picture

    // Soft frost: centre + two rings of taps, all following the bend
    float2 px = 1 / imgSize;
    float3 c = tex2D(img, uv + off).rgb;
    float3 sum = c;
    [unroll] for (int i = 0; i < 6; i++)
    {
        float a = i * 1.0471976;
        float2 dir = float2(cos(a), sin(a));
        sum += tex2D(img, uv + off + dir * BLUR_NEAR * px).rgb;
        sum += tex2D(img, uv + off + dir * BLUR_FAR * px).rgb;
    }
    float3 col = sum / 13;

    // Slight dispersion at the rim: red and blue bend by different amounts
    col.r = lerp(col.r, tex2D(img, uv + off * 1.15).r, t * 0.35);
    col.b = lerp(col.b, tex2D(img, uv + off * 0.85).b, t * 0.35);

    // Light catching the rim: strongest where the edge faces the light, with a softer return on the opposite side
    float2 L = normalize(float2(-0.7, -0.7));
    float spec = pow(abs(dot(n, L)), 1.4);
    col += pow(t, 6) * spec * RIM;

    return float4(saturate(col), 1);
}
