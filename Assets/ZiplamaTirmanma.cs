using System.Collections;
using UnityEngine;

/// <summary>
/// Ziplama ve cikintiya tirmanma.
///
/// Oyunun kendi paketinde bunlarin ikisi de YOK: ne kodda, ne Player.controller
/// icinde (Movement / Crouching / Scoping / Reloading / Assassinate / Stabbing /
/// DeadBodyCarrying), ne de SolidSnake.fbx icinde bir klip var. Bu yuzden ikisi de
/// burada fizikle yaziliyor; hazir animasyon olmadigi icin tirmanma hareketi
/// yumusatilmis bir gecisle yapiliyor.
///
/// PlayerMovement'a dokunulmuyor. Tirmanma sirasinda govde kinematige aliniyor:
/// PlayerMovement her karede linearVelocity yaziyor ama kinematik govdede bu
/// hareketi etkilemiyor, boylece iki sistem birbiriyle kavga etmiyor. Kamera da
/// donmaya devam ediyor cunku PlayerManager kapatilmiyor.
/// </summary>
public class ZiplamaTirmanma : MonoBehaviour
{
    public static ZiplamaTirmanma Etkin { get; private set; }

    /// Yukari hiz. Oyunun yercekimi gravity*fallSpeed = -9.81*5 ≈ -49 m/s²
    /// oldugu icin 9.5 m/s yaklasik 0.9 m yukseklik veriyor.
    const float ZIPLAMA_HIZI = 9.5f;
    const float ZIPLAMA_KILIDI = 0.25f;

    const float TIRMANMA_SURESI = 0.40f;
    const float EN_ALCAK_CIKINTI = 0.45f;
    const float EN_YUKSEK_CIKINTI = 2.10f;

    Rigidbody _govde;
    PlayerMovement _hareket;
    int _maske;
    float _kilitBitis;

    public bool Tirmaniyor { get; private set; }

    void Awake()
    {
        Etkin = this;
        _govde = GetComponent<Rigidbody>();
        _hareket = GetComponent<PlayerMovement>();
        _maske = ~(1 << gameObject.layer);          // kendi katmanini isinlara katma
    }

    /// <summary>Once onunde cikinti var mi bakar; yoksa ziplar.</summary>
    public void Zipla()
    {
        if (Tirmaniyor) return;
        if (Tirman(0.85f)) return;
        if (_govde == null || _hareket == null) return;
        if (!_hareket.isGrounded || Time.time < _kilitBitis) return;

        Vector3 h = _govde.linearVelocity;
        h.y = ZIPLAMA_HIZI;
        _govde.linearVelocity = h;
        _hareket.isGrounded = false;                // temas bir kare daha surebiliyor
        _kilitBitis = Time.time + ZIPLAMA_KILIDI;
    }

    /// <summary>
    /// Onunde tirmanilabilir bir cikinti ariyor. Bulursa hareketi baslatip
    /// true doner.
    /// </summary>
    public bool Tirman(float ulasma)
    {
        if (Tirmaniyor || _govde == null || _hareket == null) return false;

        Vector3 taban = transform.position;
        Vector3 on = transform.forward;

        // 1) Gogus hizasindan ileri: onumde bir yuzey var mi?
        if (!Physics.Raycast(taban + Vector3.up * 0.95f, on, out RaycastHit duvar,
                             ulasma, _maske, QueryTriggerInteraction.Ignore))
            return false;

        // 2) O yuzeyin biraz otesinden asagi: ustu nerede bitiyor?
        Vector3 tepeden = duvar.point + on * 0.30f + Vector3.up * (EN_YUKSEK_CIKINTI + 0.35f);
        if (!Physics.Raycast(tepeden, Vector3.down, out RaycastHit ust,
                             EN_YUKSEK_CIKINTI + 0.40f, _maske, QueryTriggerInteraction.Ignore))
            return false;

        float yukseklik = ust.point.y - taban.y;
        if (yukseklik < EN_ALCAK_CIKINTI || yukseklik > EN_YUKSEK_CIKINTI) return false;
        if (Vector3.Angle(ust.normal, Vector3.up) > 40f) return false;   // egik yuzeye cikma

        // 3) Ustte durabilecek yer var mi?
        Vector3 varis = ust.point + on * 0.28f;
        if (Physics.CheckCapsule(varis + Vector3.up * 0.40f, varis + Vector3.up * 1.55f,
                                 0.26f, _maske, QueryTriggerInteraction.Ignore))
            return false;

        StartCoroutine(TirmanmaHareketi(varis));
        return true;
    }

    IEnumerator TirmanmaHareketi(Vector3 varis)
    {
        Tirmaniyor = true;
        bool eskiKinematik = _govde.isKinematic;
        _govde.linearVelocity = Vector3.zero;
        _govde.isKinematic = true;

        Vector3 basla = transform.position;
        // Once dikey, sonra yatay: duvarin icinden gecmemek icin.
        Vector3 ara = new Vector3(basla.x, varis.y + 0.05f, basla.z);
        const float DIKEY_PAY = 0.58f;

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / TIRMANMA_SURESI;
            float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            transform.position = e < DIKEY_PAY
                ? Vector3.Lerp(basla, ara, e / DIKEY_PAY)
                : Vector3.Lerp(ara, varis, (e - DIKEY_PAY) / (1f - DIKEY_PAY));
            yield return null;
        }

        transform.position = varis;
        _govde.isKinematic = eskiKinematik;
        _govde.linearVelocity = Vector3.zero;
        _hareket.isGrounded = true;
        _kilitBitis = Time.time + 0.15f;
        Tirmaniyor = false;
    }
}
