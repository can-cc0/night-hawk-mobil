using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.InputSystem;

/// <summary>
/// Telefon icin dokunmatik kontrol katmani.
///
/// TASARIM SEBEPLERI (hepsi oyunun kendi kodundan okundu):
///
/// * Oyun WASD icin yazilmis. PlayerMovement.HandleMovement icinde
///   `moveAmount > 0.5f` sert bir kapi: altinda kalinca hicbir hiz carpani
///   uygulanmiyor, karakter animasyonsuz suruklenip duruyor. moveAmount ise
///   Clamp01(|x|+|y|). Bu yuzden cubuk analog bir oran degil, olu bolgeyi
///   gectiginde BIRIM VEKTOR uretiyor — tipki WASD gibi. Yuru/kos ayrimini
///   klavyedeki shift gibi KOS anahtari yapiyor.
///
/// * AnimatorManager.UpdateAnimationValues degerleri 0/±0.5/±1'e kirpiyor.
///   Ham dokunma degeri 0.55 sinirinda titredigi icin animasyon zipliyordu;
///   yon yumusatiliyor.
///
/// * CameraManager: `lookAngle += cameraInputX * camLookSpeed` ve camLookSpeed
///   yalnizca 0.1. Yani 90 derece donmek icin toplam 900 birim gerekiyor.
///   Eski 0.16 carpani ekran boyu suruklemede ancak ~30 derece veriyordu.
///   Simdi ekran yuksekligine gore normalize edilip carpiliyor.
///
/// * FiringController: `if (fireInput && scopeInput && ...)` — oyunda ATES
///   ancak NISAN basiliyken calisiyor. Dokunmatikte basili tutulamadigi icin
///   ates hic olmuyordu. NISAN artik anahtar (basinca acik kaliyor).
///
/// * PlayerMovement: kosma icin `sprintInput == true` gerekiyor ama
///   Sprint.canceled bayragi hemen sifirliyor. KOS da anahtar yapildi.
///
/// Bayraklar InputManager'a dogrudan yaziliyor; her bayragin oyun tarafinda
/// nasil temizlendigine gore uc davranis var (Basili / Tekil / Anahtar).
/// </summary>
[DefaultExecutionOrder(-1000)]
public class MobilKontrol : MonoBehaviour
{
    static readonly string[] SADECE_OYUNDA = { "Chapter1", "Chapter2", "TestScene" };

    /// Ekran yuksekligi kadar surukleme kac birim kamera girdisi versin.
    /// camLookSpeed 0.1 oldugu icin 1700 birim ≈ 170 derece donus demek.
    const float BAKIS_BIRIMI = 1700f;
    /// Cubugun tam gaz sayilacagi surukleme yaricapi (1080p'ye gore piksel).
    const float CUBUK_YARICAP = 150f;
    /// Bu oranin altinda parmak oynamasi hareket sayilmiyor.
    const float OLU_BOLGE = 0.16f;
    /// Parmak yokken halkanin durdugu yer (tuval birimi).
    static readonly Vector2 CUBUK_DINLENME = new Vector2(300f, 300f);

    /// <summary>Bayragin oyun tarafinda nasil temizlendigine gore davranis.</summary>
    enum Tur
    {
        Basili,    // parmak durdukca true, kalkinca false
        Tekil,     // basisa bir kez true; temizlemesi oyunun isi
        Anahtar    // basinca acilir, tekrar basinca kapanir
    }

    class Dugme
    {
        public string Ad, Alan;
        public Tur Tur;
        public Vector2 Kose, Kaydir;
        public float Yaricap;
        public Color Renk;

        public RectTransform Gorsel;
        public Image Zemin, Simge;
        public int Parmak = -1;
        public Vector2 SonKonum;       // bakis gecirmek icin
        public bool BakisGecirir;      // sag taraftaki dugmeler kamerayi da cevirir
        public bool OncekiBasili;      // kenar yakalamak icin
        public bool AnahtarAcik;       // yalnizca Tur.Anahtar

        /// Ekran konumu her karede yeniden hesaplaniyor; cihaz donunce
        /// kurulum anindaki degerler bayatliyor.
        public Vector2 EkranMerkez(float olcek)
        {
            return new Vector2(Kose.x * Screen.width + Kaydir.x * olcek,
                               Kose.y * Screen.height + Kaydir.y * olcek);
        }
    }

    static GameObject _kok;
    static Sprite _daire, _halka;
    static readonly Dictionary<string, Sprite> _simgeler = new Dictionary<string, Sprite>();

    readonly List<Dugme> _dugmeler = new List<Dugme>();
    GameObject _katman;
    RectTransform _cubukArka, _cubukTopuz;

    int _cubukParmak = -1, _bakisParmak = -1;
    Vector2 _cubukMerkez, _cubukSon, _bakisSon;
    Vector2 _yon, _yonHedef, _bakisDelta;

    InputManager _girdi;
    CameraManager _kamera;
    FieldInfo _alanKamera;

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

        _alanKamera = typeof(InputManager).GetField(
            "cameraInput", BindingFlags.NonPublic | BindingFlags.Instance);

        SceneManager.sceneLoaded += (s, m) => Yenile(s.name);
        Yenile(SceneManager.GetActiveScene().name);
    }

    void Yenile(string sahne)
    {
        bool oyunda = false;
        for (int i = 0; i < SADECE_OYUNDA.Length; i++)
            if (sahne == SADECE_OYUNDA[i]) { oyunda = true; break; }

        _girdi = null;
        _kamera = null;
        Birak();

        if (!oyunda)
        {
            if (_katman != null) Destroy(_katman);
            _katman = null;
            _dugmeler.Clear();
            return;
        }
        if (_katman == null) Kur();
    }

    void Birak()
    {
        _cubukParmak = _bakisParmak = -1;
        _yon = _yonHedef = Vector2.zero;
        for (int i = 0; i < _dugmeler.Count; i++)
        {
            _dugmeler[i].Parmak = -1;
            _dugmeler[i].OncekiBasili = false;
        }
    }

    // ======================================================================
    // Her kare
    // ======================================================================
    void Update()
    {
        if (_katman == null) return;

        if (_girdi == null)
        {
            _girdi = InputManager.instance;
            if (_girdi == null) return;
            _kamera = FindFirstObjectByType<CameraManager>();
        }

        // Duraklatma menusu acikken kontroller cekilsin, menuye dokunmayi yemesin.
        bool duruyor = _girdi.isPaused;
        if (_katman.activeSelf == duruyor) _katman.SetActive(!duruyor);
        if (duruyor) { Birak(); YazHareket(Vector2.zero, Vector2.zero); return; }

        DokunuslariOku();
        DugmeleriIsle();

        // Nisandayken oyun camLookSpeed'i 0.1'den 0.05'e dusuruyor. Telafi
        // edilmezse kamera yari hiza iniyor; 1.7 ile ~0.85 kat kaliyor.
        float telafi = (_kamera != null && _kamera.isScoped) ? 1.7f : 1f;
        YazHareket(_yon, _bakisDelta * (telafi * BAKIS_BIRIMI / Mathf.Max(1f, Screen.height)));
    }

    readonly HashSet<int> _aktif = new HashSet<int>();

    void DokunuslariOku()
    {
        _bakisDelta = Vector2.zero;
        var ts = Touchscreen.current;
        if (ts == null) { Birak(); return; }

        float bolme = Screen.width * 0.42f;
        var parmaklar = ts.touches;
        _aktif.Clear();

        for (int i = 0; i < parmaklar.Count; i++)
        {
            var p = parmaklar[i];
            var faz = p.phase.ReadValue();
            bool canli = faz == UnityEngine.InputSystem.TouchPhase.Began ||
                         faz == UnityEngine.InputSystem.TouchPhase.Moved ||
                         faz == UnityEngine.InputSystem.TouchPhase.Stationary;
            if (!canli) continue;

            int kimlik = p.touchId.ReadValue();
            Vector2 konum = p.position.ReadValue();
            _aktif.Add(kimlik);

            if (faz == UnityEngine.InputSystem.TouchPhase.Began)
            {
                Dugme d = DugmeBul(konum);
                if (d != null) { d.Parmak = kimlik; d.SonKonum = konum; continue; }

                if (konum.x < bolme && _cubukParmak == -1)
                {
                    _cubukParmak = kimlik; _cubukMerkez = konum; _cubukSon = konum;
                }
                else if (konum.x >= bolme && _bakisParmak == -1)
                {
                    _bakisParmak = kimlik; _bakisSon = konum;
                }
                continue;
            }

            if (kimlik == _cubukParmak) { _cubukSon = konum; continue; }
            if (kimlik == _bakisParmak)
            {
                _bakisDelta += konum - _bakisSon;
                _bakisSon = konum;
                continue;
            }

            // Sag taraftaki dugmeyi tutan parmak ayni anda kamerayi da cevirsin.
            // Aksi halde ATES basiliyken bakacak parmak kalmiyor.
            for (int k = 0; k < _dugmeler.Count; k++)
            {
                var d = _dugmeler[k];
                if (d.Parmak != kimlik) continue;
                if (d.BakisGecirir) _bakisDelta += konum - d.SonKonum;
                d.SonKonum = konum;
                break;
            }
        }

        // TAKILMAYI ONLEYEN SIFIRLAMA: ekranda olmayan bir parmagin tuttugu her
        // sey birakiliyor. Unity touchId'leri geri donusturdugu icin Ended olayi
        // kacabiliyordu ve buton sonsuza kadar basili kaliyordu.
        if (_cubukParmak != -1 && !_aktif.Contains(_cubukParmak))
        { _cubukParmak = -1; _yonHedef = Vector2.zero; }
        if (_bakisParmak != -1 && !_aktif.Contains(_bakisParmak)) _bakisParmak = -1;
        for (int k = 0; k < _dugmeler.Count; k++)
            if (_dugmeler[k].Parmak != -1 && !_aktif.Contains(_dugmeler[k].Parmak))
                _dugmeler[k].Parmak = -1;

        // Cubuk: olu bolgeyi gectiyse BIRIM vektor (WASD gibi), yoksa sifir.
        if (_cubukParmak != -1)
        {
            Vector2 fark = _cubukSon - _cubukMerkez;
            float o = Mathf.Max(0.0001f, Screen.height / 1080f);
            _yonHedef = fark.magnitude < CUBUK_YARICAP * o * OLU_BOLGE
                      ? Vector2.zero : fark.normalized;
        }

        // Yon yumusatiliyor: animasyon degerleri 0.55'te kirpildigi icin ham
        // deger titreyince yuru/kos animasyonu zipliyor.
        _yon = _yonHedef == Vector2.zero
             ? Vector2.zero
             : Vector2.Lerp(_yon, _yonHedef, 1f - Mathf.Exp(-18f * Time.deltaTime)).normalized;

        CubuguCiz();
    }

    void YazHareket(Vector2 hareket, Vector2 bakis)
    {
        if (_girdi == null) return;
        _girdi.movementInput = hareket;
        if (_alanKamera != null) _alanKamera.SetValue(_girdi, bakis);
    }

    void DugmeleriIsle()
    {
        for (int i = 0; i < _dugmeler.Count; i++)
        {
            var d = _dugmeler[i];
            bool basili = d.Parmak != -1;
            bool indi = basili && !d.OncekiBasili;     // yalnizca bu karede basildi

            switch (d.Tur)
            {
                case Tur.Basili:                       // parmak durdukca acik
                    Yaz(d.Alan, basili);
                    break;

                case Tur.Tekil:                        // bir kez; temizlemesi oyunun isi
                    if (indi) Yaz(d.Alan, true);
                    break;

                case Tur.Anahtar:                      // bas-ac, tekrar bas-kapat
                    if (indi) d.AnahtarAcik = !d.AnahtarAcik;
                    Yaz(d.Alan, d.AnahtarAcik);
                    break;
            }

            d.OncekiBasili = basili;
            Boya(d, basili);
        }
    }

    void Yaz(string alan, bool deger)
    {
        switch (alan)
        {
            case "fireInput":         _girdi.fireInput = deger; break;
            case "scopeInput":        _girdi.scopeInput = deger; break;
            case "sprintInput":       _girdi.sprintInput = deger; break;
            case "reloadInput":       _girdi.reloadInput = deger; break;
            case "assassinateInput":  _girdi.assassinateInput = deger; break;
            case "switchWeaponInput": _girdi.switchWeaponInput = deger; break;
            case "crouchInput":       _girdi.crouchInput = deger; break;
            case "interactInput":     _girdi.interactInput = deger; break;
            case "lootInput":         _girdi.lootInput = deger; break;
            case "pauseGameInput":    _girdi.pauseGameInput = deger; break;
        }
    }

    Dugme DugmeBul(Vector2 ekranKonum)
    {
        float o = Mathf.Max(0.0001f, Screen.height / 1080f);
        Dugme en = null; float enYakin = float.MaxValue;
        for (int i = 0; i < _dugmeler.Count; i++)
        {
            var d = _dugmeler[i];
            if (d.Parmak != -1) continue;
            float r = d.Yaricap * o * 1.12f;
            float u = (ekranKonum - d.EkranMerkez(o)).sqrMagnitude;
            if (u <= r * r && u < enYakin) { enYakin = u; en = d; }
        }
        return en;
    }

    void Boya(Dugme d, bool basili)
    {
        bool vurgu = basili || (d.Tur == Tur.Anahtar && d.AnahtarAcik);
        float a = vurgu ? 0.72f : 0.24f;
        d.Zemin.color = new Color(d.Renk.r, d.Renk.g, d.Renk.b, a);
        d.Simge.color = new Color(1f, 1f, 1f, vurgu ? 1f : 0.88f);
    }

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
        _cubukArka.anchoredPosition = _cubukMerkez / o;
        _cubukTopuz.anchoredPosition =
            Vector2.ClampMagnitude(_cubukSon - _cubukMerkez, CUBUK_YARICAP * o) / o;
    }

    // ======================================================================
    // Arayuz kurulumu
    // ======================================================================
    void Kur()
    {
        _dugmeler.Clear();
        _katman = new GameObject("MobilTuval");
        _katman.transform.SetParent(_kok.transform, false);

        var c = _katman.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 500;
        var sc = _katman.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight = 1f;
        var t = _katman.transform;

        _cubukArka  = Gorsel(t, "CubukArka", new Vector2(0f, 0f), CUBUK_DINLENME,
                             300f, Halka(), new Color(1f, 1f, 1f, 0.16f));
        _cubukTopuz = Gorsel(_cubukArka, "CubukTopuz", new Vector2(.5f, .5f), Vector2.zero,
                             132f, Dolu(), new Color(1f, 1f, 1f, 0.34f));

        var sol = new Vector2(0f, 0f);
        var sag = new Vector2(1f, 0f);
        var ust = new Vector2(.5f, 1f);

        Ekle("ates",    "fireInput",         Tur.Basili,  sag, new Vector2(-205f, 205f), 118f, new Color(1f, .40f, .30f));
        Ekle("nisan",   "scopeInput",        Tur.Anahtar, sag, new Vector2(-440f, 155f),  84f, new Color(.50f, .78f, 1f));
        Ekle("comel",   "crouchInput",       Tur.Tekil,   sag, new Vector2(-398f, 382f),  76f, new Color(.72f, 1f, .78f));
        Ekle("sarjor",  "reloadInput",       Tur.Basili,  sag, new Vector2(-612f, 252f),  70f, new Color(1f, .84f, .42f));
        Ekle("silah",   "switchWeaponInput", Tur.Tekil,   sag, new Vector2(-580f, 490f),  66f, new Color(.84f, .80f, .98f));

        Ekle("kos",     "sprintInput",       Tur.Anahtar, sol, new Vector2( 200f, 622f),  78f, new Color(.78f, .90f, 1f));
        Ekle("suikast", "assassinateInput",  Tur.Basili,  sol, new Vector2( 482f, 560f),  74f, new Color(1f, .52f, .52f));
        Ekle("al",      "interactInput",     Tur.Tekil,   sol, new Vector2( 622f, 360f),  70f, new Color(.95f, .95f, .58f));
        Ekle("yagma",   "lootInput",         Tur.Tekil,   sol, new Vector2( 700f, 562f),  64f, new Color(.74f, .86f, .96f));

        Ekle("duraklat","pauseGameInput",    Tur.Tekil,   ust, new Vector2(   0f, -78f),  48f, new Color(1f, 1f, 1f));
    }

    void Ekle(string ad, string alan, Tur tur, Vector2 kose, Vector2 kaydir,
              float yaricap, Color renk)
    {
        var d = new Dugme
        {
            Ad = ad, Alan = alan, Tur = tur, Kose = kose,
            Kaydir = kaydir, Yaricap = yaricap, Renk = renk,
            BakisGecirir = kose.x > 0.9f
        };

        d.Gorsel = Gorsel(_katman.transform, "Dugme_" + ad, kose, kaydir,
                          yaricap * 2f, Dolu(), new Color(renk.r, renk.g, renk.b, 0.24f));
        d.Zemin = d.Gorsel.GetComponent<Image>();

        var cerceve = Gorsel(d.Gorsel, "Cerceve", new Vector2(.5f, .5f), Vector2.zero,
                             yaricap * 2f, Halka(), new Color(renk.r, renk.g, renk.b, 0.85f));
        cerceve.SetAsFirstSibling();

        var s = Gorsel(d.Gorsel, "Simge", new Vector2(.5f, .5f), Vector2.zero,
                       yaricap * 1.15f, Simge(ad), new Color(1f, 1f, 1f, 0.88f));
        d.Simge = s.GetComponent<Image>();

        _dugmeler.Add(d);
    }

    RectTransform Gorsel(Transform ust, string ad, Vector2 kose, Vector2 kaydir,
                         float boyut, Sprite resim, Color renk)
    {
        var go = new GameObject(ad);
        go.transform.SetParent(ust, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = kose; rt.anchorMax = kose;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = kaydir;
        rt.sizeDelta = new Vector2(boyut, boyut);
        var im = go.AddComponent<Image>();
        im.sprite = resim;
        im.color = renk;
        im.raycastTarget = false;       // dokunuslari kendimiz esliyoruz
        return rt;
    }

    // ======================================================================
    // Cizimler — hicbir dis dosya yok, hepsi kodda uretiliyor
    // ======================================================================
    // Not: `??` Unity'nin == asiri yuklemesini atladigi icin acik == null kullaniliyor.
    static Sprite Dolu()  { if (_daire == null) _daire = Cember(true);  return _daire; }
    static Sprite Halka() { if (_halka == null) _halka = Cember(false); return _halka; }

    static Sprite Cember(bool dolu)
    {
        const int B = 128;
        var tex = new Texture2D(B, B, TextureFormat.RGBA32, false);
        var p = new Color[B * B];
        float m = (B - 1) * 0.5f;
        for (int y = 0; y < B; y++)
            for (int x = 0; x < B; x++)
            {
                float d = Mathf.Sqrt((x - m) * (x - m) + (y - m) * (y - m)) / m;
                float a = dolu
                    ? Mathf.Clamp01((0.94f - d) / 0.05f)
                    : Mathf.Clamp01((0.045f - Mathf.Abs(d - 0.93f)) / 0.022f);
                p[y * B + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(p); tex.Apply(); tex.wrapMode = TextureWrapMode.Clamp;
        return Sprite.Create(tex, new Rect(0, 0, B, B), new Vector2(.5f, .5f));
    }

    static Sprite Simge(string ad)
    {
        if (_simgeler.TryGetValue(ad, out var hazir) && hazir != null) return hazir;

        const int B = 96;
        var p = new Color[B * B];
        for (int i = 0; i < p.Length; i++) p[i] = new Color(1f, 1f, 1f, 0f);
        const float K = 0.085f;   // cizgi kalinligi

        switch (ad)
        {
            case "ates":                                     // artı nişangah
                Yay(p, B, .5f, .5f, .30f, K * .8f);
                Cizgi(p, B, .5f, .82f, .5f, .66f, K);
                Cizgi(p, B, .5f, .34f, .5f, .18f, K);
                Cizgi(p, B, .82f, .5f, .66f, .5f, K);
                Cizgi(p, B, .34f, .5f, .18f, .5f, K);
                Nokta(p, B, .5f, .5f, .075f);
                break;

            case "nisan":                                    // dürbün
                Yay(p, B, .5f, .5f, .34f, K * .9f);
                Cizgi(p, B, .5f, .16f, .5f, .84f, K * .7f);
                Cizgi(p, B, .16f, .5f, .84f, .5f, K * .7f);
                Nokta(p, B, .5f, .5f, .065f);
                break;



            case "comel":                                    // aşağı ok + zemin
                Cizgi(p, B, .5f, .82f, .5f, .34f, K);
                Cizgi(p, B, .28f, .54f, .5f, .32f, K);
                Cizgi(p, B, .72f, .54f, .5f, .32f, K);
                Cizgi(p, B, .22f, .18f, .78f, .18f, K);
                break;

            case "kos":                                      // çift ileri ok
                Cizgi(p, B, .26f, .24f, .50f, .50f, K);
                Cizgi(p, B, .26f, .76f, .50f, .50f, K);
                Cizgi(p, B, .52f, .24f, .76f, .50f, K);
                Cizgi(p, B, .52f, .76f, .76f, .50f, K);
                break;

            case "sarjor":                                   // dönen ok
                Yay(p, B, .5f, .5f, .30f, K, 40f, 330f);
                Cizgi(p, B, .73f, .68f, .78f, .48f, K * .85f);
                Cizgi(p, B, .73f, .68f, .92f, .62f, K * .85f);
                break;

            case "silah":                                    // değiştir
                Cizgi(p, B, .20f, .62f, .80f, .62f, K);
                Cizgi(p, B, .66f, .74f, .80f, .62f, K);
                Cizgi(p, B, .66f, .50f, .80f, .62f, K);
                Cizgi(p, B, .80f, .36f, .20f, .36f, K);
                Cizgi(p, B, .34f, .48f, .20f, .36f, K);
                Cizgi(p, B, .34f, .24f, .20f, .36f, K);
                break;

            case "al":                                       // tepsiye inen ok
                Cizgi(p, B, .5f, .86f, .5f, .44f, K);
                Cizgi(p, B, .32f, .60f, .5f, .42f, K);
                Cizgi(p, B, .68f, .60f, .5f, .42f, K);
                Cizgi(p, B, .18f, .36f, .18f, .18f, K);
                Cizgi(p, B, .18f, .18f, .82f, .18f, K);
                Cizgi(p, B, .82f, .18f, .82f, .36f, K);
                break;

            case "suikast":                                  // hançer
                Cizgi(p, B, .22f, .80f, .60f, .42f, K * 1.15f);
                Cizgi(p, B, .22f, .80f, .34f, .84f, K * .8f);
                Cizgi(p, B, .52f, .30f, .74f, .52f, K);
                Cizgi(p, B, .62f, .40f, .84f, .18f, K);
                break;

            case "yagma":                                    // sandık
                Cizgi(p, B, .16f, .28f, .84f, .28f, K);
                Cizgi(p, B, .16f, .28f, .16f, .70f, K);
                Cizgi(p, B, .84f, .28f, .84f, .70f, K);
                Cizgi(p, B, .16f, .70f, .84f, .70f, K);
                Cizgi(p, B, .16f, .56f, .84f, .56f, K * .8f);
                Cizgi(p, B, .5f, .56f, .5f, .28f, K * .8f);
                break;

            default:                                         // duraklat
                Kutu(p, B, .34f, .22f, .44f, .78f);
                Kutu(p, B, .56f, .22f, .66f, .78f);
                break;
        }

        var tex = new Texture2D(B, B, TextureFormat.RGBA32, false);
        tex.SetPixels(p); tex.Apply(); tex.wrapMode = TextureWrapMode.Clamp;
        var sp = Sprite.Create(tex, new Rect(0, 0, B, B), new Vector2(.5f, .5f));
        _simgeler[ad] = sp;
        return sp;
    }

    static void Koy(Color[] p, int B, int x, int y, float a)
    {
        if (x < 0 || y < 0 || x >= B || y >= B || a <= 0f) return;
        int i = y * B + x;
        if (a > p[i].a) p[i] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
    }

    static void Cizgi(Color[] p, int B, float x0, float y0, float x1, float y1, float k)
    {
        Vector2 a = new Vector2(x0, y0) * B, b = new Vector2(x1, y1) * B;
        float yari = k * B * 0.5f;
        int eb = Mathf.FloorToInt(yari) + 2;
        int xa = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x)) - eb);
        int xz = Mathf.Min(B - 1, Mathf.CeilToInt(Mathf.Max(a.x, b.x)) + eb);
        int ya = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y)) - eb);
        int yz = Mathf.Min(B - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y)) + eb);
        Vector2 ab = b - a;
        float uz = Mathf.Max(1e-5f, ab.sqrMagnitude);

        for (int y = ya; y <= yz; y++)
            for (int x = xa; x <= xz; x++)
            {
                Vector2 q = new Vector2(x + .5f, y + .5f);
                float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / uz);
                float d = Vector2.Distance(q, a + ab * t);
                Koy(p, B, x, y, Mathf.Clamp01(yari - d + 0.5f));
            }
    }

    static void Yay(Color[] p, int B, float cx, float cy, float r, float k,
                    float bas = 0f, float bit = 360f)
    {
        float yari = k * B * 0.5f, R = r * B;
        Vector2 m = new Vector2(cx, cy) * B;
        int xa = Mathf.Max(0, Mathf.FloorToInt(m.x - R - yari - 2));
        int xz = Mathf.Min(B - 1, Mathf.CeilToInt(m.x + R + yari + 2));
        int ya = Mathf.Max(0, Mathf.FloorToInt(m.y - R - yari - 2));
        int yz = Mathf.Min(B - 1, Mathf.CeilToInt(m.y + R + yari + 2));

        for (int y = ya; y <= yz; y++)
            for (int x = xa; x <= xz; x++)
            {
                Vector2 v = new Vector2(x + .5f, y + .5f) - m;
                float aci = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
                if (aci < 0f) aci += 360f;
                if (aci < bas || aci > bit) continue;
                Koy(p, B, x, y, Mathf.Clamp01(yari - Mathf.Abs(v.magnitude - R) + 0.5f));
            }
    }

    static void Nokta(Color[] p, int B, float cx, float cy, float r)
    {
        Vector2 m = new Vector2(cx, cy) * B; float R = r * B;
        for (int y = 0; y < B; y++)
            for (int x = 0; x < B; x++)
                Koy(p, B, x, y,
                    Mathf.Clamp01(R - (new Vector2(x + .5f, y + .5f) - m).magnitude + 0.5f));
    }

    static void Kutu(Color[] p, int B, float x0, float y0, float x1, float y1)
    {
        for (int y = Mathf.FloorToInt(y0 * B); y < Mathf.CeilToInt(y1 * B); y++)
            for (int x = Mathf.FloorToInt(x0 * B); x < Mathf.CeilToInt(x1 * B); x++)
                Koy(p, B, x, y, 1f);
    }
}
