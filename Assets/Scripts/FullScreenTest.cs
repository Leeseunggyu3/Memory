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
            "전체화면",
            "창모드"
        };

        dropdown.ClearOptions();
        dropdown.AddOptions(options);

        dropdown.value = 1; // 창 모드 시작
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
    /// 스크린 모드 변경
    /// </summary>
    /// <param name="mode">변경할 스크린 모드</param>
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
