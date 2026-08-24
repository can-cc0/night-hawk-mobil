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
///   uygulanmiyor. moveAmount = Clamp01(|x|+|y|). Bu yuzden cubuk analog oran
///   degil, olu bolgeyi gectiginde BIRIM VEKTOR uretiyor — tipki WASD gibi.
///   Yuru/kos ayrimini klavyedeki shift gibi KOS anahtari yapiyor.
///
/// * AnimatorManager.UpdateAnimationValues degerleri 0/±0.5/±1'e kirpiyor;
///   ham dokunma degeri sinirda titreyince animasyon zipliyordu, yon yumusatildi.
///
/// * CameraManager: `lookAngle += cameraInputX * camLookSpeed`, camLookSpeed 0.1.
///   90 derece donmek icin toplam 900 birim gerekiyor; surukleme ekran
///   yuksekligine gore normalize edilip carpiliyor. Nisandayken oyun bu degeri
///   0.05'e dusurdugu icin ayrica telafi ediliyor.
///
/// * FiringController: `if (fireInput && scopeInput && ...)` — oyunda ATES
///   ancak NISAN aciyken calisiyor. Bu yuzden ATES dugmesi basili tutuldugunda
///   nisani da kendisi aciyor; tek dokunusla nisan alip ates ediliyor. NISAN
///   dugmesi ise surekli nisanda kalmak icin ayri bir anahtar olarak duruyor.
///
/// * PlayerMovement: kosma icin sprintInput gerekiyor ama Sprint.canceled
///   bayragi hemen sifirliyor; KOS anahtar yapildi.
///
/// DUZENLEME KIPI: sag ustteki ayar dugmesi butonlari surukleyerek yeniden
/// yerlestirmeyi aciyor. Konumlar PlayerPrefs'e yaziliyor, oyun yeniden
/// acildiginda geri yukleniyor.
/// </summary>
[DefaultExecutionOrder(-1000)]
public class MobilKontrol : MonoBehaviour
{
    static readonly string[] SADECE_OYUNDA = { "Chapter1", "Chapter2", "TestScene" };

    /// Ekran yuksekligi kadar surukleme kac birim kamera girdisi versin.
    /// camLookSpeed 0.1 oldugu icin 1700 birim ≈ 170 derece donus demek.
    const float BAKIS_BIRIMI = 1700f;
    /// Nisandayken oyun camLookSpeed'i yariya dusuruyor; telafi carpani.
    const float NISAN_TELAFI = 1.7f;
    /// Cubugun tam gaz sayilacagi surukleme yaricapi (1080p'ye gore piksel).
    const float CUBUK_YARICAP = 150f;
    /// Bu oranin altinda parmak oynamasi hareket sayilmiyor.
    const float OLU_BOLGE = 0.16f;
    /// Parmak yokken halkanin durdugu yer (tuval birimi).
    static readonly Vector2 CUBUK_DINLENME = new Vector2(300f, 300f);
    /// Kayitli konumlarin PlayerPrefs onadi.
    const string KAYIT = "mobilkontrol_";

    /// <summary>Bayragin oyun tarafinda nasil temizlendigine gore davranis.</summary>
    enum Tur
    {
        Basili,    // parmak durdukca true, kalkinca false
        Tekil,     // basisa bir kez true; temizlemesi oyunun isi
        Anahtar,   // basinca acilir, tekrar basinca kapanir
        Arac       // oyun bayragi degil: duzenleme kipi araclari
    }

    class Dugme
    {
        public string Ad, Alan;
        public Tur Tur;
        public Vector2 Kose, Varsayilan, Kaydir;
        public float Yaricap;
        public Color Renk;
        public System.Action Islev;
        public bool Tasinabilir = true;

        public RectTransform Gorsel;
        public Image Zemin, Cerceve, Simge;
        public int Parmak = -1;
        public Vector2 SonKonum;
        public bool BakisGecirir;
        public bool OncekiBasili;
        public bool AnahtarAcik;

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
    readonly HashSet<int> _aktif = new HashSet<int>();
    GameObject _katman;
    RectTransform _cubukArka, _cubukTopuz;
    Image _perde;
    Dugme _ates, _nisan, _kaydet, _sifirla;

    int _cubukParmak = -1, _bakisParmak = -1;
    Vector2 _cubukMerkez, _cubukSon, _bakisSon;
    Vector2 _yon, _yonHedef, _bakisDelta;
    bool _duzenleme;

    InputManager _girdi;
    CameraManager _kamera;
    FieldInfo _alanKamera;

    static float Olcek() { return Mathf.Max(0.0001f, Screen.height / 1080f); }

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
        _duzenleme = false;
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
        if (duruyor) { Birak(); Sifirla(); return; }

        if (_duzenleme)
        {
            DuzenlemeDokunus();
            Sifirla();                 // duzenlerken oyun girdisi donuk
            return;
        }

        DokunuslariOku();
        DugmeleriIsle();

        float telafi = (_kamera != null && _kamera.isScoped) ? NISAN_TELAFI : 1f;
        YazHareket(_yon, _bakisDelta * (telafi * BAKIS_BIRIMI / Mathf.Max(1f, Screen.height)));
    }

    /// <summary>Tum oyun girdilerini bosa cekiyor.</summary>
    void Sifirla()
    {
        if (_girdi == null) return;
        _girdi.movementInput = Vector2.zero;
        if (_alanKamera != null) _alanKamera.SetValue(_girdi, Vector2.zero);
        _girdi.fireInput = false;
        _girdi.scopeInput = false;
        _girdi.sprintInput = false;
        _girdi.reloadInput = false;
        _girdi.assassinateInput = false;
    }

    void YazHareket(Vector2 hareket, Vector2 bakis)
    {
        if (_girdi == null) return;
        _girdi.movementInput = hareket;
        if (_alanKamera != null) _alanKamera.SetValue(_girdi, bakis);
    }

    // ======================================================================
    // Oynanis dokunuslari
    // ======================================================================
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
            if (!Canli(faz)) continue;

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

            // Sagdaki dugmeyi tutan parmak ayni anda kamerayi da cevirsin;
            // aksi halde ATES basiliyken bakacak parmak kalmiyor.
            for (int k = 0; k < _dugmeler.Count; k++)
            {
                var d = _dugmeler[k];
                if (d.Parmak != kimlik) continue;
                if (d.BakisGecirir) _bakisDelta += konum - d.SonKonum;
                d.SonKonum = konum;
                break;
            }
        }

        BayatParmaklariBirak();

        if (_cubukParmak != -1)
        {
            Vector2 fark = _cubukSon - _cubukMerkez;
            _yonHedef = fark.magnitude < CUBUK_YARICAP * Olcek() * OLU_BOLGE
                      ? Vector2.zero : fark.normalized;
        }

        _yon = _yonHedef == Vector2.zero
             ? Vector2.zero
             : Vector2.Lerp(_yon, _yonHedef, 1f - Mathf.Exp(-18f * Time.deltaTime)).normalized;

        CubuguCiz();
    }

    static bool Canli(UnityEngine.InputSystem.TouchPhase f)
    {
        return f == UnityEngine.InputSystem.TouchPhase.Began ||
               f == UnityEngine.InputSystem.TouchPhase.Moved ||
               f == UnityEngine.InputSystem.TouchPhase.Stationary;
    }

    /// <summary>
    /// Ekranda olmayan bir parmagin tuttugu her sey birakiliyor. Unity touchId
    /// degerlerini geri donusturdugu icin Ended olayi kacabiliyor ve buton
    /// sonsuza kadar basili kaliyordu.
    /// </summary>
    void BayatParmaklariBirak()
    {
        if (_cubukParmak != -1 && !_aktif.Contains(_cubukParmak))
        { _cubukParmak = -1; _yonHedef = Vector2.zero; }
        if (_bakisParmak != -1 && !_aktif.Contains(_bakisParmak)) _bakisParmak = -1;
        for (int k = 0; k < _dugmeler.Count; k++)
            if (_dugmeler[k].Parmak != -1 && !_aktif.Contains(_dugmeler[k].Parmak))
                _dugmeler[k].Parmak = -1;
    }

    void DugmeleriIsle()
    {
        for (int i = 0; i < _dugmeler.Count; i++)
        {
            var d = _dugmeler[i];
            if (d.Tur == Tur.Arac && d.Ad != "ayar") continue;

            bool basili = d.Parmak != -1;
            bool indi = basili && !d.OncekiBasili;

            switch (d.Tur)
            {
                case Tur.Basili:  Yaz(d.Alan, basili); break;
                case Tur.Tekil:   if (indi) Yaz(d.Alan, true); break;
                case Tur.Anahtar: if (indi) d.AnahtarAcik = !d.AnahtarAcik;
                                  Yaz(d.Alan, d.AnahtarAcik); break;
                case Tur.Arac:    if (indi && d.Islev != null) d.Islev(); break;
            }

            d.OncekiBasili = basili;
            Boya(d, basili);
        }

        // ATES basiliyken nisan da acilir: oyun `fireInput && scopeInput`
        // istiyor, yoksa nisan alip ates edene kadar iki ayri dokunus gerekiyor.
        bool atesBasili = _ates != null && _ates.Parmak != -1;
        _girdi.fireInput = atesBasili;
        _girdi.scopeInput = atesBasili || (_nisan != null && _nisan.AnahtarAcik);
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

    /// <summary>Arac dugmeleri once bakiliyor: ustuste gelirse onlar kazansin.</summary>
    Dugme DugmeBul(Vector2 ekranKonum)
    {
        float o = Olcek();
        for (int gecis = 0; gecis < 2; gecis++)
        {
            Dugme en = null; float enYakin = float.MaxValue;
            for (int i = 0; i < _dugmeler.Count; i++)
            {
                var d = _dugmeler[i];
                if (d.Parmak != -1) continue;
                if (!d.Gorsel.gameObject.activeSelf) continue;
                bool arac = d.Tur == Tur.Arac;
                bool buGeciste = (gecis == 0) ? arac : !arac;
                if (!buGeciste) continue;

                float r = d.Yaricap * o * 1.12f;
                float u = (ekranKonum - d.EkranMerkez(o)).sqrMagnitude;
                if (u <= r * r && u < enYakin) { enYakin = u; en = d; }
            }
            if (en != null) return en;
        }
        return null;
    }

    void Boya(Dugme d, bool basili)
    {
        bool vurgu = basili || (d.Tur == Tur.Anahtar && d.AnahtarAcik);
        d.Zemin.color = new Color(d.Renk.r, d.Renk.g, d.Renk.b, vurgu ? 0.72f : 0.24f);
        d.Cerceve.color = new Color(d.Renk.r, d.Renk.g, d.Renk.b,
                                    _duzenleme ? 1f : (vurgu ? 1f : 0.85f));
        d.Simge.color = new Color(1f, 1f, 1f, vurgu ? 1f : 0.88f);
    }

    void CubuguCiz()
    {
        if (_cubukTopuz == null || _cubukArka == null) return;
        float o = Olcek();

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
    // Duzenleme kipi
    // ======================================================================
    void DuzenlemeKipi(bool ac)
    {
        _duzenleme = ac;
        Birak();
        Sifirla();
        _perde.gameObject.SetActive(ac);
        _kaydet.Gorsel.gameObject.SetActive(ac);
        _sifirla.Gorsel.gameObject.SetActive(ac);
        if (_cubukArka != null) _cubukArka.gameObject.SetActive(!ac);
        for (int i = 0; i < _dugmeler.Count; i++) Boya(_dugmeler[i], false);
    }

    void DuzenlemeDokunus()
    {
        var ts = Touchscreen.current;
        if (ts == null) return;

        float o = Olcek();
        var parmaklar = ts.touches;
        _aktif.Clear();

        for (int i = 0; i < parmaklar.Count; i++)
        {
            var p = parmaklar[i];
            var faz = p.phase.ReadValue();
            if (!Canli(faz)) continue;

            int kimlik = p.touchId.ReadValue();
            Vector2 konum = p.position.ReadValue();
            _aktif.Add(kimlik);

            if (faz == UnityEngine.InputSystem.TouchPhase.Began)
            {
                Dugme d = DugmeBul(konum);
                if (d != null) { d.Parmak = kimlik; d.SonKonum = konum; }
                continue;
            }

            for (int k = 0; k < _dugmeler.Count; k++)
            {
                var d = _dugmeler[k];
                if (d.Parmak != kimlik) continue;
                if (d.Tasinabilir)
                {
                    d.Kaydir += (konum - d.SonKonum) / o;
                    Yerlestir(d);
                }
                d.SonKonum = konum;
                break;
            }
        }

        // Arac dugmeleri (ayar / kaydet / sifirla) basildiginda calissin.
        for (int i = 0; i < _dugmeler.Count; i++)
        {
            var d = _dugmeler[i];
            bool basili = d.Parmak != -1;
            if (d.Tur == Tur.Arac && basili && !d.OncekiBasili && d.Islev != null)
            { d.OncekiBasili = true; d.Islev(); return; }
            d.OncekiBasili = basili;
        }

        BayatParmaklariBirak();
    }

    /// <summary>Dugmeyi ekran icinde tutup gorseli yeni yerine tasiyor.</summary>
    void Yerlestir(Dugme d)
    {
        float o = Olcek();
        float tuvalEn = Screen.width / o, tuvalBoy = 1080f;
        Vector2 mutlak = new Vector2(d.Kose.x * tuvalEn + d.Kaydir.x,
                                     d.Kose.y * tuvalBoy + d.Kaydir.y);
        mutlak.x = Mathf.Clamp(mutlak.x, d.Yaricap, tuvalEn - d.Yaricap);
        mutlak.y = Mathf.Clamp(mutlak.y, d.Yaricap, tuvalBoy - d.Yaricap);
        d.Kaydir = mutlak - new Vector2(d.Kose.x * tuvalEn, d.Kose.y * tuvalBoy);
        d.Gorsel.anchoredPosition = d.Kaydir;
    }

    void KonumlariKaydet()
    {
        for (int i = 0; i < _dugmeler.Count; i++)
        {
            var d = _dugmeler[i];
            if (!d.Tasinabilir) continue;
            PlayerPrefs.SetFloat(KAYIT + d.Ad + "_x", d.Kaydir.x);
            PlayerPrefs.SetFloat(KAYIT + d.Ad + "_y", d.Kaydir.y);
        }
        PlayerPrefs.Save();
        DuzenlemeKipi(false);
    }

    void KonumlariSifirla()
    {
        for (int i = 0; i < _dugmeler.Count; i++)
        {
            var d = _dugmeler[i];
            if (!d.Tasinabilir) continue;
            PlayerPrefs.DeleteKey(KAYIT + d.Ad + "_x");
            PlayerPrefs.DeleteKey(KAYIT + d.Ad + "_y");
            d.Kaydir = d.Varsayilan;
            Yerlestir(d);
        }
        PlayerPrefs.Save();
    }

    // ======================================================================
    // Kurulum
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

        // Duzenleme kipinde ekrani karartan perde (dugmelerin altinda kalir).
        var perde = new GameObject("Perde");
        perde.transform.SetParent(t, false);
        var prt = perde.AddComponent<RectTransform>();
        prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
        prt.offsetMin = Vector2.zero; prt.offsetMax = Vector2.zero;
        _perde = perde.AddComponent<Image>();
        _perde.color = new Color(0f, 0f, 0f, 0.55f);
        _perde.raycastTarget = false;
        perde.SetActive(false);

        _cubukArka  = Gorsel(t, "CubukArka", new Vector2(0f, 0f), CUBUK_DINLENME,
                             300f, Halka(), new Color(1f, 1f, 1f, 0.16f));
        _cubukTopuz = Gorsel(_cubukArka, "CubukTopuz", new Vector2(.5f, .5f), Vector2.zero,
                             132f, Dolu(), new Color(1f, 1f, 1f, 0.34f));

        var sol = new Vector2(0f, 0f);
        var sag = new Vector2(1f, 0f);
        var ustOrta = new Vector2(.5f, 1f);
        var ustSag = new Vector2(1f, 1f);
        var ustSol = new Vector2(0f, 1f);

        _ates = Ekle("ates",  "fireInput",  Tur.Basili,  sag, new Vector2(-205f, 205f), 118f, new Color(1f, .40f, .30f));
        _nisan = Ekle("nisan","scopeInput", Tur.Anahtar, sag, new Vector2(-440f, 155f),  84f, new Color(.50f, .78f, 1f));
        Ekle("comel",   "crouchInput",       Tur.Tekil,   sag, new Vector2(-398f, 382f),  76f, new Color(.72f, 1f, .78f));
        Ekle("sarjor",  "reloadInput",       Tur.Basili,  sag, new Vector2(-612f, 252f),  70f, new Color(1f, .84f, .42f));
        Ekle("silah",   "switchWeaponInput", Tur.Tekil,   sag, new Vector2(-580f, 490f),  66f, new Color(.84f, .80f, .98f));
        Ekle("kos",     "sprintInput",       Tur.Anahtar, sol, new Vector2( 200f, 622f),  78f, new Color(.78f, .90f, 1f));
        Ekle("suikast", "assassinateInput",  Tur.Basili,  sol, new Vector2( 482f, 560f),  74f, new Color(1f, .52f, .52f));
        Ekle("al",      "interactInput",     Tur.Tekil,   sol, new Vector2( 622f, 360f),  70f, new Color(.95f, .95f, .58f));
        Ekle("yagma",   "lootInput",         Tur.Tekil,   sol, new Vector2( 700f, 562f),  64f, new Color(.74f, .86f, .96f));
        Ekle("duraklat","pauseGameInput",    Tur.Tekil,   ustOrta, new Vector2(0f, -78f), 48f, new Color(1f, 1f, 1f));

        var ayar = Ekle("ayar", null, Tur.Arac, ustSag, new Vector2(-78f, -78f), 44f,
                        new Color(.85f, .85f, .90f));
        ayar.Tasinabilir = false;
        ayar.Islev = () => DuzenlemeKipi(!_duzenleme);

        _kaydet = Ekle("kaydet", null, Tur.Arac, ustSol, new Vector2(160f, -86f), 58f,
                       new Color(.55f, 1f, .60f));
        _kaydet.Tasinabilir = false;
        _kaydet.Islev = KonumlariKaydet;
        _kaydet.Gorsel.gameObject.SetActive(false);

        _sifirla = Ekle("sifirla", null, Tur.Arac, ustSol, new Vector2(320f, -86f), 58f,
                        new Color(1f, .70f, .45f));
        _sifirla.Tasinabilir = false;
        _sifirla.Islev = KonumlariSifirla;
        _sifirla.Gorsel.gameObject.SetActive(false);
    }

    Dugme Ekle(string ad, string alan, Tur tur, Vector2 kose, Vector2 kaydir,
               float yaricap, Color renk)
    {
        var d = new Dugme
        {
            Ad = ad, Alan = alan, Tur = tur, Kose = kose,
            Varsayilan = kaydir, Yaricap = yaricap, Renk = renk,
            BakisGecirir = kose.x > 0.9f && kose.y < 0.5f
        };
        // Kayitli konum varsa oradan basla.
        d.Kaydir = new Vector2(PlayerPrefs.GetFloat(KAYIT + ad + "_x", kaydir.x),
                               PlayerPrefs.GetFloat(KAYIT + ad + "_y", kaydir.y));

        d.Gorsel = Gorsel(_katman.transform, "Dugme_" + ad, kose, d.Kaydir,
                          yaricap * 2f, Dolu(), new Color(renk.r, renk.g, renk.b, 0.24f));
        d.Zemin = d.Gorsel.GetComponent<Image>();

        var cerceve = Gorsel(d.Gorsel, "Cerceve", new Vector2(.5f, .5f), Vector2.zero,
                             yaricap * 2f, Halka(), new Color(renk.r, renk.g, renk.b, 0.85f));
        d.Cerceve = cerceve.GetComponent<Image>();

        var s = Gorsel(d.Gorsel, "Simge", new Vector2(.5f, .5f), Vector2.zero,
                       yaricap * 1.15f, Simge(ad), new Color(1f, 1f, 1f, 0.88f));
        d.Simge = s.GetComponent<Image>();

        _dugmeler.Add(d);
        Yerlestir(d);          // kayit baska ekran boyutundan gelmis olabilir
        return d;
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
        const float K = 0.085f;

        switch (ad)
        {
            case "ates":                                     // nişangah
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

            case "ayar":                                     // sürgüler
                Cizgi(p, B, .16f, .74f, .84f, .74f, K * .8f);
                Cizgi(p, B, .16f, .50f, .84f, .50f, K * .8f);
                Cizgi(p, B, .16f, .26f, .84f, .26f, K * .8f);
                Nokta(p, B, .64f, .74f, .105f);
                Nokta(p, B, .34f, .50f, .105f);
                Nokta(p, B, .70f, .26f, .105f);
                break;

            case "kaydet":                                   // onay
                Cizgi(p, B, .20f, .52f, .42f, .28f, K * 1.2f);
                Cizgi(p, B, .42f, .28f, .82f, .74f, K * 1.2f);
                break;

            case "sifirla":                                  // geri al
                Yay(p, B, .5f, .5f, .30f, K, 30f, 320f);
                Cizgi(p, B, .27f, .68f, .22f, .48f, K * .85f);
                Cizgi(p, B, .27f, .68f, .08f, .62f, K * .85f);
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
