// Realistic Volumetric Aurorae (RVA). Copyright (c) 2026 HereComesTheBoom ("Realistic Volumetric Aurorae")
// SPDX-License-Identifier: CC-BY-NC-SA-4.0  (see LICENSE.txt)
using System.Text;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class AuroraRenderer : MonoBehaviour
{

    public const int EvAfterForwardAlpha = 0, EvAfterImageEffects = 1, EvBeforeImageEffects = 2,
                     EvBeforeForwardAlpha = 3, EvAfterEverything = 4, EvAfterForwardOpaque = 5;
    public static readonly string[] EventNames = { "AfterForwardAlpha", "AfterImageEffects", "BeforeImageEffects", "BeforeForwardAlpha", "AfterEverything", "AfterForwardOpaque" };

    public Material auroraMaterial;
    public string   cameraName;
    public Material syncFrom;
    public float    syncScale = 1f;
    public bool     renderingEnabled = true;
    public Shader   blitShader;

    [Range(0.1f, 1f)] public float renderScale = 0.3f;
    public float meshRadius = 5000f;
    public float exposure = 1f;
    public bool  flipY = false;
    public float smoothing = 0.5f;
    public bool  edgeAware = true;
    public bool  auroraOnly = false;
    public int   compositeEvent = EvBeforeForwardAlpha;
    public bool  cloudOcclusion = true;
    public int   cloudBufferFormat = 2;
    public bool  bufferFlipY = false;
    public bool  sceneDepthOcclusion = true;
    public Transform occluder;
    public CelestialBody occluderBody;
    public CelestialBody planetBody;

    public Vector3 OccluderCentre { get; private set; }
    public float     occluderRadius;
    public Mesh      occluderMesh;
    public Matrix4x4 occluderMeshRS = Matrix4x4.identity;
    public bool      occluderMeshOn;
    public Texture2D occluderHeightMap;
    public float     occluderDatum;
    public bool      planetModelOn;

    public bool      OccluderDepthActive { get; private set; }
    public bool  forceFar = false;

    public bool  temporal = true;
    public float temporalWeight = 0.9f;
    public float temporalUpscale = 2f;
    public float temporalClamp = 2f;
    public float temporalParallax = 3f;

    public float cloudGrazeBoost = 1f;

    public int   blitDebug = 0;

    public bool   Initialized => initialized;
    public bool   DirectMode  => directMode;
    public string Status { get; private set; } = "waiting for flight camera";

    const string LogPrefix = "[RVA:Renderer]";

    Camera        farCam;
    RenderTexture rt, rtFar, rtAux;
    RenderTargetIdentifier[] auroraTargets;
    RenderTexture occluderDepth;
    const int     OccluderDepthPass = 3;

    readonly RenderTexture[] histNear = new RenderTexture[2], histFar = new RenderTexture[2];
    readonly RenderTargetIdentifier[][] histTargets = new RenderTargetIdentifier[2][];
    int           histIndex;
    bool          histValid;
    int           lastRenderFrame = -10;
    int           temporalFrame;
    Matrix4x4     prevVP, curVP;
    bool          prevVPValid, curVPValid;
    bool          histWarned;
    Mesh          sphereMesh;
    Material      blitMaterial;
    CommandBuffer cbRender, cbBlit;

    int appliedRender = -1, appliedComposite = -1;
    GameObject    directSphere;
    float         radius;
    bool          initialized, directMode;
    int           findAttempts, cameraRecheck;
    readonly System.Diagnostics.Stopwatch preCullWatch = new System.Diagnostics.Stopwatch();

    public float LastPreCullMs { get; private set; }

    public Camera TargetCamera => farCam;
    string        cameraMode = "";

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

    bool OcclusionActive => cloudOcclusion && eveVolumetrics;
    bool eveVolumetrics;

    bool Cloud2DWanted => cloudOcclusion && !string.IsNullOrEmpty(cloudBody) && RVA.AuroraCloud2D.Available;
    RenderTexture cloud2DBlack, cloud2DWhite;
    CommandBuffer cbCloud2DScaled; Camera cloud2DScaledCam; bool cloud2DScaledAttached;
    int  cloud2DClearedFrame = -1;
    int  cloud2DScaledCamSearch;

    public int Cloud2DLayers { get; private set; }
    public int Cloud2DScaledLayers { get; private set; }

    bool EnsureCloud2D(Camera cam)
    {
        int w = Mathf.Max(8, cam.pixelWidth / 2), h = Mathf.Max(8, cam.pixelHeight / 2);
        if (cloud2DBlack != null && cloud2DBlack.width == w && cloud2DBlack.height == h) return true;
        Drop(ref cloud2DBlack); Drop(ref cloud2DWhite);
        cloud2DBlack = MakeRT("RVA_cloud2D_black", w, h, 0, RenderTextureFormat.ARGBHalf, FilterMode.Bilinear);
        cloud2DWhite = MakeRT("RVA_cloud2D_white", w, h, 0, RenderTextureFormat.ARGBHalf, FilterMode.Bilinear);
        if (cloud2DBlack != null && cloud2DWhite != null) Log($"flat cloud opacity buffers {w}x{h} x2");
        return cloud2DBlack != null && cloud2DWhite != null;
    }

    static int RecordCloud2D(CommandBuffer cb, RenderTexture black, RenderTexture white, List<RVA.AuroraCloud2D.Layer> layers, bool scaled, bool clear)
    {
        int n = 0;
        cb.SetRenderTarget(black);
        if (clear) cb.ClearRenderTarget(false, true, Color.black);
        foreach (var l in layers)
            if (l.scaled == scaled && l.renderer != null && l.material != null) { cb.DrawRenderer(l.renderer, l.material, 0, 0); n++; }
        cb.SetRenderTarget(white);
        if (clear) cb.ClearRenderTarget(false, true, Color.white);
        foreach (var l in layers)
            if (l.scaled == scaled && l.renderer != null && l.material != null) cb.DrawRenderer(l.renderer, l.material, 0, 0);
        return n;
    }

    void DropCloud2D()
    {
        if (cloud2DScaledAttached && cloud2DScaledCam != null && cbCloud2DScaled != null)
            try { cloud2DScaledCam.RemoveCommandBuffer(CameraEvent.AfterForwardAlpha, cbCloud2DScaled); } catch { }
        cloud2DScaledAttached = false;
        if (cbCloud2DScaled != null) { cbCloud2DScaled.Release(); cbCloud2DScaled = null; }
        Drop(ref cloud2DBlack); Drop(ref cloud2DWhite);
    }
    int  eveCheckFrame = -1000;
    static System.Type eveRendererType; static bool eveTypeSearched;

    public float cameraAltitudeM = -1f;
    public float volumetricsCeilingM = -1f;

    public string OcclusionIdle { get; private set; } = "";

    public string cloudBody;
    float idleLogged = -100f;

    static System.Reflection.FieldInfo eveRenderingField, eveVolumesField; static bool eveFieldsSearched, eveFlagSeenTrue; static int eveFlagFrame = -10;

    static bool EveVolumetricsRendering(Camera cam)
    {
        if (eveRendererType == null) return false;
        if (!eveFieldsSearched)
        {
            eveFieldsSearched = true;
            try
            {
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                eveRenderingField = eveRendererType.GetField("renderingEnabled", flags);
                if (eveRenderingField != null && eveRenderingField.FieldType != typeof(bool)) eveRenderingField = null;
                eveVolumesField = eveRendererType.GetField("volumesAdded", flags);
                if (eveVolumesField != null && !typeof(System.Collections.ICollection).IsAssignableFrom(eveVolumesField.FieldType)) eveVolumesField = null;
                Debug.Log($"[RVA] EVE volumetrics renderer: per-frame state {(eveRenderingField != null ? "readable (renderingEnabled)" : eveVolumesField != null ? "readable (volumesAdded)" : "not readable; the fade-out altitude from the configs decides alone")}");
            }
            catch { eveRenderingField = null; eveVolumesField = null; }
        }
        if (eveRenderingField == null && eveVolumesField == null) return true;
        bool known = false, any = false;
        try
        {
            Component comp = cam != null ? cam.GetComponent(eveRendererType) : null;
            if (comp != null) { known = true; any = Flag(comp); }
            else
            {

                foreach (var c in Camera.allCameras)
                {
                    if (c == null) continue;
                    var other = c.GetComponent(eveRendererType);
                    if (other == null) continue;
                    known = true;
                    if (Flag(other)) { any = true; break; }
                }
            }
        }
        catch { known = false; }
        if (any) { eveFlagSeenTrue = true; eveFlagFrame = Time.frameCount; }
        return !known || !eveFlagSeenTrue || any || eveFlagFrame == Time.frameCount;
    }
    static bool Flag(Component comp)
    {
        if (eveRenderingField != null) return (bool)eveRenderingField.GetValue(comp);
        var col = eveVolumesField.GetValue(comp) as System.Collections.ICollection;
        return col != null && col.Count > 0;
    }

    bool BufferLive(out string why)
    {
        why = "";
        if (!OcclusionActive) return false;
        if (volumetricsCeilingM > 0f && cameraAltitudeM >= volumetricsCeilingM)
        {
            why = $"camera {cameraAltitudeM / 1000f:F0} km up, above the volumetrics fade-out at {volumetricsCeilingM / 1000f:F0} km";
            return false;
        }
        if (!EveVolumetricsRendering(farCam)) { why = "EVE is not rendering volumetrics on this camera this frame"; return false; }
        return true;
    }

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

    int CompositeEventCode()
    {
        int code = Clamp(compositeEvent);
        if (OcclusionActive && Rank(code) < Rank(EvBeforeForwardAlpha)) code = EvBeforeForwardAlpha;
        return code;
    }

    int RenderEventCode() { int c = CompositeEventCode(); return Rank(c) < Rank(EvAfterForwardAlpha) ? c : EvAfterForwardAlpha; }

    void TryInit()
    {
        if (auroraMaterial == null) return;

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

        if (sceneDepthOcclusion) farCam.depthTextureMode |= DepthTextureMode.Depth;
        auroraMaterial.SetFloat("_UseSceneDepth", sceneDepthOcclusion ? 1f : 0f);

        if (string.IsNullOrEmpty(cameraName)) farCam.forceIntoRenderTexture = true;

        Camera.onPreCull   += OnAnyCameraPreCull;
        Camera.onPreRender += OnAnyCameraPreRender;

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
        auroraMaterial.SetFloat("_FarSplit", 0f);
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
        var mesh = tmp.GetComponent<MeshFilter>().sharedMesh;
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

        rt = MakeRT("RVA_RT", w, h, 16, RenderTextureFormat.ARGBHalf, FilterMode.Bilinear);
        if (rt == null) return false;

        rtFar = MakeRT("RVA_RT_far", w, h, 0, RenderTextureFormat.ARGBHalf, FilterMode.Bilinear);
        if (rtFar == null) return false;

        rtAux = MakeRT("RVA_RT_aux", w, h, 0, RenderTextureFormat.RGFloat, FilterMode.Point);
        if (rtAux == null) return false;
        auroraTargets = new RenderTargetIdentifier[] { rt, rtFar, rtAux };
        DropHistory();
        return true;
    }

    void DropHistory()
    {
        for (int k = 0; k < 2; k++) { Drop(ref histNear[k]); Drop(ref histFar[k]); histTargets[k] = null; }
        histValid = false;
    }

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

            histNear[k] = MakeRT("RVA_hist_near" + k, w, h, 16, RenderTextureFormat.ARGBHalf, FilterMode.Bilinear);
            histFar[k]  = MakeRT("RVA_hist_far"  + k, w, h, 0,  RenderTextureFormat.ARGBHalf, FilterMode.Bilinear);
            if (histNear[k] == null || histFar[k] == null) { DropHistory(); return false; }
            histTargets[k] = new RenderTargetIdentifier[] { histNear[k], histFar[k] };
        }
        histValid = false;
        return true;
    }

    bool TemporalActive => temporal && !directMode && histNear[0] != null && histNear[1] != null;

    bool EnsureOccluderDepth(Camera cam)
    {
        if (rt == null) return false;

        int w = Mathf.Max(8, Mathf.Max(rt.width,  cam.pixelWidth));
        int h = Mathf.Max(8, Mathf.Max(rt.height, cam.pixelHeight));
        if (occluderDepth != null && occluderDepth.width == w && occluderDepth.height == h) return true;
        Drop(ref occluderDepth);
        occluderDepth = MakeRT("RVA_occluder_depth", w, h, 0, RenderTextureFormat.RFloat, FilterMode.Point);
        occluderDepthAllocs++;
        if (occluderDepth != null) Log($"occluder model depth buffer {w}x{h} (allocation {occluderDepthAllocs}; camera {cam.pixelWidth}x{cam.pixelHeight}, aurora buffer {rt.width}x{rt.height})");
        return occluderDepth != null;
    }
    int occluderDepthAllocs;

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

    void OnAnyCameraPreRender(Camera cam)
    {
        if (cam != farCam || !initialized || !renderingEnabled || directMode || blitMaterial == null || cbBlit == null) return;

        bool live = BufferLive(out string idleWhy);
        blitMaterial.SetFloat("_CloudOcclusion", live ? 1f : 0f);
        if (idleWhy != OcclusionIdle)
        {
            OcclusionIdle = idleWhy;
            UpdateStatus();
            if (Time.unscaledTime - idleLogged > 10f) { idleLogged = Time.unscaledTime; Log(idleWhy.Length > 0 ? $"volumetrics buffer not read ({idleWhy}), flat layers only" : "volumetrics buffer live again"); }
        }
    }

    void OnAnyCameraPreCull(Camera cam)
    {
        if (!initialized) return;

        if (cam != farCam)
        {
            if (cam == cloud2DScaledCam && renderingEnabled && !directMode && cbCloud2DScaled != null && cloud2DBlack != null)
            {
                cbCloud2DScaled.Clear();
                if (Cloud2DWanted)
                {
                    var layers = RVA.AuroraCloud2D.Layers(cloudBody);
                    Cloud2DScaledLayers = RecordCloud2D(cbCloud2DScaled, cloud2DBlack, cloud2DWhite, layers, true, true);
                    cloud2DClearedFrame = Time.frameCount;
                }
            }
            return;
        }

        if (directMode && directSphere != null && directSphere.activeSelf != renderingEnabled)
            directSphere.SetActive(renderingEnabled);

        if (!renderingEnabled)
        {
            if (cbRender != null) { cbRender.Clear(); cbBlit.Clear(); }
            return;
        }

        preCullWatch.Restart();

        if (syncFrom != null)
        {
            auroraMaterial.CopyPropertiesFromMaterial(syncFrom);
            if (syncScale != 1f)
            {
                auroraMaterial.SetFloat("_SphereRadius",   syncFrom.GetFloat("_SphereRadius")   * syncScale);
                auroraMaterial.SetFloat("_AltBase",        syncFrom.GetFloat("_AltBase")        * syncScale);
                auroraMaterial.SetFloat("_AltTop",         syncFrom.GetFloat("_AltTop")         * syncScale);
                auroraMaterial.SetFloat("_CloudTopAlt",    syncFrom.GetFloat("_CloudTopAlt")    * syncScale);
                auroraMaterial.SetFloat("_CloudBaseAlt",   syncFrom.GetFloat("_CloudBaseAlt")   * syncScale);
            }
            auroraMaterial.SetFloat("_UseSceneDepth", sceneDepthOcclusion ? 1f : 0f);
        }

        Vector3 centre = planetBody != null ? (Vector3)planetBody.position
                       : transform.parent != null ? transform.parent.position : Vector3.zero;
        if (planetBody != null || transform.parent != null)
            auroraMaterial.SetVector("_PlanetCenter", new Vector4(centre.x, centre.y, centre.z, 0f));

        bool ownModel = planetModelOn && occluder == null && occluderBody == null;
        Vector3 occPos = occluderBody != null ? (Vector3)occluderBody.position : occluder != null ? occluder.position : ownModel ? centre : Vector3.zero;

        if (ownModel && planetBody != null)
        {
            try { occluderMeshRS = occluderHeightMap != null ? RVA.AuroraTerrainMap.Rotation(planetBody) : RVA.ScaledAuroraInstance.ScaledMeshRS(planetBody); }
            catch { }
        }
        OccluderCentre = occPos;
        if ((occluder != null || occluderBody != null) && occluderRadius > 0f)
        {
            Vector3 o = occPos - centre;
            auroraMaterial.SetVector("_Occluder", new Vector4(o.x, o.y, o.z, occluderRadius));
        }
        else auroraMaterial.SetVector("_Occluder", Vector4.zero);
        auroraMaterial.SetFloat("_ForceFar", forceFar ? 1f : 0f);

        int bufferHeight = (rt != null) ? rt.height : Mathf.Max(cam.pixelHeight, 1);
        float tanV = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        auroraMaterial.SetFloat("_PixelAngle", 2f * tanV / bufferHeight);

        if (directMode || cbRender == null || cbBlit == null || rt == null || rtFar == null || rtAux == null) return;

        bool newFrame = Time.frameCount != lastRenderFrame;
        if (newFrame)
        {
            if (Time.frameCount - lastRenderFrame != 1) { histValid = false; curVPValid = false; }
            prevVP = curVP; prevVPValid = curVPValid; curVPValid = false;
            histIndex = 1 - histIndex;
            temporalFrame++;
            lastRenderFrame = Time.frameCount;
        }
        bool useTemporal = TemporalActive;
        if (!useTemporal) { histValid = false; prevVPValid = false; }

        Vector2 jit   = useTemporal ? JitterSeq[temporalFrame & 7] : Vector2.zero;
        float   tOff  = useTemporal ? Mathf.Repeat(0.6180340f * temporalFrame, 1f) : 0f;
        float   tanH  = tanV * cam.aspect;

        cbRender.Clear();

        cbRender.SetGlobalVector("_RayJitter", new Vector4(jit.x * 2f * tanH / rt.width, jit.y * 2f * tanV / rt.height, 0f, 0f));
        cbRender.SetGlobalFloat("_TemporalOffset", tOff);

        bool cloud2D = Cloud2DWanted && EnsureCloud2D(cam);
        if (cloud2D)
        {
            if (cloud2DScaledCam == null && ++cloud2DScaledCamSearch % 30 == 1)
                foreach (var c in Camera.allCameras) if (c != null && c.name == "Camera ScaledSpace") { cloud2DScaledCam = c; break; }
            if (cloud2DScaledCam != null && !cloud2DScaledAttached)
            {
                if (cbCloud2DScaled == null) cbCloud2DScaled = new CommandBuffer { name = "RVA flat clouds (scaled)" };
                cloud2DScaledCam.AddCommandBuffer(CameraEvent.AfterForwardAlpha, cbCloud2DScaled);
                cloud2DScaledAttached = true;
                Log($"flat cloud layers in scaled space drawn on '{cloud2DScaledCam.name}'");
            }
            var layers2D = RVA.AuroraCloud2D.Layers(cloudBody);
            bool cleared = cloud2DClearedFrame == Time.frameCount;
            if (!cleared) Cloud2DScaledLayers = 0;
            Cloud2DLayers = RecordCloud2D(cbRender, cloud2DBlack, cloud2DWhite, layers2D, false, !cleared) + Cloud2DScaledLayers;
            blitMaterial.SetTexture("_Cloud2DBlack", cloud2DBlack);
            blitMaterial.SetTexture("_Cloud2DWhite", cloud2DWhite);
        }
        else Cloud2DLayers = 0;
        blitMaterial.SetFloat("_Cloud2DOn", cloud2D ? 1f : 0f);

        bool modelDepth = occluderMeshOn && occluderMesh != null && (((occluder != null || occluderBody != null) && occluderRadius > 0f) || ownModel) && EnsureOccluderDepth(cam);
        if (modelDepth)
        {
            cbRender.SetRenderTarget(occluderDepth);
            cbRender.ClearRenderTarget(false, true, Color.clear);

            blitMaterial.SetTexture("_OccluderHeightMap", occluderHeightMap);
            blitMaterial.SetVector("_OccluderHeightParams", new Vector4(occluderDatum, occluderHeightMap != null ? 1f : 0f, 0f, 0f));
            cbRender.DrawMesh(occluderMesh, Matrix4x4.Translate(occPos) * occluderMeshRS, blitMaterial, 0, OccluderDepthPass);
            auroraMaterial.SetTexture("_OccluderDepth", occluderDepth);
            blitMaterial.SetTexture("_LimbModelDepth", occluderDepth);
        }
        auroraMaterial.SetVector("_OccluderDepthParams", new Vector4(1f / rt.width, 1f / rt.height, modelDepth ? 1f : 0f, 0f));
        blitMaterial.SetFloat("_LimbModelOn", modelDepth ? 1f : 0f);
        OccluderDepthActive = modelDepth;

        cbRender.SetRenderTarget(rtFar);
        cbRender.ClearRenderTarget(false, true, Color.clear);
        cbRender.SetRenderTarget(rtAux);
        cbRender.ClearRenderTarget(false, true, Color.clear);
        cbRender.SetRenderTarget(rt);
        cbRender.ClearRenderTarget(true, true, Color.clear);

        auroraMaterial.SetFloat("_FarSplit", 1f);
        cbRender.SetRenderTarget(auroraTargets, rt);
        var m = Matrix4x4.TRS(cam.transform.position, Quaternion.identity, Vector3.one * radius * 2f);
        cbRender.DrawMesh(sphereMesh, m, auroraMaterial, 0, 0);

        Matrix4x4 vp = cam.projectionMatrix * cam.worldToCameraMatrix * Matrix4x4.Translate(centre);

        Vector3 camRel = cam.transform.position - centre;
        float   R      = auroraMaterial.GetFloat("_SphereRadius");
        blitMaterial.SetFloat("_CloudGraze", cloudGrazeBoost);
        blitMaterial.SetFloat("_BlitDebug", blitDebug);

        {
            bool  occ  = (occluder != null || occluderBody != null) && occluderRadius > 0f;
            float capR = occ ? occluderRadius : R;
            Vector3 capC = occ ? occPos : centre;
            float dCap = (cam.transform.position - capC).magnitude;
            float limb = Mathf.Sqrt(Mathf.Max(dCap * dCap - capR * capR, 0f));
            blitMaterial.SetFloat("_GroundCapKm", (1.5f * limb + 0.2f * capR) * 0.001f);
        }

        RenderTexture compNear = rt, compFar = rtFar;
        if (useTemporal)
        {

            int cur = histIndex, prev = 1 - histIndex;
            float shellMid = R + 0.5f * (auroraMaterial.GetFloat("_AltBase") + auroraMaterial.GetFloat("_AltTop"));
            blitMaterial.SetTexture("_CurNear",  rt);
            blitMaterial.SetTexture("_CurFar",   rtFar);
            blitMaterial.SetTexture("_CurAux",   rtAux);
            blitMaterial.SetTexture("_HistNear", histNear[prev]);
            blitMaterial.SetTexture("_HistFar",  histFar[prev]);
            blitMaterial.SetVector("_CurTexelSize",   new Vector4(1f / rt.width, 1f / rt.height, rt.width, rt.height));
            blitMaterial.SetVector("_HistTexelSize",  new Vector4(1f / histNear[cur].width, 1f / histNear[cur].height, histNear[cur].width, histNear[cur].height));
            blitMaterial.SetVector("_TemporalJitter", new Vector4(jit.x, jit.y, 0f, 0f));

            blitMaterial.SetVector("_TemporalParams", new Vector4(Mathf.Clamp(temporalWeight, 0f, 0.99f), Mathf.Max(temporalClamp, 0.1f), 2.469f, (histValid && prevVPValid) ? 1f : 0f));

            float parallax = Mathf.Max(temporalParallax, 0.5f);
            blitMaterial.SetVector("_TemporalParams2", new Vector4(0.25f * parallax, parallax, 0f, 0f));
            blitMaterial.SetVector("_CamPosRel",      new Vector4(camRel.x, camRel.y, camRel.z, 0f));
            blitMaterial.SetVector("_ShellParams",    new Vector4(shellMid, shellMid * 20f, 0f, 0f));
            blitMaterial.SetMatrix("_PrevVP", prevVPValid ? prevVP : vp);
            cbRender.SetRenderTarget(histTargets[cur], histNear[cur]);
            cbRender.DrawProcedural(Matrix4x4.identity, blitMaterial, 2, MeshTopology.Triangles, 3);
            compNear = histNear[cur]; compFar = histFar[cur];
            histValid = true;
        }
        cbRender.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
        curVP = vp; curVPValid = useTemporal;

        blitMaterial.SetTexture("_FarTex", compFar);
        cbBlit.Clear();
        cbBlit.Blit(compNear, BuiltinRenderTextureType.CameraTarget, blitMaterial, (auroraOnly || blitDebug > 0) ? 1 : 0);
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

        blitMaterial.SetFloat("_NearDepthKm", auroraMaterial != null && auroraMaterial.HasProperty("_SphereRadius") ? auroraMaterial.GetFloat("_SphereRadius") * 0.05f / 1000f : 30f);

        blitMaterial.SetFloat("_GrabFlipY", bufferFlipY ? 1f : 0f);
        blitMaterial.SetFloat("_CloudBufferFormat", cloudBufferFormat);
        blitMaterial.SetFloat("_GroundCapKm", 1e7f);

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
        string clamped = cc != Clamp(compositeEvent) ? $" (asked for {NameOf(Clamp(compositeEvent))}, cloud occlusion needs EVE's buffer from AfterForwardOpaque)" : "";
        Status = $"low-res mode: RT {rt.width}x{rt.height} x3 (scale {renderScale:F2}), render @ {NameOf(RenderEventCode())}, composite @ {NameOf(cc)}{clamped}, cloud occlusion {(cloudOcclusion ? $"{(OcclusionActive ? (OcclusionIdle.Length > 0 ? $"volumetrics buffer not read ({OcclusionIdle})" : "volumetrics per pixel (EVE buffer) with the horizon boost") : "no EVE volumetrics on any camera")}; flat layers {(Cloud2DWanted ? $"{Cloud2DLayers} drawn ({Cloud2DScaledLayers} in scaled space)" : RVA.AuroraCloud2D.Available ? "none (no body)" : $"off ({RVA.AuroraCloud2D.Why})")}" : "off")}, mesh radius {radius:F0} m, camera '{farCam.name}' ({cameraMode}), depth occlusion {(sceneDepthOcclusion ? "on" : "off")}";
    }

    public string PerfStatus
    {
        get
        {
            if (!initialized || directMode) return "";
            return TemporalActive ? $"temporal history {histNear[0].width}x{histNear[0].height} x2, weight {temporalWeight:F2}, clamp {temporalClamp:F1} sigma, parallax limit {temporalParallax:F1} texels{(histValid ? "" : " (filling)")}"
                 : temporal ? "temporal history pending" : "temporal accumulation off";
        }
    }

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
                if (c == null || !c.gameObject.scene.IsValid()) continue;
                bool on = c.enabled && c.gameObject.activeInHierarchy;
                if (!on) continue;
                sb.AppendLine($"  camera '{c.name}' depth {c.depth:F1} ENABLED near {c.nearClipPlane:F2} far {c.farClipPlane:F0} clear {c.clearFlags} HDR {c.allowHDR} target {(c.targetTexture != null ? c.targetTexture.name : "screen")} mask 0x{c.cullingMask:X8}");

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

    static Camera FindCamera(string name)
    {
        foreach (var cam in Camera.allCameras)
            if (cam != null && cam.name == name) return cam;
        return null;
    }

    Camera FindTargetCamera(out string mode)
    {
        if (!string.IsNullOrEmpty(cameraName)) { mode = cameraName; return FindCamera(cameraName); }
        return FindFlightCamera(out mode);
    }

    static Camera FindFlightCamera(out string mode)
    {
        var far = FindCamera("Camera 01");
        if (far != null) { mode = "two-camera mode"; return far; }

        var near = FindCamera("Camera 00");
        if (near != null && near.farClipPlane > 50000f) { mode = "unified camera mode"; return near; }

        mode = "";
        return null;
    }

    RenderTexture probeRT;
    Texture2D     modelTex;
    Texture2D     probeTex;

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

        float linRev = 1f / ((-1f + f / n) / f * raw + 1f / f);
        float linStd = 1f / ((1f - f / n) / f * raw + (f / n) / f);
        float lin = rev ? linRev : linStd;
        Ray ray = farCam.ScreenPointToRay(new Vector3(sp.x, sp.y, 0f));
        float t = lin / Mathf.Max(Vector3.Dot(ray.direction, farCam.transform.forward), 0.05f);
        Vector3 pRel = (farCam.transform.position - new Vector3(pc.x, pc.y, pc.z)) + ray.direction * t;
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
        Camera.onPreCull   -= OnAnyCameraPreCull;
        Camera.onPreRender -= OnAnyCameraPreRender;
        Detach();
        if (cbRender != null)     { cbRender.Release();  cbRender = null; }
        if (cbBlit != null)       { cbBlit.Release();    cbBlit = null; }
        if (directSphere != null) { Destroy(directSphere); directSphere = null; }
        if (blitMaterial != null) { Destroy(blitMaterial); blitMaterial = null; }
        Drop(ref rt); Drop(ref rtFar); Drop(ref rtAux); Drop(ref occluderDepth);
        DropCloud2D();
        DropHistory();
        auroraTargets = null;
        OccluderDepthActive = false;
        directMode = false;
        prevVPValid = curVPValid = false;
    }

    void OnDestroy() { Teardown(); }

    static void Log(string m) => Debug.Log($"{LogPrefix} {m}");
}
