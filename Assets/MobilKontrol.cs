using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.InputSystem.OnScreen;
using UnityEngine.InputSystem.UI;

/// <summary>
/// Telefon icin dokunmatik kontrol katmani.
///
/// Oyunun C# koduna hic dokunmuyor. Unity'nin ekran-ustu kontrol bilesenleri
/// (OnScreenStick / OnScreenButton) sanal bir oyun kolu uretiyor; oyunun
/// PlayerControls.inputactions dosyasina eklenen oyun kolu baglantilari da
/// bunu dinliyor. Masaustu oynanisi (klavye/fare) aynen duruyor.
///
/// Sahne duzenlemesi gerekmiyor: RuntimeInitializeOnLoadMethod ile oyun
/// acilirken kendini kuruyor. Unity Editor'e erisim olmadigi icin bu yol secildi.
///
/// Ayni aygit duzenini paylasan tum ekran-ustu kontroller tek bir sanal oyun
/// kolu olusturuyor (Unity belgesi), bu yuzden hepsi <Gamepad>/... yollarina
/// baglandi.
/// </summary>
public class MobilKontrol : MonoBehaviour
{
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
    float _o = 1f;

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
        _o = Mathf.Clamp(Screen.width / 1920f, 0.55f, 2.2f);
        OlayDizgesiniGuvenceyeAl();

        var tuval = TuvalKur();

        CubukYap(tuval, new Vector2(0f, 0f), new Vector2(300f, 300f) * _o, 300f * _o, YOL_HAREKET);
        BakisAlaniYap(tuval, YOL_BAKIS);

        DugmeYap(tuval, "ATEŞ",    new Vector2(1f, 0f), new Vector2(-170f, 150f) * _o, 130f * _o, YOL_ATES,     new Color(1f, .42f, .30f));
        DugmeYap(tuval, "NİŞAN",   new Vector2(1f, 0f), new Vector2(-360f, 245f) * _o,  95f * _o, YOL_NISAN,    new Color(.55f, .80f, 1f));
        DugmeYap(tuval, "ÇÖMEL",   new Vector2(1f, 0f), new Vector2(-155f, 345f) * _o,  95f * _o, YOL_COMEL,    new Color(.70f, 1f, .75f));
        DugmeYap(tuval, "ŞARJÖR",  new Vector2(1f, 0f), new Vector2(-335f, 445f) * _o,  85f * _o, YOL_SARJOR,   new Color(1f, .85f, .45f));
        DugmeYap(tuval, "SİLAH",   new Vector2(1f, 0f), new Vector2(-150f, 545f) * _o,  85f * _o, YOL_SILAH,    new Color(.85f, .80f, .95f));
        DugmeYap(tuval, "AL",      new Vector2(0f, 0f), new Vector2(370f, 310f) * _o,   90f * _o, YOL_ETKILES,  new Color(.95f, .95f, .60f));
        DugmeYap(tuval, "SUİKAST", new Vector2(0f, 0f), new Vector2(370f, 150f) * _o,   90f * _o, YOL_SUIKAST,  new Color(1f, .55f, .55f));
        DugmeYap(tuval, "YAĞMA",   new Vector2(0f, 0f), new Vector2(535f, 230f) * _o,   80f * _o, YOL_YAGMA,    new Color(.75f, .85f, .95f));
        DugmeYap(tuval, "KOŞ",     new Vector2(0f, 0f), new Vector2(190f, 430f) * _o,   90f * _o, YOL_KOS,      new Color(.80f, .90f, 1f));
        DugmeYap(tuval, "II",      new Vector2(.5f, 1f), new Vector2(0f, -70f) * _o,    58f * _o, YOL_DURAKLAT, new Color(1f, 1f, 1f));
    }

    /// <summary>Dokunmatik arayuzun calismasi icin sahnede bir EventSystem sart.</summary>
    void OlayDizgesiniGuvenceyeAl()
    {
        if (Object.FindFirstObjectByType<EventSystem>() != null) return;
        var go = new GameObject("MobilOlayDizgesi");
        go.transform.SetParent(_kok.transform, false);
        go.AddComponent<EventSystem>();
        go.AddComponent<InputSystemUIInputModule>();
    }

    Canvas TuvalKur()
    {
        var go = new GameObject("MobilTuval");
        go.transform.SetParent(_kok.transform, false);
        var c = go.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 500;                       // oyunun kendi arayuzunun ustunde
        var sc = go.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();
        return c;
    }

    /// <summary>
    /// Denetim yolunu atar. OnScreenControl.controlPath public bir ozellik ve
    /// setter'i gerekli kaydi kendisi yapiyor (paket kaynagindan dogrulandi).
    /// </summary>
    static T KontrolEkle<T>(GameObject hedef, string yol) where T : OnScreenControl
    {
        var bilesen = hedef.AddComponent<T>();
        bilesen.controlPath = yol;
        return bilesen;
    }


    void CubukYap(Canvas tuval, Vector2 kose, Vector2 boyut, float aralik, string yol)
    {
        var arka = Gorsel(tuval.transform, "HareketArka", kose, boyut * 0.5f + new Vector2(40f, 40f) * _o,
                          boyut, new Color(1f, 1f, 1f, 0.10f));
        var topuz = Gorsel(arka.transform, "HareketTopuz", new Vector2(.5f, .5f), Vector2.zero,
                           boyut * 0.42f, new Color(1f, 1f, 1f, 0.32f));

        var s = KontrolEkle<OnScreenStick>(topuz.gameObject, yol);
        s.movementRange = aralik * 0.30f;
    }

    void BakisAlaniYap(Canvas tuval, string yol)
    {
        var go = new GameObject("BakisAlani");
        go.transform.SetParent(tuval.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.45f, 0f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var im = go.AddComponent<Image>();
        im.color = new Color(0f, 0f, 0f, 0.004f);   // gorunmez ama dokunusu yakalar
        im.raycastTarget = true;

        var s = KontrolEkle<OnScreenStick>(go, yol);
        s.movementRange = 110f;                     // surukleme hassasiyeti
    }

    void DugmeYap(Canvas tuval, string etiket, Vector2 kose, Vector2 kaydir,
                  float yaricap, string yol, Color renk)
    {
        var go = Gorsel(tuval.transform, "Dugme_" + etiket, kose, kaydir,
                        new Vector2(yaricap * 2f, yaricap * 2f),
                        new Color(renk.r, renk.g, renk.b, 0.22f));

        var yazi = new GameObject("Etiket");
        yazi.transform.SetParent(go.transform, false);
        var trt = yazi.AddComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
        var t = yazi.AddComponent<Text>();
        t.text = etiket;
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = Mathf.Max(10, Mathf.RoundToInt(yaricap * 0.40f));
        t.alignment = TextAnchor.MiddleCenter;
        t.color = new Color(1f, 1f, 1f, 0.92f);
        t.raycastTarget = false;

        KontrolEkle<OnScreenButton>(go.gameObject, yol);
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
        im.color = renk;
        return rt;
    }
}
