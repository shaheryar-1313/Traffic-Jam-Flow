using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuManager : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject shopPanel;
    [SerializeField] private GameObject settingPanel;
    [SerializeField] private GameObject noAdsPanel;

    [Header("Buttons")]
    [SerializeField] private Animator homeButtonAnimator;

    private void Start()
    {
        if (homeButtonAnimator != null)
        {
            homeButtonAnimator.Play("Selected");
        }
    }

    public void OnPlayButtonClicked()
    {
        SceneManager.LoadScene("Gameplay (UI Design)");
    }

    public void OnShopButtonClicked()
    {
        ShowPanel(shopPanel);
    }

    public void OnSettingsButtonClicked()
    {
        ShowPanel(settingPanel);
    }

    public void OnMainMenuButtonClicked()
    {
        ShowPanel(mainMenuPanel);
    }

    public void OpenNoAdsPanel()
    {
        if (noAdsPanel != null)
            noAdsPanel.SetActive(true);
    }

    public void CloseNoAdsPanel()
    {
        if (noAdsPanel != null)
            noAdsPanel.SetActive(false);
    }

    private void ShowPanel(GameObject activePanel)
    {
        if (mainMenuPanel != null)
            mainMenuPanel.SetActive(activePanel == mainMenuPanel);

        if (shopPanel != null)
            shopPanel.SetActive(activePanel == shopPanel);

        if (settingPanel != null)
            settingPanel.SetActive(activePanel == settingPanel);
    }
}
