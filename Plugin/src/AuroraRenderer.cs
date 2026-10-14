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

    public bool   Initialized => initialized;
    public bool   DirectMode  => directMode;
    public string Status { get; private set; } = "waiting for flight camera";

    const string LogPrefix = "[RVA:Renderer]";

    Camera        farCam;
    RenderTexture rt, rtFar;
    RenderTargetIdentifier[] auroraTargets;     // { rt, rtFar } — the aurora pass renders near / far of the cloud shell
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

    bool CreateRenderTextures()
    {
        int w = Mathf.Max(8, Mathf.RoundToInt(farCam.pixelWidth  * renderScale));
        int h = Mathf.Max(8, Mathf.RoundToInt(farCam.pixelHeight * renderScale));
        if (rt != null)    { rt.Release();    Destroy(rt);    rt = null; }
        if (rtFar != null) { rtFar.Release(); Destroy(rtFar); rtFar = null; }
        // near-of-the-cloud-shell aurora (+ irradiance in alpha); carries the depth
        // buffer the two-target binding needs (the pass itself ignores depth)
        rt = new RenderTexture(w, h, 16, RenderTextureFormat.ARGBHalf);
        rt.name             = "RVA_RT";
        rt.filterMode       = FilterMode.Bilinear;
        rt.wrapMode         = TextureWrapMode.Clamp;
        rt.useMipMap        = false;
        rt.autoGenerateMips = false;
        if (!rt.Create()) return false;
        // beyond-the-cloud-shell aurora, occluded per pixel by EVE's transmittance in the composite
        rtFar = new RenderTexture(w, h, 0, RenderTextureFormat.ARGBHalf);
        rtFar.name             = "RVA_RT_far";
        rtFar.filterMode       = FilterMode.Bilinear;
        rtFar.wrapMode         = TextureWrapMode.Clamp;
        rtFar.useMipMap        = false;
        rtFar.autoGenerateMips = false;
        if (!rtFar.Create()) return false;
        auroraTargets = new RenderTargetIdentifier[] { rt, rtFar };
        return true;
    }

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
        if (transform.parent != null)
        {
            Vector3 c = transform.parent.position;
            auroraMaterial.SetVector("_PlanetCenter", new Vector4(c.x, c.y, c.z, 0f));
        }

        // Pixel footprint for the shader's anti-aliasing (radians per aurora-buffer pixel)
        int bufferHeight = (rt != null) ? rt.height : Mathf.Max(cam.pixelHeight, 1);
        auroraMaterial.SetFloat("_PixelAngle", 2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / bufferHeight);

        if (directMode || cbRender == null || cbBlit == null || rt == null || rtFar == null) return;

        blitMaterial.SetFloat("_CloudOcclusion", OcclusionActive ? 1f : 0f);

        // RENDER — rebuilt every frame: the mesh follows the camera and the RT may change.
        cbRender.Clear();
        cbRender.SetRenderTarget(rtFar);
        cbRender.ClearRenderTarget(false, true, Color.clear);
        cbRender.SetRenderTarget(rt);
        cbRender.ClearRenderTarget(true, true, Color.clear);
        // two colour targets: aurora before the cloud-top shell (rt) and beyond it (rtFar)
        auroraMaterial.SetFloat("_FarSplit", 1f);
        cbRender.SetRenderTarget(auroraTargets, rt);
        var m = Matrix4x4.TRS(cam.transform.position, Quaternion.identity, Vector3.one * radius * 2f);
        cbRender.DrawMesh(sphereMesh, m, auroraMaterial, 0, 0);
        cbRender.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);

        // COMPOSITE
        cbBlit.Clear();
        cbBlit.Blit(rt, BuiltinRenderTextureType.CameraTarget, blitMaterial, auroraOnly ? 1 : 0);
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
        if (rtFar != null) blitMaterial.SetTexture("_FarTex", rtFar);
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
        Status = $"low-res mode: RT {rt.width}x{rt.height} ×2 (scale {renderScale:F2}), render @ {NameOf(RenderEventCode())}, composite @ {NameOf(cc)}{clamped}, cloud occlusion {(OcclusionActive ? "per pixel (EVE buffer)" : cloudOcclusion ? "off (no EVE volumetrics on any camera)" : "off")}, mesh radius {radius:F0} m, camera '{farCam.name}' ({cameraMode}), depth occlusion {(sceneDepthOcclusion ? "on" : "off")}";
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
        if (rt != null)           { rt.Release(); Destroy(rt); rt = null; }
        if (rtFar != null)        { rtFar.Release(); Destroy(rtFar); rtFar = null; }
        auroraTargets = null;
        directMode = false;
    }

    void OnDestroy() { Teardown(); }

    static void Log(string m) => Debug.Log($"{LogPrefix} {m}");
}
