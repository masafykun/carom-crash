using System.Collections.Generic;
using UnityEngine;

// CAROM CRASH — 3D BANK-SHOT slingshot demolition (Angry-Birds-in-3D, ricochet family).
// ONE control: DRAG BACK & RELEASE (or ARROWS + SPACE / touch). Pull the chrome ball back to load
// power, drag sideways to aim; a dotted arc previews the flight — INCLUDING the bounces it will take
// off the bright neon DEFLECTOR panels. That is the whole game: kiss the glowing panels to bank the
// ball around cover and into the fortress. Every deflector you kiss BEFORE impact climbs a RICOCHET
// multiplier (×2 ×3 ×4 …) that scales ALL the destruction on that shot. Targets tuck behind dull
// shield walls, so the fat scores live in the trick-shot banks. Limited balls, clear the crystals.
//
// Distinct from the studio's other two slingshot games: sling-smash = direct knock-off, chain-blast =
// TNT chain reactions. CAROM CRASH = ricochet bank shots (kiss panels → multiply → bank around cover).
//
// Studio identity: (1) RICOCHET CHAIN — each bank kiss climbs a pentatonic scale + builds the mult.
// (2) TRICK SHOT — break a target that hid behind cover = fat bonus + gold bloom. (3) OVERBLAST FEVER —
// carry a ×4+ ricochet into your first structure hit and it detonates a shockwave that scatters the
// whole fortress (that shot's breaks score ×2). Bank shots are aimable, not luck: the preview reflects
// off the same axis-aligned panels the ball does. Accessible: every crystal is also reachable by a
// direct lob (don't-die = easy), banking is the skill/score path (big-score = skill).
//
// Built entirely from code (CreatePrimitive) so it renders & simulates reliably in WebGL with engine-
// code stripping DISABLED (see AutoBuilder). ONLY Box/Sphere/Capsule colliders that ship with
// primitives — never MeshCollider (coin-cruiser's WebGL killer). Real Rigidbody physics drives the
// toppling & ricochet; preview dots / debris are collider-free. Coexists with Juice & AutoShot.
public class CaromCrash : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        Application.runInBackground = true;
        var go = new GameObject("__CaromCrash");
        go.AddComponent<CaromCrash>();
        DontDestroyOnLoad(go);
    }

    // ---- scene refs ----
    Transform cam; Camera camComp;
    Transform pouch, band0, band1;
    Vector3 pouchRest;
    TextMesh hudScore, hudBest, hudLevel, hudShots, hudTargets, hudMult, hudHint, comboText, bannerText, dbg;

    readonly List<Block> blocks = new List<Block>();
    readonly List<Transform> dots = new List<Transform>();
    readonly List<Reflector> reflectors = new List<Reflector>();   // bright deflector panels (bounce + multiply)
    readonly List<GameObject> arena = new List<GameObject>();       // per-level static geometry to clear
    CcProjectile activeProj;

    struct Reflector { public Vector3 c; public Vector3 half; public int axis; } // axis = thin/normal axis

    // ---- state ----
    enum State { Idle, Flying, Cleared, GameOver }
    State state = State.Idle;
    int score, best, level = 1, shots, targetsRemaining;
    int combo; float comboTimer, comboFlash, bannerTimer, clearTimer;
    bool aiming; Vector2 dragStart; Vector3 lastVel; float lastSpeed01, lastYaw;
    bool attract = true, started; float attractTimer;
    float aspect = 1.78f, hudScale = 1f, halfH, halfW;

    // per-shot ricochet
    int bankKiss; int ricoMult = 1; bool overblast; int shotBreaks;
    int pentIdx; static readonly float[] PENT = { 523.25f, 587.33f, 659.25f, 783.99f, 880f, 1046.5f, 1174.66f, 1318.5f };

    // ---- tuning ----
    const float FZ = 7.2f;                 // fortress centre (forward = +Z)
    static readonly Vector3 LAUNCH = new Vector3(0f, 2.7f, -3.0f);
    const float MAXDRAG = 260f;
    const float MINSPEED = 9f, MAXSPEED = 23f;
    const float MAXYAW = 40f;
    const float BALL_R = 0.35f;
    const float GROUND_Y = 0f;
    const float KILL_Y = -3.4f;
    const float HUD_Z = 6.5f;
    const int DOT_COUNT = 26;
    float towerTopY = 5f;

    // key-aim (non-drag) fallback
    float aimPow = 0.6f, aimYaw = 0f; bool keyAiming;

    // debug
    bool showDbg; int dbgPops, dbgBanks;

    Material crateMat, crateMat2, stoneMat, targMat, targMat2, platMat, edgeMat, ballMat, ballCore,
             bandMat, postMat, dotMat, dotMatFar, deflMat, deflCore, shieldMat, silMat;

    // ===================================================================== boot
    void Start()
    {
        // clean slate — kill any pre-existing camera/light/leftover primitive (chain-blast lesson)
        foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None)) Destroy(c.gameObject);
        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) Destroy(l.gameObject);
        foreach (var mr in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            if (mr.gameObject.name != "T") Destroy(mr.gameObject);

        best = PlayerPrefs.GetInt("caromcrash_best", 0);

        Physics.gravity = new Vector3(0f, -22f, 0f);
        Physics.defaultSolverIterations = 14;

        BuildMaterials();
        BuildWorld();
        BuildCamera();
        BuildSling();
        BuildHud();
        BuildDots();

        level = 1; score = 0;
        BuildLevel();
        attractTimer = 1.0f;
    }

    // ===================================================================== materials
    static Material Mat(Color c, float metal = 0f, float smooth = 0.25f, bool emi = false, float ei = 0.8f)
    {
        var sh = Shader.Find("Universal Render Pipeline/Lit"); if (sh == null) sh = Shader.Find("Standard");
        var m = new Material(sh);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
        if (emi && m.HasProperty("_EmissionColor")) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * ei); }
        return m;
    }

    void BuildMaterials()
    {
        crateMat  = Mat(new Color(0.80f, 0.53f, 0.27f), 0f, 0.18f);
        crateMat2 = Mat(new Color(0.88f, 0.63f, 0.33f), 0f, 0.18f);
        stoneMat  = Mat(new Color(0.46f, 0.49f, 0.56f), 0.15f, 0.30f);
        targMat   = Mat(new Color(0.25f, 0.95f, 0.85f), 0f, 0.7f, true, 1.6f);
        targMat2  = Mat(new Color(1.00f, 0.45f, 0.85f), 0f, 0.7f, true, 1.6f);
        platMat   = Mat(new Color(0.13f, 0.16f, 0.26f), 0f, 0.22f);
        edgeMat   = Mat(new Color(0.35f, 0.85f, 1.00f), 0f, 0.5f, true, 0.9f);
        ballMat   = Mat(new Color(0.95f, 0.97f, 1.00f), 0.55f, 0.9f);
        ballCore  = Mat(new Color(1.00f, 0.75f, 0.25f), 0f, 0.6f, true, 1.5f);
        bandMat   = Mat(new Color(0.55f, 0.20f, 0.16f), 0f, 0.3f);
        postMat   = Mat(new Color(0.34f, 0.24f, 0.16f), 0f, 0.25f);
        dotMat    = Mat(new Color(1.00f, 0.92f, 0.45f), 0f, 0.5f, true, 1.3f);
        dotMatFar = Mat(new Color(0.45f, 0.85f, 1.00f), 0f, 0.5f, true, 1.2f);   // bank-segment dots go cyan
        deflMat   = Mat(new Color(0.20f, 0.95f, 0.80f), 0f, 0.65f, true, 1.7f);  // bright neon panel
        deflCore  = Mat(new Color(0.85f, 1.00f, 0.95f), 0f, 0.8f, true, 1.9f);
        shieldMat = Mat(new Color(0.24f, 0.26f, 0.34f), 0.1f, 0.15f);            // dull dark cover
        silMat    = Mat(new Color(0.22f, 0.28f, 0.40f), 0f, 0.1f);
    }

    // ===================================================================== world
    static GameObject Prim(PrimitiveType pt, Vector3 pos, Vector3 scale, Material m, bool keepCollider)
    {
        var g = GameObject.CreatePrimitive(pt);
        if (!keepCollider) { var c = g.GetComponent<Collider>(); if (c) Destroy(c); }
        g.transform.position = pos; g.transform.localScale = scale;
        g.GetComponent<Renderer>().sharedMaterial = m;
        return g;
    }

    void BuildWorld()
    {
        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(0.95f, 0.97f, 1.0f);
        sun.intensity = 1.12f;
        sun.transform.rotation = Quaternion.Euler(48f, 32f, 0f);
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.55f;

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor     = new Color(0.42f, 0.52f, 0.72f);
        RenderSettings.ambientEquatorColor = new Color(0.30f, 0.34f, 0.44f);
        RenderSettings.ambientGroundColor  = new Color(0.12f, 0.14f, 0.20f);

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.30f, 0.40f, 0.58f);
        RenderSettings.fogStartDistance = 30f;
        RenderSettings.fogEndDistance = 110f;

        // finite platform (blocks knocked past the edge fall into the void)
        var plat = Prim(PrimitiveType.Cube, new Vector3(0f, -0.5f, FZ - 0.3f), new Vector3(16f, 1f, 15f), platMat, true);
        plat.name = "Platform";
        plat.GetComponent<Collider>().material = new PhysicsMaterial { dynamicFriction = 0.7f, staticFriction = 0.8f, bounciness = 0.02f };
        for (int s = -1; s <= 1; s += 2)
            Prim(PrimitiveType.Cube, new Vector3(s * 8f, 0.03f, FZ - 0.3f), new Vector3(0.16f, 0.07f, 15f), edgeMat, false);
        Prim(PrimitiveType.Cube, new Vector3(0f, 0.03f, FZ + 7f), new Vector3(16f, 0.07f, 0.16f), edgeMat, false);
        Prim(PrimitiveType.Cube, new Vector3(0f, 0.03f, FZ - 7.6f), new Vector3(16f, 0.07f, 0.16f), edgeMat, false);

        // distant silhouette pylons for depth
        for (int i = 0; i < 7; i++)
        {
            float x = (i - 3f) * 10f;
            Prim(PrimitiveType.Cube, new Vector3(x, 4.5f, FZ + 42f + (i % 2) * 7f), new Vector3(3.4f, 9f + (i % 3) * 3f, 3.4f), silMat, false);
        }
    }

    void BuildCamera()
    {
        var cgo = new GameObject("MainCamera"); cgo.tag = "MainCamera";
        camComp = cgo.AddComponent<Camera>();
        camComp.clearFlags = CameraClearFlags.SolidColor;
        camComp.backgroundColor = new Color(0.10f, 0.15f, 0.26f);
        camComp.fieldOfView = 54f;
        camComp.farClipPlane = 240f;
        cgo.AddComponent<AudioListener>();
        cam = cgo.transform;
        cam.position = new Vector3(0f, 7.2f, -13.5f);
        cam.rotation = Quaternion.LookRotation(new Vector3(0f, 2.8f, FZ) - cam.position, Vector3.up);
    }

    void BuildSling()
    {
        for (int s = -1; s <= 1; s += 2)
            Prim(PrimitiveType.Cube, new Vector3(s * 0.7f, 1.1f, LAUNCH.z - 0.1f), new Vector3(0.22f, 2.2f, 0.22f), postMat, false);
        pouchRest = LAUNCH;
        var p = Prim(PrimitiveType.Sphere, pouchRest, Vector3.one * 0.46f, ballMat, false);
        p.name = "Pouch"; pouch = p.transform;
        Prim(PrimitiveType.Sphere, Vector3.zero, Vector3.one * 0.5f, ballCore, false).transform.SetParent(pouch, false);

        band0 = MakeBand(); band1 = MakeBand();
        UpdateBands();
    }

    Transform MakeBand() { return Prim(PrimitiveType.Cube, Vector3.zero, new Vector3(0.06f, 0.06f, 1f), bandMat, false).transform; }

    void UpdateBands()
    {
        if (band0 == null) return;
        PlaceBand(band0, new Vector3(-0.7f, 2.15f, LAUNCH.z - 0.1f), pouch.position);
        PlaceBand(band1, new Vector3( 0.7f, 2.15f, LAUNCH.z - 0.1f), pouch.position);
    }

    void PlaceBand(Transform b, Vector3 a, Vector3 c)
    {
        Vector3 mid = (a + c) * 0.5f; float len = (c - a).magnitude;
        b.position = mid;
        b.rotation = Quaternion.LookRotation((c - a).normalized, Vector3.up);
        b.localScale = new Vector3(0.06f, 0.06f, Mathf.Max(0.01f, len));
    }

    // ===================================================================== HUD
    TextMesh MakeText(float size, Color c, TextAnchor anchor)
    {
        var t = new GameObject("T").AddComponent<TextMesh>();
        t.fontSize = 96; t.characterSize = size; t.color = c; t.anchor = anchor;
        t.alignment = TextAlignment.Center;
        t.transform.SetParent(cam, false);
        t.transform.localRotation = Quaternion.identity;
        return t;
    }

    void BuildHud()
    {
        hudScore   = MakeText(0.085f, Color.white, TextAnchor.UpperLeft);
        hudBest    = MakeText(0.052f, new Color(1f, 0.92f, 0.7f), TextAnchor.UpperRight);
        hudLevel   = MakeText(0.052f, new Color(0.7f, 0.95f, 1f), TextAnchor.UpperRight);
        hudShots   = MakeText(0.075f, new Color(1f, 0.85f, 0.4f), TextAnchor.LowerLeft);
        hudTargets = MakeText(0.06f, new Color(0.4f, 1f, 0.9f), TextAnchor.LowerRight);
        hudMult    = MakeText(0.075f, new Color(0.35f, 1f, 0.85f), TextAnchor.UpperCenter);
        hudHint    = MakeText(0.052f, new Color(1f, 1f, 0.92f), TextAnchor.MiddleCenter);
        comboText  = MakeText(0.10f, new Color(0.4f, 1f, 0.85f), TextAnchor.MiddleCenter);
        bannerText = MakeText(0.12f, Color.white, TextAnchor.MiddleCenter);
        dbg        = MakeText(0.04f, new Color(0.7f, 1f, 0.7f), TextAnchor.LowerLeft);
        dbg.gameObject.SetActive(false);
        comboText.text = ""; bannerText.text = ""; hudMult.text = "";
        AdjustHud();
        hudHint.text = "DRAG BACK & RELEASE\nkiss the neon panels to BANK";
    }

    void AdjustHud()
    {
        if (camComp == null) return;
        aspect = Mathf.Max(0.3f, camComp.aspect);
        halfH = HUD_Z * Mathf.Tan(camComp.fieldOfView * 0.5f * Mathf.Deg2Rad);
        halfW = halfH * aspect;
        hudScale = Mathf.Clamp(halfW / 6.2f, 0.16f, 1.3f);
        bool portrait = aspect < 1f;
        float ix = halfW * 0.95f, iy = halfH * 0.93f;

        hudScore.transform.localPosition   = new Vector3(-ix, iy, HUD_Z);          hudScore.characterSize   = 0.082f * hudScale;
        hudBest.transform.localPosition     = new Vector3( ix, iy, HUD_Z);          hudBest.characterSize     = 0.05f  * hudScale;
        hudLevel.transform.localPosition    = new Vector3( ix, iy - 0.30f * halfH, HUD_Z); hudLevel.characterSize = 0.05f * hudScale;
        hudMult.transform.localPosition     = new Vector3(0f, iy - 0.15f * halfH, HUD_Z); hudMult.characterSize = 0.07f * hudScale;
        hudShots.transform.localPosition    = new Vector3(-ix, -iy, HUD_Z);         hudShots.characterSize    = 0.072f * hudScale;
        hudTargets.transform.localPosition  = new Vector3( ix, -iy, HUD_Z);         hudTargets.characterSize  = 0.058f * hudScale;
        hudHint.transform.localPosition     = new Vector3(0f, iy * 0.52f, HUD_Z);   hudHint.characterSize     = (portrait ? 0.046f : 0.052f) * hudScale;
        dbg.transform.localPosition         = new Vector3(-ix, -iy * 0.42f, HUD_Z); dbg.characterSize         = 0.038f * hudScale;
        comboText.transform.localPosition   = new Vector3(0f, halfH * 0.46f, HUD_Z);
        if (comboFlash <= 0f) comboText.characterSize = 0.10f * hudScale;
    }

    void RefreshHud()
    {
        if (hudScore)   hudScore.text   = "SCORE  " + score;
        if (hudBest)    hudBest.text    = "BEST  " + best;
        if (hudLevel)   hudLevel.text   = "LEVEL  " + level;
        if (hudShots)   hudShots.text   = "BALLS  " + Mathf.Max(0, shots);
        if (hudTargets) hudTargets.text = "CRYSTALS  " + targetsRemaining;
    }

    void SetHudVisible(bool v)
    {
        hudScore.gameObject.SetActive(v); hudBest.gameObject.SetActive(v); hudLevel.gameObject.SetActive(v);
        hudShots.gameObject.SetActive(v); hudTargets.gameObject.SetActive(v); hudMult.gameObject.SetActive(v);
    }

    void BuildDots()
    {
        for (int i = 0; i < DOT_COUNT; i++)
        {
            var d = Prim(PrimitiveType.Sphere, Vector3.zero, Vector3.one * 0.16f, dotMat, false);
            d.name = "dot"; d.SetActive(false);
            dots.Add(d.transform);
        }
    }

    // ===================================================================== level
    void BuildLevel()
    {
        ClearArena();
        shots = 4 + Mathf.Min(level, 3);              // 5..7 balls
        int H = 4 + Mathf.Min(level, 3);              // tower height 5..7
        int wantTargets = Mathf.Clamp(2 + level / 2, 2, 5);

        // -------- deflector panels (the bank tools) --------
        BuildDeflectors();

        // -------- fortress: iconic 2x2 stacked tower --------
        const float SZ = 0.98f; float step = 1.0f;
        float[] xs = { -0.51f, 0.51f };
        float[] zs = { FZ - 0.51f, FZ + 0.51f };

        var cells = new List<Vector3>();
        var layerOf = new List<int>();
        for (int L = 0; L < H; L++)
        {
            float y = SZ * 0.5f + L * step;
            foreach (var x in xs) foreach (var z in zs) { cells.Add(new Vector3(x, y, z)); layerOf.Add(L); }
        }
        towerTopY = SZ * 0.5f + (H - 1) * step + 0.5f;

        var targetIdx = new HashSet<int>();
        int topStart = (H - 1) * 4;
        targetIdx.Add(topStart + Random.Range(0, 4));   // one always on top
        int guard = 0;
        while (targetIdx.Count < wantTargets && guard++ < 400)
        {
            int idx = Random.Range(0, cells.Count);
            if (layerOf[idx] < H / 2) continue;           // upper half only
            targetIdx.Add(idx);
        }

        targetsRemaining = 0;
        for (int i = 0; i < cells.Count; i++)
        {
            bool isTarget = targetIdx.Contains(i);
            bool isStone = !isTarget && layerOf[i] == 0;
            MakeBlock(cells[i], isTarget, isStone);
        }

        // -------- cover: a dull shield slab in front of the low fortress (from level 2) --------
        // Blocks the flat direct line to the base cluster → lob over it, or BANK around for the mult.
        if (level >= 2)
        {
            float sy = 1.3f + Mathf.Min(level, 3) * 0.25f;
            var sh = Prim(PrimitiveType.Cube, new Vector3(0f, sy * 0.5f, FZ - 2.3f), new Vector3(3.4f, sy, 0.5f), shieldMat, true);
            sh.name = "shield";
            sh.GetComponent<Collider>().material = new PhysicsMaterial { dynamicFriction = 0.8f, staticFriction = 0.9f, bounciness = 0.02f };
            arena.Add(sh);
            // faint outline so it reads as a wall, not a target
            Prim(PrimitiveType.Cube, new Vector3(0f, sy - 0.02f, FZ - 2.3f), new Vector3(3.5f, 0.06f, 0.6f), edgeMat, false).transform.SetParent(sh.transform, true);
            // mark upper crystals as "covered" (trick-shot eligible)
            foreach (var b in blocks) if (b.isTarget && b.transform.position.y < sy + 1.2f) b.covered = true;
        }

        RefreshHud();
        if (!started) hudHint.text = "DRAG BACK & RELEASE\nkiss the neon panels to BANK";
    }

    void BuildDeflectors()
    {
        reflectors.Clear();
        // side panels facing inward (thin in X). Positioned & sized so a YAWED shot kisses ONE panel
        // and is funnelled into the central fortress (a single clean bank, not a ping-pong trap).
        float sx = 4.4f;
        AddDeflector(new Vector3(-sx, 2.4f, FZ - 0.8f), new Vector3(0.35f, 2.3f, 2.4f), 0);
        AddDeflector(new Vector3( sx, 2.4f, FZ - 0.8f), new Vector3(0.35f, 2.3f, 2.4f), 0);
        if (level >= 2)
        {
            // a low ramp panel (thin in Y) in front — a fast flat shot skips UP off it, over the shield.
            AddDeflector(new Vector3(0f, 0.5f, FZ - 4.2f), new Vector3(2.4f, 0.3f, 0.9f), 1);
        }
        if (level >= 3)
        {
            // a back wall (thin in Z) behind the fortress — arc over the tower, bank back into it.
            AddDeflector(new Vector3(0f, 3.0f, FZ + 3.4f), new Vector3(4.0f, 2.8f, 0.35f), 2);
        }
    }

    void AddDeflector(Vector3 c, Vector3 half, int axis)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);   // keeps BoxCollider (WebGL-safe)
        g.name = "deflector";
        g.transform.position = c;
        g.transform.localScale = half * 2f;
        g.GetComponent<Renderer>().sharedMaterial = deflMat;
        g.GetComponent<Collider>().material = new PhysicsMaterial {
            dynamicFriction = 0f, staticFriction = 0f, bounciness = 0.78f,
            frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Maximum };
        g.AddComponent<Deflector>();
        // glowing core stripe
        Vector3 stripe = half * 2f; stripe[axis] *= 1.06f;
        int longAxis = axis == 1 ? 0 : 1;
        for (int k = 0; k < 3; k++) stripe[k] *= 0.5f;
        stripe[axis] = half[axis] * 2f * 1.08f; stripe[longAxis] = half[longAxis] * 2f * 0.9f;
        var core = Prim(PrimitiveType.Cube, c, stripe, deflCore, false);
        core.transform.SetParent(g.transform, true);
        arena.Add(g);
        reflectors.Add(new Reflector { c = c, half = half, axis = axis });
    }

    void MakeBlock(Vector3 pos, bool isTarget, bool isStone)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = isTarget ? "target" : (isStone ? "stone" : "crate");
        float sc = isTarget ? 0.86f : 0.98f;
        g.transform.position = pos;
        g.transform.localScale = Vector3.one * sc;
        Material m = isTarget ? (Random.value < 0.5f ? targMat : targMat2) : (isStone ? stoneMat : (Random.value < 0.5f ? crateMat : crateMat2));
        g.GetComponent<Renderer>().sharedMaterial = m;

        var rb = g.AddComponent<Rigidbody>();
        rb.mass = isTarget ? 0.6f : (isStone ? 3.0f : 1.0f);
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.maxDepenetrationVelocity = 3f;
        rb.sleepThreshold = 0.05f;

        g.GetComponent<Collider>().material = new PhysicsMaterial { dynamicFriction = 0.6f, staticFriction = 0.7f, bounciness = 0.03f,
            frictionCombine = PhysicsMaterialCombine.Average, bounceCombine = PhysicsMaterialCombine.Minimum };

        var b = g.AddComponent<Block>();
        b.Init(this, isTarget, pos);
        blocks.Add(b);
        rb.Sleep();
        if (isTarget) targetsRemaining++;
    }

    void ClearArena()
    {
        foreach (var b in blocks) if (b) Destroy(b.gameObject);
        blocks.Clear();
        foreach (var g in arena) if (g) Destroy(g);
        arena.Clear();
        reflectors.Clear();
    }

    // ===================================================================== input + aiming
    void Update()
    {
        float dt = Time.deltaTime;
        if (Input.GetKeyDown(KeyCode.F1)) { showDbg = !showDbg; dbg.gameObject.SetActive(showDbg); }

        bool down = Input.GetMouseButtonDown(0);
        bool held = Input.GetMouseButton(0);
        bool up = Input.GetMouseButtonUp(0);
        Vector2 ptr = Input.mousePosition;
        for (int i = 0; i < Input.touchCount; i++)
        {
            var t = Input.GetTouch(i);
            if (t.phase == TouchPhase.Began) { down = true; ptr = t.position; }
            if (t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary) { held = true; ptr = t.position; }
            if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled) { up = true; ptr = t.position; }
        }

        bool anyKey = Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.RightArrow) ||
                      Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.DownArrow) ||
                      Input.GetKeyDown(KeyCode.Space) || Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.D);
        if (down || anyKey) { attract = false; started = true; if (hudHint) hudHint.text = ""; }

        switch (state)
        {
            case State.Idle:    HandleAimInput(down, held, up, ptr); HandleKeyAim(dt); break;
            case State.Flying:  break;
            case State.Cleared:
                clearTimer -= dt;
                if (clearTimer <= 0f) { level++; BuildLevel(); ReturnToIdle(); bannerText.text = ""; }
                break;
            case State.GameOver:
                if (down || Input.GetKeyDown(KeyCode.R) || Input.GetKeyDown(KeyCode.Space)) RestartGame();
                break;
        }

        if (attract && state == State.Idle && activeProj == null)
        {
            attractTimer -= dt;
            if (attractTimer <= 0f) { AttractShoot(); attractTimer = 2.6f; }
        }

        UpdateProjectile(dt);
        CullBlocks();
        UpdateCamera(dt);
        UpdatePouch(dt);
        TickHud(dt);
        if (combo > 0) { comboTimer -= dt; if (comboTimer <= 0f) EndCombo(); }
        if (showDbg) UpdateDbg();
    }

    void HandleAimInput(bool down, bool held, bool up, Vector2 ptr)
    {
        if (down) { aiming = true; dragStart = ptr; keyAiming = false; }
        if (aiming && (held || up))
        {
            Vector2 d = ptr - dragStart;
            float back = Mathf.Clamp(-d.y, 0f, MAXDRAG);
            float side = Mathf.Clamp(d.x, -MAXDRAG, MAXDRAG);
            float power01 = back / MAXDRAG;
            float yawDeg = (side / MAXDRAG) * MAXYAW;
            lastSpeed01 = power01; lastYaw = yawDeg;
            lastVel = LaunchVel(power01, yawDeg);

            Vector3 pull = new Vector3(side / MAXDRAG * 0.9f, -power01 * 0.9f, -power01 * 1.1f);
            pouch.position = pouchRest + pull; UpdateBands();

            if (power01 > 0.06f) ShowPreview(lastVel); else HidePreview();

            if (up)
            {
                aiming = false;
                if (power01 > 0.08f) Launch(lastVel);
                else { pouch.position = pouchRest; UpdateBands(); HidePreview(); }
            }
        }
    }

    // keyboard aiming: arrows adjust yaw/power, Space fires (desktop accessibility)
    void HandleKeyAim(float dt)
    {
        if (aiming) return;
        bool touchAim = false;
        if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A))  { aimYaw -= 45f * dt; touchAim = true; }
        if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D)) { aimYaw += 45f * dt; touchAim = true; }
        if (Input.GetKey(KeyCode.UpArrow))   { aimPow += 0.6f * dt; touchAim = true; }
        if (Input.GetKey(KeyCode.DownArrow)) { aimPow -= 0.6f * dt; touchAim = true; }
        aimYaw = Mathf.Clamp(aimYaw, -MAXYAW, MAXYAW);
        aimPow = Mathf.Clamp01(aimPow);
        if (touchAim)
        {
            keyAiming = true;
            lastSpeed01 = aimPow; lastYaw = aimYaw;
            lastVel = LaunchVel(aimPow, aimYaw);
            ShowPreview(lastVel);
            Vector3 pull = new Vector3(aimYaw / MAXYAW * 0.9f, -aimPow * 0.9f, -aimPow * 1.1f);
            pouch.position = pouchRest + pull; UpdateBands();
        }
        if (keyAiming && Input.GetKeyDown(KeyCode.Space) && aimPow > 0.08f) { Launch(LaunchVel(aimPow, aimYaw)); keyAiming = false; }
    }

    Vector3 LaunchVel(float power01, float yawDeg)
    {
        float speed = Mathf.Lerp(MINSPEED, MAXSPEED, power01);
        float pitch = Mathf.Lerp(32f, 17f, power01);
        Vector3 dir = Quaternion.Euler(-pitch, yawDeg, 0f) * Vector3.forward;
        return dir * speed;
    }

    // ---- shared flight simulation w/ deflector reflection (preview + autopilot + hit test) ----
    // Returns number of deflector kisses; fills 'path' with sampled points if provided; sets hitFortress.
    int SimFlight(Vector3 vel, List<Vector3> path, List<bool> kissed, out bool hitFortress)
    {
        hitFortress = false;
        Vector3 p = pouchRest; Vector3 v = vel; float h = 0.02f;
        int banks = 0; int sample = 0; const int EVERY = 5;
        for (int step = 0; step < 900; step++)
        {
            p += v * h; v += Physics.gravity * h;
            bool didBank = false;
            for (int r = 0; r < reflectors.Count; r++)
            {
                var rf = reflectors[r];
                Vector3 d = p - rf.c;
                if (Mathf.Abs(d.x) < rf.half.x + BALL_R && Mathf.Abs(d.y) < rf.half.y + BALL_R && Mathf.Abs(d.z) < rf.half.z + BALL_R)
                {
                    int a = rf.axis;
                    float sign = d[a] >= 0f ? 1f : -1f;
                    p[a] = rf.c[a] + sign * (rf.half[a] + BALL_R + 0.001f);
                    Vector3 vv = v; vv[a] = -vv[a] * 0.85f; v = vv;
                    banks++; didBank = true;
                }
            }
            // reached fortress?
            if (p.z > FZ - 1.6f && p.z < FZ + 1.6f && p.y > 0.2f && p.y < towerTopY + 0.5f && Mathf.Abs(p.x) < 2.0f)
            { hitFortress = true; if (path != null) { AddSample(path, kissed, p, didBank, ref sample, EVERY); } break; }
            if (p.y < GROUND_Y + BALL_R) break;
            if (p.z < LAUNCH.z - 4f || Mathf.Abs(p.x) > 12f || p.z > FZ + 12f) break;
            if (path != null) AddSample(path, kissed, p, didBank, ref sample, EVERY);
        }
        return banks;
    }

    void AddSample(List<Vector3> path, List<bool> kissed, Vector3 p, bool bankNow, ref int sample, int every)
    {
        sample++;
        if (sample % every == 0 && path.Count < DOT_COUNT) { path.Add(p); if (kissed != null) kissed.Add(bankNow); }
    }

    static readonly List<Vector3> _prevPath = new List<Vector3>();
    static readonly List<bool> _prevKiss = new List<bool>();
    void ShowPreview(Vector3 vel)
    {
        _prevPath.Clear(); _prevKiss.Clear();
        bool hf; SimFlight(vel, _prevPath, _prevKiss, out hf);
        int shown = 0; bool bankedYet = false;
        for (int i = 0; i < dots.Count; i++)
        {
            if (i < _prevPath.Count)
            {
                if (_prevKiss[i]) bankedYet = true;
                var d = dots[i]; d.gameObject.SetActive(true); d.position = _prevPath[i];
                float f = i / (float)dots.Count;
                d.localScale = Vector3.one * Mathf.Lerp(0.28f, 0.14f, f);
                d.GetComponent<Renderer>().sharedMaterial = bankedYet ? dotMatFar : dotMat;  // post-bank dots turn cyan
                shown++;
            }
            else dots[i].gameObject.SetActive(false);
        }
    }

    void HidePreview() { foreach (var d in dots) d.gameObject.SetActive(false); }

    void Launch(Vector3 vel)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        g.name = "Ball";
        g.transform.position = pouchRest;
        g.transform.localScale = Vector3.one * (BALL_R * 2f);
        g.GetComponent<Renderer>().sharedMaterial = ballMat;
        var core = Prim(PrimitiveType.Sphere, Vector3.zero, Vector3.one * 0.55f, ballCore, false);
        core.transform.SetParent(g.transform, false);
        g.GetComponent<Collider>().material = new PhysicsMaterial {
            dynamicFriction = 0.05f, staticFriction = 0.05f, bounciness = 0.55f,
            frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Maximum };

        var rb = g.AddComponent<Rigidbody>();
        rb.mass = 7f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.linearVelocity = vel;

        activeProj = g.AddComponent<CcProjectile>();
        activeProj.Init(this);

        // reset per-shot ricochet
        bankKiss = 0; ricoMult = 1; overblast = false; shotBreaks = 0; pentIdx = 0;
        UpdateMultHud();

        if (!attract) shots--;
        state = State.Flying;
        HidePreview();
        pouch.gameObject.SetActive(false);
        Juice.Blip(340f + lastSpeed01 * 200f, 0.09f, 0.45f);
        Juice.Shake(0.12f);
        RefreshHud();
    }

    void AttractShoot()
    {
        // Reliable demo: mostly clean DIRECT hits (parabola == preview == physics), sometimes a single
        // funnelled BANK to show off the ricochet. Never a multi-bank trap shot.
        float chP = 0.6f, chY = 0f; bool found = false;
        bool wantBank = Random.value < 0.4f && reflectors.Count > 0;

        if (wantBank)
        {
            for (int it = 0; it < 60; it++)
            {
                float side = Random.value < 0.5f ? -1f : 1f;
                float y = side * Random.Range(15f, 32f);
                float p = Random.Range(0.45f, 0.9f);
                bool hf; int banks = SimFlight(LaunchVel(p, y), null, null, out hf);
                if (hf && banks == 1) { chP = p; chY = y; found = true; break; }
            }
        }
        if (!found)   // direct
        {
            for (int it = 0; it < 60; it++)
            {
                float y = Random.Range(-7f, 7f);
                float p = Random.Range(0.32f, 0.9f);
                bool hf; int banks = SimFlight(LaunchVel(p, y), null, null, out hf);
                if (hf && banks == 0) { chP = p; chY = y; found = true; break; }
            }
        }
        if (!found) { chP = 0.62f; chY = Random.Range(-5f, 5f); }
        lastSpeed01 = chP; lastYaw = chY;
        Launch(LaunchVel(chP, chY));
    }

    // ===================================================================== projectile lifecycle
    void UpdateProjectile(float dt)
    {
        if (activeProj == null) return;
        if (activeProj.ShouldEnd()) { Destroy(activeProj.gameObject); activeProj = null; OnShotResolved(); }
    }

    void OnShotResolved()
    {
        if (state != State.Flying) return;
        pouch.gameObject.SetActive(true); pouch.position = pouchRest; UpdateBands();
        ricoMult = 1; if (hudMult) hudMult.text = "";
        if (targetsRemaining <= 0) { LevelClear(); return; }
        if (shots <= 0) { GameOver(); return; }
        state = State.Idle;
    }

    void ReturnToIdle() { state = State.Idle; pouch.gameObject.SetActive(true); pouch.position = pouchRest; UpdateBands(); }

    // ---- called by projectile ----
    public void OnDeflectorKiss(Vector3 pos)
    {
        bankKiss++;
        ricoMult = Mathf.Min(1 + bankKiss, 6);
        dbgBanks++;
        Juice.Pop(pos, new Color(0.3f, 1f, 0.85f), 8);
        Juice.Shake(0.1f);
        Juice.Blip(PENT[Mathf.Min(pentIdx, PENT.Length - 1)], 0.09f, 0.4f); pentIdx++;
        UpdateMultHud();
        FloatFade("BANK  ×" + ricoMult, new Color(0.35f, 1f, 0.85f));
    }

    void UpdateMultHud()
    {
        if (hudMult == null) return;
        if (state == State.Flying && ricoMult > 1) { hudMult.text = "RICOCHET  ×" + ricoMult; hudMult.color = ricoMult >= 4 ? new Color(1f, 0.8f, 0.3f) : new Color(0.35f, 1f, 0.85f); }
        else hudMult.text = "";
    }

    public void OnFirstStructureHit(Vector3 pos)
    {
        if (ricoMult >= 4 && !overblast)
        {
            overblast = true;
            // OVERBLAST — shockwave scatters the fortress, this shot's breaks score x2
            foreach (var b in blocks)
            {
                if (b == null) continue;
                var rb = b.GetComponent<Rigidbody>();
                if (rb) { rb.WakeUp(); rb.AddExplosionForce(900f, pos, 7.5f, 1.2f, ForceMode.Impulse); }
            }
            Juice.Shake(0.55f);
            Juice.Pop(pos, new Color(1f, 0.85f, 0.3f), 22);
            Juice.Pop(pos, new Color(1f, 0.6f, 0.2f), 14);
            Juice.Blip(1100f, 0.14f, 0.5f);
            bannerText.transform.localPosition = new Vector3(0f, halfH * 0.24f, HUD_Z);
            bannerText.characterSize = 0.1f * hudScale; bannerText.color = new Color(1f, 0.8f, 0.3f);
            bannerText.text = "OVERBLAST!  ×2"; bannerTimer = 1.3f;
        }
    }

    public void OnBlockHit(Block b, float impact, Vector3 pos)
    {
        if (impact > 4f) { Juice.Shake(Mathf.Min(0.5f, impact * 0.02f)); Juice.Blip(180f, 0.05f, 0.25f); }
    }

    public void OnBlockKnocked(Block b)
    {
        int gain = 15 * ricoMult * (overblast ? 2 : 1);
        score += gain; shotBreaks++; RefreshHud();
        Juice.Pop(b.transform.position, new Color(0.9f, 0.75f, 0.45f), 5);
    }

    public void PopTarget(Block b)
    {
        if (b.dead) return;
        b.dead = true;
        dbgPops++;
        targetsRemaining = Mathf.Max(0, targetsRemaining - 1);
        BumpCombo();
        int baseGain = 150 * ricoMult * Mathf.Min(combo, 8);
        if (overblast) baseGain *= 2;
        int trick = b.covered ? 1000 * Mathf.Max(1, ricoMult / 2) : 0;
        int gain = baseGain + trick;
        score += gain; shotBreaks++;
        Vector3 wp = b.transform.position;
        Juice.Score(wp);
        Juice.Pop(wp, new Color(0.3f, 1f, 0.85f), 16);
        Juice.Pop(wp, new Color(1f, 0.9f, 0.4f), 10);
        Juice.Shake(0.25f);
        Juice.Blip(700f + Mathf.Min(combo, 12) * 60f, 0.08f, 0.4f);
        if (trick > 0)
        {
            Juice.Pop(wp, new Color(1f, 0.85f, 0.3f), 18);
            FloatFade("TRICK SHOT!  +" + trick, new Color(1f, 0.85f, 0.3f));
        }
        else FloatFade((ricoMult > 1 ? "×" + ricoMult + "  " : "") + "+" + gain, new Color(0.4f, 1f, 0.85f));
        blocks.Remove(b);
        Destroy(b.gameObject);
        RefreshHud();
        if (targetsRemaining <= 0 && (state == State.Flying || state == State.Idle)) LevelClear();
    }

    void BumpCombo()
    {
        combo++; comboTimer = 2.2f; comboFlash = 1f;
        if (combo >= 2) { comboText.text = "COMBO ×" + combo; FlashCombo(); }
    }
    void EndCombo() { combo = 0; if (comboText) comboText.text = ""; }
    void FlashCombo()
    {
        comboText.color = combo >= 6 ? new Color(1f, 0.4f, 0.8f) : combo >= 3 ? new Color(1f, 0.8f, 0.3f) : new Color(0.4f, 1f, 0.85f);
    }

    void LevelClear()
    {
        state = State.Cleared;
        int bonus = 500 + Mathf.Max(0, shots) * 250;
        score += bonus;
        if (score > best) { best = score; PlayerPrefs.SetInt("caromcrash_best", best); PlayerPrefs.Save(); }
        Juice.Score(new Vector3(0, 3f, FZ));
        Juice.Shake(0.4f);
        bannerText.transform.localPosition = new Vector3(0, 0.2f * halfH, HUD_Z);
        bannerText.characterSize = 0.1f * hudScale; bannerText.color = new Color(0.5f, 1f, 0.7f);
        bannerText.text = "FORTRESS DOWN!\n+" + bonus + " BONUS";
        hudMult.text = "";
        RefreshHud();
        clearTimer = 2.0f;
    }

    void GameOver()
    {
        state = State.GameOver;
        bool nb = score >= best;
        if (score > best) { best = score; PlayerPrefs.SetInt("caromcrash_best", best); PlayerPrefs.Save(); }
        Juice.Lose();
        hudHint.gameObject.SetActive(false); comboText.text = ""; hudMult.text = "";
        SetHudVisible(false);
        bannerText.transform.localPosition = new Vector3(0, 0, HUD_Z);
        bannerText.characterSize = 0.082f * hudScale; bannerText.color = Color.white;
        bannerText.text = "OUT OF BALLS\n\nSCORE  " + score + (nb ? "\nNEW BEST!" : "\nBEST  " + best)
                        + "\nREACHED LEVEL " + level + "\n\nTAP TO PLAY AGAIN";
    }

    void RestartGame()
    {
        bannerText.text = ""; comboText.text = ""; combo = 0;
        hudHint.gameObject.SetActive(true); hudHint.text = "";
        SetHudVisible(true);
        level = 1; score = 0;
        BuildLevel(); ReturnToIdle();
    }

    void CullBlocks()
    {
        for (int i = blocks.Count - 1; i >= 0; i--)
        {
            var b = blocks[i];
            if (b == null) { blocks.RemoveAt(i); continue; }
            if (b.transform.position.y < KILL_Y)
            {
                if (b.isTarget && !b.dead) PopTarget(b);
                else { blocks.RemoveAt(i); Destroy(b.gameObject); }
            }
        }
    }

    // ===================================================================== camera / pouch / hud
    void UpdateCamera(float dt)
    {
        if (cam == null) return;
        // pull back a touch for narrow (portrait) aspects so the wide arena (side panels) always fits
        float wide = Mathf.Clamp01((1.4f - aspect) / 1.1f);
        Vector3 look = new Vector3(0f, 2.8f, FZ);
        Vector3 wantPos = new Vector3(0f, 7.2f + wide * 2.2f, -13.5f - wide * 5.5f);
        if (state == State.Flying && activeProj != null)
        {
            Vector3 bp = activeProj.transform.position;
            look = Vector3.Lerp(look, new Vector3(bp.x * 0.4f, Mathf.Max(1.6f, bp.y), Mathf.Clamp(bp.z, -2f, FZ + 4f)), 0.55f);
            wantPos.x = Mathf.Clamp(bp.x * 0.2f, -3f, 3f);
        }
        cam.position = Vector3.Lerp(cam.position, wantPos, 1f - Mathf.Exp(-4f * dt));
        Quaternion q = Quaternion.LookRotation(look - cam.position, Vector3.up);
        cam.rotation = Quaternion.Slerp(cam.rotation, q, 1f - Mathf.Exp(-6f * dt));
        AdjustHud();
    }

    void UpdatePouch(float dt)
    {
        if (pouch == null || !pouch.gameObject.activeSelf) return;
        if (!aiming && !keyAiming && state != State.Flying)
        {
            pouch.position = Vector3.Lerp(pouch.position, pouchRest, 1f - Mathf.Exp(-14f * dt));
            UpdateBands();
        }
    }

    void TickHud(float dt)
    {
        if (comboFlash > 0f)
        {
            comboFlash -= dt * 2.2f;
            if (comboText) comboText.characterSize = 0.10f * hudScale * (1f + Mathf.Max(0f, comboFlash) * 0.6f);
        }
        if (bannerTimer > 0f) { bannerTimer -= dt; if (bannerTimer <= 0f && state != State.GameOver && state != State.Cleared) bannerText.text = ""; }
        if (!started && hudHint)
        {
            float a = 0.55f + 0.45f * Mathf.Sin(Time.time * 4f);
            hudHint.color = new Color(1f, 1f, 0.92f, a);
        }
    }

    void FloatFade(string s, Color c)
    {
        bannerText.transform.localPosition = new Vector3(0, -halfH * 0.32f, HUD_Z);
        bannerText.characterSize = 0.095f * hudScale;
        bannerText.text = s; bannerText.color = c; bannerTimer = 0.9f;
    }

    void UpdateDbg()
    {
        dbg.text = string.Format(
            "state {0}  lvl {1}  balls {2}\nscore {3}  best {4}  combo {5}\ncrystals {6}  blocks {7}  refl {8}\npops {9} banks {10}  proj {11}\nrico x{12}  overblast {13}  pwr {14:0.00} yaw {15:0.0}\nattract {16}  fps {17:0}  asp {18:0.00}",
            state, level, shots, score, best, combo, targetsRemaining, blocks.Count, reflectors.Count,
            dbgPops, dbgBanks, activeProj != null ? 1 : 0, ricoMult, overblast, lastSpeed01, lastYaw,
            attract, 1f / Mathf.Max(0.0001f, Time.smoothDeltaTime), aspect);
    }
}

// ---------------------------------------------------------------------------- marker for bright bounce panels
public class Deflector : MonoBehaviour { }

// ---------------------------------------------------------------------------- a physics block
public class Block : MonoBehaviour
{
    CaromCrash game; public bool isTarget; public bool dead; public bool covered;
    Vector3 startPos; bool knocked;

    public void Init(CaromCrash g, bool target, Vector3 sp) { game = g; isTarget = target; startPos = sp; }

    void OnCollisionEnter(Collision c)
    {
        if (dead) return;
        var pj = c.gameObject.GetComponent<CcProjectile>();
        if (pj != null)
        {
            float impact = c.relativeVelocity.magnitude;
            pj.NotifyStructureHit(transform.position);
            game.OnBlockHit(this, impact, transform.position);
            if (isTarget && impact > 6.5f) { game.PopTarget(this); return; }
        }
    }

    void Update()
    {
        if (dead) return;
        float moved = (transform.position - startPos).magnitude;
        if (!knocked && moved > 1.4f) { knocked = true; if (!isTarget) game.OnBlockKnocked(this); }
        if (isTarget && moved > 2.3f) game.PopTarget(this);
    }
}

// ---------------------------------------------------------------------------- the launched ball
public class CcProjectile : MonoBehaviour
{
    CaromCrash game; Rigidbody rb; float age; int contacts; float restTime; bool hitStructure;

    public void Init(CaromCrash g) { game = g; rb = GetComponent<Rigidbody>(); }

    public void NotifyStructureHit(Vector3 pos)
    {
        if (!hitStructure) { hitStructure = true; game.OnFirstStructureHit(pos); }
    }

    void Update()
    {
        age += Time.deltaTime;
        if (rb != null && rb.linearVelocity.magnitude < 0.8f && age > 0.4f) restTime += Time.deltaTime; else restTime = 0f;
    }

    void OnCollisionEnter(Collision c)
    {
        contacts++;
        if (c.gameObject.GetComponent<Deflector>() != null) { game.OnDeflectorKiss(c.GetContact(0).point); return; }
        if (contacts <= 2)
        {
            Juice.Shake(0.16f);
            Juice.Pop(c.GetContact(0).point, new Color(1f, 0.85f, 0.4f), 8);
            Juice.Blip(220f, 0.06f, 0.3f);
        }
    }

    public bool ShouldEnd()
    {
        if (transform.position.y < -3.4f) return true;
        if (age > 6f) return true;
        if (restTime > 0.7f && contacts > 0) return true;
        if (age > 3.0f && contacts == 0) return true;
        return false;
    }
}
