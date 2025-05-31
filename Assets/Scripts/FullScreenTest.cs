using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class FullScreenTest : MonoBehaviour
{
    public TMP_Dropdown dropdown;

    public enum ScreenMode
    {
        FullScreenWindow,
        window
    }

    private void Start()
    {
        List<string> options = new List<string>
        {
            "��üȭ��",
            "â���"
        };

        dropdown.ClearOptions();
        dropdown.AddOptions(options);

        dropdown.value = 1; // â ��� ����
        dropdown.onValueChanged.AddListener(index => ChangeFullScreenMode((ScreenMode)index));

        switch (dropdown.value)
        {
            case 0:
                Screen.SetResolution(1920, 1080, FullScreenMode.FullScreenWindow);
                break;
            case 1:
                Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
                break;
        }
    }

    /// <summary>
    /// ��ũ�� ��� ����
    /// </summary>
    /// <param name="mode">������ ��ũ�� ���</param>
    private void ChangeFullScreenMode(ScreenMode mode)
    {
        switch (mode)
        {
            case ScreenMode.FullScreenWindow:
                Screen.SetResolution(1920, 1080, FullScreenMode.FullScreenWindow);
                break;
            case ScreenMode.window:
                Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
                break;
        }
    }
}
