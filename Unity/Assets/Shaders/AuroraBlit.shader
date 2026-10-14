// Realistic Volumetric Aurorae (RVA) — Copyright (c) 2026 HereComesTheBoom ("Realistic Volumetric Aurorae")
// SPDX-License-Identifier: CC-BY-NC-SA-4.0  (see LICENSE.txt)
// =============================================================================
// AuroraBlit.shader — composites the low-resolution aurora buffer onto the
// flight camera (full-screen blits from command buffers, see AuroraRenderer.cs)
//
// The aurora buffer (_MainTex) holds  rgb = the aurora the ray meets before
// it first crosses the cloud-top shell (HDR)   a = aurora irradiance on the
// cloud deck under that pixel (HDR, from the emission map). A second buffer
// (_FarTex) holds the aurora beyond that shell. Pass 0 adds
//     near + far × T
// where T is EVE's own per-pixel cloud transmittance (its `colorBuffer`
// alpha), so the clouds occlude exactly the aurora behind them — looking up
// from the ground all of it, from orbit only the band beyond the limb seen
// through the limb clouds — at every altitude, with no composite-order
// switch (v3.12). With occlusion off (or no EVE buffer) T = 1 and the
// composite event decides, as before. The upsample is depth-aware (v3.16):
// far.a holds the scene depth each texel was cut against, and a screen pixel
// only blends texels whose depth agrees with its own.
//
//   Pass 0  aurora, additive onto the scene
//   Pass 1  replaces the scene with the aurora buffer (debug: "aurora only";
//           the irradiance is shown as a flat tint)
//   Pass 2  CLOUD LIGHT, additive. The cloud layer as EVE shaded it comes from
//           one of three sources (_CloudGrabMode):
//             2  EVE's own reconstructed cloud buffer, which its command buffer
//                publishes as the global texture `colorBuffer` (public source:
//                DeferredRaymarchedVolumetricCloudsRenderer.cs). Verified
//                in-game: rgb = lit cloud colour (black on the night side —
//                his clouds get no ambient there), a = transmittance (1 =
//                clear). So in this mode the light uses the OPACITY, 1 − a:
//                a cloud top is a white diffuse surface, and opacity carries
//                its shape and thickness. _CloudBufferFormat selects how the
//                alpha is read in case a version stores it the other way.
//                Opacity below _CloudMinOpacity counts as sky: EVE's haze
//                and ambience layers leave ~0.1 everywhere (v3.10).
//             1  the difference of two GrabPass textures _AuroraBeforeClouds /
//                _AuroraAfterClouds taken at render queues 2997 / 2999 around
//                EVE's composite quad (2998).
//             0  the difference of two command-buffer copies _AuroraPreCloud /
//                _AuroraSceneCopy taken at two camera events.
//           Multiplying the aurora irradiance by that cloud amount lights the
//           cloud tops in the aurora's colour with the clouds' own shape,
//           without touching the cloud shader.
//   Pass 3  debug: left half = the cloud amount pass 2 is lighting (white =
//           cloud, after the haze floor); right half = the aurora's irradiance
//           on the cloud-top shell, in the light's colour — where the cloud
//           light would land and how strong, clouds or not (v3.11)
//
// UVs come from the fragment's own screen pixel position (VPOS), so the
// orientation does not depend on how Unity set up the blit quad: the aurora
// buffer was rasterised with the same camera matrices as the screen, so
// pixel (x, y) of the screen reads pixel (x, y) of the buffer. If a platform
// still shows it upside-down, _FlipY = 1 mirrors it (live toggle in the GUI).
//
// Tonemap: 1 - exp(-c): bright cores roll off to white instead of clipping.
// =============================================================================
Shader "RVA/AuroraBlit"
{
    Properties
    {
        _MainTex  ("Aurora RT — before the cloud shell (rgb), irradiance (a)", 2D) = "black" {}
        _FarTex   ("Aurora RT — beyond the cloud shell (set by C#)", 2D) = "black" {}
        // 1 = the far part of the aurora is multiplied by EVE's per-pixel cloud
        // transmittance (buffer mode only): the clouds occlude what is behind
        // them and nothing else, from every altitude
        _CloudOcclusion ("Clouds occlude the aurora per pixel (0 or 1)", Float) = 1.0
        // 1 = depth-aware upsample: each screen pixel takes only the buffer
        // texels whose scene depth (far.a, km) agrees with its own, so the
        // silhouettes the march cut against (vessel, terrain, horizon) come
        // out at screen resolution instead of as blurred buffer texels
        _EdgeAware      ("Depth-aware upsample (0 or 1)", Float) = 1.0
        // Depth (km) within which the aurora march clips against the scene (5 % of the
        // planet radius, 30 km on Kerbin; set by C#). The upsample is depth-aware only
        // there: nearer pixels and texels must agree, everything farther is "sky" (v3.24)
        _NearDepthKm    ("Near depth (km)", Float) = 30.0
        _Exposure ("Exposure", Float) = 1.0
        _FlipY    ("Flip Y (0 or 1)", Float) = 0.0
        // Blend toward a 5-tap blur of the low-res buffer: hides sample jitter
        // grain and the blockiness of the upsample at the cost of a little
        // sharpness (0 = plain bilinear, 1 = fully smoothed)
        _Smoothing ("Upsample smoothing", Range(0, 1)) = 0.5
        _GlowColor      ("Cloud light colour", Color) = (0.4, 0.9, 0.5, 1)
        _CloudLightGain ("Cloud light gain", Float) = 1.0
        // 1 when the camera is above the cloud tops; the underside factor when
        // it is below them (thin cloud glows a little from transmitted light)
        _CloudLightView ("Cloud light view factor", Float) = 1.0
        _AuroraPreCloud ("Scene before the clouds (set by C#)", 2D) = "black" {}
        _CloudGrabMode  ("0 = camera-event copies, 1 = render-queue grabs, 2 = EVE cloud buffer", Float) = 2.0
        _GrabFlipY      ("Flip Y of the grabs / EVE buffer (0 or 1)", Float) = 0.0
        // How EVE's buffer stores its alpha: 0 / 1 = opacity, 2 = transmittance (verified for the current volumetrics)
        _CloudBufferFormat ("EVE buffer format", Float) = 2.0
        // Mode 2: opacity below this gets no light. EVE's haze / ambience
        // layers leave a small opacity over the whole sky (~0.1 at the KSC);
        // without the threshold the light tints the clear sky, with it only
        // real cloud is lit (0..threshold → 0, threshold..1 → 0..1)
        _CloudMinOpacity ("Min cloud opacity lit", Range(0, 0.9)) = 0.15
    }

    SubShader
    {
        Tags { "Queue" = "Overlay" "IgnoreProjector" = "True" }
        ZTest Always
        ZWrite Off
        Cull Off

        CGINCLUDE
        #include "UnityCG.cginc"

        sampler2D _MainTex;
        float4 _MainTex_TexelSize;
        sampler2D _FarTex;
        float _CloudOcclusion;
        float _EdgeAware;
        float _NearDepthKm;
        UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
        float _Exposure;
        float _FlipY;
        float _Smoothing;
        float4 _GlowColor;
        float _CloudLightGain;
        float _CloudLightView;
        sampler2D _AuroraPreCloud;
        float _CloudGrabMode;
        float _GrabFlipY;
        float _CloudBufferFormat;
        float _CloudMinOpacity;
        // Global set by the command buffer: the camera target after the clouds (event mode)
        sampler2D _AuroraSceneCopy;
        // Globals written by the GrabPasses of the AuroraGrab* objects (queue mode)
        sampler2D _AuroraBeforeClouds;
        sampler2D _AuroraAfterClouds;
        // Global written by EVE's cloud renderer: its reconstructed cloud buffer (mode 2)
        sampler2D colorBuffer;

        struct v2f { float2 dummy : TEXCOORD0; };

        // Position goes out as a separate SV_POSITION so VPOS can be read in frag
        v2f vert (float4 vertex : POSITION, float2 texcoord : TEXCOORD0, out float4 outpos : SV_POSITION)
        {
            v2f o;
            o.dummy = texcoord;
            outpos = UnityObjectToClipPos(vertex);
            return o;
        }

        float2 screenUV(float2 pixel)
        {
            float2 uv = pixel / _ScreenParams.xy;
            uv.y = lerp(uv.y, 1.0 - uv.y, _FlipY);
            return uv;
        }

        // Aurora buffer at this pixel, smoothed: rgb = aurora before the cloud
        // shell, a = irradiance (both HDR × exposure)
        float4 bufferSample(float2 uv)
        {
            float4 c = tex2D(_MainTex, uv);
            if (_Smoothing > 0.001)
            {
                float2 px = _MainTex_TexelSize.xy;
                float4 n = tex2D(_MainTex, uv + float2( px.x, 0))
                         + tex2D(_MainTex, uv + float2(-px.x, 0))
                         + tex2D(_MainTex, uv + float2(0,  px.y))
                         + tex2D(_MainTex, uv + float2(0, -px.y));
                c = lerp(c, (c * 2.0 + n) / 6.0, _Smoothing);
            }
            return c * _Exposure;
        }

        // Both aurora buffers at this screen pixel, upsampled DEPTH-AWARE (v3.16).
        // The buffers are a fraction of the screen, so the occlusion edges the
        // march cut against (vessel, nearby terrain, the horizon) used to come
        // back as blurred, stair-stepped texels. Each buffer texel carries the
        // scene depth its march used (far.a, km); the screen pixel reads its own
        // depth and weights the 3×3 texels around it by a tent (radius 1 = plain
        // bilinear, wider with _Smoothing) × depth agreement, so texels on the
        // other side of a silhouette drop out. A pixel nearer than everything
        // the buffer saw is an occluder thinner than a texel (a strut, a chute
        // line) and gets no aurora at all; a pixel with no agreeing texel at
        // all falls back to the plain tent. Texels with depth 0 (depth
        // occlusion off, nothing marched, or — since v3.24 — scene depth beyond
        // the near range the march clips against) are "sky": they agree with
        // every sky pixel, and a near pixel never takes them nor a sky pixel a
        // near texel. The near range is _NearDepthKm (5 % of the planet radius),
        // the same rule the aurora shader uses for its cut, so over terrain seen
        // from orbit the upsample is the plain smoothed one again instead of
        // breaking into per-texel grain along the horizon.
        void auroraSample(float2 uv, out float4 nearOut, out float3 farOut)
        {
            float2 texel = _MainTex_TexelSize.xy;
            float2 pos   = uv * _MainTex_TexelSize.zw - 0.5;     // texel space, centre-aligned
            float2 c     = floor(pos + 0.5);                      // nearest texel
            float  r     = 1.0 + 1.2 * _Smoothing;                // tent radius (texels)
            bool   aware = _EdgeAware > 0.5;
            float  zHR   = 0.0;
            bool   pixNear = false;
            if (aware)
            {
                zHR = min(LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv)), 1e7) * 0.001;
                pixNear = zHR < _NearDepthKm;
                if (!pixNear) zHR = 0.0;                                // sky / far scene
            }

            float4 nSum = 0, nTent = 0;
            float3 fSum = 0, fTent = 0;
            float  wSum = 0, wTent = 0, zMin = 1e9;
            [unroll]
            for (int j = -1; j <= 1; j++)
            {
                [unroll]
                for (int i = -1; i <= 1; i++)
                {
                    float2 t  = c + float2(i, j);
                    float2 d  = abs(pos - t);
                    float  wb = max(0.0, 1.0 - d.x / r) * max(0.0, 1.0 - d.y / r);
                    if (wb > 0.0)
                    {
                        float2 tuv = (t + 0.5) * texel;
                        float4 nn  = tex2Dlod(_MainTex, float4(tuv, 0, 0));
                        float4 ff  = tex2Dlod(_FarTex,  float4(tuv, 0, 0));
                        float  wd  = 1.0;
                        if (aware)
                        {
                            bool texNear = ff.a > 0.0;
                            if (texNear && pixNear)
                            {
                                float rel = abs(ff.a - zHR) / max(min(ff.a, zHR), 1e-4);
                                wd   = saturate(1.0 - rel * 10.0);       // full agreement within 0 %, none beyond 10 %
                                zMin = min(zMin, ff.a);
                            }
                            else if (texNear != pixNear) wd = 0.0;      // one side of a silhouette, the other not
                            // both sky: plain tent weight
                        }
                        nSum  += nn * (wb * wd);  fSum  += ff.rgb * (wb * wd);  wSum  += wb * wd;
                        nTent += nn * wb;         fTent += ff.rgb * wb;         wTent += wb;
                    }
                }
            }
            if (wSum > 1e-4)                      { nearOut = nSum / wSum;  farOut = fSum / wSum; }
            else if (aware && pixNear && zHR < zMin * 0.9) { nearOut = 0;   farOut = 0; }
            else                                  { nearOut = nTent / max(wTent, 1e-4); farOut = fTent / max(wTent, 1e-4); }
            nearOut *= _Exposure;
            farOut  *= _Exposure;
        }

        // EVE's cloud buffer as (premultiplied colour, opacity)
        float4 eveCloud(float2 uv)
        {
            float2 guv = uv;
            guv.y = lerp(guv.y, 1.0 - guv.y, _GrabFlipY);
            float4 cb = tex2D(colorBuffer, guv);
            float opacity = (_CloudBufferFormat > 1.5) ? saturate(1.0 - cb.a) : saturate(cb.a);
            float3 c = (_CloudBufferFormat > 0.5) ? cb.rgb * opacity : cb.rgb;
            return float4(max(c, 0.0), opacity);
        }

        // The cloud layer EVE drew this frame (premultiplied colour, ≥ 0).
        // One return per function: fxc tracks an early return inside a branch
        // as a "potentially uninitialized" result and warns.
        float3 cloudDelta(float2 uv)
        {
            float3 delta = float3(0, 0, 0);
            if (_CloudGrabMode > 1.5)
                delta = eveCloud(uv).rgb;
            else if (_CloudGrabMode > 0.5)
            {
                float2 guv = uv;
                guv.y = lerp(guv.y, 1.0 - guv.y, _GrabFlipY);
                delta = max(tex2D(_AuroraAfterClouds, guv).rgb - tex2D(_AuroraBeforeClouds, guv).rgb, 0.0);
            }
            else
                delta = max(tex2D(_AuroraSceneCopy, uv).rgb - tex2D(_AuroraPreCloud, uv).rgb, 0.0);
            return delta;
        }

        // How much cloud the light falls on at this pixel (0..1-ish).
        // Buffer mode: the opacity above the haze floor (opacity up to the
        // floor is sky, not cloud). Grab modes: the luminance of the cloud
        // layer the frame difference captured.
        float cloudAmount(float2 uv)
        {
            float amount = 0.0;
            if (_CloudGrabMode > 1.5)
            {
                float t = clamp(_CloudMinOpacity, 0.0, 0.95);
                amount = saturate((eveCloud(uv).a - t) / (1.0 - t));
            }
            else
                amount = dot(cloudDelta(uv), float3(0.299, 0.587, 0.114));
            return amount;
        }

        // Transmittance of EVE's clouds along this pixel's ray (1 = clear).
        // Buffer mode with occlusion on: from the cloud buffer's alpha; otherwise
        // 1, and the composite event alone decides the order.
        float cloudTransmittance(float2 uv)
        {
            float T = 1.0;
            if (_CloudOcclusion > 0.5 && _CloudGrabMode > 1.5)
            {
                float2 guv = uv;
                guv.y = lerp(guv.y, 1.0 - guv.y, _GrabFlipY);
                float a = tex2D(colorBuffer, guv).a;
                T = (_CloudBufferFormat > 1.5) ? saturate(a) : saturate(1.0 - a);
            }
            return T;
        }

        // The whole aurora at this pixel as it should appear: the part in front
        // of the clouds plus the part behind them through their transmittance
        float3 auroraAt(float2 uv)
        {
            float4 nearC; float3 farC;
            auroraSample(uv, nearC, farC);
            return nearC.rgb + farC * cloudTransmittance(uv);
        }

        float3 tonemap(float3 c) { return 1.0 - exp(-c); }
        ENDCG

        // 0: additive composite — the aurora
        Pass
        {
            Blend One One
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   3.0
            fixed4 frag (v2f i, UNITY_VPOS_TYPE screenPos : VPOS) : SV_Target
            {
                return fixed4(tonemap(auroraAt(screenUV(screenPos.xy))), 1.0);
            }
            ENDCG
        }

        // 1: debug — aurora buffer only (aurora + irradiance as a flat tint, scene hidden)
        Pass
        {
            Blend One Zero
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   3.0
            fixed4 frag (v2f i, UNITY_VPOS_TYPE screenPos : VPOS) : SV_Target
            {
                float2 uv = screenUV(screenPos.xy);
                return fixed4(tonemap(auroraAt(uv) + _GlowColor.rgb * bufferSample(uv).a), 1.0);
            }
            ENDCG
        }

        // 2: cloud light, additive
        Pass
        {
            Blend One One
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   3.0
            fixed4 frag (v2f i, UNITY_VPOS_TYPE screenPos : VPOS) : SV_Target
            {
                float2 uv = screenUV(screenPos.xy);
                float  E  = bufferSample(uv).a;
                if (E <= 0.0005) return fixed4(0, 0, 0, 1);
                float  cloud = cloudAmount(uv);
                float3 c = _GlowColor.rgb * (E * cloud * _CloudLightGain * _CloudLightView);
                return fixed4(tonemap(c), 1.0);
            }
            ENDCG
        }

        // 3: debug, scene hidden. Left half: the cloud amount pass 2 lights
        //    (white = cloud; in buffer mode after the haze floor, so clear sky
        //    should be black). Right half: the irradiance the aurora puts on
        //    the cloud-top shell under each pixel, in the light's colour and at
        //    the light's strength — the glow you would get on a solid cloud.
        Pass
        {
            Blend One Zero
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   3.0
            fixed4 frag (v2f i, UNITY_VPOS_TYPE screenPos : VPOS) : SV_Target
            {
                float2 uv = screenUV(screenPos.xy);
                if (uv.x > 0.5)
                {
                    float E = bufferSample(uv).a;
                    return fixed4(tonemap(_GlowColor.rgb * (E * _CloudLightGain * _CloudLightView)), 1.0);
                }
                float amount = (_CloudGrabMode > 1.5) ? cloudAmount(uv) : saturate(cloudAmount(uv) * 4.0);
                return fixed4(amount, amount, amount, 1.0);
            }
            ENDCG
        }
    }
}
