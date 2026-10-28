// Realistic Volumetric Aurorae (RVA). Copyright (c) 2026 HereComesTheBoom ("Realistic Volumetric Aurorae")
// SPDX-License-Identifier: CC-BY-NC-SA-4.0  (see LICENSE.txt)
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Renders the aurora at reduced resolution and composites it onto KSP's
/// flight camera, with command buffers, no extra camera, no layers.
///
/// Every frame, right before the flight camera culls, the buffers are rebuilt:
///   RENDER: target a low-res ARGBHalf render texture, clear it, draw a small
///     sphere pinned to the camera with the aurora material (the raymarch only
///     needs a ray direction per pixel). Runs at the composite event when that
///     is earlier than AfterForwardAlpha, otherwise at AfterForwardAlpha. The
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
///   v3.23: the aurora lights nothing. No point lights, no cloud light, no
///     ambient. It is the volumetric glow only.
///
///   1.1.0 TEMPORAL ACCUMULATION (optional, per body): each frame the march's rays
///     are tilted by a sub-pixel offset and its sample offsets are shifted, and the
///     blit shader's pass 4 averages the frames into a pair of history buffers
///     (near / far, up to 2x the render buffers' size) reprojected through the
///     camera's motion relative to the planet. The composite then reads the
///     history. Lets the render scale and the step count come down for the same
///     converged picture. The per-draw state (_RayJitter, _TemporalOffset) is set
///     as shader GLOBALS from the command buffer, not on the material: the
///     scaled-space instances copy the local material every frame.
///
///   1.1.0 OCCLUDER MODEL DEPTH (another body's aurora drawn from here): above the
///     body's PQS limit (120 km on the Mun) KSP replaces its terrain with its
///     scaled-space model, which another camera draws, so the flight camera's depth
///     buffer holds sky at the limb and the aurora showed through the ground there.
///     While that is the case the instance hands over the model (occluderMesh, its
///     rotation and scale in metres) and the render buffer first draws it, with this
///     camera's own matrices, into a buffer of its own (blit shader pass 5, eye depth
///     as 1 / metres, max-blended onto a clear to 0, so no depth buffer is involved). The
///     march reads that buffer where the scene depth is clear or lands outside the
///     body's shell. Same camera, same matrices: the two buffers line up by raster
///     position. The dev window's "model probe" shows the buffer itself. The model is
///     a sphere pushed out by a height map of the body's terrain (AuroraTerrainMap), so
///     its silhouette carries the real highlands; KSP's smooth scaled-space model stands
///     in until the map is built.
///
/// Camera choice: KSP normally renders flight with two cameras: "Camera 01"
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
    [Tooltip("When set, this material is copied into auroraMaterial every frame and its world-unit values are multiplied by syncScale: the scaled-space instance follows the local one (same storm, same animation time, same sun).")]
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
    [Tooltip("Depth-aware upsample: screen pixels only blend buffer texels whose scene depth agrees with their own, for crisp vessel / terrain silhouettes.")]
    public bool  edgeAware = true;
    [Tooltip("Debug: replace the scene with the aurora buffer.")]
    public bool  auroraOnly = false;
    [Tooltip("Event code of the COMPOSITE blit (see EventNames). With per-pixel cloud occlusion the clouds no longer depend on it (it is clamped to BeforeForwardAlpha or later, after EVE's buffer is written); without it, AfterForwardOpaque puts the aurora under EVE's volumetric clouds, which are composited between it and BeforeForwardAlpha.")]
    public int   compositeEvent = EvBeforeForwardAlpha;
    [Tooltip("EVE's volumetric clouds occlude the aurora per pixel: the aurora beyond the cloud-top shell is multiplied by their own transmittance (EVE's global 'colorBuffer'). No altitude switch needed.")]
    public bool  cloudOcclusion = true;
    [Tooltip("How EVE's cloud buffer stores its alpha: 2 = transmittance (verified for the current volumetrics), 0 = opacity.")]
    public int   cloudBufferFormat = 2;
    [Tooltip("Flip EVE's cloud buffer vertically (only if the occlusion is upside-down).")]
    public bool  bufferFlipY = false;
    [Tooltip("Clip the aurora at the camera depth buffer (terrain / vessel occlusion).")]
    public bool  sceneDepthOcclusion = true;
    [Tooltip("1.1.0: a sphere that hides this aurora: the body the vessel is at, when this is another body's aurora drawn on the flight camera. Null = none.")]
    public Transform occluder;
    [Tooltip("1.1.0: the occluder as a body. Its orbital position (CelestialBody.position) places the sphere and the model, not its transform: KSP does not keep a body's local transform on its orbit while the body's terrain is unloaded.")]
    public CelestialBody occluderBody;
    [Tooltip("1.1.0: the aurora's own body, for the same reason: its orbital position is the planet centre.")]
    public CelestialBody planetBody;
    /// <summary>Where the sphere and the model were placed this frame, world (local-space) coordinates.</summary>
    public Vector3 OccluderCentre { get; private set; }
    public float     occluderRadius;
    public float     occluderShellInner, occluderShellOuter;   // the occluder body's terrain shell, for the depth test (1.1.0)
    [Tooltip("1.1.0: the occluder body's scaled-space model, drawn into a depth buffer of its own while KSP shows that model instead of the terrain. Null = none.")]
    public Mesh      occluderMesh;
    [Tooltip("Rotation and scale of that model in metres (the position is added every frame from the occluder body's orbital position).")]
    public Matrix4x4 occluderMeshRS = Matrix4x4.identity;
    [Tooltip("Draw the model this frame (the instance sets it while the body's terrain is off).")]
    public bool      occluderMeshOn;
    [Tooltip("1.1.0: the occluder body's terrain height map (AuroraTerrainMap). Set, the model is a unit sphere the pass pushes out by the map; null, the model is drawn as it is.")]
    public Texture2D occluderHeightMap;
    [Tooltip("The occluder body's datum radius in metres, for the height map's sphere.")]
    public float     occluderDatum;
    /// <summary>The model depth was drawn and handed to the march this frame.</summary>
    public bool      OccluderDepthActive { get; private set; }
    [Tooltip("1.1.0: every sample goes to the far target, i.e. behind this world's clouds everywhere (another body's aurora).")]
    public bool  forceFar = false;

    [Header("1.1.0 temporal accumulation")]
    [Tooltip("Average the frames into a reprojected history buffer; each frame samples a different sub-pixel and different depths.")]
    public bool  temporal = true;
    [Tooltip("History weight: 0.9 = each frame contributes a tenth. Higher is smoother and slower to follow change.")]
    public float temporalWeight = 0.9f;
    [Tooltip("History buffer size as a multiple of the render buffers (capped at the screen).")]
    public float temporalUpscale = 2f;
    [Tooltip("History clamp, in standard deviations of the current frame's 3x3 neighbourhood. Lower = less ghosting, more flicker.")]
    public float temporalClamp = 2f;

    public bool   Initialized => initialized;
    public bool   DirectMode  => directMode;
    public string Status { get; private set; } = "waiting for flight camera";

    const string LogPrefix = "[RVA:Renderer]";

    Camera        farCam;
    RenderTexture rt, rtFar, rtAux;
    RenderTargetIdentifier[] auroraTargets;     // { rt, rtFar, rtAux }: the aurora pass renders near / far of the cloud shell + its mean distance
    RenderTexture occluderDepth;                // 1.1.0: the occluder body's model, eye depth in metres (see the header)
    const int     OccluderDepthPass = 5;        // AuroraBlit.shader pass that draws it
    // 1.1.0: the temporal history (two sets, ping-pong)
    readonly RenderTexture[] histNear = new RenderTexture[2], histFar = new RenderTexture[2];
    readonly RenderTargetIdentifier[][] histTargets = new RenderTargetIdentifier[2][];
    int           histIndex;                     // the set written this frame
    bool          histValid;                     // the other set holds last frame's result
    int           lastRenderFrame = -10;         // Time.frameCount of the last frame this renderer drew
    int           temporalFrame;                 // counts drawn frames (drives the jitter sequences)
    Matrix4x4     prevVP, curVP;                 // last frame's / this frame's projection x view x translate(planet centre)
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
    /// <summary>The camera this renderer draws on, once found (null before that).</summary>
    public Camera TargetCamera => farCam;
    string        cameraMode = "";

    // Sub-pixel offsets for the temporal accumulation: the first eight points of the
    // Halton (2, 3) sequence, centred: every frame of the cycle lands on a different part
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
        // image effect (Scatterer, TUFX) is active); kept from the versions that
        // read the camera target back, so the composite path stays the one tested.
        // Flight camera only: the scaled-space camera is left exactly as KSP set it up.
        if (string.IsNullOrEmpty(cameraName)) farCam.forceIntoRenderTexture = true;

        Camera.onPreCull += OnAnyCameraPreCull;

        if (blitShader == null)       { Log("No blit shader in bundle, direct full-res mode"); StartDirect(); return; }
        if (!CreateRenderTextures())  { Log("Render texture creation failed, direct full-res mode"); StartDirect(); return; }

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

    /// <summary>History buffers at temporalUpscale x the render buffers, capped at the screen.</summary>
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

    /// <summary>1.1.0: the occluder model's depth buffer, at half the camera's size and never
    /// smaller than the render buffers (point sampled by raster position: see the header).</summary>
    bool EnsureOccluderDepth(Camera cam)
    {
        if (rt == null) return false;
        int w = Mathf.Max(8, Mathf.Max(rt.width,  cam.pixelWidth  / 2));
        int h = Mathf.Max(8, Mathf.Max(rt.height, cam.pixelHeight / 2));
        if (occluderDepth != null && occluderDepth.width == w && occluderDepth.height == h) return true;
        Drop(ref occluderDepth);
        occluderDepth = MakeRT("RVA_occluder_depth", w, h, 0, RenderTextureFormat.RFloat, FilterMode.Point);
        occluderDepthAllocs++;
        if (occluderDepth != null) Log($"occluder model depth buffer {w}x{h} (allocation {occluderDepthAllocs}; camera {cam.pixelWidth}x{cam.pixelHeight}, aurora buffer {rt.width}x{rt.height})");
        return occluderDepth != null;
    }
    int occluderDepthAllocs;

    // ---------------------------------------------------------- attachment
    /// <summary>(Re)attach both buffers in canonical order, so that at a shared
    /// event they run: render, then composite.</summary>
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
        // this frame) so the ray-sphere math never sees a stale centre. The body's
        // orbital position when the body is known (1.1.0): the local transform a
        // renderer is parented to is not kept on the orbit while that body's terrain
        // is unloaded, and the aurora would drift off the planet it belongs to.
        Vector3 centre = planetBody != null ? (Vector3)planetBody.position
                       : transform.parent != null ? transform.parent.position : Vector3.zero;
        if (planetBody != null || transform.parent != null)
            auroraMaterial.SetVector("_PlanetCenter", new Vector4(centre.x, centre.y, centre.z, 0f));

        // Occluder sphere (1.1.0), relative to the planet centre like the shader's ray origin.
        // Its centre is the body's orbital position for the same reason: anchored on the
        // transform, the sphere and the model drifted away from the body you see once its
        // terrain switched off, at the vessel's orbital speed, until the terrain came back.
        Vector3 occPos = occluderBody != null ? (Vector3)occluderBody.position : occluder != null ? occluder.position : Vector3.zero;
        OccluderCentre = occPos;
        if ((occluder != null || occluderBody != null) && occluderRadius > 0f)
        {
            Vector3 o = occPos - centre;
            auroraMaterial.SetVector("_Occluder", new Vector4(o.x, o.y, o.z, occluderRadius));
            auroraMaterial.SetVector("_OccluderShell", new Vector4(occluderShellInner, occluderShellOuter, 0f, 0f));
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

        // RENDER, rebuilt every frame: the mesh follows the camera and the RT may change.
        cbRender.Clear();
        // this frame's sub-pixel ray tilt and sample-offset shift (globals: see the header)
        cbRender.SetGlobalVector("_RayJitter", new Vector4(jit.x * 2f * tanH / rt.width, jit.y * 2f * tanV / rt.height, 0f, 0f));
        cbRender.SetGlobalFloat("_TemporalOffset", tOff);

        // 1.1.0: the occluder body's scaled-space model as a depth source while the camera sees
        // past its terrain (see the header). Drawn first, with this camera's matrices, into its
        // own buffer: 1 / eye depth, cleared to 0 (= nothing) and max-blended, so no depth buffer
        // and none of its conventions are involved; each texel ends up with the nearest surface.
        bool modelDepth = occluderMeshOn && occluderMesh != null && (occluder != null || occluderBody != null) && occluderRadius > 0f && EnsureOccluderDepth(cam);
        if (modelDepth)
        {
            cbRender.SetRenderTarget(occluderDepth);
            cbRender.ClearRenderTarget(false, true, Color.clear);
            // the terrain map's sphere is pushed out by the map in the pass; KSP's own model is drawn as it is
            blitMaterial.SetTexture("_OccluderHeightMap", occluderHeightMap);
            blitMaterial.SetVector("_OccluderHeightParams", new Vector4(occluderDatum, occluderHeightMap != null ? 1f : 0f, 0f, 0f));
            cbRender.DrawMesh(occluderMesh, Matrix4x4.Translate(occPos) * occluderMeshRS, blitMaterial, 0, OccluderDepthPass);
            auroraMaterial.SetTexture("_OccluderDepth", occluderDepth);
        }
        auroraMaterial.SetVector("_OccluderDepthParams", new Vector4(1f / rt.width, 1f / rt.height, modelDepth ? 1f : 0f, 0f));
        OccluderDepthActive = modelDepth;

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
            // reconstruction kernel sigma = 0.45 render pixels, so 1 / (2 sigma^2)
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

        // COMPOSITE: from the history when accumulating, else straight from the render buffers
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
        // _FarTex is bound per frame (the render buffer, or, since 1.1.0, the history when accumulating)
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
        if (temporal) { if (!EnsureHistory() && !histWarned) { histWarned = true; Log("Temporal history buffers could not be created, accumulation off"); } }
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
                Log($"Camera changed ('{farCam.name}' -> '{now.name}', {mode}), re-initialising");
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
        Status = $"low-res mode: RT {rt.width}x{rt.height} x3 (scale {renderScale:F2}), render @ {NameOf(RenderEventCode())}, composite @ {NameOf(cc)}{clamped}, cloud occlusion {(OcclusionActive ? "per pixel (EVE buffer)" : cloudOcclusion ? "off (no EVE volumetrics on any camera)" : "off")}, mesh radius {radius:F0} m, camera '{farCam.name}' ({cameraMode}), depth occlusion {(sceneDepthOcclusion ? "on" : "off")}";
    }

    /// <summary>1.1.0: the temporal history as it stands this frame (for the window).</summary>
    public string PerfStatus
    {
        get
        {
            if (!initialized || directMode) return "";
            return TemporalActive ? $"temporal history {histNear[0].width}x{histNear[0].height} x2, weight {temporalWeight:F2}, clamp {temporalClamp:F1} sigma{(histValid ? "" : " (filling)")}"
                 : temporal ? "temporal history pending" : "temporal accumulation off";
        }
    }

    // ----------------------------------------------------------- diagnostics
    /// <summary>Every camera in the scene and every command buffer attached to
    /// the flight camera, per event and in execution order: this is where EVE
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
                if (!on) continue;   // the disabled ones are part/IVA prefabs: dozens of them, no information
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

    // ---- depth readback (the dev window): what the depth texture holds at a screen point ----
    RenderTexture probeRT;
    Texture2D     modelTex;                     // 1x1 readback of the occluder model buffer, for the depth readout
    Texture2D     probeTex;

    /// <summary>Read the camera depth texture back at one screen point (pixels, bottom-left origin) and
    /// at a 3 x 3 grid, and describe it: raw value, the linear distance both ways round, and where that
    /// distance puts the point against the occluder sphere. Slow (a GPU readback); for the dev window.</summary>
    public string DepthProbe(Vector2 screenPos)
    {
        if (farCam == null) return "depth probe: no camera yet";
        Texture depth = Shader.GetGlobalTexture("_CameraDepthTexture");
        if (depth == null) return "depth probe: no _CameraDepthTexture is bound";
        int w = Mathf.Max(depth.width, 1), h = Mathf.Max(depth.height, 1);
        if (probeRT == null || probeRT.width != w || probeRT.height != h)
        {
            if (probeRT != null) probeRT.Release();
            probeRT = new RenderTexture(w, h, 0, RenderTextureFormat.RFloat);
            probeRT.Create();
        }
        if (probeTex == null) probeTex = new Texture2D(w, h, TextureFormat.RFloat, false, true);
        else if (probeTex.width != w || probeTex.height != h) probeTex.Resize(w, h);
        Graphics.Blit(depth, probeRT);
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = probeRT;
        probeTex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        probeTex.Apply();
        RenderTexture.active = prev;

        bool  rev = SystemInfo.usesReversedZBuffer;
        float n = farCam.nearClipPlane, f = farCam.farClipPlane;
        var sb = new StringBuilder();
        sb.Append($"depth texture {w}x{h} ({depth.graphicsFormat}) as bound in Update, which may be another camera's; camera '{farCam.name}' near {n:F2} far {f:F0}, reversed-Z {rev}, "
                + $"depthTextureMode {farCam.depthTextureMode}, clear {farCam.clearFlags}, screen {Screen.width}x{Screen.height}");
        Vector4 occ = auroraMaterial != null ? auroraMaterial.GetVector("_Occluder") : Vector4.zero;
        Vector4 pc  = auroraMaterial != null ? auroraMaterial.GetVector("_PlanetCenter") : Vector4.zero;
        sb.Append($"; occluder sphere {occ.w:F0} m");
        // 1.1.0: the occluder model buffer under the mouse (1 / eye depth, 0 = nothing drawn). The
        // buffer is read at the mouse's row counted from the bottom and from the top, so a flipped
        // buffer shows up as the two disagreeing.
        if (occluderDepth != null && OccluderDepthActive)
        {
            if (modelTex == null) modelTex = new Texture2D(1, 1, TextureFormat.RFloat, false, true);
            int mx = Mathf.Clamp((int)(screenPos.x / Screen.width  * occluderDepth.width),  0, occluderDepth.width  - 1);
            int my = Mathf.Clamp((int)(screenPos.y / Screen.height * occluderDepth.height), 0, occluderDepth.height - 1);
            RenderTexture prev2 = RenderTexture.active;
            RenderTexture.active = occluderDepth;
            modelTex.ReadPixels(new Rect(mx, my, 1, 1), 0, 0); modelTex.Apply();
            float a = modelTex.GetPixel(0, 0).r;
            modelTex.ReadPixels(new Rect(mx, occluderDepth.height - 1 - my, 1, 1), 0, 0); modelTex.Apply();
            float b = modelTex.GetPixel(0, 0).r;
            RenderTexture.active = prev2;
            sb.Append($"; model buffer {occluderDepth.width}x{occluderDepth.height} at the mouse: {(b > 0f ? (0.001f / b).ToString("F1") + " km" : "nothing")} (buffer row counted from the top, the one that lines up with the screen), {(a > 0f ? (0.001f / a).ToString("F1") + " km" : "nothing")} (counted from the bottom); aurora buffer {rt.width}x{rt.height}");
        }
        else sb.Append($"; model buffer: {(occluderMeshOn ? "wanted but not drawn" : "off")}");
        ProbePoint(sb, "mouse", screenPos, w, h, rev, n, f, occ, pc);
        for (int j = 0; j < 3; j++) for (int i = 0; i < 3; i++)
            ProbePoint(sb, $"grid {i},{j}", new Vector2(Screen.width * (0.2f + 0.3f * i), Screen.height * (0.2f + 0.3f * j)), w, h, rev, n, f, occ, pc);
        return sb.ToString();
    }

    void ProbePoint(StringBuilder sb, string label, Vector2 sp, int w, int h, bool rev, float n, float f, Vector4 occ, Vector4 pc)
    {
        int px = Mathf.Clamp((int)(sp.x / Screen.width * w), 0, w - 1), py = Mathf.Clamp((int)(sp.y / Screen.height * h), 0, h - 1);
        float raw = probeTex.GetPixel(px, py).r;
        // Unity's LinearEyeDepth, both ways round: lin = 1 / (zbp.z * raw + zbp.w)
        float linRev = 1f / ((-1f + f / n) / f * raw + 1f / f);
        float linStd = 1f / ((1f - f / n) / f * raw + (f / n) / f);
        float lin = rev ? linRev : linStd;
        Ray ray = farCam.ScreenPointToRay(new Vector3(sp.x, sp.y, 0f));
        float t = lin / Mathf.Max(Vector3.Dot(ray.direction, farCam.transform.forward), 0.05f);
        Vector3 pRel = (farCam.transform.position - new Vector3(pc.x, pc.y, pc.z)) + ray.direction * t;   // planet-relative, like the shader's ro + rd * t
        string shell = "";
        if (occ.w > 0f)
        {
            float rS = (pRel - new Vector3(occ.x, occ.y, occ.z)).magnitude;
            shell = $", {rS / 1000f:F1} km from the occluder's centre ({rS / occ.w:F3} x sphere): {(rS < occ.w * 1.02f && rS > occ.w * 0.85f ? "GROUND" : "sky")}";
        }
        sb.Append($"\n  {label} ({px},{py}): raw {raw:G6}, linear {lin:F0} m (the other convention {(rev ? linStd : linRev):F0} m){(lin < f * 0.98f ? "" : " = far plane")}{(lin < 100f ? " NEAR" : "")}{shell}");
    }

    void Teardown()
    {
        if (probeRT != null) { probeRT.Release(); probeRT = null; }
        if (probeTex != null) { Destroy(probeTex); probeTex = null; }
        if (modelTex != null) { Destroy(modelTex); modelTex = null; }
        Camera.onPreCull -= OnAnyCameraPreCull;
        Detach();
        if (cbRender != null)     { cbRender.Release();  cbRender = null; }
        if (cbBlit != null)       { cbBlit.Release();    cbBlit = null; }
        if (directSphere != null) { Destroy(directSphere); directSphere = null; }
        if (blitMaterial != null) { Destroy(blitMaterial); blitMaterial = null; }
        Drop(ref rt); Drop(ref rtFar); Drop(ref rtAux); Drop(ref occluderDepth);
        DropHistory();
        auroraTargets = null;
        OccluderDepthActive = false;
        directMode = false;
        prevVPValid = curVPValid = false;
    }

    void OnDestroy() { Teardown(); }

    static void Log(string m) => Debug.Log($"{LogPrefix} {m}");
}
