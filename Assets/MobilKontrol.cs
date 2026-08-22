using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem.OnScreen;

/// <summary>
/// Telefon icin dokunmatik kontrol katmani.
///
/// Oyunun koduna HIC dokunmuyor: Unity'nin ekran-ustu kontrol bilesenleri
/// (OnScreenStick / OnScreenButton) oyunun kendi girdi eylemlerini dogrudan
/// suruyor. InputManager.cs zaten bu eylemleri dinliyor, dolayisiyla
/// masaustu oynanisi da aynen duruyor.
///
/// Sahne duzenlemesi de gerekmiyor: RuntimeInitializeOnLoadMethod sayesinde
/// oyun acilirken kendini kuruyor. Editor'e erisimimiz olmadigi icin bu
/// yontem secildi.
/// </summary>
public class MobilKontrol : MonoBehaviour
{
    // Oyunun PlayerControls.inputactions dosyasindaki gercek yollar.
    const string YOL_HAREKET = "<Gamepad>/leftStick";
    const string YOL_BAKIS   = "<Gamepad>/rightStick";
    const string YOL_ATES    = "<Gamepad>/rightTrigger";
    const string YOL_NISAN   = "<Gamepad>/leftTrigger";
    const string YOL_COMEL   = "<Gamepad>/buttonEast";
    const string YOL_KOS     = "<Gamepad>/leftStickPress";
    const string YOL_SARJOR  = "<Gamepad>/buttonWest";
    const string YOL_SILAH   = "<Gamepad>/dpad/up";
    const string YOL_ETKILES = "<Gamepad>/buttonNorth";
    const string YOL_SUIKAST = "<Gamepad>/buttonSouth";
    const string YOL_YAGMA   = "<Gamepad>/dpad/down";
    const string YOL_DURAKLAT= "<Gamepad>/start";

    static GameObject _kok;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Baslat()
    {
        if (!Application.isMobilePlatform && !Debug.isDebugBuild) return;
        if (_kok != null) return;

        _kok = new GameObject("MobilKontrol");
        Object.DontDestroyOnLoad(_kok);
        _kok.AddComponent<MobilKontrol>();
    }

    void Awake()
    {
        var tuval = Kur();
        // Ekran olcegi: telefonun cozunurlugu ne olursa olsun ayni fiziksel buyukluk.
        float o = Mathf.Clamp(Screen.width / 1920f, 0.55f, 2.2f);

        // Sol alt: hareket cubugu
        CubukYap(tuval, "Hareket", new Vector2(0f, 0f), new Vector2(300f, 300f),
                 new Vector2(190f, 190f) * o, 300f * o * 0.30f, YOL_HAREKET);

        // Sag yari: bakis alani (surukleyerek kamera cevirme)
        BakisAlaniYap(tuval, YOL_BAKIS);

        // Sag alt dugmeler
        DugmeYap(tuval, "ATES",   new Vector2(1f, 0f), new Vector2(-170f, 150f) * o, 130f * o, YOL_ATES,   new Color(1f, .42f, .30f));
        DugmeYap(tuval, "NISAN",  new Vector2(1f, 0f), new Vector2(-360f, 245f) * o,  95f * o, YOL_NISAN,  new Color(.55f, .80f, 1f));
        DugmeYap(tuval, "ÇÖMEL",  new Vector2(1f, 0f), new Vector2(-155f, 340f) * o,  95f * o, YOL_COMEL,  new Color(.70f, 1f, .75f));
        DugmeYap(tuval, "ŞARJÖR", new Vector2(1f, 0f), new Vector2(-330f, 440f) * o,  85f * o, YOL_SARJOR, new Color(1f, .85f, .45f));
        DugmeYap(tuval, "SİLAH",  new Vector2(1f, 0f), new Vector2(-150f, 540f) * o,  85f * o, YOL_SILAH,  new Color(.85f, .80f, .95f));
        DugmeYap(tuval, "AL",     new Vector2(0f, 0f), new Vector2(360f, 300f) * o,   90f * o, YOL_ETKILES,new Color(.95f, .95f, .60f));
        DugmeYap(tuval, "SUİKAST",new Vector2(0f, 0f), new Vector2(360f, 150f) * o,   90f * o, YOL_SUIKAST,new Color(1f, .55f, .55f));
        DugmeYap(tuval, "YAĞMA",  new Vector2(0f, 0f), new Vector2(520f, 220f) * o,   80f * o, YOL_YAGMA,  new Color(.75f, .85f, .95f));
        DugmeYap(tuval, "KOŞ",    new Vector2(0f, 0f), new Vector2(190f, 420f) * o,   90f * o, YOL_KOS,    new Color(.80f, .90f, 1f));
        DugmeYap(tuval, "II",     new Vector2(0.5f, 1f), new Vector2(0f, -70f) * o,   60f * o, YOL_DURAKLAT,new Color(1f, 1f, 1f));
    }

    static Canvas Kur()
    {
        var go = new GameObject("MobilTuval");
        go.transform.SetParent(_kok.transform, false);
        var c = go.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 500;                       // oyunun arayuzunun ustunde
        var sc = go.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();
        return c;
    }

    /// <summary>Sanal cubuk: arka halka + hareketli topuz.</summary>
    static void CubukYap(Canvas tuval, string ad, Vector2 kose, Vector2 kaydir,
                         Vector2 boyut, float hareketAraligi, string yol)
    {
        var arka = Gorsel(tuval.transform, ad + "Arka", kose, kaydir, boyut,
                          new Color(1f, 1f, 1f, 0.10f));
        var topuz = Gorsel(arka.transform, ad + "Topuz", new Vector2(.5f, .5f), Vector2.zero,
                           boyut * 0.42f, new Color(1f, 1f, 1f, 0.30f));

        var s = topuz.gameObject.AddComponent<OnScreenStick>();
        s.controlPath = yol;
        s.movementRange = hareketAraligi;
    }

    /// <summary>Sag yarida gorunmez bakis alani.</summary>
    static void BakisAlaniYap(Canvas tuval, string yol)
    {
        var go = new GameObject("BakisAlani");
        go.transform.SetParent(tuval.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.45f, 0f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var im = go.AddComponent<Image>();
        im.color = new Color(0f, 0f, 0f, 0.004f);   // neredeyse gorunmez ama dokunusu yakalar

        var s = go.AddComponent<OnScreenStick>();
        s.controlPath = yol;
        s.movementRange = 90f;                      // surukleme hassasiyeti
    }

    static void DugmeYap(Canvas tuval, string etiket, Vector2 kose, Vector2 kaydir,
                         float yaricap, string yol, Color renk)
    {
        var go = Gorsel(tuval.transform, "Dugme_" + etiket, kose, kaydir,
                        new Vector2(yaricap * 2f, yaricap * 2f),
                        new Color(renk.r, renk.g, renk.b, 0.20f));

        var yazi = new GameObject("Etiket");
        yazi.transform.SetParent(go.transform, false);
        var trt = yazi.AddComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
        var t = yazi.AddComponent<Text>();
        t.text = etiket;
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = Mathf.RoundToInt(yaricap * 0.42f);
        t.alignment = TextAnchor.MiddleCenter;
        t.color = new Color(1f, 1f, 1f, 0.92f);

        var b = go.gameObject.AddComponent<OnScreenButton>();
        b.controlPath = yol;
    }

    static RectTransform Gorsel(Transform ust, string ad, Vector2 kose, Vector2 kaydir,
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
        im.color = renk;
        return rt;
    }
}
