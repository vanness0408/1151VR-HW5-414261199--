using System.Collections;
using UnityEngine;

/// <summary>
/// 一支程式產生整個場景:
/// 地形(草地 + 背景起伏的山)、樹、訓練家、寶可夢、精靈球,
/// 並播放「精靈球由左至右拋出 -> 收服寶可夢 -> 搖晃 -> 成功特效」。
///
/// 模型是選填的:
///   - 在 Inspector 把下載的 FBX 拖進 Trainer Model / Pokemon Model / Pokeball Model,程式會自動縮放、貼地、擺位。
///   - 欄位留空,就使用程式產生的簡化造型。
///
/// 用法:新增空物件,掛上此腳本,按 Play。按空白鍵可重播。
/// </summary>
public class PokemonCaptureScene : MonoBehaviour
{
    [Header("模型(選填:從 Project 視窗把 FBX 拖進來;留空就用簡化造型)")]
    public GameObject trainerModel;
    public GameObject pokemonModel;
    public GameObject pokeballModel;

    [Header("模型尺寸與朝向(模型太大/太小/背對鏡頭時調整)")]
    public float trainerHeight = 2.2f;   // 訓練家高度
    public float pokemonHeight = 2.2f;   // 寶可夢高度
    public float ballSize = 0.5f;        // 精靈球直徑
    public float trainerYaw = 180f;      // 訓練家朝向(背對鏡頭就改成 0)
    public float pokemonYaw = 180f;      // 寶可夢朝向(背對鏡頭就改成 0)

    [Header("拋球設定")]
    public float throwTime = 1.2f;       // 飛行時間(秒)
    public float arcHeight = 2.5f;       // 拋物線高度
    public float spinSpeed = 720f;       // 飛行時自轉速度

    [Header("收服設定")]
    public float absorbTime = 0.8f;      // 寶可夢被吸入的時間
    public float dropTime = 0.5f;        // 球落地時間
    public int wobbleCount = 3;          // 搖晃次數
    public float wobbleAngle = 25f;      // 搖晃角度

    Terrain terrain;
    Transform trainer, pokemon, pokeball;
    Vector3 ballStartPos, pokemonStartPos, pokemonStartScale;
    Texture2D circleTex;
    bool playing;

    // =====================================================
    //  啟動
    // =====================================================
    void Start()
    {
        circleTex = MakeCircleTexture();
        SetupCameraAndLight();
        BuildTerrain();
        BuildTrees();
        BuildTrainer();
        BuildPokemon();
        BuildPokeball();

        ballStartPos = pokeball.position;
        pokemonStartPos = pokemon.position;
        pokemonStartScale = pokemon.localScale;

        StartCoroutine(Sequence());
    }

    void Update()
    {
        // 空白鍵重播(同時相容舊版 Input Manager 與新版 Input System)
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        bool pressed = UnityEngine.InputSystem.Keyboard.current != null
                       && UnityEngine.InputSystem.Keyboard.current.spaceKey.wasPressedThisFrame;
#else
        bool pressed = Input.GetKeyDown(KeyCode.Space);
#endif
        if (pressed && !playing)
            StartCoroutine(Sequence());
    }

    // =====================================================
    //  場景建立
    // =====================================================
    void SetupCameraAndLight()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            GameObject g = new GameObject("Main Camera");
            g.tag = "MainCamera";
            cam = g.AddComponent<Camera>();
            g.AddComponent<AudioListener>();
        }
        cam.transform.position = new Vector3(0, 3f, -11f);
        cam.transform.LookAt(new Vector3(0, 3.5f, 15f));

        if (FindObjectOfType<Light>() == null)
        {
            GameObject l = new GameObject("Directional Light");
            Light light = l.AddComponent<Light>();
            light.type = LightType.Directional;
            l.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }
    }

    void BuildTerrain()
    {
        int res = 129;
        TerrainData td = new TerrainData();
        td.heightmapResolution = res;
        td.size = new Vector3(100f, 20f, 100f);

        // 高度圖:中間平坦,遠處(後方)與兩側隆起成山
        float[,] h = new float[res, res];
        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                float u = x / (float)(res - 1);   // 左右
                float v = y / (float)(res - 1);   // 前後
                float n = Mathf.PerlinNoise(u * 4f + 10f, v * 4f + 10f);
                float n2 = Mathf.PerlinNoise(u * 12f, v * 12f);
                float back = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, v));
                float side = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.8f, 1f, Mathf.Abs(u - 0.5f) * 2f));
                float mount = Mathf.Max(back, side * 0.6f);
                h[y, x] = 0.02f + 0.01f * n2 + mount * (0.25f + 0.55f * n);
            }
        }
        td.SetHeights(0, 0, h);

        // 貼圖:草地 + 岩石(山比較高的地方變灰色)
        TerrainLayer grass = new TerrainLayer();
        grass.diffuseTexture = MakeNoiseTexture(new Color(0.25f, 0.62f, 0.2f), new Color(0.14f, 0.45f, 0.12f));
        grass.tileSize = new Vector2(6f, 6f);
        TerrainLayer rock = new TerrainLayer();
        rock.diffuseTexture = MakeNoiseTexture(new Color(0.55f, 0.55f, 0.55f), new Color(0.35f, 0.35f, 0.35f));
        rock.tileSize = new Vector2(10f, 10f);

        td.alphamapResolution = 128;
        td.terrainLayers = new TerrainLayer[] { grass, rock };

        int ar = td.alphamapResolution;
        float[,,] a = new float[ar, ar, 2];
        for (int y = 0; y < ar; y++)
        {
            for (int x = 0; x < ar; x++)
            {
                float hgt = td.GetInterpolatedHeight(x / (float)(ar - 1), y / (float)(ar - 1));
                float r = Mathf.InverseLerp(4f, 8f, hgt);
                a[y, x, 0] = 1f - r;
                a[y, x, 1] = r;
            }
        }
        td.SetAlphamaps(0, 0, a);

        GameObject go = Terrain.CreateTerrainGameObject(td);
        go.name = "Terrain";
        go.transform.position = new Vector3(-50f, 0f, -30f);
        terrain = go.GetComponent<Terrain>();
    }

    float GroundY(float x, float z)
    {
        return terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y;
    }

    void BuildTrees()
    {
        Transform root = new GameObject("Trees").transform;
        Random.InitState(1234);
        int placed = 0, tries = 0;
        while (placed < 45 && tries < 2000)
        {
            tries++;
            float x = Random.Range(-45f, 45f);
            float z = Random.Range(-25f, 38f);
            if (Mathf.Abs(x) < 10f && z < 8f) continue;   // 角色與鏡頭前方保持空曠
            float y = GroundY(x, z);
            if (y > 6f) continue;                          // 不種在高山上
            BuildTree(root, new Vector3(x, y, z), Random.Range(0.8f, 1.5f));
            placed++;
        }
    }

    void BuildTree(Transform parent, Vector3 pos, float s)
    {
        GameObject tree = new GameObject("Tree");
        tree.transform.SetParent(parent, false);
        tree.transform.position = pos;
        Prim(PrimitiveType.Cylinder, tree.transform, new Vector3(0, s, 0), new Vector3(0.4f * s, s, 0.4f * s), new Color(0.4f, 0.26f, 0.13f), "Trunk");
        Prim(PrimitiveType.Sphere, tree.transform, new Vector3(0, 2.8f * s, 0), Vector3.one * 2.6f * s, new Color(0.1f, 0.5f, 0.15f), "Leaves1");
        Prim(PrimitiveType.Sphere, tree.transform, new Vector3(0, 4.0f * s, 0), Vector3.one * 1.8f * s, new Color(0.15f, 0.6f, 0.2f), "Leaves2");
    }

    void BuildTrainer()
    {
        float x = -5f, z = 0f;
        GameObject t = new GameObject("Trainer");
        t.transform.position = new Vector3(x, GroundY(x, z), z);
        trainer = t.transform;

        // 有指定模型就用模型
        if (trainerModel != null)
        {
            PlaceModel(trainerModel, trainer, trainerHeight, true, trainerYaw, false);
            return;
        }

        // 否則使用簡化造型
        Color skin = new Color(1f, 0.82f, 0.65f);
        Color blue = new Color(0.2f, 0.35f, 0.8f);
        Color red = new Color(0.85f, 0.15f, 0.15f);

        Prim(PrimitiveType.Cylinder, trainer, new Vector3(-0.2f, 0.3f, 0), new Vector3(0.25f, 0.3f, 0.25f), new Color(0.2f, 0.2f, 0.3f), "LegL");
        Prim(PrimitiveType.Cylinder, trainer, new Vector3(0.2f, 0.3f, 0), new Vector3(0.25f, 0.3f, 0.25f), new Color(0.2f, 0.2f, 0.3f), "LegR");
        Prim(PrimitiveType.Capsule, trainer, new Vector3(0, 0.95f, 0), new Vector3(0.9f, 0.7f, 0.9f), blue, "Body");
        Prim(PrimitiveType.Sphere, trainer, new Vector3(0, 1.95f, 0), Vector3.one * 0.6f, skin, "Head");
        Prim(PrimitiveType.Sphere, trainer, new Vector3(0, 2.1f, 0), new Vector3(0.64f, 0.5f, 0.64f), red, "Cap");
        Prim(PrimitiveType.Cube, trainer, new Vector3(0, 2.0f, -0.3f), new Vector3(0.5f, 0.05f, 0.35f), red, "Brim");
        Prim(PrimitiveType.Sphere, trainer, new Vector3(-0.1f, 1.95f, -0.27f), Vector3.one * 0.07f, Color.black, "EyeL");
        Prim(PrimitiveType.Sphere, trainer, new Vector3(0.1f, 1.95f, -0.27f), Vector3.one * 0.07f, Color.black, "EyeR");
        Prim(PrimitiveType.Cylinder, trainer, new Vector3(-0.6f, 1.0f, 0), new Vector3(0.2f, 0.4f, 0.2f), skin, "ArmL");
        GameObject arm = Prim(PrimitiveType.Cylinder, trainer, new Vector3(0.6f, 1.35f, 0), new Vector3(0.2f, 0.45f, 0.2f), skin, "ArmR(Throw)");
        arm.transform.localRotation = Quaternion.Euler(0, 0, -50f);
    }

    void BuildPokemon()
    {
        float x = 5f, z = 0f;
        GameObject p = new GameObject("Pokemon");
        p.transform.position = new Vector3(x, GroundY(x, z), z);
        pokemon = p.transform;

        // 有指定模型就用模型
        if (pokemonModel != null)
        {
            PlaceModel(pokemonModel, pokemon, pokemonHeight, true, pokemonYaw, false);
            return;
        }

        // 否則使用簡化造型(皮卡丘風格)
        Color yellow = new Color(1f, 0.85f, 0.1f);
        Color brown = new Color(0.55f, 0.35f, 0.1f);

        Prim(PrimitiveType.Sphere, pokemon, new Vector3(0, 0.55f, 0), new Vector3(0.9f, 0.95f, 0.8f), yellow, "Body");
        Prim(PrimitiveType.Sphere, pokemon, new Vector3(0, 1.2f, 0), Vector3.one * 0.8f, yellow, "Head");
        Prim(PrimitiveType.Capsule, pokemon, new Vector3(-0.28f, 1.85f, 0), new Vector3(0.14f, 0.35f, 0.1f), yellow, "EarL");
        Prim(PrimitiveType.Capsule, pokemon, new Vector3(0.28f, 1.85f, 0), new Vector3(0.14f, 0.35f, 0.1f), yellow, "EarR");
        Prim(PrimitiveType.Sphere, pokemon, new Vector3(-0.28f, 2.15f, 0), new Vector3(0.13f, 0.13f, 0.1f), Color.black, "EarTipL");
        Prim(PrimitiveType.Sphere, pokemon, new Vector3(0.28f, 2.15f, 0), new Vector3(0.13f, 0.13f, 0.1f), Color.black, "EarTipR");
        Prim(PrimitiveType.Sphere, pokemon, new Vector3(-0.2f, 1.3f, -0.33f), Vector3.one * 0.1f, Color.black, "EyeL");
        Prim(PrimitiveType.Sphere, pokemon, new Vector3(0.2f, 1.3f, -0.33f), Vector3.one * 0.1f, Color.black, "EyeR");
        Prim(PrimitiveType.Sphere, pokemon, new Vector3(-0.35f, 1.1f, -0.28f), Vector3.one * 0.16f, Color.red, "CheekL");
        Prim(PrimitiveType.Sphere, pokemon, new Vector3(0.35f, 1.1f, -0.28f), Vector3.one * 0.16f, Color.red, "CheekR");
        GameObject tail1 = Prim(PrimitiveType.Cube, pokemon, new Vector3(0.55f, 0.5f, 0.25f), new Vector3(0.5f, 0.12f, 0.1f), yellow, "Tail1");
        tail1.transform.localRotation = Quaternion.Euler(0, 0, 35f);
        GameObject tail2 = Prim(PrimitiveType.Cube, pokemon, new Vector3(0.8f, 0.85f, 0.25f), new Vector3(0.5f, 0.12f, 0.1f), brown, "Tail2");
        tail2.transform.localRotation = Quaternion.Euler(0, 0, -20f);
    }

    void BuildPokeball()
    {
        GameObject b = new GameObject("Pokeball");
        // 放在訓練家手上(畫面左邊)
        b.transform.position = trainer.position + new Vector3(1.0f, trainerHeight * 0.8f, -0.1f);
        pokeball = b.transform;

        // 有指定模型就用模型(以球心為中心)
        if (pokeballModel != null)
        {
            PlaceModel(pokeballModel, pokeball, ballSize, false, 0f, true);
            return;
        }

        // 否則使用簡化造型:紅色上半 + 白色下半 + 黑色腰帶 + 白色按鈕
        float k = ballSize / 0.5f;
        Prim(PrimitiveType.Sphere, pokeball, Vector3.zero, Vector3.one * 0.5f * k, new Color(0.9f, 0.1f, 0.1f), "Top");
        Prim(PrimitiveType.Sphere, pokeball, new Vector3(0, -0.05f, 0) * k, Vector3.one * 0.48f * k, Color.white, "Bottom");
        Prim(PrimitiveType.Cylinder, pokeball, Vector3.zero, new Vector3(0.52f, 0.012f, 0.52f) * k, Color.black, "Band");
        Prim(PrimitiveType.Sphere, pokeball, new Vector3(0, 0, -0.24f) * k, Vector3.one * 0.12f * k, Color.white, "Button");
    }

    // =====================================================
    //  動畫流程
    // =====================================================
    IEnumerator Sequence()
    {
        playing = true;

        // 0. 重置
        pokeball.position = ballStartPos;
        pokeball.rotation = Quaternion.identity;
        pokemon.gameObject.SetActive(true);
        pokemon.position = pokemonStartPos;
        pokemon.localScale = pokemonStartScale;
        yield return new WaitForSeconds(1f);

        // 1. 精靈球由左至右拋物線飛行
        Vector3 from = ballStartPos;
        Vector3 to = pokemonStartPos + Vector3.up * (pokemonHeight * 0.5f);
        for (float t = 0; t < 1f; t += Time.deltaTime / throwTime)
        {
            Vector3 pos = Vector3.Lerp(from, to, t);
            pos.y += arcHeight * 4f * t * (1f - t);
            pokeball.position = pos;
            pokeball.Rotate(Vector3.forward, spinSpeed * Time.deltaTime, Space.Self);
            yield return null;
        }
        pokeball.position = to;

        // 2. 擊中閃光
        SpawnBurst(to, Color.red, 40, 4f, 0.25f);
        SpawnBurst(to, Color.white, 40, 3f, 0.3f);

        // 3. 寶可夢縮小並被吸進球裡
        Vector3 pStart = pokemon.position;
        Vector3 sStart = pokemon.localScale;
        for (float t = 0; t < 1f; t += Time.deltaTime / absorbTime)
        {
            float k = t * t;
            pokemon.position = Vector3.Lerp(pStart, pokeball.position, k);
            pokemon.localScale = Vector3.Lerp(sStart, Vector3.zero, k);
            yield return null;
        }
        pokemon.gameObject.SetActive(false);
        SpawnBurst(pokeball.position, Color.white, 30, 2.5f, 0.2f);

        // 4. 球落地(帶一點彈跳)
        Vector3 dropFrom = pokeball.position;
        float groundY = GroundY(dropFrom.x, dropFrom.z) + ballSize * 0.56f;
        Vector3 dropTo = new Vector3(dropFrom.x, groundY, dropFrom.z);
        for (float t = 0; t < 1f; t += Time.deltaTime / dropTime)
        {
            float eased = 1f - Mathf.Pow(1f - t, 2f);
            Vector3 p = Vector3.Lerp(dropFrom, dropTo, eased);
            p.y += Mathf.Sin(t * Mathf.PI) * 0.15f * (1f - t);
            pokeball.position = p;
            yield return null;
        }
        pokeball.position = dropTo;
        pokeball.rotation = Quaternion.identity;
        yield return new WaitForSeconds(0.4f);

        // 5. 左右搖晃
        for (int i = 0; i < wobbleCount; i++)
        {
            for (float t = 0; t < 1f; t += Time.deltaTime / 0.5f)
            {
                float angle = Mathf.Sin(t * Mathf.PI * 2f) * wobbleAngle;
                pokeball.rotation = Quaternion.Euler(0, 0, angle);
                yield return null;
            }
            pokeball.rotation = Quaternion.identity;
            yield return new WaitForSeconds(0.4f);
        }

        // 6. 收服成功特效
        Vector3 up = pokeball.position + Vector3.up * 0.3f;
        SpawnBurst(up, new Color(1f, 0.85f, 0.2f), 60, 5f, 0.3f);
        SpawnBurst(up, Color.white, 30, 3f, 0.2f);

        playing = false;
    }

    // =====================================================
    //  工具函式
    // =====================================================

    /// <summary>
    /// 把外部模型放到 root 底下,自動縮放到指定大小並擺位。
    /// useHeight=true:以高度為準;false:以最大邊長為準。
    /// centered=true:以模型中心對齊 root(精靈球);false:腳底貼齊 root(角色)。
    /// </summary>
    void PlaceModel(GameObject prefab, Transform root, float targetSize, bool useHeight, float yaw, bool centered)
    {
        GameObject m = Instantiate(prefab, root);
        m.transform.localPosition = Vector3.zero;
        m.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * prefab.transform.rotation;

        // 依實際大小縮放(保留模型原有的匯入縮放,再乘上比例)
        Bounds b = GetBounds(m);
        float size = useHeight ? b.size.y : Mathf.Max(b.size.x, b.size.y, b.size.z);
        if (size > 0.0001f)
            m.transform.localScale *= targetSize / size;

        // 擺位
        b = GetBounds(m);
        Vector3 offset;
        if (centered)
            offset = root.position - b.center;
        else
            offset = new Vector3(root.position.x - b.center.x, root.position.y - b.min.y, root.position.z - b.center.z);
        m.transform.position += offset;
    }

    Bounds GetBounds(GameObject go)
    {
        Renderer[] rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b;
    }

    GameObject Prim(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 localScale, Color color, string name)
    {
        GameObject g = GameObject.CreatePrimitive(type);
        g.name = name;
        g.transform.SetParent(parent, false);
        g.transform.localPosition = localPos;
        g.transform.localScale = localScale;
        g.GetComponent<Renderer>().material.color = color;
        Collider c = g.GetComponent<Collider>();
        if (c != null) Destroy(c);
        return g;
    }

    Texture2D MakeNoiseTexture(Color a, Color b)
    {
        Texture2D tex = new Texture2D(64, 64);
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
                tex.SetPixel(x, y, Color.Lerp(a, b, Random.value));
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.Apply();
        return tex;
    }

    Texture2D MakeCircleTexture()
    {
        int s = 32;
        Texture2D tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        Vector2 c = new Vector2(s / 2f - 0.5f, s / 2f - 0.5f);
        for (int y = 0; y < s; y++)
        {
            for (int x = 0; x < s; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c) / (s / 2f);
                float alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - d) * 2f));
                tex.SetPixel(x, y, new Color(1, 1, 1, alpha));
            }
        }
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.Apply();
        return tex;
    }

    // 以程式碼產生粒子特效(不需要 Prefab)
    void SpawnBurst(Vector3 pos, Color color, int count, float speed, float size)
    {
        GameObject go = new GameObject("CaptureFX");
        go.transform.position = pos;

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop();

        var main = ps.main;
        main.duration = 1f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = 0.8f;
        main.startSpeed = speed;
        main.startSize = size;
        main.startColor = color;
        main.gravityModifier = 0.3f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = ps.emission;
        emission.rateOverTime = 0;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.1f;

        var colorOverLife = ps.colorOverLifetime;
        colorOverLife.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        colorOverLife.color = grad;

        var sizeOverLife = ps.sizeOverLifetime;
        sizeOverLife.enabled = true;
        sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

        ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
        Shader sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Particles/Standard Unlit");
        Material m = new Material(sh);
        m.mainTexture = circleTex;
        renderer.material = m;

        ps.Play();
        Destroy(go, 2f);
    }
}
