﻿using UnityEngine.UI;
using UnityEngine;
using TMPro;
using TootTallyCore.Graphics.Animations;
using static TootTallySpectator.SpectatingManager;
using TootTallyCore.Graphics;

namespace TootTallySpectator
{
    public static class SpectatingOverlay
    {
        private static GameObject _overlayCanvas;

        private static GameObject _pauseTextHolder;

        private static TootTallyAnimation _pauseTextHolderAnimation, _marqueeAnimation;

        private static SpectatingViewerIcon _viewerIcon;
        private static bool _isInitialized;
        private static SocketSpectatorInfo _spectatorInfo;
        private static UserState _currentUserState;

        private static CustomButton _stopSpectatingButton;

        private static Vector2 _marqueeStartPosition;
        private static TMP_Text _marqueeText;

        public static void Initialize()
        {
            _overlayCanvas = new GameObject("TootTallySpectatorOverlayCanvas");
            _overlayCanvas.SetActive(true);
            GameObject.DontDestroyOnLoad(_overlayCanvas);

            Canvas canvas = _overlayCanvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 0;

            GraphicRaycaster raycaster = _overlayCanvas.AddComponent<GraphicRaycaster>();

            CanvasScaler scaler = _overlayCanvas.AddComponent<CanvasScaler>();
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

            _stopSpectatingButton = GameObjectFactory.CreateCustomButton(_overlayCanvas.transform, Vector2.zero, new Vector2(200, 60), "Stop\nSpectating", "StopSpectatingButton", OnStopSpectatingButtonClick);
            _stopSpectatingButton.gameObject.SetActive(false);

            _marqueeText = GameObjectFactory.CreateSingleText(_overlayCanvas.transform, "SpectatorMarqueeText", "PlaceHolder", new Color(1, 1, 1, .75f));
            _marqueeText.fontSize = 36;
            _marqueeText.rectTransform.anchoredPosition = _marqueeStartPosition = new Vector2(1300, 0);
            _marqueeText.rectTransform.anchorMin = _marqueeText.rectTransform.anchorMax = new Vector2(0, .2f);
            _marqueeText.rectTransform.pivot = new Vector2(0, .5f);
            _marqueeText.gameObject.SetActive(false);

            _viewerIcon = new SpectatingViewerIcon(_overlayCanvas.transform, Vector2.zero, Vector2.one * 80f, "ViewerIcon");

            _pauseTextHolder = GameObjectFactory.CreateOverlayPanel(_overlayCanvas.transform, Vector2.zero, new Vector2(1000, 350), 8f, "PauseTextOverlay");
            _pauseTextHolder.SetActive(false);
            _pauseTextHolder.transform.Find("FSLatencyPanel").GetComponent<Image>().enabled = false;
            var rectPauseText = _pauseTextHolder.GetComponent<RectTransform>();
            rectPauseText.anchorMax = rectPauseText.anchorMin = rectPauseText.pivot = Vector2.one / 2f;
            var pauseTextContainer = _pauseTextHolder.transform.Find("FSLatencyPanel/LatencyFG").gameObject;
            GameObjectFactory.DestroyFromParent(pauseTextContainer, "title");
            GameObjectFactory.DestroyFromParent(pauseTextContainer, "subtitle");
            GameObjectFactory.DestroyFromParent(pauseTextContainer, "MainPage");

            var pauseText = GameObjectFactory.CreateSingleText(pauseTextContainer.transform, "PauseText", "Host paused the song.\n Waiting for an action.", Color.white);
            pauseText.fontSize = 72;
            pauseText.alignment = TMPro.TextAlignmentOptions.Center;
            pauseText.rectTransform.pivot = new Vector2(0, .5f);

            _isInitialized = true;
        }

        public static void UpdateViewerList(SocketSpectatorInfo spectatorInfo)
        {
            if (!_isInitialized) return;

            _spectatorInfo = spectatorInfo;
            _viewerIcon?.UpdateViewerList(spectatorInfo);
            UpdateViewIcon();
        }

        public static void SetCurrentUserState(UserState newUserState)
        {
            if (!_isInitialized) return;

            _currentUserState = newUserState;
            UpdateViewIcon();
            UpdateStopSpectatingButton();
        }

        public static void OnStopSpectatingButtonClick()
        {
            if (!_isInitialized) return;

            if (IsInGameController)
                SpectatingManagerPatches.QuitSong();
            StopAllSpectator();
        }

        public static void UpdateViewIcon()
        {
            if (_viewerIcon == null || !_isInitialized) return;

            if (_spectatorInfo.count == 0 || _spectatorInfo.count < 1 || !Plugin.Instance.ShowSpectatorCount.Value)
                _viewerIcon.Hide();
            else if (IsInLevelSelect)
            {
                _viewerIcon.SetAnchorMinMax(new Vector2(.92f, .97f));
                _viewerIcon.SetBubblePivot(Vector2.one);
                _viewerIcon.Show();
            }
            else if (IsInGameController)
            {
                _viewerIcon.SetAnchorMinMax(new Vector2(.03f, .07f));
                _viewerIcon.SetBubblePivot(Vector2.zero);
                _viewerIcon.Show();
            }
            else _viewerIcon.Hide();
        }

        public static void UpdateStopSpectatingButton()
        {
            if (!_isInitialized) return;

            if (IsInLevelSelect && SpectatingManager.IsSpectating)
            {
                _stopSpectatingButton.gameObject.SetActive(true);
                _stopSpectatingButton.GetComponent<RectTransform>().anchorMin = _stopSpectatingButton.GetComponent<RectTransform>().anchorMax = new Vector2(0.14f, 0.88f);
            }
            else if (IsInGameController && SpectatingManager.IsSpectating)
            {
                _stopSpectatingButton.gameObject.SetActive(true);
                _stopSpectatingButton.GetComponent<RectTransform>().anchorMin = _stopSpectatingButton.GetComponent<RectTransform>().anchorMax = new Vector2(0.2f, 0.09f);
            }
            else
                _stopSpectatingButton.gameObject.SetActive(false);

        }

        private static bool IsInGameController => _currentUserState == UserState.Playing || _currentUserState == UserState.Paused;
        private static bool IsInLevelSelect => _currentUserState == UserState.SelectingSong;

        public static void ShowViewerIcon() => _viewerIcon?.Show();
        public static void HideViewerIcon() => _viewerIcon?.Hide();

        public static void ShowPauseText()
        {
            if (_pauseTextHolder == null) return;

            _pauseTextHolder.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -1500);
            _pauseTextHolder.SetActive(true);
            _pauseTextHolderAnimation?.Dispose();
            _pauseTextHolderAnimation = TootTallyAnimationManager.AddNewPositionAnimation(_pauseTextHolder, Vector2.zero, 0.8f, new SecondDegreeDynamicsAnimation(2.25f, .94f, 1.15f));
        }

        public static void HidePauseText()
        {
            if (_pauseTextHolder == null) return;

            _pauseTextHolderAnimation?.Dispose();
            _pauseTextHolder.SetActive(false);

        }

        public static void ShowStopSpectatingButton()
        {
            _stopSpectatingButton?.gameObject.SetActive(true);
        }

        public static void HideStopSpectatingButton()
        {
            _stopSpectatingButton?.gameObject.SetActive(false);
        }

        public static void ShowMarquee(string playerName, string songName, float songSpeed, string modifiers)
        {
            _marqueeText.rectTransform.anchoredPosition = _marqueeStartPosition;
            _marqueeText.text = $"Currently Spectating {playerName}\nPlaying {songName}";
            if (songSpeed != 1)
                _marqueeText.text += $" [{songSpeed:0.00}x]";
            if (modifiers != null && modifiers != "None")
                _marqueeText.text += $" [{modifiers}]";
            AnimateMarquee();
            _marqueeText.gameObject.SetActive(true);
        }

        public static void AnimateMarquee()
        {
            _marqueeAnimation = TootTallyAnimationManager.AddNewPositionAnimation(_marqueeText.gameObject, -_marqueeStartPosition * 1.2f, 30f, new SecondDegreeDynamicsAnimation(0.009f, 0f, 1f), (sender) =>
            {
                _marqueeText.rectTransform.anchoredPosition = _marqueeStartPosition;
                AnimateMarquee();
            });
        }

        public static void HideMarquee()
        {
            if (_marqueeText == null) return;

            _marqueeAnimation?.Dispose();
            _marqueeText.gameObject.SetActive(false);
        }

    }
}
