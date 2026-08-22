using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.OnScreen;
using UnityEngine.InputSystem.UI;

/// <summary>
/// Telefon icin dokunmatik kontrol katmani.
///
/// NEDEN BOYLE YAZILDI:
/// Ilk surumde hem cubuklar hem dugmeler Unity'nin OnScreen bilesenleriyle
/// sanal bir oyun koluna baglanmisti. Cihazda DUGMELER calisti, CUBUKLAR
/// calismadi — yani sanal oyun kolu ve baglantilar dogruydu, sorun yalnizca
/// OnScreenStick bilesenindeydi. Bu yuzden:
///   * Dugmeler       -> OnScreenButton ile (calisiyor, dokunulmadi)
///   * Hareket/bakis  -> dokunus burada elle isleniyor ve degerler dogrudan
///                       InputManager'a yaziliyor. Aradaki katmanlar kalkti.
///
/// Yazma sirasi onemli: PlayerManager.Update icinde once HandleAllInputs,
/// sonra kamera calisiyor. [DefaultExecutionOrder(-1000)] ile bu betik daha
/// once calisiyor, boylece degerler ayni karede kullaniliyor.
/// </summary>
[DefaultExecutionOrder(-1000)]
public class MobilKontrol : MonoBehaviour
{
    static readonly string[] SADECE_OYUNDA = { "Chapter1", "Chapter2", "TestScene" };

    const string YOL_ATES     = "<Gamepad>/rightTrigger";
    const string YOL_NISAN    = "<Gamepad>/leftTrigger";
    const string YOL_COMEL    = "<Gamepad>/buttonEast";
    const string YOL_KOS      = "<Gamepad>/leftStickPress";
    const string YOL_SARJOR   = "<Gamepad>/buttonWest";
    const string YOL_SILAH    = "<Gamepad>/dpad/up";
    const string YOL_ETKILES  = "<Gamepad>/buttonNorth";
    const string YOL_SUIKAST  = "<Gamepad>/buttonSouth";
    const string YOL_YAGMA    = "<Gamepad>/dpad/down";
    const string YOL_DURAKLAT = "<Gamepad>/start";

    /// Bakis hassasiyeti: fare deltasi olcegine yaklastiriyor.
    const float BAKIS_CARPANI = 0.16f;
    /// Cubugun tam gaz sayilacagi surukleme yaricapi (ekran pikseli).
    const float CUBUK_YARICAP = 140f;
    /// Parmak yokken halkanin durdugu yer (tuval birimi, sol alt koseye gore).
    static readonly Vector2 CUBUK_DINLENME = new Vector2(300f, 300f);

    static GameObject _kok;
    static Sprite _daire;

    GameObject _katman;
    RectTransform _cubukArka, _cubukTopuz;

    // Dokunma takibi
    int _cubukParmak = -1, _bakisParmak = -1;
    Vector2 _cubukMerkez, _cubukSon, _bakisSon;
    Vector2 _hareket;          // -1..1
    Vector2 _bakisDelta;       // bu karedeki surukleme

    // Oyunun girdi yoneticisi
    Object _girdiYonetici;
    FieldInfo _alanHareket, _alanKamera;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Baslat()
    {
#if !UNITY_EDITOR
        if (!Application.isMobilePlatform) return;
#endif
        if (_kok != null) return;
        _kok = new GameObject("MobilKontrol");
        DontDestroyOnLoad(_kok);
        _kok.AddComponent<MobilKontrol>();
    }

    void Awake()
    {
        Screen.autorotateToPortrait = false;
        Screen.autorotateToPortraitUpsideDown = false;
        Screen.autorotateToLandscapeLeft = true;
        Screen.autorotateToLandscapeRight = true;
        Screen.orientation = ScreenOrientation.AutoRotation;

        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("MobilOlayDizgesi");
            es.transform.SetParent(_kok.transform, false);
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();
        }

        SceneManager.sceneLoaded += (s, m) => Yenile(s.name);
        Yenile(SceneManager.GetActiveScene().name);
    }

    void Yenile(string sahne)
    {
        bool oyunda = false;
        for (int i = 0; i < SADECE_OYUNDA.Length; i++)
            if (sahne == SADECE_OYUNDA[i]) { oyunda = true; break; }

        _girdiYonetici = null;
        _cubukParmak = _bakisParmak = -1;
        _hareket = Vector2.zero;

        if (!oyunda)
        {
            if (_katman != null) Destroy(_katman);
            _katman = null;
            return;
        }
        if (_katman == null) Kur();
    }

    // ----------------------------------------------------------------------
    // Her kare: dokunuslari oku, degerleri InputManager'a yaz
    // ----------------------------------------------------------------------
    void Update()
    {
        if (_katman == null) return;
        DokunuslariOku();
        DegerleriYaz();
    }

    void DokunuslariOku()
    {
        _bakisDelta = Vector2.zero;
        var ts = Touchscreen.current;
        if (ts == null) return;

        float yariEkran = Screen.width * 0.42f;
        var parmaklar = ts.touches;

        for (int i = 0; i < parmaklar.Count; i++)
        {
            var p = parmaklar[i];
            var faz = p.phase.ReadValue();
            int kimlik = p.touchId.ReadValue();
            Vector2 konum = p.position.ReadValue();

            if (faz == UnityEngine.InputSystem.TouchPhase.Began)
            {
                if (DugmeUstunde(konum)) continue;              // dugmeler onceliklidir
                if (konum.x < yariEkran && _cubukParmak == -1)
                {
                    _cubukParmak = kimlik; _cubukMerkez = konum; _cubukSon = konum;
                }
                else if (konum.x >= yariEkran && _bakisParmak == -1)
                {
                    _bakisParmak = kimlik; _bakisSon = konum;
                }
            }
            else if (faz == UnityEngine.InputSystem.TouchPhase.Moved ||
                     faz == UnityEngine.InputSystem.TouchPhase.Stationary)
            {
                if (kimlik == _cubukParmak) _cubukSon = konum;
                else if (kimlik == _bakisParmak)
                {
                    _bakisDelta += konum - _bakisSon;
                    _bakisSon = konum;
                }
            }
            else if (faz == UnityEngine.InputSystem.TouchPhase.Ended ||
                     faz == UnityEngine.InputSystem.TouchPhase.Canceled)
            {
                if (kimlik == _cubukParmak) { _cubukParmak = -1; _hareket = Vector2.zero; }
                else if (kimlik == _bakisParmak) _bakisParmak = -1;
            }
        }

        if (_cubukParmak != -1)
        {
            Vector2 fark = _cubukSon - _cubukMerkez;
            float u = fark.magnitude;
            _hareket = u < 12f ? Vector2.zero
                     : fark.normalized * Mathf.Clamp01(u / CUBUK_YARICAP);
        }
        CubuguCiz();
    }

    /// <summary>Degerleri oyunun girdi yoneticisine dogrudan yazar.</summary>
    void DegerleriYaz()
    {
        if (_girdiYonetici == null && !YoneticiyiBul()) return;

        // movementInput: public Vector2 — dogrudan
        _alanHareket.SetValue(_girdiYonetici, _hareket);

        // cameraInput: private Vector2 — fare deltasi gibi kare basina sapma
        _alanKamera.SetValue(_girdiYonetici, _bakisDelta * BAKIS_CARPANI);
    }

    bool YoneticiyiBul()
    {
        var tip = System.Type.GetType("InputManager, Assembly-CSharp");
        if (tip == null) return false;
        var alanOrnek = tip.GetField("instance", BindingFlags.Public | BindingFlags.Static);
        _girdiYonetici = alanOrnek != null ? alanOrnek.GetValue(null) as Object : null;
        if (_girdiYonetici == null)
        {
            _girdiYonetici = FindFirstObjectByType(tip);
            if (_girdiYonetici == null) return false;
        }
        _alanHareket = tip.GetField("movementInput", BindingFlags.Public | BindingFlags.Instance);
        _alanKamera  = tip.GetField("cameraInput", BindingFlags.NonPublic | BindingFlags.Instance);
        if (_alanHareket == null || _alanKamera == null) { _girdiYonetici = null; return false; }
        return true;
    }

    // ----------------------------------------------------------------------
    // Arayuz
    // ----------------------------------------------------------------------

    struct DugmeKutu { public Vector2 merkez; public float yaricap; }
    System.Collections.Generic.List<DugmeKutu> _kutular = new System.Collections.Generic.List<DugmeKutu>();

    bool DugmeUstunde(Vector2 ekranKonum)
    {
        for (int i = 0; i < _kutular.Count; i++)
            if ((ekranKonum - _kutular[i].merkez).sqrMagnitude <= _kutular[i].yaricap * _kutular[i].yaricap)
                return true;
        return false;
    }

    void Kur()
    {
        _kutular.Clear();
        _katman = new GameObject("MobilTuval");
        _katman.transform.SetParent(_kok.transform, false);

        var c = _katman.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 500;
        var sc = _katman.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight = 1f;
        _katman.AddComponent<GraphicRaycaster>();
        var t = _katman.transform;

        // Hareket cubugu — yalnizca gorsel; degerleri Update hesapliyor.
        _cubukArka = Gorsel(t, "CubukArka", new Vector2(0f, 0f), CUBUK_DINLENME,
                            new Vector2(300f, 300f), new Color(1f, 1f, 1f, 0.12f), false);
        _cubukTopuz = Gorsel(_cubukArka, "CubukTopuz", new Vector2(.5f, .5f), Vector2.zero,
                             new Vector2(130f, 130f), new Color(1f, 1f, 1f, 0.30f), false);

        Dugme(t, "ATEŞ",    new Vector2(1f, 0f), new Vector2(-200f, 200f), 118f, YOL_ATES,    new Color(1f, .42f, .30f));
        Dugme(t, "NİŞAN",   new Vector2(1f, 0f), new Vector2(-430f, 175f),  86f, YOL_NISAN,   new Color(.55f, .80f, 1f));
        Dugme(t, "ÇÖMEL",   new Vector2(1f, 0f), new Vector2(-195f, 425f),  86f, YOL_COMEL,   new Color(.70f, 1f, .75f));
        Dugme(t, "ŞARJÖR",  new Vector2(1f, 0f), new Vector2(-415f, 385f),  78f, YOL_SARJOR,  new Color(1f, .85f, .45f));
        Dugme(t, "SİLAH",   new Vector2(1f, 0f), new Vector2(-185f, 620f),  74f, YOL_SILAH,   new Color(.85f, .80f, .95f));
        Dugme(t, "KOŞ",     new Vector2(0f, 0f), new Vector2( 175f, 560f),  80f, YOL_KOS,     new Color(.80f, .90f, 1f));
        Dugme(t, "AL",      new Vector2(0f, 0f), new Vector2( 420f, 455f),  80f, YOL_ETKILES, new Color(.95f, .95f, .60f));
        Dugme(t, "SUİKAST", new Vector2(0f, 0f), new Vector2( 420f, 265f),  80f, YOL_SUIKAST, new Color(1f, .55f, .55f));
        Dugme(t, "YAĞMA",   new Vector2(0f, 0f), new Vector2( 590f, 360f),  70f, YOL_YAGMA,   new Color(.75f, .85f, .95f));
        Dugme(t, "II",      new Vector2(.5f, 1f), new Vector2(0f, -78f),    50f, YOL_DURAKLAT, new Color(1f, 1f, 1f));
    }

    /// <summary>
    /// Cubuk dinamik: parmagin dokundugu yer merkez oluyor. Halka da oraya
    /// tasiniyor, birakilinca sol alttaki dinlenme yerine donuyor. Yoksa
    /// gorsel ile gercek merkez birbirini tutmaz.
    /// </summary>
    void CubuguCiz()
    {
        if (_cubukTopuz == null || _cubukArka == null) return;
        float o = Mathf.Max(0.0001f, Screen.height / 1080f);

        if (_cubukParmak == -1)
        {
            _cubukArka.anchoredPosition = CUBUK_DINLENME;
            _cubukTopuz.anchoredPosition = Vector2.zero;
            return;
        }
        _cubukArka.anchoredPosition = _cubukMerkez / o;   // ekran pikseli -> tuval birimi
        _cubukTopuz.anchoredPosition =
            Vector2.ClampMagnitude(_cubukSon - _cubukMerkez, CUBUK_YARICAP) / o;
    }

    void Dugme(Transform ust, string etiket, Vector2 kose, Vector2 kaydir,
               float yaricap, string yol, Color renk)
    {
        var go = Gorsel(ust, "Dugme_" + etiket, kose, kaydir,
                        new Vector2(yaricap * 2f, yaricap * 2f),
                        new Color(renk.r, renk.g, renk.b, 0.28f), true);

        var yazi = new GameObject("Etiket");
        yazi.transform.SetParent(go, false);
        var trt = yazi.AddComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
        var tx = yazi.AddComponent<Text>();
        tx.text = etiket;
        tx.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        tx.fontSize = Mathf.Max(12, Mathf.RoundToInt(yaricap * 0.36f));
        tx.alignment = TextAnchor.MiddleCenter;
        tx.color = new Color(1f, 1f, 1f, 0.95f);
        tx.raycastTarget = false;
        tx.horizontalOverflow = HorizontalWrapMode.Overflow;

        var b = go.gameObject.AddComponent<OnScreenButton>();
        b.controlPath = yol;

        // Cubuk/bakis dokunusu bu alani yok saysin diye ekran koordinati saklaniyor.
        float o = Screen.height / 1080f;
        Vector2 ekranMerkez = new Vector2(
            (kose.x * Screen.width) + kaydir.x * o,
            (kose.y * Screen.height) + kaydir.y * o);
        _kutular.Add(new DugmeKutu { merkez = ekranMerkez, yaricap = yaricap * o * 1.15f });
    }

    RectTransform Gorsel(Transform ust, string ad, Vector2 kose, Vector2 kaydir,
                         Vector2 boyut, Color renk, bool tiklanabilir)
    {
        var go = new GameObject(ad);
        go.transform.SetParent(ust, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = kose; rt.anchorMax = kose;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = kaydir;
        rt.sizeDelta = boyut;
        var im = go.AddComponent<Image>();
        im.sprite = DaireSprite();
        im.color = renk;
        im.raycastTarget = tiklanabilir;
        return rt;
    }

    static Sprite DaireSprite()
    {
        if (_daire != null) return _daire;
        const int B = 128;
        var tex = new Texture2D(B, B, TextureFormat.RGBA32, false);
        float m = (B - 1) * 0.5f;
        for (int y = 0; y < B; y++)
            for (int x = 0; x < B; x++)
            {
                float d = Mathf.Sqrt((x - m) * (x - m) + (y - m) * (y - m)) / m;
                float dolgu = Mathf.Clamp01((0.90f - d) / 0.06f);
                float cember = Mathf.Clamp01((0.05f - Mathf.Abs(d - 0.92f)) / 0.03f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(dolgu * 0.60f + cember)));
            }
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        _daire = Sprite.Create(tex, new Rect(0, 0, B, B), new Vector2(.5f, .5f));
        return _daire;
    }
}
