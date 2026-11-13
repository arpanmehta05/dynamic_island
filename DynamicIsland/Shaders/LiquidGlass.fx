// Liquid glass for the island. Compiled to LiquidGlass.ps by tools\compile-shader.ps1.
// One pass draws everything the material needs: the frosted, refracted backdrop inside the shape, an anti-aliased edge,
// the light caught by the rim, and the soft shadow the island casts on the desktop below it.
sampler2D img : register(s0);
float4 bounds : register(c0);   // glass rect in image space: x, y, width, height
float2 imgSize : register(c1);  // backdrop image size
float radius  : register(c2);   // corner radius of the glass
float tint    : register(c3);   // 0 = liquid (nearly nothing but the edges) ... 0.3 clear ... 0.6 frosted ... 1 = dark smoked glass

static const float LENS = 16;       // how deep the bending reaches in from the edge
static const float STRENGTH = 9;    // how far the edge bends the picture, in pixels
static const float BLUR_NEAR = 7;   // frost: radius of the inner / outer tap rings, in pixels (the backdrop is already low-res)
static const float BLUR_FAR = 16;
static const float SHADOW = 24;     // how far the shadow reaches
static const float DROP = 5;        // the shadow sits a little below the island

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

    // Frost: centre + two rings of taps, all following the bend
    float2 px = lerp(0.3, 1, saturate(tint * 3.4)) / imgSize;    // liquid glass barely blurs
    float3 sum = tex2D(img, uv + off).rgb;
    [unroll] for (int i = 0; i < 8; i++)
    {
        float a = i * 0.7853982;
        float2 dir = float2(cos(a), sin(a));
        sum += tex2D(img, uv + off + dir * BLUR_NEAR * px).rgb;
        sum += tex2D(img, uv + off + float2(dir.y, -dir.x) * 0.92 * BLUR_FAR * px + dir * 0.38 * BLUR_FAR * px).rgb;
    }
    float3 col = sum / 17;

    // Slight dispersion at the rim: red and blue bend by different amounts
    col.r = lerp(col.r, tex2D(img, uv + off * 1.2).r, t * 0.4);
    col.b = lerp(col.b, tex2D(img, uv + off * 0.8).b, t * 0.4);

    // Colours behind the glass are calmed a little (more so the darker the glass), and brightness is folded down so white
    // text always reads: dark content passes almost untouched, bright content settles at a ceiling that depends on the tint.
    float luma = dot(col, float3(0.299, 0.587, 0.114));
    col = lerp(luma, col, lerp(1.0, 0.6, tint));
    float k = pow(abs(tint), 0.6);
    float gain = lerp(0.94, 0.30, k);
    float cap = lerp(0.60, 0.13, k);
    float l = max(luma * gain, 1e-3);
    col *= gain * cap * (1 - exp(-l / cap)) / l;
    col += 0.03;                                                 // the glass has a little body of its own, even over black

    // Light: a soft glow hugging the rim, strongest where the edge faces the light (top-left) with a fainter return opposite,
    // and a crisp hairline right on the edge
    float facing = dot(n, normalize(float2(-0.55, -0.83)));
    float rim = pow(t, 7);
    col += rim * (0.05 + 0.20 * pow(saturate(facing), 2) + 0.09 * pow(saturate(-facing), 2));
    col += saturate(1 - abs(d + 0.9)) * (0.10 + 0.16 * saturate(facing) + 0.06 * saturate(-facing));

    float a = saturate(0.5 - d);                                 // anti-aliased edge

    float s = saturate(1 - max(sdf(p - float2(0, DROP)), 0) / SHADOW);
    float shadow = 0.34 * s * s * s;

    return float4(saturate(col) * a, a + (1 - a) * shadow);     // premultiplied
}
