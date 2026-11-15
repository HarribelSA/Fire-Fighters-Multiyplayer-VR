using TMPro;
using UnityEngine;

public class UI : MonoBehaviour
{
    [SerializeField] BasicCarController basicCarController;
    BasicNitroSystem basicNitroSystem;
    BasicGearBox basicGearBox;

    public TMP_Text speedText;
    public TMP_Text gearText;
    public TMP_Text nitroText;

    void Start()
    {
        basicNitroSystem = basicCarController.GetComponent<BasicNitroSystem>();
        basicGearBox = basicCarController.GetComponent<BasicGearBox>();
    }

    void Update()
    {
        UpdateUI();
        NitroAmount();
    }

    void UpdateUI()
    {
        if (speedText != null)
            speedText.text = "Speed: " + basicCarController.carSpeedKmh.ToString("F1") + " KM/H";

        if (gearText != null)
        {
            if (basicGearBox.IsReversing())
                gearText.text = "Gear: R";
            else if (basicGearBox.currentGear > 0)
                gearText.text = "Gear: " + basicGearBox.currentGear;
            else
                gearText.text = "Gear: N";
        }
    }

    void NitroAmount()
    {
        int barLength = 35;

        float nitroPercent = 1f - (basicNitroSystem.nitroTimer / basicNitroSystem.nitroDuration);
        nitroPercent = Mathf.Clamp01(nitroPercent);

        int filledCount = Mathf.RoundToInt(nitroPercent * barLength);
        int emptyCount = barLength - filledCount;

        string bar = new string('|', filledCount) + new string(' ', emptyCount);

        if (nitroText != null)
        {
            nitroText.text = "Nitro: " + bar;

            if (basicNitroSystem.nitroTimer >= basicNitroSystem.nitroDuration)
                nitroText.color = Color.red;
            else if (basicNitroSystem.nitroTimer < basicNitroSystem.nitroDuration && basicNitroSystem.nitroTimer > 0 && !basicNitroSystem.refillTimer)
                nitroText.color = Color.cyan;
            else if (basicNitroSystem.refillTimer)
                nitroText.color = Color.gray;
            else
                nitroText.color = Color.green;
        } 
    }
}
