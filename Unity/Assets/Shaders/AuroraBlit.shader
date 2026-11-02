// Realistic Volumetric Aurorae (RVA). Copyright (c) 2026 HereComesTheBoom ("Realistic Volumetric Aurorae")
// SPDX-License-Identifier: CC-BY-NC-SA-4.0  (see LICENSE.txt)
Shader "RVA/AuroraBlit"
{
    Properties
    {
        _MainTex  ("Aurora RT, before the cloud shell (rgb), slab chord (a)", 2D) = "black" {}
        _FarTex   ("Aurora RT, beyond the cloud shell (set by C#)", 2D) = "black" {}

        _CloudOcclusion ("Cloud occlusion from EVE's volumetrics buffer: 0 off (none, or stale), 1 live, with the horizon boost", Float) = 1.0

        _EdgeAware      ("Depth-aware upsample (0 or 1)", Float) = 1.0

        _NearDepthKm    ("Near depth (km)", Float) = 30.0
        _Exposure ("Exposure", Float) = 1.0
        _FlipY    ("Flip Y (0 or 1)", Float) = 0.0

        _Smoothing ("Upsample smoothing", Range(0, 1)) = 0.5
        _GrabFlipY      ("Flip Y of EVE's buffer (0 or 1)", Float) = 0.0

        _CloudBufferFormat ("EVE buffer format", Float) = 2.0

        _OccluderHeightMap    ("Occluder terrain height map (set by C#)", 2D) = "black" {}
        _OccluderHeightParams ("Occluder datum radius, map on (set by C#)", Vector) = (0, 0, 0, 0)
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
        float4 _CameraDepthTexture_TexelSize;
        float _Exposure;
        float _FlipY;
        float _Smoothing;
        float _GrabFlipY;
        float _CloudBufferFormat;

        sampler2D colorBuffer;

        sampler2D _CurNear, _CurFar, _CurAux;
        sampler2D _HistNear, _HistFar;
        float4    _CurTexelSize;
        float4    _HistTexelSize;
        float4    _TemporalJitter;
        float4    _TemporalParams;
        float4    _TemporalParams2;
        float4    _CamPosRel;
        float     _CloudGraze;
        sampler2D _LimbModelDepth;
        float     _LimbModelOn;
        float     _GroundCapKm;
        sampler2D _Cloud2DBlack, _Cloud2DWhite;
        float     _Cloud2DOn;
        float     _BlitDebug;
        float4    _ShellParams;
        float4x4  _PrevVP;

        struct v2f { float2 dummy : TEXCOORD0; };

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

        bool terrainDrawnNear(float2 uv, float zRef)
        {
            float2 px = _CameraDepthTexture_TexelSize.xy;
            [unroll]
            for (int k = 0; k < 8; k++)
            {
                float  d   = (k < 4) ? 8.0 : 24.0;
                int    q   = k & 3;
                float2 off = (q == 0) ? float2(d, 0) : (q == 1) ? float2(-d, 0) : (q == 2) ? float2(0, d) : float2(0, -d);
                float raw2 = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv + off * px);
                if (raw2 <= 0.0 || raw2 >= 1.0) continue;
                float z2 = LinearEyeDepth(raw2);
                if (abs(z2 - zRef) <= 0.3 * zRef) return true;
            }
            return false;
        }

        void auroraSample(float2 uv, out float4 nearOut, out float3 farOut, out float farVis)
        {
            float2 texel = _MainTex_TexelSize.xy;
            float2 pos   = uv * _MainTex_TexelSize.zw - 0.5;
            float2 c     = floor(pos + 0.5);
            float  r     = 1.0 + 1.2 * _Smoothing;
            bool   aware = _EdgeAware > 0.5;
            float  zHR   = 0.0;
            bool   pixNear = false, pixCleared = true, pixGround = false;
            if (aware)
            {
                float raw  = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv);
                pixCleared = raw <= 0.0 || raw >= 1.0;
                zHR = min(LinearEyeDepth(raw), 1e7) * 0.001;
                pixNear = !pixCleared && zHR < _NearDepthKm;
                pixGround = !pixCleared && !pixNear && zHR < _GroundCapKm;

                if (pixCleared && _LimbModelOn > 0.5)
                {
                    float iM = tex2Dlod(_LimbModelDepth, float4(uv, 0, 0)).r;
                    if (iM > 0.0)
                    {
                        float zM = 1.0 / iM;
                        if (!terrainDrawnNear(uv, zM)) { pixGround = true; zHR = min(zM, 1e7) * 0.001; }
                    }
                }
            }
            farVis = (pixNear || pixGround) ? 0.0 : 1.0;

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
                            bool texNear  = ff.a > 0.0;
                            bool texDatum = ff.a < 0.0;
                            if (texNear && pixNear)
                            {
                                float rel = abs(ff.a - zHR) / max(min(ff.a, zHR), 1e-4);
                                wd   = saturate(1.0 - rel * 10.0);
                                zMin = min(zMin, ff.a);
                            }
                            else if (texNear != pixNear) wd = 0.0;
                            else if (texDatum && !pixGround) wd = 0.0;

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

        float cloudBufferT(float2 uv)
        {
            float2 guv = uv;
            guv.y = lerp(guv.y, 1.0 - guv.y, _GrabFlipY);
            float a = tex2D(colorBuffer, guv).a;
            return (_CloudBufferFormat > 1.5) ? saturate(a) : saturate(1.0 - a);
        }

        float grazeExponent(float2 uv)
        {
            if (_CloudGraze <= 0.0) return 1.0;
            float chord = tex2D(_MainTex, uv).a;
            return 1.0 + _CloudGraze * max(chord - 1.0, 0.0);
        }

        float cloudTransmittance(float2 uv)
        {
            float T = 1.0;
            if (_CloudOcclusion > 0.5)
            {
                float Tb = cloudBufferT(uv);
                if (Tb < 1.0 && Tb > 0.0) Tb = pow(Tb, min(grazeExponent(uv), 64.0));
                T *= Tb;
            }
            if (_Cloud2DOn > 0.5)
            {
                float3 b = tex2D(_Cloud2DBlack, uv).rgb, w = tex2D(_Cloud2DWhite, uv).rgb;
                T *= saturate(dot(w - b, 1.0 / 3.0));
            }
            return T;
        }

        float3 auroraAt(float2 uv)
        {
            float4 nearC; float3 farC; float farVis;
            auroraSample(uv, nearC, farC, farVis);
            return nearC.rgb + farC * (farVis * cloudTransmittance(uv));
        }

        float3 tonemap(float3 c) { return 1.0 - exp(-c); }

        struct v2fTri { float2 dummy : TEXCOORD0; };

        v2fTri vertTri (uint vid : SV_VertexID, out float4 outpos : SV_POSITION)
        {
            v2fTri o;
            o.dummy = float2(0, 0);
            float2 p = float2((vid == 1) ? 3.0 : -1.0, (vid == 2) ? 3.0 : -1.0);
            outpos = float4(p, 0.5, 1.0);
            return o;
        }

        struct ResolveOut
        {
            float4 near : SV_Target0;
            float4 far  : SV_Target1;
        };

        ResolveOut fragResolve (v2fTri i, UNITY_VPOS_TYPE screenPos : VPOS)
        {
            ResolveOut o;
            float2 hp  = screenPos.xy;
            float2 huv = hp * _HistTexelSize.xy;

            float2 xr   = hp * (_CurTexelSize.zw * _HistTexelSize.xy);
            float2 j    = _TemporalJitter.xy;
            float2 kf   = floor(xr - j);
            float2 dd   = xr - (kf + 0.5 + j);
            float  wRec = exp(-dot(dd, dd) * _TemporalParams.z);
            float2 cuv  = (kf + 0.5) * _CurTexelSize.xy;
            float4 cN   = tex2Dlod(_CurNear, float4(cuv, 0, 0));
            float4 cF   = tex2Dlod(_CurFar,  float4(cuv, 0, 0));
            float2 aux  = tex2Dlod(_CurAux, float4(cuv, 0, 0)).rg;
            float  tMean = aux.r, tSpread = aux.g;

            float2 buv  = (xr - j) * _CurTexelSize.xy;
            float4 bN   = tex2Dlod(_CurNear, float4(buv, 0, 0));
            float4 bF   = tex2Dlod(_CurFar,  float4(buv, 0, 0));

            float2 ndc = huv * 2.0 - 1.0;
            ndc.y *= -_ProjectionParams.x;
            float3 right = UNITY_MATRIX_V[0].xyz, up = UNITY_MATRIX_V[1].xyz, fwd = -UNITY_MATRIX_V[2].xyz;
            float  tanH = 1.0 / max(abs(unity_CameraProjection._m00), 1e-6);
            float  tanV = 1.0 / max(abs(unity_CameraProjection._m11), 1e-6);
            float3 rd   = normalize(fwd + right * (ndc.x * tanH) + up * (ndc.y * tanV));
            float  t    = tMean;
            if (t <= 0.0)
            {

                float3 ro   = _CamPosRel.xyz;
                float  b    = dot(ro, rd);
                float  c    = dot(ro, ro) - _ShellParams.x * _ShellParams.x;
                float  disc = b * b - c;
                t = _ShellParams.y;
                if (disc > 0.0)
                {
                    float sd = sqrt(disc), tn = -b - sd, tf = -b + sd;
                    float th = (c > 0.0) ? ((tn > 0.0) ? tn : tf) : tf;
                    if (th > 0.0) t = th;
                }
            }
            float3 P   = _CamPosRel.xyz + rd * t;
            float4 pc  = mul(_PrevVP, float4(P, 1.0));
            bool valid = _TemporalParams.w > 0.5 && pc.w > 1e-6;
            float2 pn  = pc.xy / max(pc.w, 1e-6);
            float2 puv = float2(pn.x * 0.5 + 0.5, pn.y * (-_ProjectionParams.x) * 0.5 + 0.5);
            valid = valid && puv.x >= 0.0 && puv.x <= 1.0 && puv.y >= 0.0 && puv.y <= 1.0;

            float4 hN = float4(0, 0, 0, 0), hF = float4(0, 0, 0, 0);
            float  conf = 1.0;
            if (valid)
            {
                hN = tex2Dlod(_HistNear, float4(puv, 0, 0));
                hF = tex2Dlod(_HistFar,  float4(puv, 0, 0));

                float dc = cF.a, dp = hF.a;
                bool same = (dc == 0.0 && dp == 0.0) || (dc < 0.0 && dp < 0.0)
                         || (dc > 0.0 && dp > 0.0 && abs(dc - dp) <= 0.1 * max(dc, dp));
                valid = same;

                if (tSpread > 0.0)
                {
                    float  t1 = max(t - tSpread, 0.01 * t), t2 = t + tSpread;
                    float4 p1 = mul(_PrevVP, float4(_CamPosRel.xyz + rd * t1, 1.0));
                    float4 p2 = mul(_PrevVP, float4(_CamPosRel.xyz + rd * t2, 1.0));
                    float2 u1 = p1.xy / max(p1.w, 1e-6), u2 = p2.xy / max(p2.w, 1e-6);
                    float  spreadTex = length((u1 - u2) * 0.5 * _HistTexelSize.zw);
                    conf = saturate(1.0 - (spreadTex - _TemporalParams2.x) / max(_TemporalParams2.y - _TemporalParams2.x, 0.01));
                }

                float  moveTex   = length((puv - huv) * _HistTexelSize.zw);
                float2 edgeTex   = min(puv, 1.0 - puv) * _HistTexelSize.zw;
                float  marginTex = clamp(2.0 * moveTex, 1.0, 0.04 * _HistTexelSize.w);
                conf *= saturate(min(edgeTex.x, edgeTex.y) / marginTex);
            }
            if (!valid || conf <= 0.0)
            {
                o.near = float4(bN.rgb, cN.a);
                o.far  = float4(bF.rgb, cF.a);
                return o;
            }

            float3 mN = 0, qN = 0, mF = 0, qF = 0;
            [unroll]
            for (int y = -1; y <= 1; y++)
            {
                [unroll]
                for (int x = -1; x <= 1; x++)
                {
                    float2 uv2 = (kf + 0.5 + float2(x, y)) * _CurTexelSize.xy;
                    float3 n = tex2Dlod(_CurNear, float4(uv2, 0, 0)).rgb;
                    float3 f = tex2Dlod(_CurFar,  float4(uv2, 0, 0)).rgb;
                    mN += n; qN += n * n; mF += f; qF += f * f;
                }
            }
            mN /= 9.0; qN /= 9.0; mF /= 9.0; qF /= 9.0;
            float3 sN = sqrt(max(qN - mN * mN, 0.0)), sF = sqrt(max(qF - mF * mF, 0.0));

            float3 hT    = hN.rgb + hF.rgb;
            float3 mT    = mN + mF;
            float3 fCur  = mF / max(mT, 1e-6);
            float3 fHist = hF.rgb / max(hT, 1e-6);
            float  wCur  = saturate(dot(mT, 1.0 / 3.0) * 1e4);
            float3 fFar  = lerp(fHist, fCur, wCur);
            hN.rgb = hT * (1.0 - fFar);
            hF.rgb = hT * fFar;

            float  g  = _TemporalParams.y;
            float3 hNc = clamp(hN.rgb, mN - g * sN, mN + g * sN);
            float3 hFc = clamp(hF.rgb, mF - g * sF, mF + g * sF);

            hNc = lerp(bN.rgb, hNc, conf);
            hFc = lerp(bF.rgb, hFc, conf);

            float a = (1.0 - _TemporalParams.x) * wRec;
            o.near = float4(lerp(hNc, cN.rgb, a), cN.a);
            o.far  = float4(lerp(hFc, cF.rgb, a), cF.a);
            return o;
        }
        ENDCG

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
                if (_BlitDebug > 0.5)
                {

                    float  Tb = (_CloudOcclusion > 0.5) ? cloudBufferT(uv) : 1.0;
                    float  Te = cloudTransmittance(uv);
                    float4 nearC; float3 farC; float farVis;
                    auroraSample(uv, nearC, farC, farVis);
                    float  far = saturate(dot(farC, 1.0 / 3.0) * _Exposure) * farVis;
                    return fixed4(Tb, Te + far * (1.0 - Te), Te, 1.0);
                }
                return fixed4(tonemap(auroraAt(uv)), 1.0);
            }
            ENDCG
        }

        Pass
        {
            Blend Off
            CGPROGRAM
            #pragma vertex   vertTri
            #pragma fragment fragResolve
            #pragma target   4.0
            ENDCG
        }

        Pass
        {
            BlendOp Max
            Blend One One
            ZTest Always
            ZWrite Off
            Cull Off
            CGPROGRAM
            #pragma vertex   vertDepth
            #pragma fragment fragDepth
            #pragma target   3.0
            sampler2D _OccluderHeightMap;
            float4    _OccluderHeightParams;
            struct adDepth  { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2fDepth { float4 pos : SV_POSITION; float eye : TEXCOORD0; };
            v2fDepth vertDepth (adDepth v)
            {
                v2fDepth o;
                float3 p = v.vertex.xyz;
                if (_OccluderHeightParams.y > 0.5)
                {

                    float h = tex2Dlod(_OccluderHeightMap, float4(v.uv, 0.0, 0.0)).r;
                    p = normalize(p) * (_OccluderHeightParams.x + h);
                }
                o.pos = UnityObjectToClipPos(float4(p, 1.0));
                #if defined(UNITY_REVERSED_Z)
                o.pos.z = max(o.pos.z, 0.0);
                #else
                o.pos.z = min(o.pos.z, o.pos.w);
                #endif
                o.eye = -UnityObjectToViewPos(p).z;
                return o;
            }
            float4 fragDepth (v2fDepth i) : SV_Target { return float4(1.0 / max(i.eye, 1.0), 0.0, 0.0, 1.0); }
            ENDCG
        }
    }
}
