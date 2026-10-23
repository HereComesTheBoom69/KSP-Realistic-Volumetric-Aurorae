// Realistic Volumetric Aurorae (RVA) — Copyright (c) 2026 HereComesTheBoom ("Realistic Volumetric Aurorae")
// SPDX-License-Identifier: CC-BY-NC-SA-4.0  (see LICENSE.txt)
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Renders the aurora at reduced resolution and composites it onto KSP's
/// flight camera — with command buffers, no extra camera, no layers.
///
/// Every frame, right before the flight camera culls, the buffers are rebuilt:
///   RENDER: target a low-res ARGBHalf render texture, clear it, draw a small
///     sphere pinned to the camera with the aurora material (the raymarch only
///     needs a ray direction per pixel). Runs at the composite event when that
///     is earlier than AfterForwardAlpha, otherwise at AfterForwardAlpha — the
///     camera depth texture is valid at both, and must be: the shader clips at
///     it for vessel / terrain occlusion.
///   COMPOSITE: blit that texture onto the camera target with the AuroraBlit
///     shader at the chosen camera event (additive + soft tonemap; pass 1 =
///     "aurora only" debug view). Earlier events put the aurora under the
///     clouds and the atmosphere, later ones over them. The part of the aurora
///     beyond the cloud-top shell is multiplied per pixel by EVE's own cloud
///     transmittance (its global `colorBuffer`), so the volumetric clouds hide
///     it exactly where they are. DumpCameraInfo() logs every camera and every
///     command buffer on the flight camera (where EVE and Scatterer hook in).
///
///   v3.23: the aurora lights nothing — no point lights, no cloud light, no
///     ambient. It is the volumetric glow only.
///
///   1.1.0 TEMPORAL ACCUMULATION (optional, per body): each frame the march's rays
///     are tilted by a sub-pixel offset and its sample offsets are shifted, and the
///     blit shader's pass 4 averages the frames into a pair of history buffers
///     (near / far, up to 2× the render buffers' size) reprojected through the
///     camera's motion relative to the planet. The composite then reads the
///     history. Lets the render scale and the step count come down for the same
///     converged picture. The per-draw state (_RayJitter, _TemporalOffset) is set
///     as shader GLOBALS from the command buffer, not on the material: the
///     scaled-space instances copy the local material every frame.
///
/// Camera choice: KSP normally renders flight with two cameras — "Camera 01"
/// (far, terrain) then "Camera 00" (near, vessel; clears depth). Scatterer's
/// unified-camera mode disables Camera 01 and lets Camera 00 cover the whole
/// range. We composite on whichever of the two actually renders the far scene.
///
/// Falls back to drawing the aurora sphere directly on the flight camera
/// (full resolution, no tonemap) only if the blit shader is missing.
/// </summary>
public class AuroraRenderer : MonoBehaviour
{
    // Event codes used by the settings / window (stable numbers, extended over time):
    //   0 AfterForwardAlpha  1 AfterImageEffects  2 BeforeImageEffects
    //   3 BeforeForwardAlpha 4 AfterEverything    5 AfterForwardOpaque
    public const int EvAfterForwardAlpha = 0, EvAfterImageEffects = 1, EvBeforeImageEffects = 2,
                     EvBeforeForwardAlpha = 3, EvAfterEverything = 4, EvAfterForwardOpaque = 5;
    public static readonly string[] EventNames = { "AfterForwardAlpha", "AfterImageEffects", "BeforeImageEffects", "BeforeForwardAlpha", "AfterEverything", "AfterForwardOpaque" };

    [Header("Inputs (set by bootstrap)")]
    public Material auroraMaterial;
    [Tooltip("Name of the camera to render on; empty = the flight camera (Camera 01, or Camera 00 in Scatterer's unified mode). The map view / tracking station instance uses 'Camera ScaledSpace'.")]
    public string   cameraName;
    [Tooltip("When set, this material is copied into auroraMaterial every frame and its world-unit values are multiplied by syncScale — the scaled-space instance follows the local one (same storm, same animation time, same sun).")]
    public Material syncFrom;
    public float    syncScale = 1f;
    [Tooltip("False = stay attached but draw nothing (the map-view instance outside the map).")]
    public bool     renderingEnabled = true;
    public Shader   blitShader;

    [Header("Settings (live)")]
    [Tooltip("Aurora render resolution as a fraction of the screen. 0.5 = quarter of the pixels, 0.25 = 1/16th.")]
    [Range(0.1f, 1f)] public float renderScale = 0.3f;
    [Tooltip("Radius of the camera-pinned mesh, meters. Clamped inside the camera's clip range.")]
    public float meshRadius = 5000f;
    public float exposure = 1f;
    public bool  flipY = false;
    [Tooltip("0 = plain bilinear upsample, 1 = 5-tap smoothed (hides jitter grain / blockiness)")]
    public float smoothing = 0.5f;
    [Tooltip("Depth-aware upsample: screen pixels only blend buffer texels whose scene depth agrees with their own — crisp vessel / terrain silhouettes.")]
    public bool  edgeAware = true;
    [Tooltip("Debug: replace the scene with the aurora buffer.")]
    public bool  auroraOnly = false;
    [Tooltip("Event code of the COMPOSITE blit (see EventNames). With per-pixel cloud occlusion the clouds no longer depend on it (it is clamped to BeforeForwardAlpha or later, after EVE's buffer is written); without it, AfterForwardOpaque puts the aurora under EVE's volumetric clouds, which are composited between it and BeforeForwardAlpha.")]
    public int   compositeEvent = EvBeforeForwardAlpha;
    [Tooltip("EVE's volumetric clouds occlude the aurora per pixel: the aurora beyond the cloud-top shell is multiplied by their own transmittance (EVE's global 'colorBuffer'). No altitude switch needed.")]
    public bool  cloudOcclusion = true;
    [Tooltip("How EVE's cloud buffer stores its alpha — 2 = transmittance (verified for the current volumetrics), 0 = opacity.")]
    public int   cloudBufferFormat = 2;
    [Tooltip("Flip EVE's cloud buffer vertically (only if the occlusion is upside-down).")]
    public bool  bufferFlipY = false;
    [Tooltip("Clip the aurora at the camera depth buffer (terrain / vessel occlusion).")]
    public bool  sceneDepthOcclusion = true;
    [Tooltip("1.1.0: a sphere that hides this aurora — the body the vessel is at, when this is another body's aurora drawn on the flight camera. Null = none.")]
    public Transform occluder;
    public float     occluderRadius;
    [Tooltip("1.1.0: every sample goes to the far target, i.e. behind this world's clouds everywhere (another body's aurora).")]
    public bool  forceFar = false;

    [Header("1.1.0 temporal accumulation")]
    [Tooltip("Average the frames into a reprojected history buffer; each frame samples a different sub-pixel and different depths.")]
    public bool  temporal = true;
    [Tooltip("History weight: 0.9 = each frame contributes a tenth. Higher is smoother and slower to follow change.")]
    public float temporalWeight = 0.9f;
    [Tooltip("History buffer size as a multiple of the render buffers (capped at the screen).")]
    public float temporalUpscale = 2f;
    [Tooltip("History clamp, in standard deviations of the current frame's 3×3 neighbourhood. Lower = less ghosting, more flicker.")]
    public float temporalClamp = 2f;

    public bool   Initialized => initialized;
    public bool   DirectMode  => directMode;
    public string Status { get; private set; } = "waiting for flight camera";

    const string LogPrefix = "[RVA:Renderer]";

    Camera        farCam;
    RenderTexture rt, rtFar, rtAux;
    RenderTargetIdentifier[] auroraTargets;     // { rt, rtFar, rtAux } — the aurora pass renders near / far of the cloud shell + its mean distance
    // 1.1.0: the temporal history (two sets, ping-pong)
    readonly RenderTexture[] histNear = new RenderTexture[2], histFar = new RenderTexture[2];
    readonly RenderTargetIdentifier[][] histTargets = new RenderTargetIdentifier[2][];
    int           histIndex;                     // the set written this frame
    bool          histValid;                     // the other set holds last frame's result
    int           lastRenderFrame = -10;         // Time.frameCount of the last frame this renderer drew
    int           temporalFrame;                 // counts drawn frames (drives the jitter sequences)
    Matrix4x4     prevVP, curVP;                 // last frame's / this frame's projection × view × translate(planet centre)
    bool          prevVPValid, curVPValid;
    bool          histWarned;
    Mesh          sphereMesh;
    Material      blitMaterial;
    CommandBuffer cbRender, cbBlit;
    // events the buffers are currently attached at (-1 = not attached)
    int appliedRender = -1, appliedComposite = -1;
    GameObject    directSphere;
    float         radius;
    bool          initialized, directMode;
    int           findAttempts, cameraRecheck;
    readonly System.Diagnostics.Stopwatch preCullWatch = new System.Diagnostics.Stopwatch();
    /// <summary>Main-thread milliseconds of the last per-frame command-buffer rebuild.</summary>
    public float LastPreCullMs { get; private set; }
    string        cameraMode = "";

    // Sub-pixel offsets for the temporal accumulation: the first eight points of the
    // Halton (2, 3) sequence, centred — every frame of the cycle lands on a different part
    // of the pixel, and the eight together cover it evenly.
    static readonly Vector2[] JitterSeq =
    {
        new Vector2(0.5f - 0.5f, 1f / 3f - 0.5f),  new Vector2(0.25f - 0.5f, 2f / 3f - 0.5f),
        new Vector2(0.75f - 0.5f, 1f / 9f - 0.5f), new Vector2(0.125f - 0.5f, 4f / 9f - 0.5f),
        new Vector2(0.625f - 0.5f, 7f / 9f - 0.5f), new Vector2(0.375f - 0.5f, 2f / 9f - 0.5f),
        new Vector2(0.875f - 0.5f, 5f / 9f - 0.5f), new Vector2(0.0625f - 0.5f, 8f / 9f - 0.5f),
    };

    void LateUpdate()
    {
        if (!initialized) { TryInit(); return; }
        Sync();
    }

    // ------------------------------------------------------------- events
    public static CameraEvent EventFor(int code)
    {
        switch (code)
        {
            case EvAfterImageEffects:  return CameraEvent.AfterImageEffects;
            case EvBeforeImageEffects: return CameraEvent.BeforeImageEffects;
            case EvBeforeForwardAlpha: return CameraEvent.BeforeForwardAlpha;
            case EvAfterEverything:    return CameraEvent.AfterEverything;
            case EvAfterForwardOpaque: return CameraEvent.AfterForwardOpaque;
            default:                   return CameraEvent.AfterForwardAlpha;
        }
    }

    /// <summary>Position of the event in the frame (forward rendering).</summary>
    public static int Rank(int code)
    {
        switch (code)
        {
            case EvAfterForwardOpaque:  return 0;
            case EvBeforeForwardAlpha:  return 1;
            case EvAfterForwardAlpha:   return 2;
            case EvBeforeImageEffects:  return 3;
            case EvAfterImageEffects:   return 4;
            case EvAfterEverything:     return 5;
            default:                    return 2;
        }
    }

    public static string NameOf(int code) => code >= 0 && code < EventNames.Length ? EventNames[code] : code.ToString();

    static int Clamp(int code) => code >= 0 && code < EventNames.Length ? code : EvAfterForwardAlpha;

    // The per-pixel occlusion reads EVE's global cloud buffer (its raymarched
    // volumetrics write it every frame). Without that renderer in the scene the
    // texture is never bound, so the occlusion switches itself off: RVA is
    // standalone, EVE is an optional enhancement (v1.0.0).
    bool OcclusionActive => cloudOcclusion && eveVolumetrics;
    bool eveVolumetrics;
    int  eveCheckFrame = -1000;
    static System.Type eveRendererType; static bool eveTypeSearched;

    /// <summary>True when EVE's raymarched-volumetrics renderer sits on an enabled camera.</summary>
    static bool EveVolumetricsPresent()
    {
        if (!eveTypeSearched)
        {
            eveTypeSearched = true;
            try
            {
                foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
                    if (a.GetName().Name == "Atmosphere") { eveRendererType = a.GetType("Atmosphere.DeferredRaymarchedVolumetricCloudsRenderer"); break; }
            }
            catch { eveRendererType = null; }
        }
        if (eveRendererType == null) return false;
        foreach (var c in Camera.allCameras)
            if (c != null && c.GetComponent(eveRendererType) != null) return true;
        return false;
    }

    void RefreshEvePresence(bool force)
    {
        if (!force && Time.frameCount - eveCheckFrame < 120) return;
        eveCheckFrame = Time.frameCount;
        bool now = EveVolumetricsPresent();
        if (now != eveVolumetrics)
        {
            eveVolumetrics = now;
            if (initialized && !directMode) { if (AttachmentStale()) Attach(); UpdateStatus(); Log(Status); }
        }
    }

    /// <summary>The composite event actually used: with per-pixel cloud
    /// occlusion it cannot be earlier than BeforeForwardAlpha, because EVE's
    /// cloud buffer for this frame is written at AfterForwardOpaque.</summary>
    int CompositeEventCode()
    {
        int code = Clamp(compositeEvent);
        if (OcclusionActive && Rank(code) < Rank(EvBeforeForwardAlpha)) code = EvBeforeForwardAlpha;
        return code;
    }

    /// <summary>The render must happen where the depth texture is valid: at the
    /// composite event when that is earlier than AfterForwardAlpha, else at AfterForwardAlpha.</summary>
    int RenderEventCode() { int c = CompositeEventCode(); return Rank(c) < Rank(EvAfterForwardAlpha) ? c : EvAfterForwardAlpha; }

    // ------------------------------------------------------------------ init
    void TryInit()
    {
        if (auroraMaterial == null) return;

        // The flight cameras are created/enabled a few frames into the scene,
        // so keep looking every frame until one of the two valid choices shows up.
        farCam = FindTargetCamera(out cameraMode);
        if (farCam == null)
        {
            findAttempts++;
            if (findAttempts == 300 || findAttempts % 3000 == 0)
            {
                var names = new System.Collections.Generic.List<string>();
                foreach (var c in Camera.allCameras) if (c != null) names.Add($"{c.name} (far {c.farClipPlane:F0} m)");
                Log($"Still waiting for the {(string.IsNullOrEmpty(cameraName) ? "flight camera" : "'" + cameraName + "' camera")} after {findAttempts} frames. Enabled cameras: {string.Join(", ", names.ToArray())}");
            }
            return;
        }
        if (findAttempts > 0) Log($"Camera '{farCam.name}' found after {findAttempts} frames");

        radius     = Mathf.Clamp(meshRadius, farCam.nearClipPlane * 4f, farCam.farClipPlane * 0.5f);
        sphereMesh = GetSphereMesh();
        RefreshEvePresence(true);

        // The aurora shader clips its march at the camera's depth texture
        // (Scatterer already turns this on; harmless to request again).
        if (sceneDepthOcclusion) farCam.depthTextureMode |= DepthTextureMode.Depth;
        auroraMaterial.SetFloat("_UseSceneDepth", sceneDepthOcclusion ? 1f : 0f);
        // The flight camera renders into a texture (it already does whenever any
        // image effect — Scatterer, TUFX — is active); kept from the versions that
        // read the camera target back, so the composite path stays the one tested.
        // Flight camera only: the scaled-space camera is left exactly as KSP set it up.
        if (string.IsNullOrEmpty(cameraName)) farCam.forceIntoRenderTexture = true;

        Camera.onPreCull += OnAnyCameraPreCull;

        if (blitShader == null)       { Log("No blit shader in bundle — direct full-res mode"); StartDirect(); return; }
        if (!CreateRenderTextures())  { Log("Render texture creation failed — direct full-res mode"); StartDirect(); return; }

        blitMaterial = new Material(blitShader);
        PushBlitParams();

        cbRender  = new CommandBuffer { name = "RVA render" };
        cbBlit    = new CommandBuffer { name = "RVA composite" };
        Attach();

        initialized = true;
        UpdateStatus();
        Log(Status);
        Log(DumpCameraInfo());
    }

    void StartDirect()
    {
        auroraMaterial.SetFloat("_FarSplit", 0f);      // single target: everything into the first output
        directSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        directSphere.name  = "AuroraVolumeDirect";
        directSphere.layer = 0;
        var col = directSphere.GetComponent<Collider>();
        if (col != null) Destroy(col);
        directSphere.transform.SetParent(farCam.transform, false);
        directSphere.transform.localPosition = Vector3.zero;
        directSphere.transform.localRotation = Quaternion.identity;
        directSphere.transform.localScale    = Vector3.one * radius * 2f;
        var mr = directSphere.GetComponent<MeshRenderer>();
        mr.sharedMaterial       = auroraMaterial;
        mr.shadowCastingMode    = ShadowCastingMode.Off;
        mr.receiveShadows       = false;
        mr.lightProbeUsage      = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        directMode  = true;
        initialized = true;
        Status = $"DIRECT full-res mode (no tonemap), mesh radius {radius:F0} m on '{farCam.name}'";
        Log(Status);
    }

    static Mesh GetSphereMesh()
    {
        var tmp  = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        var mesh = tmp.GetComponent<MeshFilter>().sharedMesh;   // built-in asset, outlives the primitive
        tmp.SetActive(false);
        Destroy(tmp);
        return mesh;
    }

    static RenderTexture MakeRT(string name, int w, int h, int depth, RenderTextureFormat fmt, FilterMode filter)
    {
        var t = new RenderTexture(w, h, depth, fmt);
        t.name             = name;
        t.filterMode       = filter;
        t.wrapMode         = TextureWrapMode.Clamp;
        t.useMipMap        = false;
        t.autoGenerateMips = false;
        return t.Create() ? t : null;
    }

    static void Drop(ref RenderTexture t)
    {
        if (t == null) return;
        t.Release(); Destroy(t); t = null;
    }

    bool CreateRenderTextures()
    {
        int w = Mathf.Max(8, Mathf.RoundToInt(farCam.pixelWidth  * renderScale));
        int h = Mathf.Max(8, Mathf.RoundToInt(farCam.pixelHeight * renderScale));
        Drop(ref rt); Drop(ref rtFar); Drop(ref rtAux);
        // near-of-the-cloud-shell aurora (+ irradiance in alpha); carries the depth
        // buffer the multi-target binding needs (the pass itself ignores depth)
        rt = MakeRT("RVA_RT", w, h, 16, RenderTextureFormat.ARGBHalf, FilterMode.Bilinear);
        if (rt == null) return false;
        // beyond-the-cloud-shell aurora, occluded per pixel by EVE's transmittance in the composite
        rtFar = MakeRT("RVA_RT_far", w, h, 0, RenderTextureFormat.ARGBHalf, FilterMode.Bilinear);
        if (rtFar == null) return false;
        // 1.1.0: the mean distance of the aurora along each ray, for the temporal reprojection
        rtAux = MakeRT("RVA_RT_aux", w, h, 0, RenderTextureFormat.RFloat, FilterMode.Point);
        if (rtAux == null) return false;
        auroraTargets = new RenderTargetIdentifier[] { rt, rtFar, rtAux };
        DropHistory();                       // sized from the render buffers: rebuilt on demand
        return true;
    }

    // ---- 1.1.0: temporal history ---------------------------------------------
    void DropHistory()
    {
        for (int k = 0; k < 2; k++) { Drop(ref histNear[k]); Drop(ref histFar[k]); histTargets[k] = null; }
        histValid = false;
    }

    /// <summary>History buffers at temporalUpscale × the render buffers, capped at the screen.</summary>
    bool EnsureHistory()
    {
        if (rt == null) return false;
        float u = Mathf.Clamp(temporalUpscale, 1f, 4f);
        u = Mathf.Min(u, Mathf.Min((float)farCam.pixelWidth / rt.width, (float)farCam.pixelHeight / rt.height));
        int w = Mathf.Max(8, Mathf.RoundToInt(rt.width  * u));
        int h = Mathf.Max(8, Mathf.RoundToInt(rt.height * u));
        if (histNear[0] != null && histNear[0].width == w && histNear[0].height == h) return true;
        DropHistory();
        for (int k = 0; k < 2; k++)
        {
            // the first carries a depth buffer: the two-target binding wants one of the right size
            histNear[k] = MakeRT("RVA_hist_near" + k, w, h, 16, RenderTextureFormat.ARGBHalf, FilterMode.Bilinear);
            histFar[k]  = MakeRT("RVA_hist_far"  + k, w, h, 0,  RenderTextureFormat.ARGBHalf, FilterMode.Bilinear);
            if (histNear[k] == null || histFar[k] == null) { DropHistory(); return false; }
            histTargets[k] = new RenderTargetIdentifier[] { histNear[k], histFar[k] };
        }
        histValid = false;
        return true;
    }

    bool TemporalActive => temporal && !directMode && histNear[0] != null && histNear[1] != null;

    // ---------------------------------------------------------- attachment
    /// <summary>(Re)attach both buffers in canonical order, so that at a shared
    /// event they run: render → composite.</summary>
    void Attach()
    {
        Detach();
        compositeEvent = Clamp(compositeEvent);
        int renderCode    = RenderEventCode();
        int compositeCode = CompositeEventCode();
        farCam.AddCommandBuffer(EventFor(renderCode), cbRender);      appliedRender = renderCode;
        farCam.AddCommandBuffer(EventFor(compositeCode), cbBlit);     appliedComposite = compositeCode;
    }

    void Detach()
    {
        if (farCam == null) { appliedRender = appliedComposite = -1; return; }
        if (cbRender != null && appliedRender    >= 0) farCam.RemoveCommandBuffer(EventFor(appliedRender),    cbRender);
        if (cbBlit   != null && appliedComposite >= 0) farCam.RemoveCommandBuffer(EventFor(appliedComposite), cbBlit);
        appliedRender = appliedComposite = -1;
    }

    bool AttachmentStale() => appliedRender != RenderEventCode() || appliedComposite != CompositeEventCode();

    // ------------------------------------------------------------ per frame
    void OnAnyCameraPreCull(Camera cam)
    {
        if (cam != farCam || !initialized) return;

        if (directMode && directSphere != null && directSphere.activeSelf != renderingEnabled)
            directSphere.SetActive(renderingEnabled);

        // Not drawing this frame (the map-view instance outside the map): empty
        // buffers cost nothing, and nothing else below is needed either (v3.18)
        if (!renderingEnabled)
        {
            if (cbRender != null) { cbRender.Clear(); cbBlit.Clear(); }
            return;
        }

        preCullWatch.Restart();

        // Scaled-space instance: take the local material's state as of this frame
        // (storm, animation time, sun, every slider) and rescale the world-unit values.
        if (syncFrom != null)
        {
            auroraMaterial.CopyPropertiesFromMaterial(syncFrom);
            if (syncScale != 1f)
            {
                auroraMaterial.SetFloat("_SphereRadius",   syncFrom.GetFloat("_SphereRadius")   * syncScale);
                auroraMaterial.SetFloat("_AltBase",        syncFrom.GetFloat("_AltBase")        * syncScale);
                auroraMaterial.SetFloat("_AltTop",         syncFrom.GetFloat("_AltTop")         * syncScale);
                auroraMaterial.SetFloat("_CloudTopAlt",    syncFrom.GetFloat("_CloudTopAlt")    * syncScale);
            }
            auroraMaterial.SetFloat("_UseSceneDepth", sceneDepthOcclusion ? 1f : 0f);
        }

        // Planet centre as late as possible (after any floating-origin shift
        // this frame) so the ray-sphere math never sees a stale centre.
        Vector3 centre = transform.parent != null ? transform.parent.position : Vector3.zero;
        if (transform.parent != null)
            auroraMaterial.SetVector("_PlanetCenter", new Vector4(centre.x, centre.y, centre.z, 0f));

        // Occluder sphere (1.1.0), relative to the planet centre like the shader's ray origin
        if (occluder != null && occluderRadius > 0f)
        {
            Vector3 o = occluder.position - centre;
            auroraMaterial.SetVector("_Occluder", new Vector4(o.x, o.y, o.z, occluderRadius));
        }
        else auroraMaterial.SetVector("_Occluder", Vector4.zero);
        auroraMaterial.SetFloat("_ForceFar", forceFar ? 1f : 0f);

        // Pixel footprint for the shader's anti-aliasing (radians per aurora-buffer pixel)
        int bufferHeight = (rt != null) ? rt.height : Mathf.Max(cam.pixelHeight, 1);
        float tanV = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        auroraMaterial.SetFloat("_PixelAngle", 2f * tanV / bufferHeight);

        if (directMode || cbRender == null || cbBlit == null || rt == null || rtFar == null || rtAux == null) return;

        blitMaterial.SetFloat("_CloudOcclusion", OcclusionActive ? 1f : 0f);

        // 1.1.0: a new frame of this renderer? The callback can fire more than once per frame
        // for a camera that renders twice, and every call re-records the same buffers, so the
        // per-frame state (jitter, history set, last frame's camera) advances once per frame
        // and a repeated call records exactly what the first did.
        bool newFrame = Time.frameCount != lastRenderFrame;
        if (newFrame)
        {
            if (Time.frameCount - lastRenderFrame != 1) { histValid = false; curVPValid = false; }   // frames were skipped: the history is stale
            prevVP = curVP; prevVPValid = curVPValid; curVPValid = false;
            histIndex = 1 - histIndex;
            temporalFrame++;
            lastRenderFrame = Time.frameCount;
        }
        bool useTemporal = TemporalActive;
        if (!useTemporal) { histValid = false; prevVPValid = false; }

        // sub-pixel offset of this frame's rays (render pixels, memory axes) and the shift of the sample offsets
        Vector2 jit   = useTemporal ? JitterSeq[temporalFrame & 7] : Vector2.zero;
        float   tOff  = useTemporal ? Mathf.Repeat(0.6180340f * temporalFrame, 1f) : 0f;
        float   tanH  = tanV * cam.aspect;

        // RENDER — rebuilt every frame: the mesh follows the camera and the RT may change.
        cbRender.Clear();
        // this frame's sub-pixel ray tilt and sample-offset shift (globals: see the header)
        cbRender.SetGlobalVector("_RayJitter", new Vector4(jit.x * 2f * tanH / rt.width, jit.y * 2f * tanV / rt.height, 0f, 0f));
        cbRender.SetGlobalFloat("_TemporalOffset", tOff);

        cbRender.SetRenderTarget(rtFar);
        cbRender.ClearRenderTarget(false, true, Color.clear);
        cbRender.SetRenderTarget(rtAux);
        cbRender.ClearRenderTarget(false, true, Color.clear);
        cbRender.SetRenderTarget(rt);
        cbRender.ClearRenderTarget(true, true, Color.clear);
        // three colour targets: aurora before the cloud-top shell (rt), beyond it (rtFar), its mean distance (rtAux)
        auroraMaterial.SetFloat("_FarSplit", 1f);
        cbRender.SetRenderTarget(auroraTargets, rt);
        var m = Matrix4x4.TRS(cam.transform.position, Quaternion.identity, Vector3.one * radius * 2f);
        cbRender.DrawMesh(sphereMesh, m, auroraMaterial, 0, 0);

        // the camera this frame, planet-relative, in Camera.projectionMatrix's convention: next frame's reprojection
        Matrix4x4 vp = cam.projectionMatrix * cam.worldToCameraMatrix * Matrix4x4.Translate(centre);

        RenderTexture compNear = rt, compFar = rtFar;
        if (useTemporal)
        {
            // TEMPORAL RESOLVE (blit shader pass 4) into this frame's history set, from the other set
            int cur = histIndex, prev = 1 - histIndex;
            Vector3 camRel = cam.transform.position - centre;
            float R = auroraMaterial.GetFloat("_SphereRadius");
            float shellMid = R + 0.5f * (auroraMaterial.GetFloat("_AltBase") + auroraMaterial.GetFloat("_AltTop"));
            blitMaterial.SetTexture("_CurNear",  rt);
            blitMaterial.SetTexture("_CurFar",   rtFar);
            blitMaterial.SetTexture("_CurAux",   rtAux);
            blitMaterial.SetTexture("_HistNear", histNear[prev]);
            blitMaterial.SetTexture("_HistFar",  histFar[prev]);
            blitMaterial.SetVector("_CurTexelSize",   new Vector4(1f / rt.width, 1f / rt.height, rt.width, rt.height));
            blitMaterial.SetVector("_HistTexelSize",  new Vector4(1f / histNear[cur].width, 1f / histNear[cur].height, histNear[cur].width, histNear[cur].height));
            blitMaterial.SetVector("_TemporalJitter", new Vector4(jit.x, jit.y, 0f, 0f));
            // reconstruction kernel σ = 0.45 render pixels → 1 / (2σ²)
            blitMaterial.SetVector("_TemporalParams", new Vector4(Mathf.Clamp(temporalWeight, 0f, 0.99f), Mathf.Max(temporalClamp, 0.1f), 2.469f, (histValid && prevVPValid) ? 1f : 0f));
            blitMaterial.SetVector("_CamPosRel",      new Vector4(camRel.x, camRel.y, camRel.z, 0f));
            blitMaterial.SetVector("_ShellParams",    new Vector4(shellMid, shellMid * 20f, 0f, 0f));
            blitMaterial.SetMatrix("_PrevVP", prevVPValid ? prevVP : vp);
            cbRender.SetRenderTarget(histTargets[cur], histNear[cur]);
            cbRender.DrawProcedural(Matrix4x4.identity, blitMaterial, 4, MeshTopology.Triangles, 3);
            compNear = histNear[cur]; compFar = histFar[cur];
            histValid = true;                 // this frame's set is written when the buffer runs; next frame reads it
        }
        cbRender.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
        curVP = vp; curVPValid = useTemporal;

        // COMPOSITE — from the history when accumulating, else straight from the render buffers
        blitMaterial.SetTexture("_FarTex", compFar);
        cbBlit.Clear();
        cbBlit.Blit(compNear, BuiltinRenderTextureType.CameraTarget, blitMaterial, auroraOnly ? 1 : 0);
        cbBlit.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);

        preCullWatch.Stop();
        LastPreCullMs = (float)preCullWatch.Elapsed.TotalMilliseconds;
    }

    void PushBlitParams()
    {
        if (blitMaterial == null) return;
        blitMaterial.SetFloat("_Exposure", exposure);
        blitMaterial.SetFloat("_FlipY", flipY ? 1f : 0f);
        blitMaterial.SetFloat("_Smoothing", smoothing);
        blitMaterial.SetFloat("_EdgeAware", edgeAware && sceneDepthOcclusion ? 1f : 0f);
        // the march clips against scene depth only within 5 % of the planet radius; the
        // depth-aware upsample follows the same rule (v3.24)
        blitMaterial.SetFloat("_NearDepthKm", auroraMaterial != null && auroraMaterial.HasProperty("_SphereRadius") ? auroraMaterial.GetFloat("_SphereRadius") * 0.05f / 1000f : 30f);
        // the occlusion reads EVE's cloud buffer (the blit shader's "buffer mode"); the
        // shader's cloud-light passes (2, 3) are never drawn since v3.23
        blitMaterial.SetFloat("_CloudGrabMode", 2f);
        blitMaterial.SetFloat("_GrabFlipY", bufferFlipY ? 1f : 0f);
        blitMaterial.SetFloat("_CloudBufferFormat", cloudBufferFormat);
        blitMaterial.SetFloat("_CloudMinOpacity", 0f);
        blitMaterial.SetFloat("_CloudLightGain", 0f);
        // _FarTex is bound per frame (the render buffer, or the history when accumulating — 1.1.0)
    }

    void Sync()
    {
        if (farCam == null) { Teardown(); initialized = false; return; }
        if (directMode) return;

        int w = Mathf.Max(8, Mathf.RoundToInt(farCam.pixelWidth  * renderScale));
        int h = Mathf.Max(8, Mathf.RoundToInt(farCam.pixelHeight * renderScale));
        if (rt == null || rt.width != w || rt.height != h)
        {
            if (!CreateRenderTextures()) { Log("Render texture re-creation failed"); return; }
            UpdateStatus();
        }
        // 1.1.0: the history follows the render buffers and the upscale
        if (temporal) { if (!EnsureHistory() && !histWarned) { histWarned = true; Log("Temporal history buffers could not be created — accumulation off"); } }
        else if (histNear[0] != null) DropHistory();

        RefreshEvePresence(false);
        if (AttachmentStale())
        {
            Attach();
            UpdateStatus();
            Log(Status);
        }

        PushBlitParams();
        auroraMaterial.SetFloat("_UseSceneDepth", sceneDepthOcclusion ? 1f : 0f);
        if (sceneDepthOcclusion && (farCam.depthTextureMode & DepthTextureMode.Depth) == 0)
            farCam.depthTextureMode |= DepthTextureMode.Depth;

        // Scatterer can switch between two-camera and unified mode at runtime;
        // follow it (re-init on the camera that now renders the far scene).
        if (++cameraRecheck >= 120)
        {
            cameraRecheck = 0;
            var now = FindTargetCamera(out string mode);
            if (now != null && now != farCam)
            {
                Log($"Camera changed ('{farCam.name}' → '{now.name}', {mode}) — re-initialising");
                Teardown();
                initialized = false;
            }
        }
    }

    void UpdateStatus()
    {
        if (rt == null) return;
        int cc = CompositeEventCode();
        string clamped = cc != Clamp(compositeEvent) ? $" (asked for {NameOf(Clamp(compositeEvent))}: per-pixel cloud occlusion needs EVE's buffer, written at AfterForwardOpaque)" : "";
        Status = $"low-res mode: RT {rt.width}x{rt.height} ×3 (scale {renderScale:F2}), render @ {NameOf(RenderEventCode())}, composite @ {NameOf(cc)}{clamped}, cloud occlusion {(OcclusionActive ? "per pixel (EVE buffer)" : cloudOcclusion ? "off (no EVE volumetrics on any camera)" : "off")}, mesh radius {radius:F0} m, camera '{farCam.name}' ({cameraMode}), depth occlusion {(sceneDepthOcclusion ? "on" : "off")}";
    }

    /// <summary>1.1.0: the temporal history as it stands this frame (for the window).</summary>
    public string PerfStatus
    {
        get
        {
            if (!initialized || directMode) return "";
            return TemporalActive ? $"temporal history {histNear[0].width}x{histNear[0].height} ×2, weight {temporalWeight:F2}, clamp {temporalClamp:F1}σ{(histValid ? "" : " (filling)")}"
                 : temporal ? "temporal history pending" : "temporal accumulation off";
        }
    }

    // ----------------------------------------------------------- diagnostics
    /// <summary>Every camera in the scene and every command buffer attached to
    /// the flight camera, per event and in execution order — this is where EVE
    /// and Scatterer hook in. Logged once at init and on demand from the window.</summary>
    public string DumpCameraInfo()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Camera / command-buffer dump:");
        try
        {
            var cams = Resources.FindObjectsOfTypeAll<Camera>();
            System.Array.Sort(cams, (a, b) => a.depth.CompareTo(b.depth));
            foreach (var c in cams)
            {
                if (c == null || !c.gameObject.scene.IsValid()) continue;   // skip prefabs / assets
                bool on = c.enabled && c.gameObject.activeInHierarchy;
                if (!on) continue;   // the disabled ones are part/IVA prefabs — dozens of them, no information
                sb.AppendLine($"  camera '{c.name}' depth {c.depth:F1} ENABLED near {c.nearClipPlane:F2} far {c.farClipPlane:F0} clear {c.clearFlags} HDR {c.allowHDR} target {(c.targetTexture != null ? c.targetTexture.name : "screen")} mask 0x{c.cullingMask:X8}");
                // scripts on the camera object: this is where EVE / Scatterer / TUFX hook their per-frame work
                var comps = c.GetComponents<Component>();
                var names = new StringBuilder();
                foreach (var comp in comps) if (comp != null && !(comp is Transform) && !(comp is Camera)) names.Append(comp.GetType().FullName).Append(comp is Behaviour b && !b.enabled ? "(off) " : " ");
                if (names.Length > 0) sb.AppendLine($"      components: {names}");
            }
            if (farCam != null)
            {
                sb.AppendLine($"  command buffers on '{farCam.name}' (execution order within each event):");
                foreach (CameraEvent ev in System.Enum.GetValues(typeof(CameraEvent)))
                {
                    var list = farCam.GetCommandBuffers(ev);
                    if (list == null || list.Length == 0) continue;
                    sb.Append($"    {ev}:");
                    foreach (var cb in list) sb.Append($"  [{cb.name} {cb.sizeInBytes} B]");
                    sb.AppendLine();
                }
                sb.AppendLine($"  depthTextureMode {farCam.depthTextureMode}, forceIntoRenderTexture {farCam.forceIntoRenderTexture}");
            }
        }
        catch (System.Exception e) { sb.AppendLine("  (dump failed: " + e.Message + ")"); }
        return sb.ToString();
    }

    // --------------------------------------------------------------- helpers
    static Camera FindCamera(string name)
    {
        foreach (var cam in Camera.allCameras)          // enabled cameras only
            if (cam != null && cam.name == name) return cam;
        return null;
    }

    /// <summary>The named camera when cameraName is set (scaled-space instance), else the flight camera.</summary>
    Camera FindTargetCamera(out string mode)
    {
        if (!string.IsNullOrEmpty(cameraName)) { mode = cameraName; return FindCamera(cameraName); }
        return FindFlightCamera(out mode);
    }

    /// <summary>The camera that renders the far scene: "Camera 01" in stock
    /// two-camera mode, or "Camera 00" when Scatterer's unified-camera mode
    /// has disabled Camera 01 and stretched Camera 00's far plane.</summary>
    static Camera FindFlightCamera(out string mode)
    {
        var far = FindCamera("Camera 01");
        if (far != null) { mode = "two-camera mode"; return far; }

        var near = FindCamera("Camera 00");
        if (near != null && near.farClipPlane > 50000f) { mode = "unified camera mode"; return near; }

        mode = "";
        return null;
    }

    void Teardown()
    {
        Camera.onPreCull -= OnAnyCameraPreCull;
        Detach();
        if (cbRender != null)     { cbRender.Release();  cbRender = null; }
        if (cbBlit != null)       { cbBlit.Release();    cbBlit = null; }
        if (directSphere != null) { Destroy(directSphere); directSphere = null; }
        if (blitMaterial != null) { Destroy(blitMaterial); blitMaterial = null; }
        Drop(ref rt); Drop(ref rtFar); Drop(ref rtAux);
        DropHistory();
        auroraTargets = null;
        directMode = false;
        prevVPValid = curVPValid = false;
    }

    void OnDestroy() { Teardown(); }

    static void Log(string m) => Debug.Log($"{LogPrefix} {m}");
}
