using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Telefon icin grafik ve kare hizi ayarlari.
///
/// NEDEN GEREKLI:
/// QualitySettings.asset icinde m_CurrentQuality = 5, yani oyun telefonda da
/// "Ultra" seviyede aciliyor: 150 m golge mesafesi, yuksek cozunurluklu yumusak
/// golge, 2x MSAA, 4 piksel isigi. Bunlar masaustu degerleri. Ustune projede
/// hicbir yerde Application.targetFrameRate ayarlanmamis — Unity mobilde bunu
/// varsayilan olarak 30 fps'e sabitliyor.
///
/// Menudeki eski UISettingsManager kalite ayarlarina dokunabildigi icin ayarlar
/// yalnizca acilista degil, her sahne yuklendiginde tekrar uygulaniyor.
/// </summary>
public static class MobilAyar
{
    /// Isleme cozunurluk tavani (yatay modda kisa kenar).
    const int EN_YUKSEK_KISA_KENAR = 1080;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Baslat()
    {
        if (!Application.isMobilePlatform) return;

        CozunurlugeTavanKoy();
        Uygula();
        SceneManager.sceneLoaded += (s, m) => Uygula();
    }

    static void Uygula()
    {
        // vSync acikken targetFrameRate yok sayiliyor.
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;

        QualitySettings.shadowDistance = 35f;          // 150 m -> 35 m
        QualitySettings.shadowResolution = ShadowResolution.Low;
        QualitySettings.shadowCascades = 1;
        QualitySettings.antiAliasing = 0;              // 2x MSAA kapali
        QualitySettings.pixelLightCount = 1;           // 4 -> 1
    }

    /// <summary>
    /// Yuksek cozunurluklu telefonlarda islenen piksel sayisi kare hizini
    /// belirleyen en buyuk etken. Kisa kenar 1080'e cekiliyor; 1440p bir
    /// ekranda bu ~1.8 kat daha az piksel demek.
    /// </summary>
    static void CozunurlugeTavanKoy()
    {
        int en = Screen.width, boy = Screen.height;
        int kisa = Mathf.Min(en, boy);
        if (kisa <= EN_YUKSEK_KISA_KENAR) return;

        float oran = (float) EN_YUKSEK_KISA_KENAR / kisa;
        Screen.SetResolution(Mathf.RoundToInt(en * oran),
                             Mathf.RoundToInt(boy * oran), true);
    }
}
