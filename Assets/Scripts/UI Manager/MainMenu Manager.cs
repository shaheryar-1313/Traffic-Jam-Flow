using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuManager : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject shopPanel;
    [SerializeField] private GameObject settingPanel;

    public void OnPlayButtonClicked()
    {
        SceneManager.LoadScene("Gameplay");
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
