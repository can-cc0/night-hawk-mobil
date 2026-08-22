using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.InputSystem.OnScreen;
using UnityEngine.InputSystem.UI;

/// <summary>
/// Telefon icin dokunmatik kontrol katmani.
///
/// Oyunun C# koduna hic dokunmuyor. Unity'nin ekran-ustu kontrol bilesenleri
/// sanal bir oyun kolu uretiyor; PlayerControls.inputactions dosyasina eklenen
/// oyun kolu baglantilari da bunu dinliyor. Masaustu oynanisi aynen duruyor.
///
/// ILK DENEMEDE CIKAN HATALAR (telefon ekran goruntusuyle tespit edildi):
///  1) Kontroller ANA MENUDE ve VIDEO sahnelerinde de goruluyordu. Sag yaridaki
///     gorunmez bakis paneli menudeki PLAY dugmesini, ATES dugmesi ise videodaki
///     "Skip" dugmesini yutuyordu -> oyun kilitleniyordu.
///     Cozum: kontroller yalnizca OYUN sahnelerinde kuruluyor.
///  2) Dugmeler KARE cikiyordu (Image'a sprite verilmemisti) -> daire sprite'i
///     kodla uretiliyor.
///  3) Dugmeler ekran kenarindan tasiyordu -> yerlesim kenar payiyla yeniden
///     hesaplandi ve tuval yuksekligi baz aliyor.
///  4) Oyun dikey aciliyordu -> calisma aninda yataya sabitleniyor.
/// </summary>
public class MobilKontrol : MonoBehaviour
{
    /// Yalnizca bu sahnelerde kontroller gorunur. Menu ve videolarda gorunmez.
    static readonly string[] SADECE_OYUNDA = { "Chapter1", "Chapter2", "TestScene" };

    const string YOL_HAREKET  = "<Gamepad>/leftStick";
    const string YOL_BAKIS    = "<Gamepad>/rightStick";
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

    static GameObject _kok;
    static Sprite _daire;
    GameObject _katman;

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
        YatayaZorla();
        OlayDizgesiniGuvenceyeAl();
        SceneManager.sceneLoaded += SahneYuklendi;
        Yenile(SceneManager.GetActiveScene().name);
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= SahneYuklendi;
    }

    void SahneYuklendi(Scene s, LoadSceneMode m)
    {
        Yenile(s.name);
    }

    static void YatayaZorla()
    {
        Screen.autorotateToPortrait = false;
        Screen.autorotateToPortraitUpsideDown = false;
        Screen.autorotateToLandscapeLeft = true;
        Screen.autorotateToLandscapeRight = true;
        Screen.orientation = ScreenOrientation.AutoRotation;
    }

    /// <summary>Oyun sahnesindeysek kontrolleri kurar, degilsek tamamen kaldirir.</summary>
    void Yenile(string sahneAdi)
    {
        bool oyunda = false;
        for (int i = 0; i < SADECE_OYUNDA.Length; i++)
            if (sahneAdi == SADECE_OYUNDA[i]) { oyunda = true; break; }

        if (!oyunda)
        {
            if (_katman != null) Destroy(_katman);
            _katman = null;
            return;
        }
        if (_katman == null) Kur();
    }

    void OlayDizgesiniGuvenceyeAl()
    {
        if (FindFirstObjectByType<EventSystem>() != null) return;
        var go = new GameObject("MobilOlayDizgesi");
        go.transform.SetParent(_kok.transform, false);
        go.AddComponent<EventSystem>();
        go.AddComponent<InputSystemUIInputModule>();
    }

    // ----------------------------------------------------------------------

    void Kur()
    {
        _katman = new GameObject("MobilTuval");
        _katman.transform.SetParent(_kok.transform, false);

        var c = _katman.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 500;

        var sc = _katman.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight = 1f;          // yuksekligi baz al -> yatayda tutarli
        _katman.AddComponent<GraphicRaycaster>();

        var t = _katman.transform;

        // Bakis alani EN ONCE eklenir: uGUI'de sonraki kardesler ustte kalir,
        // boylece dugmeler bakis alanindan once dokunusu alir.
        BakisAlani(t, YOL_BAKIS);

        Cubuk(t, YOL_HAREKET);

        // Sag alt kume
        Dugme(t, "ATEŞ",    new Vector2(1f, 0f), new Vector2(-200f, 200f), 118f, YOL_ATES,    new Color(1f, .42f, .30f));
        Dugme(t, "NİŞAN",   new Vector2(1f, 0f), new Vector2(-430f, 175f),  86f, YOL_NISAN,   new Color(.55f, .80f, 1f));
        Dugme(t, "ÇÖMEL",   new Vector2(1f, 0f), new Vector2(-195f, 425f),  86f, YOL_COMEL,   new Color(.70f, 1f, .75f));
        Dugme(t, "ŞARJÖR",  new Vector2(1f, 0f), new Vector2(-415f, 385f),  78f, YOL_SARJOR,  new Color(1f, .85f, .45f));
        Dugme(t, "SİLAH",   new Vector2(1f, 0f), new Vector2(-185f, 620f),  74f, YOL_SILAH,   new Color(.85f, .80f, .95f));

        // Sol kume (hareket cubugunun ustunde/yaninda)
        Dugme(t, "KOŞ",     new Vector2(0f, 0f), new Vector2( 175f, 545f),  80f, YOL_KOS,     new Color(.80f, .90f, 1f));
        Dugme(t, "AL",      new Vector2(0f, 0f), new Vector2( 415f, 440f),  80f, YOL_ETKILES, new Color(.95f, .95f, .60f));
        Dugme(t, "SUİKAST", new Vector2(0f, 0f), new Vector2( 415f, 255f),  80f, YOL_SUIKAST, new Color(1f, .55f, .55f));
        Dugme(t, "YAĞMA",   new Vector2(0f, 0f), new Vector2( 585f, 350f),  70f, YOL_YAGMA,   new Color(.75f, .85f, .95f));

        // Ust orta: duraklat
        Dugme(t, "II",      new Vector2(.5f, 1f), new Vector2(0f, -78f),    50f, YOL_DURAKLAT, new Color(1f, 1f, 1f));
    }

    void BakisAlani(Transform ust, string yol)
    {
        var go = new GameObject("BakisAlani");
        go.transform.SetParent(ust, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.42f, 0f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var im = go.AddComponent<Image>();
        im.color = new Color(0f, 0f, 0f, 0.004f);   // gorunmez ama dokunusu yakalar
        im.raycastTarget = true;

        var s = go.AddComponent<OnScreenStick>();
        s.controlPath = yol;
        s.movementRange = 120f;
    }

    void Cubuk(Transform ust, string yol)
    {
        var arka = Gorsel(ust, "HareketArka", new Vector2(0f, 0f), new Vector2(240f, 240f),
                          new Vector2(300f, 300f), new Color(1f, 1f, 1f, 0.13f));
        var topuz = Gorsel(arka.transform, "HareketTopuz", new Vector2(.5f, .5f), Vector2.zero,
                           new Vector2(126f, 126f), new Color(1f, 1f, 1f, 0.34f));
        var s = topuz.gameObject.AddComponent<OnScreenStick>();
        s.controlPath = yol;
        s.movementRange = 95f;
    }

    void Dugme(Transform ust, string etiket, Vector2 kose, Vector2 kaydir,
               float yaricap, string yol, Color renk)
    {
        var go = Gorsel(ust, "Dugme_" + etiket, kose, kaydir,
                        new Vector2(yaricap * 2f, yaricap * 2f),
                        new Color(renk.r, renk.g, renk.b, 0.28f));

        var yazi = new GameObject("Etiket");
        yazi.transform.SetParent(go.transform, false);
        var trt = yazi.AddComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;

        var t = yazi.AddComponent<Text>();
        t.text = etiket;
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = Mathf.Max(12, Mathf.RoundToInt(yaricap * 0.36f));
        t.alignment = TextAnchor.MiddleCenter;
        t.color = new Color(1f, 1f, 1f, 0.95f);
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;

        var b = go.gameObject.AddComponent<OnScreenButton>();
        b.controlPath = yol;
    }

    RectTransform Gorsel(Transform ust, string ad, Vector2 kose, Vector2 kaydir,
                         Vector2 boyut, Color renk)
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
        im.type = Image.Type.Simple;
        im.color = renk;
        return rt;
    }

    /// <summary>Yuvarlak dugme dokusu — kodla uretiliyor, dosya gerekmiyor.</summary>
    static Sprite DaireSprite()
    {
        if (_daire != null) return _daire;
        const int B = 128;
        var tex = new Texture2D(B, B, TextureFormat.RGBA32, false);
        float m = (B - 1) * 0.5f;
        for (int y = 0; y < B; y++)
        {
            for (int x = 0; x < B; x++)
            {
                float d = Mathf.Sqrt((x - m) * (x - m) + (y - m) * (y - m)) / m;
                float dolgu  = Mathf.Clamp01((0.90f - d) / 0.06f);
                float cember = Mathf.Clamp01((0.05f - Mathf.Abs(d - 0.92f)) / 0.03f);
                float a = Mathf.Clamp01(dolgu * 0.60f + cember);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        _daire = Sprite.Create(tex, new Rect(0, 0, B, B), new Vector2(.5f, .5f));
        return _daire;
    }
}
